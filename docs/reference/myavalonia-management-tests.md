# MyAvaloniaManagement 测试说明

Document 生命周期回归除 Scope 隔离外，还必须覆盖：确认关闭后 `IDocumentLifetime` 先取消再 Dispose、重复释放幂等、在途 HTTP/Excel/内容浏览停止、迟到 UI 回调被抑制，以及 BiliDownloader 已提交后台任务不随标签关闭而取消。原生文件选择器与已经进入 EPPlus 同步 `SaveAs` 的写入属于显式不可强制中断边界。

> Managed Plugin v1 历史基线由 `managed-plugin-v1.0.0` 定位；当前分支已完成 V2 G14 封板：Core/UI SDK、
> manifest v2、精确入口加载、构建协议、每插件独立容器、声明式贡献目录、Host Dock Adapter、
> Document V2、Layout V2、internal 生命周期、四个业务插件迁移及 V1 生产面删除。最新测试数量和覆盖率必须从本轮
> TRX/Cobertura 动态读取，不以文档数字作为永久门槛。当前两轮 Release 证据见
> [V2 G14 封板](../plan-history/host-v2/g14-v2-sealing.md)；V1 历史门禁见
> [V1 G14 Windows 本地发布门禁](../plan-history/host-v1/g14-windows-release-gate.md)，G15 的脱敏边界见
> [G15 宿主诊断脱敏](../plan-history/host-v1/g15-host-diagnostic-redaction.md)，最终文档签署见
> [G16 文档与 v1 基线](../plan-history/host-v1/g16-documentation-and-v1-baseline.md)。

## 当前文档与非发布基线门禁

修改当前文档、脚本路径、集中版本、SDK 基线或四插件兼容声明时运行：

```powershell
.\scripts\Test-DocumentationCore.ps1
.\scripts\Test-Documentation.ps1
```

核心测试在系统临时目录验证正常和失败夹具；正式入口检查当前文档与 host-v1/host-v2 历史记录的本地链接，
并只对当前事实应用过期措辞规则。它还验证 G14 正向哨兵、V1 历史页首、关键源码类型、V2 Shipped，以及四个
插件项目中的版本、精确入口属性与单一 SDK 投影区间。结果写入
`artifacts/test-results/Documentation/summary.json`。

当前文档门禁不调用 Windows Smoke、发布总门禁或发布验收项目；通过它不能冒充 Windows 发布放行。

### V3 G0 非发布绿色基线

V3 G0 仍验证完整的 V2 生产事实，只新增一个保存竞争特征测试和文档门禁。该测试通过真实
`DocumentSaveService` 把第一次主文件写入暂停在“内容已捕获、磁盘尚未提交”的位置，再提交第二次编辑，
证明磁盘得到旧快照时无参 `AcceptChanges()` 会错误清除新修改的 Dirty。它是 G2 修复前的缺陷证据，
不是期望行为或兼容承诺。

G0 执行 Release 零警告构建、Host/SDK/三个业务测试项目、SDK API/包消费、四插件确定性包矩阵、
诊断脱敏和文档门禁。实际动态数量、覆盖率、包摘要及回滚边界见
[V3 G0 专项记录](../plan-history/host-v3/g0-green-baseline.md)。本阶段固定记录 AIFLOW、Windows CI/Smoke、
ReleaseAcceptance、发布门禁和 `publishable` 均为 `false`，不调用任何发布入口。

### V2 G13 唯一生产面门禁

修改 Host/SDK 依赖、插件入口、统一构建 Target、打包脚本或 Legacy 防回流规则时运行：

```powershell
.\scripts\Test-HostV2ProductionSurface.ps1 -Configuration Release
```

该入口执行 Release `-warnaserror` 构建、Host 覆盖率、SDK 与三个业务测试项目、旧 API 编译负例、
源码和依赖闭包扫描、四插件两轮确定性测试 ZIP、真实 Host 加载、诊断脱敏及文档门禁。MyPlugTest
没有独立业务测试项目，由 Host Plugin/UI 套件覆盖。结果写入
`artifacts/test-results/HostV2ProductionSurface/summary.json`，并固定记录 AIFLOW、Windows CI/Smoke、
ReleaseAcceptance、发布门禁和 `publishable` 均为 `false`。

2026-08-22 实测 Host Unit 168、UI 52、Plugin 202，共 **422/422**；行覆盖率 **83.19%**、
分支覆盖率 **68.81%**。PluginSdk **34/34**，DaTang **62/62**、MySmallTools **184/184**、
BiliDownloader **718/718**。四插件各两次隔离测试包完全一致，具体文件数和 SHA-256 见
[V2 G13 专项记录](../plan-history/host-v2/g13-remove-v1-production-surface.md)。

### V2 G12 BiliDownloader 专项门禁

修改 BiliDownloader V2 入口、Document schema 3、readiness、Lifecycle、Tool 或 JSON 边界时运行：

```powershell
.\scripts\Test-BiliDownloaderV2.ps1 -Configuration Release -NoRestore
```

脚本输出到 `artifacts/test-results/BiliDownloaderV2/`，串行运行 Host Plugin、Headless UI、SDK 边界和
BiliDownloader 完整单元测试，要求 0 失败、0 跳过；随后完成结构扫描、两次隔离 ZIP 构建、逐文件与
归档摘要比较、共享程序集/RID 边界扫描，以及解压后的真实 Loader、Preflight、Registry 和私有
Provider 组合。本次实际为 **812/812**，测试 ZIP 14 个文件，归档 SHA-256 为
`4F73359B0B1AD8E559391EC254BF892794EFF1FED79973D3E2B8F60C12B331D8`。BiliDownloader 覆盖率门禁
为行 **83.77%** / 分支 **67.62%**，现有阈值没有降低。完整设计与失败矩阵见
[V2 G12 专项记录](../plan-history/host-v2/g12-bili-downloader-v2.md)。

该入口固定记录 `aiflow=false`、`windowsCi=false`、`windowsSmoke=false`、
`releaseAcceptance=false`、`releaseGate=false`、`publishable=false`，不调用历史
`Release-BiliDownloaderP0.ps1`，也不访问真实账号、Bilibili 或真实 FFmpeg 媒体。

### V2 G11 MySmallTools 专项门禁

修改 MySmallTools V2 入口、四个 Document、关闭令牌、原生播放器、媒体库、批处理或全屏端口时运行：

```powershell
.\scripts\Test-MySmallToolsV2.ps1 -Configuration Release
```

脚本输出到 `artifacts/test-results/MySmallToolsV2/`，执行 Plugin/Loader/SDK 边界与 Headless UI
定向测试、完整 MySmallTools 单元测试、真实 G3 媒体 Harness、两次确定性打包、禁止程序集扫描和
最终 ZIP 真实加载。本次实际为 **275/275**、真实媒体 20 轮、测试 ZIP 431 个文件；归档 SHA-256 为
`2B2879D28D92A8251A21674D83F5752814AE2A09D0D01EED999061E168D38126`。数量和摘要只是本次证据，
脚本不硬编码它们作为未来门槛。完整设计与资源回零证据见
[V2 G11 专项记录](../plan-history/host-v2/g11-my-small-tools-v2.md)。

该入口明确不使用 AIFLOW，不运行 Windows CI、Windows Smoke、ReleaseAcceptance、发布门禁、签名、
上传或标签；也不调用历史 MySmallTools 产品 G11 的 Accept/Approve 脚本。

### V2 G1 当前绿色基线

2026-08-21 串行执行锁定还原、Release 零警告构建、G1 版本/API/数据根专项、三套 Host 测试、
三个业务插件完整单元测试、V2 API 文本变异和文档门禁。Host 为 Unit 173、UI 38、Plugin 151，
共 **362/362**；行覆盖率 81.12%、分支覆盖率 66.85%。BiliDownloader、DaTang、MySmallTools
共 **967/967**。本轮没有运行 Windows Smoke、Windows CI、发布门禁或发布验收；完整边界见
[V2 G1 专项记录](../plan-history/host-v2/g1-version-and-data-boundaries.md)。

### V2 G2 当前绿色基线

G2 串行执行锁定还原、Release `-warnaserror` 全解决方案构建、32 项 SDK 专用单元测试、Core/UI
API 变异与真实 nupkg 消费门禁、三套 Host 测试、三个业务插件完整单元测试和文档门禁。Host 为
Unit 173、UI 38、Plugin 152，共 **363/363**；行覆盖率 81.12%、分支覆盖率 66.85%。BiliDownloader、
DaTang、MySmallTools 为 720、64、183，共 **967/967**。这些数字只记录本轮实际结果，不是永久阈值。
完整命令证据见 [V2 G2 专项记录](../plan-history/host-v2/g2-plugin-sdk-rebuild.md)。本轮明确不运行 Windows
Smoke、Windows CI、发布总门禁、发布验收、联网/真实媒体、上传、标签或发布操作。

### V2 G3 当前绿色基线

G3 增加严格 manifest v2 reader、精确入口 Loader、单一 SDK 诊断、MSBuild 入口探针与包复核。
专项覆盖根/嵌套字段、版本与区间、入口语法和结构、v1 拒绝、双模块不扫描、构建属性变异，以及
四插件两轮确定性 ZIP 和解压后真实 Host Loader 验证。实际测试数量、覆盖率与命令结果记录在
[V2 G3 专项记录](../plan-history/host-v2/g3-manifest-v2-and-build-protocol.md)，不在这里设置固定数量阈值。
本阶段只运行非发布构建协议门禁，明确排除 Windows Smoke、Windows CI、G14 发布总门禁、
ReleaseAcceptance、联网/真实媒体、上传、标签和任何发布操作。

### V2 G4 当前绿色基线

G4 专项入口为：

```powershell
.\scripts\Test-PluginContainerIsolation.ps1 -Configuration Release
```

它验证宿主注册逐项不变、插件间私有服务不可解析、开放泛型/keyed/多实现、配置与 Provider 构建失败
隔离、四个真实插件分别建容器、每插件 Document Scope 和逆序 Dispose，并扫描已删除的 Policy、
Transaction 与旁路检测符号。结果写入 `artifacts/test-results/PluginContainerIsolation/`。

本轮 Host 三套回归为 Unit 172、UI 39、Plugin 158，共 **369/369**；Host 行覆盖率 **81.58%**、分支
覆盖率 **66.99%**，均未降低既有门槛。完整命令、SOLID 设计、阶段桥和回滚边界见
[V2 G4 专项记录](../plan-history/host-v2/g4-per-plugin-containers.md)。本阶段没有运行 Windows Smoke、
Windows CI、发布门禁、ReleaseAcceptance、联网/真实媒体、上传或发布操作。

### V2 G5 当前绿色基线

G5 专项入口为：

```powershell
.\scripts\Test-DeclarativeContributionCatalog.ps1 -Configuration Release
```

脚本串行执行声明式 Registry 单元、插件两阶段提交和受影响 Headless UI 用例，扫描生产组合路径不得
引用 Strategy、`GetMetadata`、Intent Provider 或独立 `AddView`，并生成明确包含
`windowsCi=false`、`releaseGate=false` 的 `summary.json`。结果位于
`artifacts/test-results/DeclarativeContributionCatalog/`。

测试覆盖 Descriptor 防御性复制与无模型副作用、泛型/生命周期约束、注册与服务集合封闭、Document
scoped、Tool/Lifecycle singleton、插件内错误、跨插件及 Host 冲突整体隔离、失败 Provider/Scope 不泄漏、
Registry 隔离、View 按需创建与脱敏占位，以及 Host Welcome/Tool 统一读取目录。完整门禁数字与回滚边界见
[V2 G5 专项记录](../plan-history/host-v2/g5-declarative-contribution-catalog.md)。本阶段不运行 Windows CI、
Windows Smoke、`Invoke-HostV1ReleaseGate`、ReleaseAcceptance、真实媒体/联网或任何发布操作。

本轮实际结果为：G5 专项 51/51、SDK 32/32、Host Unit 175 + UI 39 + Plugin 160 = **374/374**；
Host 行覆盖率 **81.36%**、分支覆盖率 **66.97%**。既有 baseline 没有降低，并新增 Registry、Builder、
ProviderOwner、Registration 和 Activator 五个 G5 关键文件阈值。BiliDownloader、DaTang、MySmallTools
分别为 720、64、183，共 **967/967**。

### V2 G6 阶段绿色基线

G6 专项入口为：

```powershell
.\scripts\Test-HostDockAdapter.ps1 -Configuration Release
```

脚本串行执行 Adapter Unit、Plugin 与 Headless UI 过滤集，扫描普通模型 Dock 继承、Adapter
`internal sealed` 边界、Activator Dock 转换和 ViewLocator 反射回退，并生成包含
`windowsCi=false`、`windowsSmoke=false`、`releaseGate=false` 的摘要。结果位于
`artifacts/test-results/HostDockAdapter/`。

本轮 G6 专项为 Unit 16、Plugin 35、Headless UI 23，共 **74/74**。Host 全量为 Unit 182、Headless UI
44、Plugin 160，共 **386/386**；行覆盖率 **82.41%**、分支覆盖率 **66.85%**。G6 关键文件行覆盖率为
Adapter Factory **100%**、Document Adapter **95.83%**、Tool Adapter **95.83%**、`ViewLocator` **93.18%**、
`DocumentScopeManager` **91.57%**。SDK 为 **32/32**，
Core/UI API v2 兼容门禁通过；三个业务插件为 720、64、183，共 **967/967**。完整所有权、失败原子性、
回滚和非发布边界见 [V2 G6 专项记录](../plan-history/host-v2/g6-host-dock-adapter.md)。

本阶段未运行 Windows CI、Windows Smoke、ReleaseAcceptance、SDK/业务插件发布包门禁、真实媒体/联网
Harness、上传、标签或发布。

### V2 G7 当前绿色基线

G7 非发布专项入口为：

```powershell
.\scripts\Test-DocumentV2.ps1 -Configuration Release -NoRestore
```

脚本串行执行 Unit、Plugin、Headless UI 过滤集与生产结构扫描，并在
`artifacts/test-results/DocumentV2/summary.json` 固定记录 `windowsCi=false`、
`windowsSmoke=false`、`releaseGate=false`。本轮专项 Unit 59、Plugin 8、UI 16，共 **83/83**。

Host 全量为 Unit 171、UI 44、Plugin 159，共 **374/374**；行覆盖率 **82.22%**、分支覆盖率
**67.22%**。Serializer、Persistence Coordinator、Save Service、Close Coordinator、State Store
行覆盖率分别为 **100%**、**94.51%**、**97.40%**、**97.62%**、**100%**，既有 Adapter 与 Scope
Manager 阈值继续通过。完整所有权、失败矩阵、回滚和非发布边界见
[V2 G7 专项记录](../plan-history/host-v2/g7-document-v2.md)。本阶段不运行 AIFLOW、Windows CI/Smoke、
ReleaseAcceptance、发布包门禁、上传、标签或发布。

其余非发布验收实际通过 locked restore、Release `-warnaserror` 全解决方案构建、SDK **32/32**、
Core/UI API v2 与隔离包消费，以及 BiliDownloader **720/720**、DaTangAccountingHelpPlug **64/64**、
MySmallTools **183/183**。文档核心与完整门禁均通过。

### V2 G8 当前绿色基线

G8 非发布专项入口为：

```powershell
.\scripts\Test-LayoutLifecycleV2.ps1 -Configuration Release -NoRestore
```

它串行执行 Host Unit、Plugin、Headless UI、Plugin SDK 与 BiliDownloader 受影响测试，扫描生产 Layout
V1/Migrator/浮动字段/历史 ID、Legacy public 生命周期编排类型和 Bili Host Manager 依赖。摘要位于
`artifacts/test-results/LayoutLifecycleV2/summary.json`，并明确记录
`aiflow=false`、`windowsCi=false`、`windowsSmoke=false`、`releaseGate=false`。

专项实际为 Host Unit 42、Plugin 65、Headless UI 24、Plugin SDK 5、BiliDownloader 6，共 **142/142**。
Host 全量为 Unit 172、UI 44、Plugin 173，共 **389/389**；行覆盖率 **83.05%**、分支覆盖率 **68.65%**。
测试覆盖严格字段、schema 1/V1 文件拒绝、四向状态往返、未安装/生命周期不可用整体隔离；生命周期
PluginId 正序、成功项反向停止、并发幂等、同步/异步失败、空 Task、超时协作取消、迟到结果、Host 取消、
退出继续清理、停止 UI 上下文和诊断脱敏。关键 Codec/Validator 为 100%，Coordinator/Runner/StateStore
分别为 95.93%/94.05%/98.04%。完整证据见
[G8 专项记录](../plan-history/host-v2/g8-layout-and-lifecycle-v2.md)。本阶段不运行 AIFLOW、Windows CI/Smoke、
ReleaseAcceptance、发布包或发布门禁。

### V2 G9 当前绿色基线

G9 非发布专项入口为：

```powershell
.\scripts\Test-MyPlugTestV2.ps1 -Configuration Release
```

脚本串行执行 MyPlugTest 生产组合与业务测试、受影响 Headless UI、Plugin SDK 边界、静态依赖扫描，
然后建立两份隔离测试 ZIP。两份 ZIP 的排序清单、文件长度、逐文件摘要和归档摘要必须一致；解压后还要
经过真实 `PluginLoadContext`、模块预检、插件 Provider 组合以及 4 Document + 1 Tool Registry 验证。

专项实际为 Plugin 59、Headless UI 14、Plugin SDK 12、最终 ZIP 真实加载 1，共 **86/86**；ZIP 为
11 个文件。摘要位于 `artifacts/test-results/MyPlugTestV2/summary.json`，固定记录
`aiflow=false`、`windowsCi=false`、`windowsSmoke=false`、`releaseAcceptance=false`、
`releaseGate=false`、`publishable=false`。完整贡献、内容 schema、失败矩阵和回滚边界见
[G9 专项记录](../plan-history/host-v2/g9-my-plug-test-v2.md)。

本阶段不运行 AIFLOW、Windows CI、真实窗口 Smoke、ReleaseAcceptance、正式发布门禁、签名、上传或发布；
两份 ZIP 只用于 G9 加载验证。

完整回归为 Host Unit 172、UI 46、Plugin 195，共 **413/413**；行覆盖率 **83.24%**、分支覆盖率
**68.83%**。SDK 单元 **32/32**，三个未迁移业务插件 **966/966**，Core/UI API 与包消费、四插件
两轮非发布包矩阵、locked restore、Release 零警告构建和文档门禁均通过。

### V2 G10 当前绿色基线

G10 非发布专项入口为：

```powershell
.\scripts\Test-DaTangAccountingHelpPlugV2.ps1 -Configuration Release
```

脚本串行执行 DaTang 真实 Host 组合与业务测试、受影响 Headless UI、Plugin SDK 窗口
Host Port 契约、静态依赖扫描，再建立两份隔离测试 ZIP。解压后必须经过真实
`PluginLoadContext`、模块预检、私有 Provider 组合，并形成 2 Document + 0 Tool Registry。

专项实际为 Plugin 60、Headless UI 15、Plugin SDK 13、DaTang 业务 62、最终 ZIP 加载 1，
共 **151/151**；ZIP 为 9 个文件。摘要位于
`artifacts/test-results/DaTangAccountingHelpPlugV2/summary.json`，固定记录 `aiflow=false`、
`windowsCi=false`、`windowsSmoke=false`、`releaseAcceptance=false`、`releaseGate=false`、
`publishable=false`。贡献矩阵、窗口端口、内容 schema、所有权和回滚边界见
[V2 G10 专项记录](../plan-history/host-v2/g10-datang-accounting-help-v2.md)。

完整回归为 Host Unit 172、UI 50、Plugin 205，共 **427/427**；行覆盖率 **83.15%**、
分支覆盖率 **68.74%**，既有总量、分支和关键文件下限均满足。SDK 单元 **33/33**；
BiliDownloader **719/719**、DaTang **62/62**、MySmallTools **183/183**。Core/UI API 与真实
nupkg 消费门禁通过。本轮不运行 AIFLOW、Windows CI、Windows Smoke、ReleaseAcceptance、
正式发布门禁、签名、上传、标签或发布；不得用下文的历史 Host V1 G10 Windows 证据替代本轮事实。

## G14 V2 正式发布门禁

在干净 Git 提交上运行：

```powershell
.\scripts\Invoke-HostV2ReleaseGate.ps1
```

脚本要求 Windows x64、PowerShell 7、Git 和 `global.json` 指定的 .NET SDK。它在两个独立本地克隆中
顺序执行 V2/Core 与文档核心单元测试、锁定还原、Release CI 零警告构建、V2 生产面、SDK v2 API、
四插件包矩阵和真实窗口 `layout-v2.json` Smoke。每轮使用独立 Temp、dotnet home、NuGet 缓存和 Host 数据根，不读取当前工作目录的
构建产物或用户 LocalAppData。

结果位于 `artifacts/release-gate/v2/<UTC>-<commit>/pass-1|pass-2`。日志、TRX、Cobertura、四个 ZIP、
外置清单和 JSON 必须齐全；两轮只忽略时间、耗时和绝对路径，任何测试数、覆盖率、阶段、API、包摘要
或 Smoke 漂移都会失败。当前入口不调用 AIFLOW，不绑定托管平台，也不自动执行合并、上传或标签操作。

## G15 诊断脱敏门禁

修改异常边界、诊断记录、文档错误提示、Trace 或 Console 输出后运行：

```powershell
dotnet test Host/MyAvaloniaManagement.Tests/MyAvaloniaManagement.Tests.csproj `
  -c Release --filter FullyQualifiedName~HostDiagnostics
.\scripts\Test-HostDiagnosticRedaction.ps1
```

第一步用同时含密码、Cookie、Bearer Token、签名 URL、Windows/Unix 绝对路径、请求响应和正文的
canary 异常验证内存、JSONL、默认镜像、生命周期状态、插件状态和启动失败摘要。第二步扫描 Host 与
Common 的生产 C#：默认路径不能读取/格式化异常正文、写自由 `TechnicalDetail` 或向 Console 输出路径；
敏感开关只能位于两个获准的临时输出实现，且草稿不能重新增加自由用户说明。

专项脚本由 V2 生产面门禁执行，并随 `Invoke-HostV2ReleaseGate.ps1` 在两轮隔离环境中复验。
Release 门禁不得设置 `MYAVALONIA_ENABLE_SENSITIVE_DIAGNOSTICS`。

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

## Plugin SDK API 兼容门禁

维护基础 SDK public 类型或成员时运行：

```powershell
.\scripts\Test-PluginSdkCompatibility.ps1 -Baseline v2 -Configuration Release
```

脚本分别验证 Core/UI 活动基线与 SDK 主版本一致、Shipped/Unshipped 文本稳定排序且没有删除标记，
再构建两个真实程序集。随后它在系统 Temp 的完整 SDK 测试副本中执行删除 public 类型/成员、修改参数
名/类型/数量/返回类型和收窄 UI 可见性等负例，并验证兼容新增必须先登记到对应 Unshipped。脚本不会
修改仓库源文件。长期维护规则见
[Plugin SDK API 兼容基线维护指南](./plugin-sdk-api-compatibility.md)。

## Plugin SDK 包门禁

G2 重建的独立包消费门禁：

```powershell
.\scripts\Test-PluginSdkPackage.ps1 -Configuration Release
```

脚本在系统临时目录打包并消费 `MyAvaloniaManagement.PluginSdk` 与
`MyAvaloniaManagement.PluginSdk.UI`，检查真实 DLL/XML、程序集名、nuspec、Core 零依赖和 UI 精确依赖图。
Core 正例覆盖事件、生命周期和普通/可保存 Document；UI 正例覆盖最小模块、私有 DI、Document、
Persistable Document、Tool 和 View。Core 引用 Avalonia/DI/Dock/Newtonsoft，UI 引用 Dock/Newtonsoft，
以及旧 Common 命名空间、Strategy、独立 AddView、生命周期 Manager、字符串快照和 Converter 的夹具
必须编译失败。还原使用临时隔离 NuGet 缓存，不能误命中开发机中的同版本旧包。
临时目录在结束时删除，不读取用户数据根，也不发布到公共 NuGet。

## 覆盖率门槛

门槛定义在
`Host/MyAvaloniaManagement.Tests/coverage-baseline.json`：

- `MyAvaloniaManagement` 手写代码行覆盖率不低于 65%；
- 分支覆盖率不低于 50%；
- `MainWindowViewModel` 行覆盖率不低于 75%；
- 三个宿主 Tool ViewModel 各自行覆盖率不低于 70%。
- `Business/Events/HostEventBus.cs` 行覆盖率不低于 90%。
- `HostDockAdapterFactory`、两个 Managed Adapter 与 `DocumentScopeManager` 行覆盖率不低于 90%；
- `ViewLocator` 行覆盖率不低于 85%。
- Layout V2 严格 JSON Codec 与纯快照 Validator 行覆盖率各不低于 95%。
- 生命周期 Coordinator、单操作 Runner 与 StateStore 行覆盖率各不低于 90%。

`obj`、XAML/C# 生成代码和测试程序集不参与统计。生产 View 和
`App.axaml.cs` 不排除，因为 Headless 测试应保护实际加载、绑定和窗口事件。

## Windows 真实启动冒烟

Windows 冒烟默认关闭，显式运行：

```powershell
.\scripts\Invoke-MyAvaloniaManagementTests.ps1 -WindowsSmoke
```

只运行独立 Smoke 时也可以执行：

```powershell
.\scripts\Invoke-MyAvaloniaManagementWindowsSmoke.ps1 -Configuration Release
```

宿主综合脚本会委托同一个独立 Smoke 脚本。它发布不包含插件的宿主到隔离目录，设置临时
`MYAVALONIA_DATA_DIRECTORY` 和 `MYAVALONIA_SMOKE_TEST=1`。应用仍会创建并
打开真实主窗口；窗口 `Opened` 后由 UI Dispatcher 排队执行正常关闭，让
Closing、布局保存和宿主退出完整执行。主程序必须在 15 秒内以退出码 0
结束。该过程不会读取或覆盖用户 LocalAppData 中的正式布局。

## 测试边界

- 单元层覆盖 DI、ViewModel、文件模型、文档保存、直接协调、插件事件总线和 Tool 行为。
- Headless 层覆盖生产 XAML、主题资源、绑定、DockControl、ViewLocator、
  主窗口事件、内容全屏、14 个插件语义画刷和主题动态切换。
- PluginTests 继续覆盖 Managed-only 拒绝、Dock 布局、Document Scope、插件生命周期、宿主 DI 保护、SDK 依赖边界和 UI 共享程序集。
- 像素截图、真实插件安装包、媒体播放和长时间稳定性不属于本门禁。

## 设计思路与原因

### 文件操作边界

主窗口通过文档持久化协调器使用内部 `IHostStorageService`；文件树保留文件存在检查，并只通过
单方法 `IHostDocumentOpenService` 提交打开意图。接口不会向文件树泄漏主窗口、Dock Factory、
保存流程或 `IStorageFile`，生产实现仍复用同一持久化协调器。

这样设计有三个原因：

1. 单元测试可以使用内存文件和预设的选择结果，不会弹出原生窗口；
2. Avalonia 的窗口生命周期被限制在生产实现中，ViewModel 只编排业务流程；
3. 宿主内部重构通常不改变 Plugin SDK；G11 是正式 v1 API 基线建立前的一次性例外，删除的占位
   契约没有生产实现，四个仓库插件随同一变更重新编译。

### 构造函数与依赖注入

`MainWindowViewModel` 和四个 Host Tool ViewModel 只保留显式依赖构造函数，正式容器及测试
通过该构造函数传入服务。历史静态 `ServiceProvider` 与生产无参构造已经删除；XAML 设计器
改用实现窄绑定接口的纯内存样例，不构造生产对象图。

核心行为只存在一套实现，避免“测试构造路径”和“生产构造路径”逐渐分叉。
生产 ViewModel 注册为瞬态，防止多个窗口或 Headless 测试共享绑定状态；
Dock 工厂、事件总线、布局存储等协调服务保持单例，保证一个 HostRuntime 内只有一份布局和事件事实；
另一个 HostRuntime 会建立自己的根容器与总线，不共享进程全局状态。

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
- 文件读取前检查长度，读取后严格解析唯一六字段 v2；插件只收到内容版本和克隆的原生 JSON payload；
- `DocumentEnvelopeV2Tests` 覆盖精确字段、任意 JSON、8 MiB、深度 8、UTC、主 ID、所有权与失败不发布；
- Windows 路径先转绝对路径，再按不区分大小写规则比较，避免同一文件重复打开。
- 并发打开同一路径时，后到请求会看到前一请求创建的文档并只执行激活。

Document 类型身份与文件扩展名已经解耦：新建和“另存为”统一建议 `.mamdoc`，
打开历史扩展名文件后的普通保存仍覆盖原路径。内容先通过同目录临时文件和原子替换提交；只有写入成功后才
同步 Host 路径、磁盘标题和恢复状态，再调用 `AcceptChanges`。I/O、权限、路径、严格信封与插件边界
异常会转换为稳定脱敏结果；主文件成功后的回调/备份异常只产生警告。

V2 是唯一受支持的 Document 信封。任何 V1 或非 V2 结构直接拒绝，不探测旧字段、不使用历史
Document 别名，也不创建迁移副本。失败打开不会发布 Adapter/View、泄漏 Scope 或写入文件。

### Managed Plugin v1 G7 历史绿色基线

2026-08-18 执行锁定还原、解决方案 Release 构建、两个实际插件测试、SDK 包消费门禁，以及：

```powershell
.\scripts\Invoke-MyAvaloniaManagementTests.ps1 `
  -Configuration Release -NoRestore -WindowsSmoke
```

结果为 Unit 147、UI 37、Plugin 138，共 **322/322**；Host 行覆盖率 80.3%、分支覆盖率
65.47%，Windows Smoke 通过。`DocumentEnvelopeV1` 专项 24/24，BiliDownloader 719/719，
DaTangAccountingHelpPlug 64/64。完整记录见
[G7 Document 信封 v1](../plan-history/host-v1/g7-document-envelope-v1.md)。

### G8 当前绿色基线

2026-08-18 执行锁定还原、Release 零警告构建、三个插件完整测试、SDK 包消费和带
Windows Smoke 的综合门禁。结果为 Unit 151、UI 37、Plugin 141，共 **329/329**；
Host 行覆盖率 80.41%、分支覆盖率 65.71%，Windows Smoke 通过。G8 Host 专项
37/37、Plugin 契约专项 2/2，BiliDownloader 719/719、DaTangAccountingHelpPlug 64/64、
MySmallTools 182/182。完整记录见
[G8 保存契约与内容版本](../plan-history/host-v1/g8-document-content-persistence-contract.md)。

### G9 当前绿色基线

2026-08-18 执行锁定还原、Release 零警告构建、事件总线与 Document Scope 专项、三个插件完整
测试、SDK 包消费和带 Windows Smoke 的综合门禁。结果为 Unit 162、UI 37、Plugin 146，共
**345/345**；Host 行覆盖率 80.57%、分支覆盖率 65.98%，Windows Smoke 通过。
`HostEventBusTests` 10/10、Document Scope 专项 5/5、BiliDownloader 719/719、
DaTangAccountingHelpPlug 64/64、MySmallTools 182/182。完整记录见
[G9 事件总线](../plan-history/host-v1/g9-sdk-event-bus.md)。

### G10 当前绿色基线

2026-08-20 执行锁定还原、Release 零警告构建、G10 专项、三个插件完整回归、SDK 包消费和带
Windows Smoke 的综合门禁。结果为 Unit 167、UI 37、Plugin 146，共 **350/350**；Host 行覆盖率
80.65%、分支覆盖率 65.98%，Windows Smoke 通过。G10 MainWindow/Tool/结构专项 37/37，
BiliDownloader 719/719、DaTangAccountingHelpPlug 64/64、MySmallTools 最终完整复跑 182/182。
完整记录见 [G10 Host 内部直接协调](../plan-history/host-v1/g10-host-internal-coordination.md)。

### G11 当前绿色基线

2026-08-20 执行锁定还原、Release 零警告构建、G11 public 面/依赖/播放器事件专项、三个插件完整
回归、SDK 包正反向消费和带 Windows Smoke 的综合门禁。结果为 Unit 168、UI 38、Plugin 146，
共 **352/352**；Host 行覆盖率 80.62%、分支覆盖率 65.91%，Windows Smoke 通过。
BiliDownloader 720/720、DaTangAccountingHelpPlug 64/64、MySmallTools 183/183。完整记录见
[G11 低价值 public 面清理](../plan-history/host-v1/g11-low-value-public-surface-cleanup.md)。

### G15 当前绿色基线

2026-08-20 执行 `HostDiagnostics` 专项、G15 源码扫描、Release 零警告构建和三套宿主测试。
结果为 Unit 173、UI 38、Plugin 150，共 **361/361**；Host 行覆盖率 81.12%、分支覆盖率 66.85%。
`HostDiagnostics` 专项 26/26，源码门禁检查 127 个生产 C# 文件并通过。数量和覆盖率来自本轮
TRX/Cobertura/`summary.json`，完整记录见
[G15 宿主诊断脱敏](../plan-history/host-v1/g15-host-diagnostic-redaction.md)。

### G16 当前绿色基线

2026-08-20 执行文档核心单元测试、文档事实门禁、锁定还原、Release `-warnaserror` 构建、G15
源码扫描、Host 与三个业务插件单元测试、SDK 包/API 和四插件包矩阵。Host 仍为 Unit 173、UI 38、
Plugin 150，共 **361/361**；行覆盖率 81.12%、分支覆盖率 66.85%。BiliDownloader 720/720、
DaTang 64/64、MySmallTools 183/183；SDK API 为 Shipped 243、Unshipped 0；四个最终 ZIP 均完成
两轮确定性构建和最终 Host 加载。

文档门禁检查 35 份文档、220 个本地链接、65 个脚本路径、35 个真实项目路径和 4 个插件项目。
本轮没有运行 Windows Smoke、G14 总发布门禁或发布验收项目，不能将该结果解释为新的 Windows 发布放行。完整记录见
[G16 文档与 v1 基线](../plan-history/host-v1/g16-documentation-and-v1-baseline.md)。

### 事件总线、Host 直接协调与稳定 ID

插件与存在真实多消费者需求的 Document 继续注入 SDK `IHostEventBus`。发布在调用线程同步执行，
订阅者保存独立 `IDisposable` 令牌并随自身生命周期释放；不同 HostRuntime 不共享总线。

Host 自身的文件打开、布局刷新和 Tool 显隐不再使用该总线。文件树直接调用窄文档打开服务；
`ManagementFactory` 在 Dock 变化完整提交后先同步 Tool 管理器，再通知当前主窗口刷新布局绑定。
主窗口显式解除根级通知，结构门禁同时证明三个旧消息类型不存在且相关构造函数不再依赖公共总线。

插件菜单的策略元数据、创建实例、`ContextLocator` 和
`DockableLocator["Plug"]` 共用 `DockNameConstant.PlugGroupMenu`。
Dock ID 会被持久化，集中常量可以避免一个字符的差异导致工具已经创建却无法定位。

工具管理界面不把 CheckBox 状态当作事实来源，而是重新检查 Dock 树和
`HiddenDockables`。因此无论工具由管理界面切换、用户点击关闭按钮还是布局恢复
产生变化，状态都可以重新收敛到真实布局。

根布局尚未建立时，工具管理界面使用 `ManagementFactory` 提供的内部只读注册快照，
不再通过反射读取私有字段；布局建立后仍以真实 Dock 树为事实来源。

### 契约与内部重构保护

V1 Shipped 保存历史正式签名；G14 的 Core/UI V2 Shipped 分别为 85/46，Unshipped 均为空。
Roslyn Analyzer 在普通 SDK build 中比较源符号，专项脚本再用测试副本证明各类破坏均会阻断。内部类
拆分不会改变文本；兼容新增必须登记到正确程序集，有意破坏则必须建立新主版本基线并同步插件兼容区间
和迁移证据。

`InternalRefactorTests` 保护策略元数据只读取一次、重复 ID 与元数据碰撞抛出 `HostCompositionException`、插件根目录
并发加载共享同一不可变快照，以及原子替换后不遗留临时文件。

### 布局隔离与真实冒烟

生产默认把当前阶段数据写入 `%LOCALAPPDATA%\MyAvaloniaManagement\v2\`。旧 `v1` 根不读取、迁移
或删除。仅当设置
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

### G12 独立插件包门禁

```powershell
.\scripts\Test-ManagedPluginPackages.ps1 -Configuration Release
```

脚本自动发现全部 `ManagedPlugin=true` 项目。它先用临时最小项目验证 16 个声明、必需文件、资产、
路径、共享依赖与 RID 负例，再验证 `SkipPluginDeploy`、只清理当前插件目录和 Debug/Release 默认部署。
每个真实插件从空临时部署根构建两次，比较 ZIP SHA-256 与逐文件清单；最后把四个 ZIP 解压到同一
候选 Host 根，通过真实 `PluginLoadContext` 加载并验证 manifest 精确入口。结果写入：

```text
artifacts/test-results/ManagedPluginPackages/
├── <AssemblyName>-<PluginVersion>-win-x64.zip
├── <AssemblyName>-<PluginVersion>-win-x64.manifest.json
└── summary.json
```

`summary.json` 不保存固定预期测试数量。G12 专用文档的时间点数字由
`scripts/Update-G12DocumentationEvidence.ps1` 从本目录 TRX、宿主 summary 和包 summary 重写。

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
