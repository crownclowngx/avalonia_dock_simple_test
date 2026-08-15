# Managed Plugin v1 G2：Host 实现面收口记录

> 状态：已完成
>
> 完成日期：2026-08-15
>
> 分支：`dev-重构-2026年8月13日`
>
> 前置基线：G0 绿色基线、G1 支持边界与版本线
>
> 所属任务：[Managed Plugin v1 封板评审与整改任务书](../../design/host-v1-sealing-readiness-plan.md#g2将-host-实现移出插件-api)

## 1. 结果摘要

G2 已把 `MyAvaloniaManagement` 从“被指纹意外冻结的实现程序集”改为纯 Host 可执行实现。
重新还原和 Release 构建后，该程序集只导出 Avalonia 编译器生成的
`CompiledAvaloniaXaml.!AvaloniaResources` 与 `CompiledAvaloniaXaml.!XamlLoader`；没有任何
`MyAvaloniaManagement.*` 自有类型被导出。窗口、ViewModel、加载器、注册表、Dock 工厂、消息和
内建策略均为 internal，仓库插件的唯一正式编译契约仍是 `MyAvaloniaManagementCommon`。

全局静态 `ServiceProvider` 及五个生产 ViewModel 无参构造已删除。App 使用注入的桌面 Shell，
内置策略使用窄 `Func<T>` 工厂，设计器使用无 I/O 的显式样例数据。最终锁定还原、解决方案
Release 构建、三层测试、覆盖率和 Windows 真实窗口 Smoke 全部通过。

## 2. 收口前后的边界

| 边界 | G2 前 | G2 后 |
| --- | --- | --- |
| Host 自有导出类型 | 窗口、View、ViewModel、加载器、服务、消息等数十个类型 | 0 |
| 插件 SDK 指纹 | Host 与 Common 混合为一个 SHA256 | 只保护 Common；Host 使用可读零导出断言 |
| App 依赖 | 从进程全局静态 provider 解析主题和主 ViewModel | 构造注入 `IHostDesktopShell` |
| ViewModel 构造 | 注入构造加 public 无参 Service Locator 桥 | 生产类型只有显式依赖构造 |
| 内建策略 | 注入整个 `IServiceProvider` 或使用静态 provider | 注入对应 `Func<T>` 窄工厂 |
| 设计预览 | 构造生产 ViewModel；文件树还会覆盖运行时 DataContext | 独立纯内存设计数据实现窄绑定端口 |
| 测试访问 | 部分 Harness 依赖 public Host 面 | 仅五个明确 friend assembly 可访问 internal |

Common public API 没有在 G2 中增删或改签名，SDK 指纹为：

```text
A3C41FC09E0184E3BF9255733C35A92A0DB1682121A945137760868E2BEA2977
```

该 SHA256 只是 G13 建立可审阅文本 API 基线前的临时门禁。

## 3. 启动与所有权设计

生产启动链现在是：

```text
Program
  -> HostRuntime（发现插件、构建并拥有根容器）
  -> HostAvaloniaBuilder（用 Func<App> 连接 Avalonia 与当前 Runtime）
  -> App（只适配 Avalonia 资源和生命周期）
  -> HostDesktopShell（主题、主窗口、主 ViewModel、Smoke 政策）
```

`HostRuntime` 不再暴露 `Services`，也没有 `Current` 或静态容器。Builder 只在 Avalonia 创建 App
时从当前 Runtime 的 provider 解析对象，消息循环结束后仍由同一个 Runtime 反向关闭插件并释放
容器。启动失败应用保持独立最小资源路径，不依赖生产 Shell。

`IHostDesktopShell` 是内部生命周期策略。生产实现创建主窗口；Headless UI 测试注入 no-op Shell，
只加载真实 App 资源。这样既没有为测试恢复 App 无参构造，也没有让 App 了解测试环境。

## 4. 构造注入与循环依赖

四个内建 Tool 策略分别依赖 `Func<FileSystemTreeViewModel>`、`Func<PlugGroupMenuViewModel>`、
`Func<ToolManagementViewModel>` 和 `Func<PluginStatusViewModel>`。工厂在组合根注册，策略看不到
整个容器，同时保留“每次创建得到新 ViewModel”的语义。

Welcome Document 策略依赖 `Func<ManagementFactory>`。注册表构造时只取得工厂闭包；用户真正点击
欢迎页工具入口时才解析 `ManagementFactory`，从而显式打破“Factory 依赖 Registry、Registry
激活 Welcome 策略”的构造期循环。Host 自身 Document 策略因此改用 `ActivatorUtilities`；Legacy
插件仍保持 G4 前的 public 无参策略语义，本次没有提前改变其失败行为。

生产代码搜索 `ServiceProvider.` 只剩 `DocumentScopeManager` 中的 `scope.ServiceProvider`。它表示
当前 Document 的局部 DI Scope，不是已经删除的进程全局定位器。

## 5. 设计时数据

主窗口和文件树 XAML 分别依赖内部 `IMainWindowViewBindings`、
`IFileSystemTreeViewBindings`。生产 ViewModel 与设计样例都满足同一窄端口，XAML 继续启用编译绑定。

- `MainWindowDesignData` 直接构造最小 RootDock、ToolDock、DocumentDock 和欢迎 Document；
- `FileSystemTreeDesignData` 使用显式节点工厂建立固定目录树；
- 设计命令全部无副作用；
- 不创建服务容器，不读写布局，不扫描插件，不枚举驱动器，不访问网络；
- `FileSystemTreeView` 的生产 `UserControl.DataContext` 已删除，运行时对象只来自 Tool 策略。

StaticViewLocator 包和生成器同时移除。当前 `ViewLocator` 已经拥有统一的动态视图发现路径，保留
生成器只会强制产生 public partial 类型和额外导出 Attribute，与 G2 的程序集边界冲突。

## 6. SOLID 与朴素模式

- **SRP**：Runtime 管容器所有权，App 管框架生命周期，Desktop Shell 管窗口启动，设计数据只管预览；
- **OCP**：Headless/Harness 通过内部 Shell 或 Builder 扩展，不给 App 增加环境分支；
- **LSP**：生产与设计对象均完整满足同一绑定端口，没有“部分初始化的生产 ViewModel”；
- **ISP**：XAML 和内置策略只取得所需绑定或对象工厂，不取得万能 provider；
- **DIP**：App 依赖内部 Shell 抽象，生产业务对象依赖显式服务或窄工厂。

使用的模式只有 Composition Root、Constructor Injection、Strategy 和 Factory Delegate。没有增加
公共 Facade、全局上下文、抽象工厂层级或插件侧适配框架。

## 7. Friend assembly 与生产引用门禁

Host 明确允许以下仓库测试消费者访问 internal：

- `MyAvaloniaManagement.Tests`；
- `MyAvaloniaManagement.UiTests`；
- `MyAvaloniaManagement.PluginTests`；
- `MySmallTools.Playback.IntegrationHarness`；
- `DaTangAccountingHelpPlug.Tests`。

后两者仍可复用真实 Host 组合和 Document Scope，但 friend access 不构成二进制兼容承诺。
自动化同时检查四个生产插件程序集不引用 Host，并扫描 `Plugins` 项目图，Host ProjectReference
只能出现在上述 DaTang 测试和 MySmallTools Harness。

## 8. 最终验证证据

执行命令：

```powershell
dotnet restore MyAvaloniaManagement.sln --locked-mode --nologo
dotnet build MyAvaloniaManagement.sln `
  -c Release -p:SkipPluginDeploy=true --no-restore --nologo
dotnet test Host/MyAvaloniaManagement.Tests/MyAvaloniaManagement.Tests.csproj `
  -c Release --no-build --no-restore --filter "FullyQualifiedName~HostApiBoundary"
dotnet build Plugins/MySmallTools/MySmallTools.Playback.IntegrationHarness/MySmallTools.Playback.IntegrationHarness.csproj `
  -c Release --no-restore
.\scripts\Invoke-MyAvaloniaManagementTests.ps1 `
  -Configuration Release -NoRestore -WindowsSmoke
git diff --check
```

最终结果来自 2026-08-15 生成的 TRX 与 `summary.json`：

| 门禁 | 结果 |
| --- | --- |
| 锁定还原 | 通过 |
| 解决方案 Release 构建 | 0 警告、0 错误 |
| Host 边界定向测试 | 3/3 通过 |
| Common 临时 API 指纹 | 1/1 通过 |
| Plugin Host 引用边界 | 2/2 通过 |
| `MyAvaloniaManagement.Tests` | 113/113 通过 |
| `MyAvaloniaManagement.UiTests` | 34/34 通过 |
| `MyAvaloniaManagement.PluginTests` | 118/118 通过 |
| 测试合计 | 265/265 通过，无跳过 |
| Host 覆盖率 | 行 77.75%，分支 63.91% |
| Windows Smoke | 通过 |

测试数量是本次时间点证据，不是永久门槛；后续继续读取 TRX 与 `summary.json`。

## 9. 回滚与后续

G2 没有修改 Plugin SDK、manifest、Document 文件格式、消息语义或插件业务数据，代码变更可以整体
回滚。以旧 Host 实现类型编译的外部程序集不会再链接，但这些类型从未属于 Managed Plugin v1
承诺面，本次正是 v1 冻结前的重新定基线，因此不提升 SDK 或 Host API 版本。

G3 下一步收口和打包 Common；G4 删除仍保留的 Legacy 激活；G5 用显式 View 贡献替换当前动态
发现；G13 再把 Common SHA256 换成可审阅 public API 文本基线。
