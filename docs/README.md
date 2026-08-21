# 项目文档导航

本目录保存解决方案级文档。文档按用途分为“快速开始”“当前事实”“设计方法论与探索”以及“历史验收记录”，阅读时应先确认文档类型，避免把历史基线当作当前实现。

## 快速开始

新插件作者从 [Managed 插件快速开始](./quick-start/README.md)进入：

1. [创建 Managed 插件](./quick-start/create-managed-plugin.md)：建立项目、清单、稳定 ID、模块和部署目录。
2. [添加 Document 与 Tool](./quick-start/add-document-and-tool.md)：完成两个最小可见扩展并理解生命周期。
3. [验证与排错](./quick-start/verification-and-troubleshooting.md)：验证加载、界面行为、测试、日志和常见错误码。

该路径同时说明仓库内开发和外部二进制交付边界，只推荐 Managed Plugin；详细契约仍以当前实现与兼容文档为准。

## 当前实现与维护入口

| 文档 | 用途 | 状态 |
| --- | --- | --- |
| [宿主—插件架构评审](./design/host-plugin-architecture-review.md) | 解决方案总体结构、插件边界、当前成熟度和演进方向 | 当前事实，已按主项目内部重构更新 |
| [Managed Plugin v1 封板评审与整改任务书](./design/host-v1-sealing-readiness-plan.md) | 主程序封板差距、版本与兼容策略、删除清单、独立整改包和最终验收标准 | G0–G16 已完成，作为最终签署依据保留 |
| [Plugin SDK 与 UI Profile](./plan-history/host-v1/g3-plugin-sdk-and-ui-profile.md) | 基础 SDK 包、可选 UI 依赖 Profile、宿主语义资源与插件样式兼容规则 | 当前包与样式契约，G3 已完成 |
| [Managed-only 插件加载](./plan-history/host-v1/g4-managed-only-plugin-loading.md) | 必需 deps、唯一模块、DI 激活、稳定拒绝诊断和数据兼容保留边界 | 当前插件加载契约，G4 已完成 |
| [显式贡献与 Plugin Registry](./plan-history/host-v1/g5-explicit-contributions-and-plugin-registry.md) | 破坏式 v1 重定基线、Context/Builder/Registry、贡献所有权、失败原子性与迁移结果 | 当前显式扩展契约，G5 已完成 |
| [宿主 DI 保护与插件注册事务](./plan-history/host-v1/g6-host-di-protection.md) | 追加式私有服务契约、宿主描述符保护、事务提交、稳定诊断与回滚边界 | 当前 DI 组合契约，G6 已完成 |
| [Document 信封 v1](./plan-history/host-v1/g7-document-envelope-v1.md) | 唯一七字段磁盘格式、8 MiB/深度 8 限制、Registry 所有权、插件内容 DTO 与失败原子性 | 当前 Document 磁盘契约，G7 已完成 |
| [G8 保存契约与内容版本](./plan-history/host-v1/g8-document-content-persistence-contract.md) | 内容快照最终 API、宿主路径/所有权状态、SOLID 取舍、插件矩阵和验收证据 | 当前保存内存契约，G8 已完成 |
| [G9 SDK 事件总线](./plan-history/host-v1/g9-sdk-event-bus.md) | SDK 自有同步事件、每 HostRuntime 隔离、订阅令牌和并发语义 | 当前进程内事件契约，G9 已完成 |
| [G10 Host 内部直接协调](./plan-history/host-v1/g10-host-internal-coordination.md) | 文件打开、错误状态和 Tool/Dock 直接协作边界 | 当前 Host 内部协调，G10 已完成 |
| [G11 低价值 public 面清理](./plan-history/host-v1/g11-low-value-public-surface-cleanup.md) | v1 前最终公共面删除、消费者迁移和重新引入条件 | 当前 SDK 公共面，G11 已完成 |
| [G12 统一插件构建、部署与独立发布](./plan-history/host-v1/g12-unified-plugin-build-and-deployment.md) | 声明式资产、生成清单、单插件确定性 ZIP、SOLID 取舍和门禁证据 | 当前构建与发布契约，G12 已完成 |
| [Plugin SDK API 兼容基线维护指南](./reference/plugin-sdk-api-compatibility.md) | Shipped/Unshipped 生命周期、兼容新增审阅、主版本升级和排错 | 当前 SDK API 长期知识，G13 已完成 |
| [G13 Plugin SDK API 兼容基线](./plan-history/host-v1/g13-plugin-sdk-api-compatibility-baseline.md) | v1 文本基线、成员级变异门禁、SOLID 取舍和验收证据 | 当前 SDK public 契约，G13 已完成 |
| [G14 Windows 本地发布门禁](./plan-history/host-v1/g14-windows-release-gate.md) | 平台无关单入口、两轮隔离、发布证据比较、SOLID 取舍和失败语义 | 当前 Windows 发布门禁，G14 已完成 |
| [G15 宿主诊断脱敏](./plan-history/host-v1/g15-host-diagnostic-redaction.md) | 诊断白名单、固定错误映射、敏感调试开关、SOLID 取舍和专项门禁 | 当前诊断安全边界，G15 已完成 |
| [G16 文档与 v1 基线](./plan-history/host-v1/g16-documentation-and-v1-baseline.md) | 当前文档事实、文档门禁、四插件兼容签署、标签和回退边界 | Managed Plugin v1 最终基线，G16 已完成 |
| [Document 保存 V1](./design/document-persistence-v1-design.md) | 公共脏状态、保存事务、关闭确认、备份和坏文件恢复规则 | 当前契约与设计依据 |
| [MyAvaloniaManagement 测试说明](./reference/myavalonia-management-tests.md) | 宿主专项测试、覆盖率和 Windows 冒烟门禁 | 当前事实 |
| [Dock 布局快照 V1](./reference/dock-layout-snapshot-v1.md) | `layout-v1.json` 的稳定 ID、校验、迁移和回退规则 | 当前契约 |
| [主项目内部架构](../Host/MyAvaloniaManagement/docs/design/architecture.md) | `MyAvaloniaManagement` 内部协作者、依赖方向和运行链路 | 当前事实 |
| [主项目设计方法论与取舍](../Host/MyAvaloniaManagement/docs/design/design-methodology-and-tradeoffs.md) | SOLID、设计模式、重构步骤、备选方案与取舍 | 当前决策依据 |
| [主项目兼容约束](../Host/MyAvaloniaManagement/docs/reference/compatibility-contracts.md) | public API、插件、Dock、JSON 和异常语义保护清单 | 当前契约 |

## 设计方法论与探索

以下文档用于解释产品和架构思想，其中部分内容属于研究推论或未来探索，不表示功能已经实现：

- [以注意力为中心的可停靠工作台](./theory/attention-centered-dock-workspace-design.md)：解释 Document、Tool 和 Dock 的产品设计意图。
- [基于活动理论的需求分解方法论](./theory/activity-theory-requirements-decomposition.md)：说明自然语言需求如何拆分到 Document、Tool 和后台服务。
- [AI 工作流插件接入可行性探索](./design/ai-workflow-plugin-exploration.md)：候选能力、风险和 PoC 路线；属于探索文档，不是当前宿主契约。

## 封版后候选计划

以下计划保存 v1 封板后的 V2 方向。V2 已完成 G0–G4；最终 Core/UI SDK、manifest v2 与独立插件
Provider 已建立，但声明式 Host Registry、Dock Adapter、Document 与 layout 仍未完成：

- [Managed Plugin V2 破坏式架构重构任务书](./design/host-v2-breaking-refactor-plan.md)：
  以每插件独立 DI、Host Dock Adapter、声明式 Document/Tool 贡献和全新 V2 数据契约为目标的
  G0–G14 可执行计划；G0–G4 已完成，G5–G14 尚未实现。
- [Managed Plugin V2 G0 绿色基线](./plan-history/host-v2/g0-green-baseline.md)：
  冻结 361 项 Host 测试、SDK API、包图、四插件包事实以及删除面、依赖白名单和消费者矩阵；
  本阶段未运行 Windows Smoke、CI 或发布门禁。
- [Managed Plugin V2 G1 版本与数据边界](./plan-history/host-v2/g1-version-and-data-boundaries.md)：
  集中 V2 版本、未发布 API 基线、默认 `v2` 数据根、V1 数据保留边界和非发布门禁证据。
- [Managed Plugin V2 G2 Plugin SDK 重建](./plan-history/host-v2/g2-plugin-sdk-rebuild.md)：
  记录 Core/UI 最终契约、Legacy 阶段桥、SOLID 取舍、双 API 基线和非发布门禁证据。
- [Managed Plugin V2 G3 manifest v2 与构建协议](./plan-history/host-v2/g3-manifest-v2-and-build-protocol.md)：
  记录严格 reader、精确入口加载、单 SDK 区间、构建探针、确定性 ZIP 和非发布门禁证据。
- [Managed Plugin V2 G4 每插件独立容器](./plan-history/host-v2/g4-per-plugin-containers.md)：
  记录 Host/插件/Document 所有权、失败隔离、DI 原生能力、逆序释放、SOLID 取舍和非发布门禁证据。
- [外部 Managed Plugin 开发与平台安装候选计划](./design/external-managed-plugin-development-and-installation-plan.md)：
  `MyAvaloniaManagement.Plugin.Build`、`dotnet new` 模板、单 ZIP 导入、安装事务、手工插件纳管与单版本回滚。

## 历史升级与验收记录

### Managed Plugin V2 整改记录

- [G0：冻结绿色基线](./plan-history/host-v2/g0-green-baseline.md)：保存非发布验证证据和后续破坏式重构输入，
  不修改生产行为、公共契约、版本或磁盘格式。
- [G1：建立 V2 版本与数据边界](./plan-history/host-v2/g1-version-and-data-boundaries.md)：提升产品、SDK 与
  四插件版本，切换默认数据根，并明确最终格式仍由 G3/G7/G8 负责。
- [G2：重建 Plugin SDK](./plan-history/host-v2/g2-plugin-sdk-rebuild.md)：建立平台无关 Core、真实 UI
  契约和两套 v2 API 基线，并把旧 Common 隔离为不可打包、不可扩散的内部阶段桥。
- [G3：建立 manifest v2 与构建协议](./plan-history/host-v2/g3-manifest-v2-and-build-protocol.md)：
  以清单精确入口、单一 SDK 区间和构建期契约探针替换 v1 双区间与程序集模块扫描。
- [G4：实现每插件独立容器](./plan-history/host-v2/g4-per-plugin-containers.md)：
  Host Provider 与每插件 Provider 分离，删除服务描述符保护事务，以所有权边界实现失败隔离和逆序释放。

### Managed Plugin v1 整改记录

- [G0：绿色基线恢复](./plan-history/host-v1/g0-green-baseline.md)：测试契约漂移修复、SOLID/Test Stub 设计意图、Release 构建、三层测试、覆盖率和 Windows Smoke 证据。
- [G1：支持边界与版本线冻结](./plan-history/host-v1/g1-support-boundary-and-version-lines.md)：集中版本事实、v1 数据根隔离、版本政策门禁和正式支持范围。
- [G2：Host 实现面收口](./plan-history/host-v1/g2-host-api-surface.md)：Host 零自有导出类型、构造注入启动链、显式设计数据、friend 测试边界和 265 项绿色证据。
- [G3：Plugin SDK 与 UI Profile](./plan-history/host-v1/g3-plugin-sdk-and-ui-profile.md)：正式基础包、可选 UI Profile、14 个语义资源、共享程序集边界和 275 项绿色证据。
- [G4：Managed-only 插件加载](./plan-history/host-v1/g4-managed-only-plugin-loading.md)：删除无模块二进制路径、无 deps 回退和双轨策略激活，保留稳定 ID 与数据迁移，277 项绿色证据。
- [G5：显式贡献与 Plugin Registry](./plan-history/host-v1/g5-explicit-contributions-and-plugin-registry.md)：破坏式删除重复身份和隐式发现，四插件与宿主迁移到 Context/Builder/不可变 Registry，283 项绿色证据与 SDK 新旧契约夹具通过。
- [G6：宿主 DI 保护](./plan-history/host-v1/g6-host-di-protection.md)：插件在隔离工作副本追加私有服务，宿主按描述符引用校验并事务提交；删除、替换、重排和覆盖宿主类型会在容器构建前阻断。
- [G7：Document 信封 v1](./plan-history/host-v1/g7-document-envelope-v1.md)：建立第一个且唯一的严格七字段信封，分离宿主元数据与插件内容，加入资源边界、所有权校验和 322 项宿主绿色证据。
- [G8：保存契约与内容版本](./plan-history/host-v1/g8-document-content-persistence-contract.md)：删除插件路径与身份所有权，最终收窄为不可变内容快照与恢复契约，保持七字段磁盘信封不变。
- [G9：SDK 事件总线](./plan-history/host-v1/g9-sdk-event-bus.md)：删除第三方消息器泄漏和进程全局状态，建立每 HostRuntime 隔离的强类型同步事件。
- [G10：Host 内部直接协调](./plan-history/host-v1/g10-host-internal-coordination.md)：删除 Host 内部广播，用根级状态和 Dock 协调器直接协作。
- [G11：低价值 public 面清理](./plan-history/host-v1/g11-low-value-public-surface-cleanup.md)：删除无消费者、无生产实现或已有定向替代的候选 SDK 面。
- [G12：统一插件构建、部署与独立发布](./plan-history/host-v1/g12-unified-plugin-build-and-deployment.md)：四插件共享声明式构建协议，但保持独立版本、ZIP 与回滚节奏。
- [G13：Plugin SDK API 兼容基线](./plan-history/host-v1/g13-plugin-sdk-api-compatibility-baseline.md)：以可读文本和成员级变异门禁替换临时 SHA256，兼容新增必须显式登记。
- [G14：Windows 本地发布门禁](./plan-history/host-v1/g14-windows-release-gate.md)：以平台无关 PowerShell 单入口执行两轮隔离 Release 门禁并比较机器可读证据。
- [G15：宿主诊断脱敏](./plan-history/host-v1/g15-host-diagnostic-redaction.md)：以白名单转换和固定错误映射保护内存、UI、JSONL 与默认 Trace/stderr，并提供显式短期敏感调试通道。
- [G16：文档与 v1 基线](./plan-history/host-v1/g16-documentation-and-v1-baseline.md)：同步当前事实，以独立文档门禁签署 SDK/API 与四插件兼容边界，并创建 `managed-plugin-v1.0.0` 本地注解标签。

### .NET 10 升级记录

`upgrade/net10/phase-*.md` 是分阶段升级时的证据快照。文档中的 `.NET 9`、旧依赖版本、当时的测试数量或阶段性限制是有意保留的历史事实，不应为了匹配当前代码而覆盖。

- [阶段 0：基线](./plan-history/net10/phase-0-baseline.md)
- [阶段 1：治理](./plan-history/net10/phase-1-governance.md)
- [阶段 2：.NET 10 基座](./plan-history/net10/phase-2-net10-foundation.md)
- [阶段 3：插件依赖](./plan-history/net10/phase-3-plugin-dependencies.md)
- [阶段 4：Avalonia 12 与 LibVLCSharp 闸门](./plan-history/net10/phase-4-avalonia12-libvlc-gate.md)

## 文档维护规则

1. 描述当前代码的文档必须引用实际类型、测试或稳定文件格式，不把建议写成已实现事实。
2. 历史验收记录只追加勘误或状态说明，不改写当时的命令和结果。
3. public API、稳定 Dock ID、文档 JSON 或布局 JSON 发生有意变化时，代码、契约测试和文档必须在同一变更中更新。
4. 内部重构只更新主项目内部架构文档；除非外部行为发生变化，不应要求插件文档跟随内部类名调整。
5. 测试数量和覆盖率属于时间点证据，应同时记录日期与执行命令。
