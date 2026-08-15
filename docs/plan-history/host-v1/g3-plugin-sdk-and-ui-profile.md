# Managed Plugin v1 G3：正式 Plugin SDK 与 UI Profile

> 状态：已完成
>
> 完成日期：2026-08-15
>
> 前置基线：G0 绿色基线、G1 支持边界与版本线、G2 Host 实现面收口
>
> 所属任务：[Managed Plugin v1 封板评审与整改任务书](../../design/host-v1-sealing-readiness-plan.md#g3形成正式-plugin-sdk)

## 1. 结果摘要

G3 已把 `MyAvaloniaManagementCommon` 形成可独立消费的基础 NuGet 包
`MyAvaloniaManagement.PluginSdk`。程序集和命名空间保持不变，Host 可执行程序集没有进入包；
字体、桌面后端、Fluent/Semi/Ursa/Dock 主题和 Dock 视觉控件不再是 Common 的直接依赖。

为兼顾插件 UI 自由度与基础契约最小化，新增纯依赖包
`MyAvaloniaManagement.PluginSdk.UI`。普通插件只引用基础 SDK；需要直接使用 Semi、Ursa 或
Dock UI 类型的插件引用同版本 UI Profile。Host 继续统一加载全局主题，并显式共享这组已验证
UI 程序集。manifest schema 没有变化，UI Profile 的破坏性变化随 Plugin SDK 主版本演进。

宿主同时冻结了 14 个 `App*` 语义画刷。仓库插件已不再直接消费 `DockTheme*` 背景或边框，
主题切换通过 `DynamicResource` 生效。最终锁定还原、Release 构建、包消费、三层测试和 Windows
真实窗口 Smoke 全部通过，共 **275/275** 项测试通过。

## 2. 依赖所有权

### 2.1 基础 SDK

基础包只保留以下直接依赖：

| 依赖 | 保留原因 |
| --- | --- |
| Avalonia | 全屏宿主契约使用 `Control`，Behavior 使用 Avalonia 属性系统 |
| CommunityToolkit.Mvvm | 当前消息契约仍暴露 `IMessenger`，由 G9 收口 |
| Dock.Model.Mvvm | Document/Tool 策略签名直接使用 Dock Model 类型 |
| DI Abstractions | `IPluginModule.ConfigureServices` 使用 `IServiceCollection` |
| Newtonsoft.Json | Document 强类型 ID 的现有 JSON 边界 |
| Xaml.Behaviors | `HandledEventsAwareBehavior` 的基类，由 G11 决定删除 |

`Dock.Model.Mvvm 12.0.0.2` 上游会传递 `Dock.Controls.Recycling.Model`。Common 已不再直接引用
该包，但在不改变现有 Dock public 签名前无法从最终还原图消除；包门禁把它作为唯一已知例外，
仍严格禁止 Dock Avalonia、主题、ProportionalStackPanel 和 Recycling 视觉实现进入基础包直接依赖。

### 2.2 Host

Host 直接引用并拥有 Desktop、Inter Font、Fluent、Semi、Ursa、Dock Avalonia、Dock Fluent Theme
和 Dock Recycling。`App.axaml` 是唯一全局主题组合入口，加载顺序为：

```text
Fluent → Semi → Ursa Semi → Dock Fluent → Host styles
```

这消除了此前“Host 的 XAML 使用 Semi/Ursa，但项目靠 Common 传递引用才能编译”的倒置关系。

### 2.3 可选 UI Profile

`MyAvaloniaManagement.PluginSdk.UI` 是 dependency-only package：

- `IncludeBuildOutput=false`，包中以 `lib/net10.0/_._` 表示有意不提供程序集；
- 自动依赖同版本 `MyAvaloniaManagement.PluginSdk`；
- Fluent、Semi、Ursa 与 Dock UI 版本使用闭区间，例如 `[12.1.0]`；
- 版本值来自 `Directory.Version.props`，Host、中央包版本和 Profile 不复制数字；
- 不包含 Host、Common 或 UI Profile 自身 DLL。

宿主的 `HostContractAssemblyPolicy` 现在从两个根集合构建共享闭包：基础 SDK 和显式 UI Profile。
只有这些 UI 家族进入默认加载上下文；插件普通业务包仍由各自 `PluginLoadContext` 私有解析。

## 3. 插件样式契约

### 3.1 正式语义资源

以下键由 Host 在 Light/Dark 中都提供，值类型固定为 `SolidColorBrush`：

| 类别 | 资源键 |
| --- | --- |
| 表面 | `AppPanelBrush`、`AppSubtlePanelBrush`、`AppToolSelectedBrush` |
| 分隔与边框 | `AppDividerBrush`、`AppBorderBrush` |
| 文本与状态 | `AppSecondaryTextBrush`、`AppInfoBrush`、`AppWarningBrush`、`AppErrorBrush`、`AppDangerBrush` |
| 警告容器 | `AppWarningPanelBrush`、`AppWarningBorderBrush` |
| 消息状态 | `AppReadMessageBackgroundBrush`、`AppUnreadMessageBackgroundBrush` |

删除、改名或改变值类型属于 Plugin SDK 破坏性变化；新增语义键可以作为兼容次版本新增。
插件必须使用 `DynamicResource` 消费主题相关值。`AppErrorBrush` 与 `AppDangerBrush` 当前颜色相同，
但语义不同：前者表示错误状态，后者表示破坏性操作，未来可以独立调整而不修改插件 XAML。

### 3.2 插件可以做什么

- 标准 Avalonia 控件自动继承宿主全局主题，无需 UI Profile；
- 插件可以在自身程序集打包 `Styles/*.axaml`，通过 View 局部 `StyleInclude` 复用；
- 插件局部资源键和 Style Class 应使用插件 ID 前缀，避免跨插件碰撞；
- 直接使用 Semi、Ursa 或 Dock UI 控件时引用同版本 UI Profile；
- 第三方主题自带资源键只在对应 Profile 版本内有效，不升级为基础语义资源承诺；
- 插件不得修改 `Application.Current.Styles`，全局主题和加载顺序属于 Host 生命周期。

现有 BiliDownloader 与 MySmallTools 中仅用于背景/边框的 `DockTheme*` 引用已经替换为
`AppPanelBrush` 和 `AppBorderBrush`。这不会限制插件布局、模板或动画，只切断对 Dock 内部调色板的
偶然依赖。

## 4. SOLID 与朴素设计

- **SRP**：Common 管编译契约，Host 管应用级主题，UI Profile 只描述受支持依赖组合，插件管局部样式；
- **OCP**：Host 可以更换主题实现，只要继续提供语义资源，普通插件不需要修改；
- **ISP**：后台或标准控件插件不承担 Semi、Ursa、Dock UI 的依赖闭包；
- **DIP**：插件以业务语义资源表达颜色意图，不依赖第三方主题的内部资源名称。

实现只复用了现有 Strategy（共享程序集策略）和 Composition Root，没有增加主题服务接口、全局
Service Locator、插件主题注册器或新的 manifest 占位字段。UI Profile 采用 NuGet 元包而不是空
运行时 Facade，避免制造没有行为的公共类型。

## 5. 注释与包内容门禁

基础 SDK 已启用 `GenerateDocumentationFile`，并把 `CS1591` 提升为错误。G3 补齐了此前缺失的
**102 个** public 成员 XML 注释；注释说明用途、参数、错误和设计意图，而不是逐行复述实现。

基础包包含：

```text
README.md
lib/net10.0/MyAvaloniaManagementCommon.dll
lib/net10.0/MyAvaloniaManagementCommon.xml
```

包不包含 `MyAvaloniaManagement.dll`。仓库当前没有 LICENSE 文件，因此 G3 没有擅自授予 MIT 等
许可证；README 明确标记当前制品不自动发布到公共 NuGet。对外发布前必须由项目所有者选择并
提交许可证，这是发布授权决策，不是代码生成任务。

## 6. 自动化验证

`scripts/Test-PluginSdkPackage.ps1` 每次在独立临时目录执行：

1. 打包基础 SDK 与 UI Profile；
2. 检查 nupkg 内容、nuspec、直接依赖和精确 UI 版本；
3. 从临时本地源还原只引用基础 SDK 的最小 `IPluginModule`；
4. 验证基础插件还原图没有主题、Desktop 或 Dock 视觉实现；
5. 从 UI Profile 编译真实 XAML，实际使用 `Ursa.Controls.IconButton`、`DockControl` 和
   `AppPanelBrush`；
6. 删除临时目录，不污染仓库或用户插件数据。

新增测试同时验证：

- Common 和 Host 的直接 PackageReference 白名单；
- UI Profile 版本属性与 dependency-only 结构；
- Semi、Ursa、Dock UI 从 `AssemblyLoadContext.Default` 返回；
- 14 个语义画刷在 Light/Dark 下存在且类型一致；
- 插件 `DynamicResource` 在主题切换后取得不同颜色；
- 仓库插件 XAML 不使用未登记 `App*` 键或 `DockTheme*` 资源。

## 7. 最终证据

2026-08-15 执行：

```powershell
dotnet restore MyAvaloniaManagement.sln --locked-mode --nologo
dotnet build MyAvaloniaManagement.sln -c Release `
  -p:SkipPluginDeploy=true --no-restore --nologo
.\scripts\Test-PluginSdkPackage.ps1 -Configuration Release
.\scripts\Invoke-MyAvaloniaManagementTests.ps1 `
  -Configuration Release -NoRestore -WindowsSmoke
git diff --check
```

| 门禁 | 结果 |
| --- | --- |
| 解决方案 Release 构建 | 0 警告、0 错误 |
| 基础 SDK 临时消费者 | 通过，0 警告、0 错误 |
| UI Profile 临时消费者 | 通过，Ursa/Dock XAML 编译成功 |
| Host Unit | 113/113 |
| Headless UI | 37/37 |
| Plugin | 125/125 |
| 合计 | 275/275，无跳过 |
| Host 覆盖率 | 行 77.85%，分支 64.03% |
| Windows Smoke | 通过 |

测试数量是本次时间点证据，后续仍从 TRX 和 `summary.json` 动态读取。

## 8. 回滚与后续

G3 没有修改 manifest schema、Document 磁盘格式或消息语义。代码可按“SDK/UI Profile 项目、Host
依赖和共享策略、语义资源、测试与文档”整体回滚。回滚后 Common 会重新携带主题直接依赖，因此
不能只删除 UI Profile 而保留旧 Common 依赖白名单测试。

G9 将移除 CommunityToolkit 消息泄漏，G11 处理 Xaml Behavior 和低价值 public 候选，G12 统一
插件部署 Target，G13 再冻结可审阅 public API 文本基线。G3 生成的是正式可消费包，不代表
G3–G16 之外的封板条件已经完成。
