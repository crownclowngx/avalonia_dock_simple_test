# MyAvaloniaManagement

MyAvaloniaManagement 是一个基于 **.NET 10、Avalonia 12 和 Dock 12** 的模块化桌面工作台，也是面向内部可信 Managed Plugin 的插件宿主。宿主提供统一窗口、可停靠工作区、布局持久化、文件打开与保存、依赖注入、插件生命周期和诊断入口；业务能力由独立插件贡献。

> 当前定位是同一团队维护的内部可信插件平台，主要支持 Windows x64。它不是第三方插件市场或安全沙箱，也不支持运行时热卸载；更新插件需要退出宿主、替换文件并重新启动。

> Managed Plugin v1 已完成 G0–G16 封板，源码基线由本地注解标签
> `managed-plugin-v1.0.0` 定位。签署内容、非发布门禁证据和回退边界见
> [G16 文档与 v1 基线](./docs/plan-history/host-v1/g16-documentation-and-v1-baseline.md)。

> Managed Plugin V2 已完成 G0–G7；G8–G14 尚未实现。最终 Core/UI SDK、严格 manifest v2、
> 每插件独立 Provider、声明式贡献目录、Host internal Dock Adapter 与 Document V2 已进入生产路径。
> 四个业务插件仍保留 Legacy 源码并由 Host 拒绝加载；layout/lifecycle v2 及业务插件迁移尚未完成。
> 见 [V2 G7 专项记录](./docs/plan-history/host-v2/g7-document-v2.md)。

## 核心扩展模型

| 概念 | 语义 | 典型用途 |
| --- | --- | --- |
| Document | 中央工作区中的多实例工作会话，每个标签拥有独立状态和 DI Scope | 下载方案、视频播放与加解密、发票导入、银行余额调节 |
| Tool | 宿主级单例面板，可以停靠、隐藏和恢复 | 文件树、工具管理、插件状态、下载任务中心 |
| 插件服务 | 不依赖页面可见性的业务能力，由当前插件私有 Provider 和可选生命周期管理 | 仓储、下载协调、凭据和媒体运行时 |

宿主当前具备以下基础能力：

- Left、Right、Top、Bottom 四向 Dock 布局，以及经过校验和迁移的布局持久化；
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

四个当前插件均使用 Managed Plugin 接入。G4 已删除 Legacy 二进制激活路径；无模块程序集、
缺少入口 `.deps.json` 的目录以及依赖历史加载 Facade 的代码不会进入插件运行链。

## V2 G7 当前 SDK、manifest、容器、Dock 与 Document 边界

历史 v1 正式支持 Windows x64 上同一进程内的可信 Managed Plugin。当前 G1 仍沿用这一运行模型：插件必须携带严格清单并位于
独立目录；更新时退出宿主、替换插件文件后重新启动。不支持运行时热卸载、恶意代码沙箱、
权限系统、第三方市场、跨进程 UI 或用户动态启停插件。

版本按所有者独立演进：产品版本、Plugin SDK 版本、每插件版本、manifest schema、每种宿主
持久化 schema 和插件内容 schema 不能互相代替。当前产品与 Plugin SDK 均为 `2.0.0`，Host 与
SDK 程序集版本均为 `2.0.0.0`；V2 已删除独立 Host API 版本事实。统一事实定义在
[`Directory.Version.props`](./Directory.Version.props)。普通进程内强类型消息不增加无迁移行为的
版本字段，发生破坏性语义变化时创建新消息类型或提升 SDK 主版本。

四个当前插件的 `PluginVersion` 均为 `2.0.0`。严格 manifest v2 只表达一个 SDK 左闭右开区间
`[2.0.0, 3.0.0)`，并以 `entryPoint.assembly` 与 `entryPoint.type` 精确指定入口；Host 不读取 v1，
也不会扫描或执行未声明的第二个模块。

G2 已建立真实的 `MyAvaloniaManagement.PluginSdk.dll` 与 `MyAvaloniaManagement.PluginSdk.UI.dll`。
Core 只依赖 .NET BCL，UI 只承载 Avalonia、插件注册与视图贡献契约；两者分别维护空 Shipped 和完整
Unshipped 的 v2 API 基线。旧 `MyAvaloniaManagementCommon.dll` 只由仓库内部
`MyAvaloniaManagement.LegacyPluginContracts` 项目生成，不能打包、不能新增生产消费者，并将在 G9–G12
迁移后删除。

G4 已把宿主与插件对象图彻底分开：Host Provider 先构建，每个清单入口从新的空
`ServiceCollection` 建立私有 Provider。插件配置、开放泛型、keyed 与多实现注册都只影响自身；配置或
Provider 构建失败只隔离当前插件。宿主退出时先关闭各插件 Document Scope，再停止生命周期，随后按
PluginId 反序释放插件 Provider，最后释放 Host Provider。当前 Bili Tool 对 Legacy 生命周期 Manager
仍通过一个精确阶段桥读取，按计划在 G12 由插件内部 readiness 替换；不存在任意父 Provider 回退。

G5 把 Host 生产模块入口切换到最终 UI SDK，并以 `PluginRegistration`、插件局部 Builder、不可变
`PluginRegistry` 和 internal Activator 形成唯一贡献路径。Document 自动为 scoped，Tool/Lifecycle 为
插件 singleton；Descriptor、模型和 View 在一次声明中冻结。插件内错误丢弃整个候选；跨插件 ID 或精确
模型映射冲突排除全部冲突插件，Host 冲突保留 Host，未冲突插件继续发布。Registry 不保存 Provider，
Welcome 与四个 Host Tool 也从相同目录产生。四业务插件留到 G9–G12，因此当前不会被 V2 Host 加载。

G6 进一步把 Welcome 与四个 Host Tool 变为普通模型。只有 internal sealed
`ManagedDocumentDockable`/`ManagedToolDockable` 继承 Dock 类型；View 在发布前由 Registry 精确工厂
预构建一次。Document Adapter 拥有模型、View 和独立 Scope，Tool Adapter 只拥有 View，Tool singleton
仍由插件 Provider 释放。单个 Tool 创建失败只隔离自身，Welcome 失败中止布局；所有 Adapter 禁止浮动。
G7 在该 Adapter 基线上建立唯一异步 Document 链：Registry 核对后在独立 Scope 中调用
`InitializeAsync`，成功后才构造 Adapter、预构建 View 并发布。新建、Creation Intent 与恢复统一使用
`DocumentActivationContext`；保存与关闭统一读取 Host 状态，不接受 Legacy Strategy 或字符串快照。

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
- [Managed 插件快速开始（历史示例）](./docs/quick-start/README.md)：G7 已具备最终 Document 契约，但完整业务插件教程仍需等待 G9 迁移首个真实插件；
- [宿主—插件架构评审](./docs/design/host-plugin-architecture-review.md)：理解当前架构、成熟度和边界；
- [Plugin SDK API 兼容基线维护指南](./docs/reference/plugin-sdk-api-compatibility.md)：新增或修改 SDK public API 前阅读；
- [Managed Plugin V2 任务书](./docs/design/host-v2-breaking-refactor-plan.md)：查看 G0–G7 已完成、G8–G14 尚未实现的破坏式重构路线；
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

该门禁不启动窗口、不执行发布，也不替代 G14 的历史发布门禁。

修改 Document 创建、持久化、关闭或 Scope 所有权链时，运行 G7 非发布专项：

```powershell
.\scripts\Test-DocumentV2.ps1 -Configuration Release -NoRestore
```

该脚本只执行 Unit、Plugin 与 Headless UI 专项测试，并固定记录未运行 Windows CI、Windows Smoke
及发布门禁；结果写入 `artifacts/test-results/DocumentV2/summary.json`。

在干净 Git 提交上执行完整的 G14 Windows 本地发布门禁：

```powershell
.\scripts\Invoke-HostV1ReleaseGate.ps1
```

该入口在两个独立克隆中重复执行锁定还原、Release 零警告构建、G15 诊断脱敏扫描、宿主三套测试、SDK 包/API、
四插件包矩阵和真实窗口 Smoke，并把日志、TRX、覆盖率、ZIP、清单及两轮比较写入
`artifacts/release-gate`。它不绑定代码托管平台，也不会创建或推送标签。

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

维护 Plugin SDK public API 时，必须额外运行可读基线和成员级变异门禁：

```powershell
.\scripts\Test-PluginSdkCompatibility.ps1 -Baseline v2 -Configuration Release
```

每个插件分别生成 `<AssemblyName>-<PluginVersion>-win-x64.zip`，不会生成四插件合集。

插件还包含各自的单元测试、集成 Harness 或发布验收项目；其专用前置条件和命令以插件目录中的当前文档为准。

## 当前边界

- 插件是进程内可信代码，不提供权限隔离或恶意代码防护；
- 插件加载上下文用于依赖解析隔离，不等同于安全沙箱；
- 插件目录快照和加载上下文以进程为边界，不支持热更新或运行时卸载；
- 仓库能生成正式 Plugin SDK/NuGet 制品，但不自动推送公共包源；当前没有插件市场或通用脚手架；
- G3 已完成：宿主只接受严格 manifest v2、入口 `.deps.json` 和清单精确声明的入口类型；
- G5 已完成：宿主与每个插件拥有独立 Provider，Host 生产贡献只通过最终 UI SDK 声明并发布到唯一 Registry；
- 兼容事实只有一个 Core/UI 共用的 SDK 区间；不得重新引入 Host/Common 双区间或独立 Host API 版本事实；
- Core/UI 包、manifest v2、独立容器、Host 声明式目录和 Document v2 已进入生产路径；四业务插件仍是
  Legacy 源码且不会由 G7 Host 加载，不能据此宣称 layout/lifecycle v2 或完整业务插件迁移完成。

上述边界的详细规则以[架构评审](./docs/design/host-plugin-architecture-review.md)和[兼容约束](./Host/MyAvaloniaManagement/docs/reference/compatibility-contracts.md)为准。
