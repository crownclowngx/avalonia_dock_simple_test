# MyAvaloniaManagement 测试说明

Document 生命周期回归除 Scope 隔离外，还必须覆盖：确认关闭后 `IDocumentLifetime` 先取消再 Dispose、重复释放幂等、在途 HTTP/Excel/内容浏览停止、迟到 UI 回调被抑制，以及 BiliDownloader 已提交后台任务不随标签关闭而取消。原生文件选择器与已经进入 EPPlus 同步 `SaveAs` 的写入属于显式不可强制中断边界。

> 当前 G5 专项基线：2026-08-16，Release 共 283 项通过，解决方案构建 0 警告、0 错误，
> SDK 新契约消费与旧候选拒绝夹具通过。本次没有重新采集覆盖率或 Windows Smoke；最近一次
> 覆盖率与 Smoke 仍是 G4 的独立时间点证据，不能冒充 G5 结果。数量来自命令输出，不是永久固定门槛。

## 一键门禁

在仓库根目录运行：

```powershell
.\scripts\Invoke-MyAvaloniaManagementTests.ps1
```

默认使用 Release 配置，依次运行：

1. `MyAvaloniaManagement.Tests`：宿主单元与组件测试；
2. `MyAvaloniaManagement.UiTests`：Avalonia Headless UI 测试；
3. `MyAvaloniaManagement.PluginTests`：现有宿主与插件集成回归。

所有测试必须执行且通过，不允许跳过。测试结果、合并后的 Cobertura、HTML
覆盖率报告和 `summary.json` 写入
`artifacts/test-results/MyAvaloniaManagement`。

已完成还原时可以增加 `-NoRestore`。Debug 验证使用：

```powershell
.\scripts\Invoke-MyAvaloniaManagementTests.ps1 -Configuration Debug
```

## Plugin SDK 包门禁

G3 新增、G5 扩展的独立包消费门禁：

```powershell
.\scripts\Test-PluginSdkPackage.ps1 -Configuration Release
```

脚本在系统临时目录打包并消费 `MyAvaloniaManagement.PluginSdk` 与
`MyAvaloniaManagement.PluginSdk.UI`，检查包内容、nuspec、基础依赖白名单和 UI 精确版本。
随后编译最终 `Configure(IPluginRegistrationContext)` Managed Plugin，确认旧候选
`PluginId + ConfigureServices` 夹具必须编译失败，再编译实际使用 Ursa、Dock UI、宿主语义资源的
XAML 插件。还原使用临时隔离 NuGet 缓存，不能误命中开发机中的同版本旧包。
临时目录在结束时删除，不读取用户数据根，也不发布到公共 NuGet。

## 覆盖率门槛

门槛定义在
`Host/MyAvaloniaManagement.Tests/coverage-baseline.json`：

- `MyAvaloniaManagement` 手写代码行覆盖率不低于 65%；
- 分支覆盖率不低于 50%；
- `MainWindowViewModel` 行覆盖率不低于 75%；
- 三个宿主 Tool ViewModel 各自行覆盖率不低于 70%。

`obj`、XAML/C# 生成代码和测试程序集不参与统计。生产 View 和
`App.axaml.cs` 不排除，因为 Headless 测试应保护实际加载、绑定和窗口事件。

## Windows 真实启动冒烟

Windows 冒烟默认关闭，显式运行：

```powershell
.\scripts\Invoke-MyAvaloniaManagementTests.ps1 -WindowsSmoke
```

脚本发布不包含插件的宿主到隔离目录，设置临时
`MYAVALONIA_DATA_DIRECTORY` 和 `MYAVALONIA_SMOKE_TEST=1`。应用仍会创建并
打开真实主窗口；窗口 `Opened` 后由 UI Dispatcher 排队执行正常关闭，让
Closing、布局保存和宿主退出完整执行。主程序必须在 15 秒内以退出码 0
结束。该过程不会读取或覆盖用户 LocalAppData 中的正式布局。

## 测试边界

- 单元层覆盖 DI、ViewModel、文件模型、文档保存、消息和 Tool 行为。
- Headless 层覆盖生产 XAML、主题资源、绑定、DockControl、ViewLocator、
  主窗口事件、内容全屏、14 个插件语义画刷和主题动态切换。
- PluginTests 继续覆盖 Managed-only 拒绝、Dock 布局、Document Scope、插件生命周期、SDK 依赖边界和 UI 共享程序集。
- 像素截图、真实插件安装包、媒体播放和长时间稳定性不属于本门禁。

## 设计思路与原因

### 文件操作边界

主窗口和文件树只依赖内部 `IHostStorageService`。接口只暴露路径选择、
文件存在检查和异步文本读写，不向 ViewModel 泄漏 `IStorageFile`、主窗口或
`System.IO.File` 静态调用。

这样设计有三个原因：

1. 单元测试可以使用内存文件和预设的选择结果，不会弹出原生窗口；
2. Avalonia 的窗口生命周期被限制在生产实现中，ViewModel 只编排业务流程；
3. 保持 `MyAvaloniaManagementCommon` 的 Document、Tool 和保存契约不变，
   插件不需要因为宿主测试改造而重新编译。

### 构造函数与依赖注入

`MainWindowViewModel` 和四个 Host Tool ViewModel 只保留显式依赖构造函数，正式容器及测试
通过该构造函数传入服务。历史静态 `ServiceProvider` 与生产无参构造已经删除；XAML 设计器
改用实现窄绑定接口的纯内存样例，不构造生产对象图。

核心行为只存在一套实现，避免“测试构造路径”和“生产构造路径”逐渐分叉。
生产 ViewModel 注册为瞬态，防止多个窗口或 Headless 测试共享绑定状态；
Dock 工厂、消息、布局存储等协调服务保持单例，保证应用内只有一份布局事实。

Host 与插件的 Document/Tool 策略都使用 `ActivatorUtilities` 创建，因此策略只需声明真实依赖，
不再为了另一套二进制加载协议保留无参构造。模块仍用 public 无参构造，因为它发生在根容器
建立之前。

### Managed-only 加载

`ManagedOnlyPluginLoadingTests` 通过 8 项专项场景保护严格清单、必需 `.deps.json`、唯一
`IPluginModule`、结构错误目录隔离、DI-only 策略和四个真实插件所有权。测试夹具中的无模块
策略构造函数故意抛错，用来证明宿主不会把它作为 Legacy 无参策略激活。私有依赖夹具仍验证
同名不同版本程序集进入不同 ALC，并共享同一个 SDK 契约实例。

### 文档打开与保存

`MainWindowViewModel` 把文件流程委托给 `DocumentPersistenceCoordinator`，后者通过
`DocumentWorkspace` 适配 Dock 文档区，并串行化打开和保存操作。批量打开以单个文件为错误边界：

- 已打开的文件只激活原标签，然后继续处理后续文件；
- 不存在、损坏 JSON、未知类型或读取失败只跳过当前文件；
- 每个文件只读取一次、反序列化一次，同一份 `DocumentSaveData` 直接交给文档；
- Windows 路径先转绝对路径，再按不区分大小写规则比较，避免同一文件重复打开。
- 并发打开同一路径时，后到请求会看到前一请求创建的文档并只执行激活。

Document 类型身份与文件扩展名已经解耦：新建和“另存为”统一建议 `.mamdoc`，
打开历史扩展名文件后的普通保存仍覆盖原路径。内容先通过同目录临时文件和原子替换提交；只有写入成功后才
同步文档路径、标签标题和保存完成状态。I/O、权限、路径、JSON 与
`DocumentLoadException` 等预期故障会转换为稳定错误结果，编程错误不会被吞掉。

### 工具显隐与稳定 ID

`ManagementFactory` 直接注入 `IMessengerService`，工具隐藏通知不再运行时访问
静态服务定位器。这让依赖关系可见，也避免测试、多容器或初始化顺序变化时取到
错误的消息实例。

插件菜单的策略元数据、创建实例、`ContextLocator` 和
`DockableLocator["Plug"]` 共用 `DockNameConstant.PlugGroupMenu`。
Dock ID 会被持久化，集中常量可以避免一个字符的差异导致工具已经创建却无法定位。

工具管理界面不把 CheckBox 状态当作事实来源，而是重新检查 Dock 树和
`HiddenDockables`。因此无论工具由管理界面切换、用户点击关闭按钮还是布局恢复
产生变化，状态都可以重新收敛到真实布局。

根布局尚未建立时，工具管理界面使用 `ManagementFactory` 提供的内部只读注册快照，
不再通过反射读取私有字段；布局建立后仍以真实 Dock 树为事实来源。

### 契约与内部重构保护

`PublicApiContractTests` 对 Host 与 `MyAvaloniaManagementCommon` 的导出类型、构造函数、
方法、属性、字段和事件生成稳定指纹。内部类拆分不会改变指纹；任何有意 public 契约
调整都必须在独立评审中同步更新契约测试。

`InternalRefactorTests` 保护策略元数据只读取一次、重复 ID 与元数据碰撞抛出 `HostCompositionException`、插件根目录
并发加载共享同一不可变快照，以及原子替换后不遗留临时文件。

### 布局隔离与真实冒烟

生产默认仍把布局写入 LocalAppData。仅当设置
`MYAVALONIA_DATA_DIRECTORY` 时改用指定目录，使真实进程测试不会读取或覆盖
用户数据。

`MYAVALONIA_SMOKE_TEST=1` 不绕过应用启动：它仍创建并打开真实主窗口，只在
`Opened` 之后由 UI Dispatcher 排队正常关闭。这样可以覆盖窗口创建、XAML、
DI、Opened/Closing、布局保存和退出码，同时无需使用不稳定的窗口句柄查找或
强制杀进程作为成功路径。

### 三层测试与覆盖率门禁

- 单元/组件测试快速验证分支较多的业务行为和错误边界；
- Headless UI 测试加载生产 `App.axaml`，保护真实资源、绑定和控件组合；
- PluginTests 保留跨程序集、Scope、Dock 和插件生命周期回归。

只有 Headless 项目使用 xUnit v3，因为 Avalonia 12 的官方 Headless xUnit
集成要求 v3；既有 xUnit 2 测试不迁移，以减少无关变更。覆盖率按程序集和源文件
过滤后合并，既设置宿主总体门槛，也为四个高风险 ViewModel 设置独立门槛，
防止用大量简单文件的覆盖率掩盖主流程缺口。

### G2 当前绿色基线

2026-08-15 先执行锁定还原和解决方案 Release 构建，再执行：

```powershell
.\scripts\Invoke-MyAvaloniaManagementTests.ps1 `
  -Configuration Release -NoRestore -WindowsSmoke
```

本次结果来自 `Unit.trx`、`UI.trx`、`Plugin.trx` 和 `summary.json`：

| 测试项目 | 数量 |
| --- | ---: |
| `MyAvaloniaManagement.Tests` | 113 |
| `MyAvaloniaManagement.UiTests` | 34 |
| `MyAvaloniaManagement.PluginTests` | 118 |
| **合计** | **265** |

Host 行覆盖率为 77.75%，分支覆盖率为 63.91%，Windows Smoke 为通过。完整的 Host public
收口、构造注入与 friend 边界见 [G2 Host 实现面收口记录](../plan-history/host-v1/g2-host-api-surface.md)。

以下结果保留为历史时间点证据，不用当前数字覆盖：

G0 在 2026-08-15 的独立绿色基线为 Unit 105、UI 32、Plugin 112，共 249 项；Host 行覆盖率
76.86%、分支覆盖率 63.65%，Windows Smoke 通过。详细根因与证据见
[G0 绿色基线恢复记录](../plan-history/host-v1/g0-green-baseline.md)。

G1 在 2026-08-15 的独立绿色基线为 Unit 110、UI 32、Plugin 116，共 258 项；Host 行覆盖率
77.01%、分支覆盖率 63.79%，Windows Smoke 通过。详细版本与数据根证据见
[G1 支持边界与版本线冻结记录](../plan-history/host-v1/g1-support-boundary-and-version-lines.md)。

2026-08-12 执行 `.\scripts\Invoke-MyAvaloniaManagementTests.ps1 -Configuration Release -WindowsSmoke`
的 Release 专项结果：

| 测试项目 | 数量 |
| --- | ---: |
| `MyAvaloniaManagement.Tests` | 84 |
| `MyAvaloniaManagement.UiTests` | 31 |
| `MyAvaloniaManagement.PluginTests` | 93 |
| **合计** | **208** |
