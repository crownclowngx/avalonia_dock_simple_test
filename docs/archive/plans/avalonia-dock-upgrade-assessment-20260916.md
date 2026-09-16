# Avalonia / Dock 最新稳定版升级评估

> 归档说明（2026-09-16）：正文描述当时的基线、方案或验收，不代表当前运行状态。原始命令、版本和结论保留；当前操作见[文档导航](../../README.md)。原路径中的构建产物可能已清理，外部插件材料为可选引用。

评估日期：2026-09-16。源码基线：`bc3d877fc9b72810a060c2288b1d63cd6fdd0b19`。

> 后续核查修正：当前 Host 已通过 Adapter、根能力策略和 Factory 覆盖禁止浮动。下文关于浮动窗口的风险属于上游变化背景，不代表当前产品支持浮动；生产验收应检查禁止浮动和主窗口内部停靠。具体定制处理、源码依据及执行步骤以 [Host V9 升级方案](host-v9-avalonia-dock-upgrade-plan.md) 为准。

## 结论

建议按“升级 Host，保留现有插件二进制”的方式推进本次升级。当前已具备源码编译、Host 自动测试和旧插件加载层面的可行性证据；真实窗口交互、视频播放和各插件业务流程仍需回归后才能签署发布兼容。

| 项目 | 当前版本 | 本次候选 |
| --- | --- | --- |
| Avalonia 核心 / Desktop / Fluent / Fonts | 12.1.0 | 12.1.2 |
| Dock 家族 | 12.0.0.2 | 12.1.0.6 |
| Avalonia.Headless.XUnit | 12.1.0 | 12.1.2 |
| Core/UI Plugin SDK | 3.4.0 | 实验保持 3.4.0；正式重新发布包须使用新包版本 |
| Semi / Ursa | 12.1.0 / 2.1.0 | 实验保持原版本 |

最新稳定版本已核对 [Avalonia NuGet](https://www.nuget.org/packages/Avalonia/12.1.2) 和 [Dock NuGet](https://www.nuget.org/packages/Dock.Avalonia/12.1.0.6)。后者要求 Avalonia >= 12.1.1，因此本次选择 12.1.2 满足要求。

## 1. 插件依托 Host 的判断成立到什么程度

当前代码明确实行统一 UI 运行时：

- `PluginLoadContext.Load` 优先走 Host 共享程序集策略。
- `HostContractAssemblyPolicy` 从 SDK、Dock、Fluent、Semi、Ursa 等根构造共享依赖闭包，返回默认加载上下文中的程序集。
- `HasCompatibleIdentity` 检查名称、区域、公钥，并接受“Host 程序集版本 >= 插件请求版本”。这只是加载身份规则，不校验 API 和行为兼容。
- 插件构建/打包规则禁止携带 Avalonia、Dock、SDK 等共享 DLL。
- Core SDK 没有 Avalonia / Dock 依赖；UI SDK 有 Avalonia 类型及精确包依赖，但没有 Dock 依赖。
- 扫描 11 个外部插件当前生产源码，没有发现直接使用 Dock 的代码；Dock 引用主要出现在 Host 集成测试及验收 Harness 中。
- 现有部署的 12 个插件都声明 SDK 区间 `[3.4.0, 4.0.0)`。

因此，插件不自带框架 DLL，可以在新 Host 中复用新框架。旧 DLL 内的 Avalonia 类型、方法、编译 XAML 和资源引用依然存在：Host 提供 DLL，并不会把旧插件重新编译一次。运行时修复可随 Host 生效，编译器或 XAML 生成代码的修复通常需要重编译插件才会生效。

相关源码：

- [共享程序集策略](../../../Host/MyAvaloniaManagement/Business/Plugins/Discovery/PluginSharedAssemblyPolicy.cs)
- [插件加载上下文](../../../Host/MyAvaloniaManagement/Business/Plugins/Discovery/PluginLoadContext.cs)
- [UI SDK 精确依赖](../../../Host/MyAvaloniaManagement.PluginSdk.UI/MyAvaloniaManagement.PluginSdk.UI.csproj)
- [插件打包约束](../../../build/MyAvaloniaManagement.ManagedPlugin.targets)

## 2. 哪些项目需要跟随升级

| 对象 | 本次建议 | 原因 |
| --- | --- | --- |
| Host 及其随附共享框架 DLL | 升级、重新编译和发布 | 它拥有进程内 UI 运行时 |
| Host 随附的 UI SDK DLL | 随 Host 重编译 | 它编译引用 Avalonia |
| 已安装插件 DLL / ZIP | 优先保持原样 | 本次旧二进制加载检查全部通过；仍需业务和真实 UI 回归 |
| 外部插件仓库的 SDK / Avalonia 包引用 | 不要求同时修改 | 旧编译基线可以继续保留；采用新框架 API 时再迁移 |
| 插件 Standalone 与插件自己的 Headless 测试 | 可以保持旧版，但不代表新 Host 环境 | 独立启动程序拥有自己的运行时，不会使用 Host 的 DLL |
| 对外发布的 SDK NuGet / 新插件模板 | 发布新编译基线时同步更新 | UI SDK 精确锁定 Avalonia；不能用同一包版本发布不同依赖内容 |
| Plugin.Build、Workflow SDK、普通业务依赖 | 没有发现必须联动升级的理由 | 本次没有改构建协议、工作流契约或业务 API |

现有 SDK 中的 `[12.1.0]` 是 NuGet 还原约束，不是插件运行时必须由 Host 提供 12.1.0 的限制。新插件若继续引用旧 UI SDK，又直接要求 Avalonia 12.1.2，会产生依赖约束冲突或警告，应使用新 SDK 包统一编译基线。

如正式发布新的 UI SDK 依赖组合，可采用兼容的 SDK 3.x 新版本（例如经过验证的 3.4.1），按当前仓库规则同步 Core/UI 包版本。仅升级框架不自动构成 SDK 4.0 的理由；若破坏旧插件契约，则需要新的不兼容版本边界。

## 3. 风险集中在哪里

### Dock：中等运行行为风险，优先验证

[Dock 12.1.0.2](https://github.com/wieslawsoltes/Dock/releases/tag/v12.1.0.2) 改动涉及浮动窗口、焦点、拖拽预览、布局恢复、激活跟踪等。[12.1.0.3](https://github.com/wieslawsoltes/Dock/releases/tag/v12.1.0.3) 又调整了控件复用实例的传播。这些路径与 Host 的定制实现直接重叠：

- `App.axaml` 对 Dock 12.0.0.2 浮动窗口设置了原生标题栏等修复措施。
- `DockTabPointerCaptureGuard` 补充标签拖拽的指针捕获。
- `DocumentControlRecycling` 管理视图复用、移出视觉树和最终关闭释放。
- `HostDockFactory`、`DockDocumentLifetime`、布局映射和工具停靠协调器依赖 Dock 行为。

升级后应逐项确认旧修复措施是否仍然需要，不能因为上游更新就直接删除。重点检查标签排序、拖出/停靠、跨窗口拖放、焦点、最后一个页签关闭、工具隐藏/恢复、布局重启恢复及关闭后资源释放。

布局使用 Host 自有 V2 快照，不直接以 Dock 对象序列化作为磁盘协议。本次不需要仅因 Dock 包版本变化就提升布局 schema，但仍需验证映射和恢复行为。

### Avalonia：补丁升级，源码风险较低，UI 行为仍需确认

[12.1.1](https://github.com/AvaloniaUI/Avalonia/releases/tag/12.1.1) 和 [12.1.2](https://github.com/AvaloniaUI/Avalonia/releases/tag/12.1.2) 包含绑定、焦点、输入捕获、文本、虚拟化和弹窗修复。对本项目应检查中文输入、命令绑定、列表滚动、深浅主题、弹窗与快捷键。

这次不是 11 -> 12 的跨主版本迁移；该结论不能推广到未来的 Avalonia 主版本升级。

### 原生视频与绘图插件：重点人工/集成回归

视频插件涉及 LibVLC、原生窗口句柄和 Dock 视图搬移。加载检查不能证明播放、全屏、停靠后画面、音频和资源释放正常。图像/分形插件应检查实际绘制与缩放；表单类插件应检查主题、绑定和交互。

临时还原得到的 Host 依赖图中，Semi、Ursa、WebView、Xaml.Behaviors、SkiaSharp 和 HarfBuzzSharp 版本均保持原值。它们没有阻断本次构建和已执行的测试；这不代替对所有功能路径的兼容确认。WebView 等独立产品包不应只因名称以 Avalonia 开头就机械改成相同版本。

## 4. 已执行验证

从干净源码提交导出独立临时副本，仅在副本修改 Avalonia、Dock 和 Headless.XUnit 版本。正式仓库框架版本、部署程序及已安装插件均未修改。

| 验证 | 结果 | 范围 |
| --- | --- | --- |
| Release Host 构建 | 通过，0 警告、0 错误 | Host 和依赖 SDK 项目，无业务源码适配 |
| Host 单元测试 | 397 / 397 | 现有 `MyAvaloniaManagement.Tests` |
| 现有插件基础测试 | 217 / 217 | 排除需要 Gate 提供真实 ZIP 的 `Category=PackageAcceptance` |
| Headless UI 测试 | 99 / 99 | 现有 `MyAvaloniaManagement.UiTests`，不含真实原生窗口交互 |
| 已安装旧插件二进制探测 | 12 / 12 | 清单检查、真实 PluginLoadContext 加载、GetTypes、入口结构、共享 Avalonia 解析；不构造业务视图或执行业务方法 |

二进制探测使用 `D:\data\avalonia\Controls` 的完整临时副本，包含 11 个外部插件及 MyPlugTest；12 个入口 DLL 的 SHA-256 与安装目录一致，没有重编译或替换这些旧插件。

覆盖插件：百度网盘、BiliDownloader、ClassicGame、DaTang、FractalArt、ImageLab、LayerUnpack、MyPlugTest、NovelGenerate、VideoSecurityPlayer、WorkflowStudio、XianCaiWorker。

最初直接探测外部源码的 Release bin 目录时，9/11 通过，2 项因找不到 Microsoft.Data.Sqlite 或 LibVLCSharp 私有程序集而失败。改用完整安装产物的副本后 12/12 通过。首轮原始失败日志保留，不能把不完整构建目录等同于正式插件发布包。

证据目录：artifacts/upgrade-assessment-20260916（历史产物位置，文件已清理：`artifacts/upgrade-assessment-20260916/summary.json`），含构建日志、TRX、旧 DLL 哈希与一次性探测源码。一次性探测未加入正式测试项目。

## 5. 实施建议与放行条件

1. 正式修改 `Directory.Version.props` 中的两个框架版本，统一 Dock 家族；同步 `Directory.Packages.props` 的 Headless.XUnit，并更新对应锁文件。
2. Host 与随附 SDK 重编译。保留现有插件产物，先完成新 Host + 旧插件的真实集成回归。
3. 优先运行视频播放/全屏/浮动停靠、标签跨窗口拖拽、布局重启恢复、多个文档反复打开关闭及资源释放测试；再完成各插件主要业务操作。
4. 发布采用完整 Host 产物替换，保留 Controls；升级前保存可回退的 Host 版本和布局数据。正式部署步骤另行执行。
5. 新 SDK NuGet / 模板作为新的编译基线发布，既有插件仓库可按需迁移，不必为本次 Host 升级统一提升插件版本。

**评估结论：本次 Host 升级可行，已验证旧插件加载兼容；无需预先要求全部插件升级或重编译。真实交互及业务回归通过后，才能将其作为完整运行兼容的发布承诺。**
