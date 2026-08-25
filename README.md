# MyAvaloniaManagement

MyAvaloniaManagement 是一个基于 **.NET 10、Avalonia 12 和 Dock 12** 的模块化桌面工作台，也是面向内部可信 Managed Plugin 的插件宿主。宿主提供统一窗口、可停靠工作区、布局持久化、文件打开与保存、依赖注入、插件生命周期和诊断入口；业务能力由独立插件贡献。

> 当前定位是同一团队维护的内部可信插件平台，主要支持 Windows x64。它不是第三方插件市场或安全沙箱，也不支持运行时热卸载；更新插件需要退出宿主、替换文件并重新启动。

> Managed Plugin v1 已完成 G0–G16 封板，源码基线由本地注解标签
> `managed-plugin-v1.0.0` 定位。签署内容、非发布门禁证据和回退边界见
> [G16 文档与 v1 基线](./docs/plan-history/host-v1/g16-documentation-and-v1-baseline.md)。

> Managed Plugin V2 已完成 G0–G14 并正式封板。最终 Core/UI SDK、严格 manifest v2、
> 每插件独立 Provider、声明式贡献目录、Host internal Dock Adapter、Document V2、Layout V2 与
> Host internal 生命周期已进入生产路径，四个业务插件均为真实 V2 插件；G13 已删除全部 V1
> 生产面，G14 已冻结 2.0.0 API、建立两轮隔离发布门禁并完成文档签署。见
> [V2 G14 封板记录](./docs/plan-history/host-v2/g14-v2-sealing.md)。

> Managed Plugin V3 已完成 G0–G14 并正式封板：产品、Core/UI SDK 与四插件版本为 `3.0.0`；Document 保存使用
> 修订快照和指定修订确认，激活输入使用互斥的 New/Restore 类型，插件注册改为 Host 最终提交端口与
> 贡献生命周期并强制 ID 归属；事件通信由 MyPlugTest、BiliDownloader 各自的插件 Provider 私有持有；
> Workspace/Dock 已拆分，Host Catalog 与只含真实插件的 Plugin Registry 已分离；UI SDK 全屏契约已
> 收口为 `TryPresent(Control)` 返回幂等 `IDisposable` 租约，Host 由具体会话维护唯一活动租约；
> 四个插件均已通过最终 Workspace、Host 保存竞争或资源边界、Headless UI 和真实 3.0.0 ZIP 验收。
> manifest、Document envelope、layout 仍为 schema 2，默认数据根仍为 `v2`。实施证据见
> [V3 G10 DaTang 验收](./docs/plan-history/host-v3/g10-datang-accounting-help-v3-acceptance.md)、
> [V3 G11 MySmallTools 验收](./docs/plan-history/host-v3/g11-my-small-tools-v3-acceptance.md)和
> [V3 G12 BiliDownloader 验收](./docs/plan-history/host-v3/g12-bili-downloader-v3-acceptance.md)以及
> [V3 G13 删除 V2 生产面](./docs/plan-history/host-v3/g13-remove-v2-production-surface.md)和
> [V3 G14 封板记录](./docs/plan-history/host-v3/g14-v3-sealing.md)。V3 Core/UI API 已冻结为 Shipped
> 127/45 条，并由两轮隔离门禁签署；这表示本地可发布，不表示已经上传或对外发布。

> Host V4 已完成 G0–G8 并封板：已冻结 V3 源码输入，删除 Host 死面与 Hosting 依赖，收口强类型身份、
> Layout 职责和 Document 控件回收所有权，按领域消除 `Business/Helpers`，并修复驱动器根、UNC 根与
> UNC 子目录语义。G7 又完成四插件真实包、SDK、诊断、文档和 MySmallTools 20 轮资源回归；Host 为
> 478/478，覆盖率 85.06% / 71.41%，两轮隔离门禁与 Windows Smoke 已建立本地发布资格；未上传、
> 未创建 tag、未对外发布且未使用 AIFLOW。详情见
> [V4 任务书](./docs/design/host-v4-breaking-refactor-plan.md)和
> [G7 四插件、Harness 与文档回归](./docs/plan-history/host-v4/g7-four-plugins-harness-documentation-regression.md)，
> 最终签署见 [G8 V4 封板记录](./docs/plan-history/host-v4/g8-v4-sealing.md)。

> Workflow Action G0 已重新签署，G1 Host 内核已完成：产品保持 `3.0.0`，仓库内 Core/UI SDK 候选为
> `3.1.0`；新增 caller-bound Gateway/Run、不可变目录、Schema、授权、invocation scope、资源治理、
> 脱敏诊断和关闭门控。v3 Shipped 仍为 Core 127/UI 45，新增 72/6 条只进入 Unshipped。G1 是非发布
> 开发阶段，没有修改模板或创建 Workflow Studio。见
> [G1 专用记录](./docs/plan-history/workflow-action/g1-host-workflow-action-kernel.md)。

## 核心扩展模型

| 概念 | 语义 | 典型用途 |
| --- | --- | --- |
| Document | 中央工作区中的多实例工作会话，每个标签拥有独立状态和 DI Scope | 下载方案、视频播放与加解密、发票导入、银行余额调节 |
| Tool | 宿主级单例面板，可以停靠、隐藏和恢复 | 文件树、工具管理、插件状态、下载任务中心 |
| 插件服务 | 不依赖页面可见性的业务能力，由当前插件私有 Provider 和可选生命周期管理 | 仓储、下载协调、凭据和媒体运行时 |

宿主当前具备以下基础能力：

- Left、Right、Top、Bottom 四向 Dock 布局，以及严格、无迁移的 `layout-v2.json` 持久化；
- Document 多开、独立 Scope、关闭取消和资源释放；
- Tool 单例创建、关闭隐藏和状态恢复；
- Managed Plugin 服务注册、可选初始化与反向关闭生命周期；
- 严格 `plugin.manifest.json`、插件目录隔离和私有依赖解析；
- 严格 Document 信封 v2、原生 JSON 内容、文件原子保存与恢复备份、插件状态面板和默认脱敏的会话诊断日志。

## 现有插件

| 插件 | 当前职责 |
| --- | --- |
| [BiliDownloader](./Plugins/BiliDownloader/BiliDownloader/doc/reference/PRODUCT.md) | Bilibili 链接和个人内容来源、下载计划、任务调度及媒体处理 |
| [MySmallTools](./Plugins/MySmallTools/MySmallTools/docs/secret-video-player/README.md) | SECVID03 视频播放、媒体库、视频加密和安全解密 |
| [DaTangAccountingHelpPlug](./Plugins/DaTangAccountingHelpPlug/DaTangAccountingHelpPlug/DaTangAccountingHelpPlug.csproj) | 发票信息综合计算和银行余额调节 |
| [MyPlugTest](./Plugins/MyPlugTest/MyPlugTest/MyPlugTest.csproj) | Managed Plugin 的 Document、Tool、消息通信和依赖注入示例 |

四个当前插件均已按 V3 G1 重新标记版本并继续使用 Managed Plugin 构建协议；三个持久化插件已在
V3 G2 接入修订保存，全部 11 个插件 Document 已在 V3 G3 接入互斥激活，四插件已在 V3 G4 通过
Host 最终提交与 ID 归属门禁，并在 V3 G5 把插件事件通信收回插件内部；四插件已在 V3 G9–G12
依次完成最终 Workspace、UI、资源边界与真实包验收。BiliDownloader
精确声明 1 个可持久化 Document、1 个右侧可隐藏 Tool 与 1 个 Lifecycle；Legacy 项目、旧入口探针与
Host/Common 双区间已经删除。缺少入口 `.deps.json` 或依赖历史加载 Facade 的代码不会
进入运行链。

## V3 G12 四插件验收与既有 V3 语义

历史 v1 正式支持 Windows x64 上同一进程内的可信 Managed Plugin。V2 沿用这一运行模型：插件必须携带严格清单并位于
独立目录；更新时退出宿主、替换插件文件后重新启动。不支持运行时热卸载、恶意代码沙箱、
权限系统、第三方市场、跨进程 UI 或用户动态启停插件。

版本按所有者独立演进：产品版本、Plugin SDK 版本、每插件版本、manifest schema、每种宿主
持久化 schema 和插件内容 schema 不能互相代替。当前产品为 `3.0.0`，仓库内 Plugin SDK 候选为
`3.1.0`；Host 程序集为 `3.0.0.0`，SDK 程序集为 `3.1.0.0`。V3 不重新引入独立 Host API 版本事实。统一事实定义在
[`Directory.Version.props`](./Directory.Version.props)。普通进程内强类型消息不增加无迁移行为的
版本字段，发生破坏性语义变化时创建新消息类型或提升 SDK 主版本。

四个当前插件的 `PluginVersion` 均为 `3.0.0`。仓库内构建生成的严格 manifest schema 2 表达 SDK 区间
`[3.1.0, 4.0.0)`，并以 `entryPoint.assembly` 与 `entryPoint.type` 精确指定入口；固定历史树构建的真实
`[3.0.0, 4.0.0)` 插件 ZIP 仍可由 3.1 Host 加载。Host 不读取 v1，
也不会扫描或执行未声明的第二个模块。

V2 G2 已建立真实的 `MyAvaloniaManagement.PluginSdk.dll` 与 `MyAvaloniaManagement.PluginSdk.UI.dll`。
Core 只依赖 .NET BCL，UI 只承载 Avalonia、插件注册与视图贡献契约。G14 已将 v3 Shipped 固定为 Core
127 条、UI 45 条 public 签名；Workflow Action G1 的兼容新增进入 v3 Unshipped 72/6。对应的历史签名
继续保存在 v2 Shipped。旧 `MyAvaloniaManagementCommon.dll` 与 Legacy 项目已在 V2 G13 整体删除；
历史 v1 API 文本仅用于审计，不参与编译、加载或打包。

G4 已把宿主与插件对象图彻底分开：Host Provider 先构建，每个清单入口从新的空
`ServiceCollection` 建立私有 Provider。插件配置、开放泛型、keyed 与多实现注册都只影响自身；配置或
Provider 构建失败只隔离当前插件。不存在任意父 Provider 回退。

V2 G5 把插件生产模块入口切换到最终 UI SDK，并以 `PluginRegistration`、插件局部 Builder、不可变
`PluginRegistry` 和 internal Activator 形成插件贡献路径。V3 G7 已从该 Registry 完整移出 Welcome 与
四个 Host Tool：Registry 只含 manifest 插件，跨插件冲突隔离全部冲突插件；Host 内建项由不可变
`HostWorkspaceCatalog` 声明。`WorkspaceCatalog` 只读合并两类 Descriptor、菜单和精确 View 映射，
自身不保存或解析任何 Provider。

V3 G4 在这条独立 Provider 路径上进一步收紧提交所有权：模块进入 `Configure` 时看到真正空的
`ServiceCollection`，只能修改自己的私有描述符；Document/Tool/Lifecycle 根先作为 Host 拥有的冻结事实
暂存。模块返回后先 Seal 注册窗口，强制 Document/Tool ID 分别属于
`{PluginId}.document.*`/`{PluginId}.tool.*`，再由 Host 最后追加窗口交互、
`IDocumentLifetime`、Document Scope 基础设施及固定生命周期贡献根。普通和 keyed 影子注册都会在
Provider 构建前隔离当前插件。V3 G5 已从 SDK 与 Host 删除通用事件总线；MyPlugTest 和
BiliDownloader 分别在自身插件 Provider 注册私有 singleton 消息器，消息不能跨插件或 Runtime 解析。

V3 G6 已删除万能型 `ManagementFactory` 和 `DocumentWorkspace`，由唯一 `WorkspaceSession` 拥有 Root、
Document、Tool 及退出释放；`HostDockFactory` 只适配 Dock override、Locator 和禁浮动协议，二者通过一次性
internal 回调接缝绑定。Tool 管理使用无 Dock 类型的 `ToolWorkspaceReadModel`/`ToolWorkspaceState`，主窗口
和 Tool ViewModel 不再依赖 Factory 或 Root Dock。V3 G7 又删除了生产与 Harness 中剩余的 `Plug`
Locator；现在只保留规范 Documents 与 Tool ID。

Welcome 与四个 Host Tool 仍是普通模型。只有 internal sealed
`ManagedDocumentDockable`/`ManagedToolDockable` 继承 Dock 类型；View 在发布前由 Registry 精确工厂
预构建一次。Document Adapter 拥有模型、View 和独立 Scope，Tool Adapter 只拥有 View，Tool singleton
仍由插件 Provider 释放。单个 Tool 创建失败只隔离自身，Welcome 失败中止布局；所有 Adapter 禁止浮动。
G7 在该 Adapter 基线上建立两个明确激活边界：Host Welcome 由精确 Host 工厂同步创建，插件 Document
仍由所属插件 Provider 在独立 Scope 中调用 `InitializeAsync`；两者成功后才构造 Adapter、预构建 View
并发布。V3 G3 把旧的可空组合上下文破坏式
替换为 `NewDocumentActivation` 与 `RestoreDocumentActivation`；保存与关闭统一读取 Host 状态，
不接受 Legacy Strategy、旧激活重载或字符串快照。

V3 G8 将全屏端口收口为 `IDisposable? TryPresent(Control content)`。`MainWindow` 只委托给具体的
`WindowContentFullscreenSession`；会话维护覆盖层、ContentHost、宿主有效性和唯一租约。MySmallTools
只持有租约，退出、Esc、失败、卸载、Dispose 或 Document 关闭时释放，不再保存 owner 或调用
`TryRestore`。20 轮真实媒体在全屏中直接关闭 Document 后，播放器、HWND/vout、流、缓存、Dispatcher、
Reaper 与关闭对象弱引用全部归零。

G8 把生命周期编排收回 Host internal：Registry 只保存声明；Coordinator 按规范 PluginId 正序初始化，
失败/30 秒超时只隔离当前插件，只有成功项可用并在退出时按实际成功顺序反向停止，单项关闭期限为
10 秒。菜单、直接 Activator、Document/Tool 创建和布局恢复都读取同一只读可用性投影。退出先禁止
新建并释放 Adapter/View 与 Document Scope，再清除停止工作的 UI 同步上下文、反向停止生命周期、
反向释放插件 Provider，最后释放 Host Provider。

G9 将 MyPlugTest 的 4 个 Document、1 个 Tool 与全部 View 迁移到最终声明式贡献。Document 是普通
scoped 模型，Tool 是插件级 singleton；Welcome 使用严格 content schema 1，事件订阅令牌随 Document
Scope 释放。两次隔离测试 ZIP 的 11 文件事实与归档摘要一致，解压后形成 4 Document + 1 Tool Registry。

G10 将 DaTang 的发票导入和银行余额调节迁移为 2 个声明式 Document。UI SDK 新增窄
`IPluginWindowInteraction` Host Port，只返回本地路径和操作结果；文件、报告与剪贴板操作均
联合观察命令令牌和 Document 关闭令牌。银行 Document 使用严格 content schema 1 和“完整验证后
一次提交”恢复语义。专项 151/151 及两次 9 文件测试 ZIP 已通过真实 Loader 验证。

G11 将 MySmallTools 的播放器、媒体库和加解密能力迁移为 4 个非持久化 Document；关闭令牌贯穿
异步与原生资源，最终全屏 Host Port 位于 UI SDK，真实 G3 Harness 证明重复关闭后资源计数归零。

G12 将 BiliDownloader 迁移为 1 Document + 1 Tool + 1 Lifecycle。Document 使用 schema 3 原生
`JsonElement` 并在完整验证后原子应用；插件内 readiness 隔离 Tool 与 Host 生命周期实现，未 Ready
时拒绝设置、SQLite 与 FFmpeg 工作。专项 812/812、覆盖率门禁及两次 14 文件确定性测试 ZIP 均通过；
本阶段没有运行 AIFLOW、Windows CI/Smoke、ReleaseAcceptance 或发布门禁。

布局只读写 `layout-v2.json`/schema 2，严格字段为根 `schemaVersion/panes/tools/activeToolId`、Pane
`id/proportion`、Tool `id/dockId/order/isVisible/isPinned`。V1、浮动字段、历史 ID 和 Migrator 不再存在；
未安装或生命周期不可用的插件会使整份 V2 快照隔离并重建默认布局。历史 `layout-v1.json` 不读取也不改变。

宿主默认把布局、外观和诊断写入 `%LOCALAPPDATA%\MyAvaloniaManagement\v2\`。旧 `v1` 与预发布目录
保持原样，不读取、迁移或删除。`MYAVALONIA_DATA_DIRECTORY` 仍表示完整数据根，不追加 `v2`，
以保持自动化和部署隔离语义。

诊断 JSONL 位于上述数据根的 `Diagnostics/session-*.jsonl`。内存记录、插件状态、启动失败摘要、
剪贴板和默认 Trace/stderr 只保留错误码、阶段、经校验身份、版本、异常类型及受控耗时等白名单信息。
原始异常只允许在用户明确确认后，通过进程级
`MYAVALONIA_ENABLE_SENSITIVE_DIAGNOSTICS=1` 临时输出到 Trace/stderr；它永远不进入 UI 或 JSONL。

Document 只使用六字段信封 v2。宿主拥有 schema、插件和 Document 身份、标题、路径与 UTC 时间；
插件只通过 `DocumentContent` 提供内容 schema 和原生 `JsonElement` payload。reader 严格拒绝 V1、
未知/重复/缺失字段、历史 ID、非 UTC、注释、尾逗号和所有权冲突；UTF-8 上限为 8 MiB，JSON 最大
深度为 8。打开失败不会创建、迁移或覆盖文件，恢复副本必须另存且不能覆盖损坏原件或备份。

## 快速运行

### 前置条件

- Windows x64；
- 仓库 [`global.json`](./global.json) 指定的 .NET SDK `10.0.302`，或满足 `latestPatch` 规则的补丁版本。

以下命令构建并启动最小的 **Host + MyPlugTest** 组合。请在仓库根目录执行：

```powershell
dotnet build Host/MyAvaloniaManagement/MyAvaloniaManagement.csproj -c Debug
dotnet build Plugins/MyPlugTest/MyPlugTest/MyPlugTest.csproj -c Debug
dotnet run --project Host/MyAvaloniaManagement/MyAvaloniaManagement.csproj -c Debug --no-build
```

统一 Managed Plugin 构建协议会生成严格清单，并把入口、deps、PDB 和私有运行时依赖部署到 Host
输出目录的独立 `Controls/<PluginFolder>/`。需要体验其他插件时，先构建对应插件项目，再使用
`--no-build` 启动 Host；构建只清理当前插件目录。

创建新插件、编写严格清单及处理依赖部署时，请直接阅读 [Managed 插件快速开始](./docs/quick-start/README.md)，不要从本节推断完整打包规则。

## 仓库结构

```text
Host/         桌面宿主、公共插件契约及宿主测试
Plugins/      当前业务插件、插件测试和专项验收工具
docs/         解决方案级理论、设计、快速开始、历史与参考文档
scripts/      宿主测试和插件专项验收脚本
TestResults/  需要保留的阶段验收与人工验证记录
```

根 README 只提供项目概览。继续阅读时，从以下入口选择：

- [项目文档导航](./docs/README.md)：按用途浏览全部解决方案级文档；
- [Host V4 G7 四插件、Harness 与文档回归](./docs/plan-history/host-v4/g7-four-plugins-harness-documentation-regression.md)：查看 478 项 Host、四插件真实测试包、20 轮资源归零、SOLID 和非发布边界；
- [Host V4 G8 封板](./docs/plan-history/host-v4/g8-v4-sealing.md)：查看两轮无硬链接隔离、实体 ZIP/manifest 复核、Windows Smoke、SOLID 取舍与本地发布边界；
- [Managed 插件快速开始](./docs/quick-start/README.md)：以当前 V3 G9–G12 四插件验收及 G2–G8 平台语义为事实源；
- [宿主—插件架构评审](./docs/design/host-plugin-architecture-review.md)：理解当前架构、成熟度和边界；
- [Plugin SDK API 兼容基线维护指南](./docs/reference/plugin-sdk-api-compatibility.md)：新增或修改 SDK public API 前阅读；
- [Workflow Action G1 Host 内核](./docs/plan-history/workflow-action/g1-host-workflow-action-kernel.md)：查看 Run、Schema、授权、Scope、关闭门控、测试矩阵与非发布边界；
- [Managed Plugin V3 任务书](./docs/design/host-v3-breaking-refactor-plan.md)：查看 G0–G14 最终目标、阶段和签署矩阵；
- [V3 G14 封板记录](./docs/plan-history/host-v3/g14-v3-sealing.md)：查看正式 API、SOLID、两轮隔离门禁、制品和回滚边界；
- [V3 G13 删除 V2 生产面](./docs/plan-history/host-v3/g13-remove-v2-production-surface.md)：查看零残留、真实包负例、四插件矩阵和非发布证据；
- [V3 G12 BiliDownloader 验收](./docs/plan-history/host-v3/g12-bili-downloader-v3-acceptance.md)：查看保存竞争、私有消息、Lifecycle/readiness、真实 Host 组合及 1219 项非发布证据；
- [V3 G11 MySmallTools 验收](./docs/plan-history/host-v3/g11-my-small-tools-v3-acceptance.md)：查看全屏租约、20 轮真实媒体资源归零、真实 Host 组合及 676 项非发布证据；
- [V3 G10 DaTang 验收](./docs/plan-history/host-v3/g10-datang-accounting-help-v3-acceptance.md)：查看双 Document、保存竞争、文件交互、真实 Host 组合及 554 项非发布证据；
- [V3 G9 MyPlugTest 验收](./docs/plan-history/host-v3/g9-my-plug-test-v3-acceptance.md)：查看最终 Workspace 创建链、保存竞争、消息释放、UI、SOLID 和 501 项非发布证据；
- [V3 G8 全屏租约与 Host V3 骨架](./docs/plan-history/host-v3/g8-fullscreen-lease-and-host-v3-skeleton.md)：查看租约状态机、原生表面迁移、SOLID 取舍、672 项测试和 20 轮资源证据；
- [V3 G7 Host Catalog 与 Plugin Registry](./docs/plan-history/host-v3/g7-host-catalog-and-plugin-registry.md)：查看目录职责、激活/失败时序、SOLID 取舍、448 项测试和非发布边界；
- [V3 G6 Workspace Session 与 Dock Factory](./docs/plan-history/host-v3/g6-workspace-session-and-dock-factory.md)：查看职责图、所有权、关闭/退出时序、SOLID 取舍、测试实数和整体回滚边界；
- [V3 G5 插件私有消息](./docs/plan-history/host-v3/g5-plugin-private-messaging.md)：查看最终接口、消息拓扑、SOLID 取舍、测试实数和整体回滚边界；
- [V3 G4 插件注册所有权](./docs/plan-history/host-v3/g4-plugin-registration-ownership.md)：查看 Host 最终提交、ID 归属、诊断、测试和回滚边界；
- [V3 G3 互斥 Document 激活](./docs/plan-history/host-v3/g3-exclusive-document-activation.md)：查看最终 API、激活矩阵、测试和回滚边界；
- [V3 G2 修订化 Document 保存](./docs/plan-history/host-v3/g2-revisioned-document-save.md)：查看最终 API、保存/关闭竞争语义、测试和回滚边界；
- [V3 G1 版本与数据边界](./docs/plan-history/host-v3/g1-version-and-data-boundaries.md)：查看版本、API、磁盘兼容和非发布证据；
- [Managed Plugin V2 任务书](./docs/design/host-v2-breaking-refactor-plan.md)：查看历史 G0–G14 破坏式重构与最终签署矩阵；
- [V2 G14 封板](./docs/plan-history/host-v2/g14-v2-sealing.md)：查看 API Shipped 基线、两轮隔离门禁、SOLID 取舍和发布证据；
- [V2 G13 删除 V1 生产面](./docs/plan-history/host-v2/g13-remove-v1-production-surface.md)：查看 SOLID 收口、源码/二进制负例、包矩阵和非发布证据；
- [V2 G12 BiliDownloader 迁移](./docs/plan-history/host-v2/g12-bili-downloader-v2.md)：查看 SOLID 责任划分、readiness、schema 3、关闭时序和非发布证据；
- [V2 G10 DaTang 迁移](./docs/plan-history/host-v2/g10-datang-accounting-help-v2.md)：查看窗口端口、内容 schema、所有权、SOLID 取舍和非发布证据；
- [V2 G0 绿色基线](./docs/plan-history/host-v2/g0-green-baseline.md)：查看非发布门禁、删除面、依赖白名单和消费者矩阵；
- [V2 G1 版本与数据边界](./docs/plan-history/host-v2/g1-version-and-data-boundaries.md)：查看 V2 版本事实、数据根隔离和阶段边界；
- [V2 G2 Plugin SDK 重建](./docs/plan-history/host-v2/g2-plugin-sdk-rebuild.md)：查看 Core/UI 契约、Legacy 隔离、SOLID 取舍和非发布门禁证据；
- [V2 G3 manifest v2 与构建协议](./docs/plan-history/host-v2/g3-manifest-v2-and-build-protocol.md)：查看精确入口、单 SDK 区间、构建探针、确定性包和非发布门禁证据；
- [V2 G4 每插件独立容器](./docs/plan-history/host-v2/g4-per-plugin-containers.md)：查看 Provider 所有权、失败隔离、Document Scope 路由、逆序释放和专项门禁；
- [V2 G5 声明式贡献目录](./docs/plan-history/host-v2/g5-declarative-contribution-catalog.md)：查看注册封闭、两阶段冲突隔离、不可变 Registry、Host 内建贡献和专项门禁；
- [V2 G6 Host Dock Adapter](./docs/plan-history/host-v2/g6-host-dock-adapter.md)：查看普通模型投影、View 原子发布、Scope/View 所有权、失败隔离和非发布门禁；
- [Document V2 当前设计](./docs/design/document-persistence-v2-design.md)：查看六字段线格式、所有权链、保存提交点、恢复与关闭语义；
- [V2 G7 Document V2](./docs/plan-history/host-v2/g7-document-v2.md)：查看 SOLID 取舍、失败矩阵、实际测试证据和非发布边界；
- [主项目兼容约束](./Host/MyAvaloniaManagement/docs/reference/compatibility-contracts.md)：修改 public API、插件契约或稳定 ID 前核对；
- [MyAvaloniaManagement 测试说明](./docs/reference/myavalonia-management-tests.md)：查看测试层次、门禁和结果位置。

## 测试

修改当前文档、脚本路径、版本事实或关键契约名称时先运行：

```powershell
.\scripts\Test-DocumentationCore.ps1
.\scripts\Test-Documentation.ps1
```

该门禁不启动窗口、不执行发布；正式封板证据由下述 V3 G14 入口建立。

修改 Host/SDK 生产边界、插件入口、构建 Target、打包或兼容规则时运行 G13 非发布聚合门禁：

```powershell
.\scripts\Test-HostV3ProductionSurface.ps1 -Configuration Release
```

该入口验证当前 V3 API、源码与二进制零残留、Host/四插件完整测试、覆盖率、真实 NuGet 反例、
两轮确定性测试 ZIP、真实加载、诊断脱敏和文档；不会调用 Windows CI/Smoke 或发布门禁。

修改 Document 创建、持久化、关闭或 Scope 所有权链时，运行 G7 非发布专项：

```powershell
.\scripts\Test-DocumentV2.ps1 -Configuration Release -NoRestore
```

该脚本只执行 Unit、Plugin 与 Headless UI 专项测试，并固定记录未运行 Windows CI、Windows Smoke
及发布门禁；结果写入 `artifacts/test-results/DocumentV2/summary.json`。

当前 Host V4 封板在干净 Git 提交上执行以下 Windows 本地发布门禁：

```powershell
.\scripts\Invoke-HostV4ReleaseGate.ps1
```

该入口在两个无硬链接隔离克隆中复用完整 G7 开发门禁、四插件专项、20 轮资源 Harness、API/包、
文档和真实窗口 Smoke，并复核实体 ZIP/manifest；只建立本地发布资格，不上传、不打标签且固定记录
`aiflow=false`。V3 G14 入口仅保留历史复核。

V2 封板时曾执行以下历史 Windows 本地发布门禁：

```powershell
.\scripts\Invoke-HostV2ReleaseGate.ps1
```

该入口在两个独立克隆中重复执行锁定还原、Release CI 零警告构建、V2 生产面全量门禁、SDK v2 API、
四插件包矩阵和真实窗口 `layout-v2.json` Smoke，并把日志、TRX、覆盖率、ZIP、清单及两轮比较写入
`artifacts/release-gate/v2`。它不使用 AIFLOW，不绑定代码托管平台，也不会创建或推送标签。

运行宿主标准门禁：

```powershell
.\scripts\Invoke-MyAvaloniaManagementTests.ps1 -Configuration Release
```

修改诊断、异常边界、Trace 或 Console 输出时，还必须运行：

```powershell
.\scripts\Test-HostDiagnosticRedaction.ps1
```

需要验证真实 Windows 窗口启动时运行：

```powershell
.\scripts\Invoke-MyAvaloniaManagementTests.ps1 -Configuration Release -WindowsSmoke
```

验证四个插件的独立确定性 ZIP、协议负例和最终包加载：

```powershell
.\scripts\Test-ManagedPluginPackages.ps1 -Configuration Release
```

维护 Plugin SDK public API 时，必须额外运行当前 v3 可读基线和成员级变异门禁：

```powershell
.\scripts\Test-PluginSdkCompatibility.ps1 -Baseline v3 -Configuration Release
```

每个插件分别生成 `<AssemblyName>-<PluginVersion>-win-x64.zip`，不会生成四插件合集。

插件还包含各自的单元测试、集成 Harness 或发布验收项目；其专用前置条件和命令以插件目录中的当前文档为准。

## 当前边界

- 插件是进程内可信代码，不提供权限隔离或恶意代码防护；
- 插件加载上下文用于依赖解析隔离，不等同于安全沙箱；
- 插件目录快照和加载上下文以进程为边界，不支持热更新或运行时卸载；
- 仓库能生成正式 Plugin SDK/NuGet 制品，但不自动推送公共包源；当前没有插件市场或通用脚手架；
- G3 已完成：宿主只接受严格 manifest v2、入口 `.deps.json` 和清单精确声明的入口类型；
- G5/G7 已完成：每个 manifest 插件拥有独立 Provider 并只通过最终 UI SDK 发布到 Plugin Registry；
  Host Welcome/Tool 由独立 Host Catalog 声明；
- 兼容事实只有一个 Core/UI 共用的 SDK 区间；不得重新引入 Host/Common 双区间或独立 Host API 版本事实；
- 当前代码版本线为已封板 V3 G14；Core/UI 包、manifest schema 2、独立容器、Host 独立目录、
  Document envelope v2、Layout v2 和 Host internal 生命周期继续使用既有边界，Document 保存已采用
  修订快照与指定修订确认，Document 激活已采用互斥 New/Restore 类型，插件端口和贡献根已改为 Host
  最终提交并强制 ID 归属，插件消息由对应插件 Provider 私有持有；Workspace Session、Dock Factory 和
  Tool 只读投影以及 Host Catalog / Plugin Registry 已经分离，全屏已使用单参数租约端口；四插件已
  依次通过最终 Workspace、专项资源边界与真实 ZIP 验收。V3 G13 已证明活动生产面只剩最终 V3 语义，
  G14 已冻结 v3 Shipped 127/45 并完成两轮隔离签署；仓库仍不会在无授权时自动上传或对外发布。

上述边界的详细规则以[架构评审](./docs/design/host-plugin-architecture-review.md)和[兼容约束](./Host/MyAvaloniaManagement/docs/reference/compatibility-contracts.md)为准。
