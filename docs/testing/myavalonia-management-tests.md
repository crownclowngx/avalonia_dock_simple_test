# MyAvaloniaManagement 测试说明

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
  主窗口事件和内容全屏。
- PluginTests 继续覆盖 Dock 布局、Document Scope、插件生命周期和兼容性。
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

`MainWindowViewModel` 和三个 Tool ViewModel 都增加了显式依赖构造函数，
正式容器及测试通过该构造函数传入服务；公开无参构造仍保留给 XAML 设计器和
历史代码，通过兼容 `ServiceProvider` 转接到同一个构造函数。

核心行为只存在一套实现，避免“测试构造路径”和“生产构造路径”逐渐分叉。
ViewModel 注册为瞬态，防止多个窗口、设计器或 Headless 测试共享绑定状态；
Dock 工厂、消息、布局存储等协调服务保持单例，保证应用内只有一份布局事实。

宿主 Tool 策略使用 `ActivatorUtilities` 创建，因此策略自身可以注入容器，
再由容器创建 Tool ViewModel。插件策略仍使用原 `PluginStrategyActivator`，
保留插件隔离和兼容行为。

### 文档打开与保存

批量打开以单个文件为错误边界：

- 已打开的文件只激活原标签，然后继续处理后续文件；
- 不存在、损坏 JSON、未知类型或读取失败只跳过当前文件；
- 每个文件只读取一次、反序列化一次，同一份 `DocumentSaveData` 直接交给文档；
- Windows 路径先转绝对路径，再按不区分大小写规则比较，避免同一文件重复打开。

保存新文档时通过 `DocumentMetadata.DocumentTypeId` 计算扩展名和文件类型，
而不是固定使用 TXT。写入前同步文档路径、标签标题和保存元数据标题，使 UI、
内存对象和磁盘内容保持一致。

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
