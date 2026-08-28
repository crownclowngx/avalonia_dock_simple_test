# 项目文档导航

本目录保存解决方案级文档。文档按用途分为“快速开始”“当前事实”“设计方法论与探索”以及“历史验收记录”，阅读时应先确认文档类型，避免把历史基线当作当前实现。

## 快速开始

> 当前 Workbench Command G8 基线：Core/UI SDK `3.3.0`、Workflow SDK `1.0.0`、Build `1.1.2`、
> Templates `1.3.0`、外部 WorkflowStudio `1.2.0` 与 ClassicGame `1.1.0`。
> 外部作者可以在不克隆 Host 源码的情况下使用 `dotnet new myavalonia-plugin` 创建、独立调试、测试和
> 打包插件。manifest schema 仍为 2，当前交付平台为 Windows x64。
>
> Host V4 G8 当前事实：Host internal G0–G8 已封板，产品、SDK、四插件与 v3 API/磁盘格式保持不变；
> 当前正式入口为 `scripts/Invoke-HostV4ReleaseGate.ps1`，只建立本地发布资格，不执行外部发布或 AIFLOW。
>
> Workflow Action G1–G4 保持 Host 产品 `3.0.0`；G3.1 提升 Core/UI SDK 到 `3.2.0`、新增
> Workflow SDK `1.0.0`、外部 Studio `1.1.0` 和 Templates `1.2.0`；G4 新增 MySmallTools `3.1.0`
> 非破坏性加密 Action 本地候选。G5–G10 尚未实施。

Managed Plugin 快速开始入口：

1. [从只有 Rider 和 Avalonia 的机器创建插件](./quick-start/create-managed-plugin.md)：安装 .NET 10 SDK、
   NuGet 模板，创建项目并完成第一次独立调试。
2. [添加多个 Document、Tool 和独立预览工作台](./quick-start/add-document-and-tool.md)：使用唯一 Module
   注册贡献，并说明多 Scope Document 与 singleton Tool 的预览方法。
3. [编译、打包、真实 Host 验收与排错](./quick-start/verification-and-troubleshooting.md)：验证 Standalone、
   确定性 ZIP、真实加载和新机器常见问题。
4. [Workflow Action 开发说明](./quick-start/workflow-action-development.md)：共享 Schema、Descriptor、双 revision 与 ALC 边界。
5. [G3.1 SDK 候选打包与发布](./quick-start/workflow-sdk-publication.md)：隔离 feed、同批字节、Token 与公开源复验纪律。

该路径同时说明仓库内开发和外部二进制交付边界，只推荐 Managed Plugin；详细契约仍以当前实现与兼容文档为准。

## 当前实现与维护入口

| 文档 | 用途 | 状态 |
| --- | --- | --- |
| [Host V4 内部收口任务书](./design/host-v4-breaking-refactor-plan.md) | Host 死面、身份、Layout、回收所有权、领域目录、路径语义、集成回归与封板 | 已完成；G0–G8 已封板，本地可发布但未对外发布 |
| [Workflow Action 总设计](./design/ai-workflow-plugin-exploration.md) | 手工工作流优先、Action 内核、外部 Studio 与后续 G5–G10 边界 | G0 已重新签署、G1–G4 已完成实现，G5–G10 未实施 |
| [Workflow Action G0 重新签署](./plan-history/workflow-action/g0-facts-naming-repositories-sdk-compatibility.md) | Run/进度出口、SDK 3.1 兼容路线与真实 3.0 插件证据 | 已完成；非发布 |
| [Workflow Action G1 Host 内核](./plan-history/workflow-action/g1-host-workflow-action-kernel.md) | SOLID、公共 API、调用/关闭时序、测试与回滚 | 已完成；完整非发布门禁通过 |
| [Workflow Action G2 SDK/Build/外部模板传播](./plan-history/workflow-action/g2-sdk-build-external-template-propagation.md) | NuGet、lock file、点号名称、双 ALC 实调、哈希和回滚 | 已完成并发布；Build 保持 1.1.2 |
| [Workflow Action G3 外部 Studio](./plan-history/workflow-action/g3-workflow-studio-fake-action-loop.md) | 外部 revision、定义/Runner、Standalone Fake、候选 Host 与回滚 | 已完成；完整本地非发布门禁通过 |
| [Workflow Action G3.1 协议一致性](./plan-history/workflow-action/g3.1-workflow-protocol-consistency.md) | Workflow SDK、双 revision、共享 Schema/路径与静态引用安全 | 已完成并发布；纯公开源复验通过 |
| [G3.1 Templates 1.2.0 发布补充](./plan-history/workflow-action/g3.1-template-1.2-publication.md) | SDK 3.2 模板传播、lock file、候选/公开源探针与不可变制品哈希 | 已发布；公开安装、构建、测试与打包通过 |
| [Workflow Action G4 MySmallTools 加密](./plan-history/workflow-action/g4-my-small-tools-nondestructive-encryption-action.md) | 非破坏性 Action 合同、SOLID、真实双 ZIP、文件安全与非发布证据 | 已完成；本地开发门禁 |
| [Workbench Command 引入任务书](./design/workbench-command-introduction-plan.md) | CommandId、活动 Document Target、Context v1、菜单/快捷键贡献、外部插件与 Palette 的 G0–G10 计划 | 实施中；G0–G8 已完成，G9–G10 尚未实施 |
| [Workbench Command G8 ClassicGame 全游戏命令](./plan-history/workbench-command/g8-classic-game-multi-instance-commands.md) | 13 个游戏、22 条 Restart/Undo、真实 ZIP、五子棋双实例与 Headless UI | 已完成；双仓本地非发布门禁通过 |
| [Workbench Command G7 WorkflowStudio 三命令](./plan-history/workbench-command/g7-workflow-studio-three-real-commands.md) | 外部 Studio Validate/Run/Cancel、真实 ZIP、双 ALC、业务 Action 与 Headless UI | 已完成；本地非发布门禁通过 |
| [Workbench Command G6 SDK 3.3/模板/独立消费](./plan-history/workbench-command/g6-sdk-candidate-template-independent-consumption.md) | SDK 3.3、Templates 1.3、真实 lock file、生成项目、双 ALC 与新旧兼容矩阵 | 已完成；本地门禁与公开源复验结果见专项记录 |
| [Workbench Command G0 基线与语义](./plan-history/workbench-command/g0-facts-semantics-public-api.md) | Host/SDK/外部 Studio 基线、语义、候选 API 与非发布边界 | 已完成；不修改生产源码 |
| [Workbench Command G1 契约与注册声明](./plan-history/workbench-command/g1-command-contracts-registration-declarations.md) | Core/UI 候选契约、可选注册、所有权与不可变 Registry | 已完成；完整非发布门禁通过 |
| [Workbench Command G2 Catalog 与 Executor](./plan-history/workbench-command/g2-command-catalog-executor.md) | Host/Plugin 合并目录、打开/保存 Handler、执行结果、诊断与关闭门控 | 已完成；无 UI 非发布门禁通过 |
| [Workbench Command G3 Context 与活动 Target 路由](./plan-history/workbench-command/g3-context-active-document-target-routing.md) | 活动 Document 事实、Context v1、统一状态、当前实例执行与关闭租约 | 已完成；完整非发布门禁通过 |
| [Workbench Command G4 Host 打开/保存闭环](./plan-history/workbench-command/g4-host-open-save-presentation-loop.md) | Host Presentation、File 菜单、Ctrl+S、实时 Enabled 与旧 ViewModel 命令删除 | 已完成；完整非发布门禁通过 |
| [Workbench Command G5 声明式菜单与快捷键投影](./plan-history/workbench-command/g5-declarative-menu-keybinding-projection.md) | Host-owned Projection、确定性菜单、快捷键冲突、View/Window 所有权与可用性刷新 | 已完成；完整非发布门禁通过 |
| [V4 G0 V3 源码基线](./plan-history/host-v4/g0-v3-source-baseline.md) | V3 源码输入、锁定还原、测试与非发布事实 | 已完成；不修改生产源码 |
| [V4 G1 删除 Host 死面](./plan-history/host-v4/g1-remove-dead-host-surface.md) | 空协议、菜单尾项、Hosting 依赖和开发门禁 | 已完成；非发布 |
| [V4 G2 强类型身份与用例入口](./plan-history/host-v4/g2-strongly-typed-identity-and-use-case-entry.md) | ToolTypeId 单一源、真实 Coordinator 与异步 Harness | 已完成；非发布 |
| [V4 G3 Layout 职责对齐](./plan-history/host-v4/g3-layout-responsibility-alignment.md) | Lifecycle、Mapper、Validator 的文件与变化原因 | 已完成；非发布 |
| [V4 G4 Document 控件回收所有权](./plan-history/host-v4/g4-document-control-recycling-ownership.md) | DI 唯一实例、Style/关闭链同一引用与 20 轮资源归零 | 已完成；非发布 |
| [V4 G5 领域迁移](./plan-history/host-v4/g5-domain-helper-migration.md) | Helpers 消除、Welcome/Tool 命名和领域 namespace | 已完成；非发布 |
| [V4 G6 路径语义与展示模型](./plan-history/host-v4/g6-file-system-path-and-presentation-model.md) | 驱动器/UNC 分类、存在性端口、只读展示快照与最终非发布回归 | 已完成；478/478，85.06% / 71.41%，非发布 |
| [V4 G7 四插件、Harness 与文档回归](./plan-history/host-v4/g7-four-plugins-harness-documentation-regression.md) | SDK、四插件真实包、20 轮资源 Harness、诊断与文档统一回归 | 已完成；Host 478/478，四插件专项全绿，非发布 |
| [V4 G8 封板](./plan-history/host-v4/g8-v4-sealing.md) | 两轮隔离、实体证据复核、Windows Smoke、SOLID 与发布边界 | 已完成；本地具备发布资格，未上传、未打 tag、未对外发布 |
| [宿主—插件架构评审](./design/host-plugin-architecture-review.md) | 解决方案总体结构、插件边界、当前成熟度和演进方向 | 当前事实，已按主项目内部重构更新 |
| [Managed Plugin V3 破坏式重构任务书](./design/host-v3-breaking-refactor-plan.md) | V3 保存修订、激活语义、注册所有权、消息边界和 Workspace 解耦的 G0–G14 计划 | 已完成；G0–G14 已全部封板 |
| [V3 G0 非发布绿色基线](./plan-history/host-v3/g0-green-baseline.md) | V2 当前测试、覆盖率、API、包图与保存竞争复现 | 已完成；不改变生产行为 |
| [V3 G1 版本与数据边界](./plan-history/host-v3/g1-version-and-data-boundaries.md) | 未发布 3.0.0 版本线、v3 Unshipped API、V2 磁盘兼容和非发布门禁 | 已完成；不改变 public API 形状或磁盘格式 |
| [V3 G2 修订化 Document 保存](./plan-history/host-v3/g2-revisioned-document-save.md) | 修订快照、指定修订确认、关闭竞争保护、三插件策略和非发布专项门禁 | 已完成；envelope v2 与插件内容 schema 不变 |
| [V3 G3 互斥 Document 激活](./plan-history/host-v3/g3-exclusive-document-activation.md) | New/Restore 互斥输入、11 个 Document 支持矩阵、失败回滚和非发布专项门禁 | 已完成；线格式与业务 Codec 不变 |
| [V3 G4 插件注册所有权](./plan-history/host-v3/g4-plugin-registration-ownership.md) | 空集合登记、Host 最终提交、ID 归属、稳定诊断和非发布专项门禁 | 已完成；public API 与磁盘格式不变 |
| [V3 G5 插件私有消息](./plan-history/host-v3/g5-plugin-private-messaging.md) | SDK/Host 总线删除面、插件内消息器、隔离、覆盖率和非发布门禁 | 已完成；Core 127 / UI 46，磁盘格式与业务 DTO 不变 |
| [V3 G6 Workspace Session 与 Dock Factory](./plan-history/host-v3/g6-workspace-session-and-dock-factory.md) | Factory Adapter、唯一 Session、Tool 只读投影、关闭/退出时序和专项门禁 | 已完成；441/441，Host 83.78% / 70.32%，非发布 |
| [V3 G7 Host Catalog 与 Plugin Registry](./plan-history/host-v3/g7-host-catalog-and-plugin-registry.md) | Host/插件目录、双激活边界、所有权、失败回滚、SOLID 和专项门禁 | 已完成；448/448，Host 84.04% / 70.26%，非发布 |
| [V3 G8 全屏租约与 Host V3 骨架](./plan-history/host-v3/g8-fullscreen-lease-and-host-v3-skeleton.md) | 租约状态机、窗口/Document 时序、原生表面迁移、SOLID 和资源门禁 | 已完成；672/672，Host 84.15% / 70.30%，20 轮资源归零，非发布 |
| [V3 G9 MyPlugTest 验收](./plan-history/host-v3/g9-my-plug-test-v3-acceptance.md) | 最终 Workspace 创建链、Revision 保存竞争、消息释放、UI、确定性 ZIP 与 SOLID | 已完成；501/501，Host 84.39% / 70.58%，两次 11 文件 ZIP，非发布 |
| [V3 G10 DaTangAccountingHelpPlug 验收](./plan-history/host-v3/g10-datang-accounting-help-v3-acceptance.md) | 双 Document、Revision 保存竞争、文件交互、真实 Host 组合与 SOLID | 已完成；554/554，Host 84.39% / 70.58%，插件 70.09% / 49.31%，非发布 |
| [V3 G11 MySmallTools 验收](./plan-history/host-v3/g11-my-small-tools-v3-acceptance.md) | 四 Document、全屏租约、20 轮真实媒体资源归零、真实 Host 组合与 SOLID | 已完成；676/676，Host 84.39% / 70.58%，插件 72.59% / 48.12%，非发布 |
| [V3 G12 BiliDownloader 验收](./plan-history/host-v3/g12-bili-downloader-v3-acceptance.md) | 保存竞争、私有消息、Lifecycle/readiness、真实 Host 组合与 SOLID | 已完成；1219/1219，Host 84.39% / 70.58%，插件 83.80% / 67.54%，非发布 |
| [V3 G13 删除 V2 生产面](./plan-history/host-v3/g13-remove-v2-production-surface.md) | 活动零残留、API/包负例、四插件矩阵、SOLID 与回滚边界 | 已完成；1483/1483，Host 84.39% / 70.58%，非发布 |
| [V3 G14 封板](./plan-history/host-v3/g14-v3-sealing.md) | API Shipped、两轮隔离门禁、四插件专项、Windows Smoke、SOLID 和回滚 | 已完成；Core/UI 127/45，本地可发布且未外部发布 |
| [Managed Plugin V2 封板任务书](./design/host-v2-breaking-refactor-plan.md) | V2 所有权、删除清单、阶段实施和最终签署矩阵 | G0–G14 已完成，当前签署依据 |
| [V2 G14 封板记录](./plan-history/host-v2/g14-v2-sealing.md) | API Shipped、两轮隔离门禁、制品、SOLID 和回滚边界 | 当前 V2 正式基线 |
| [Managed Plugin v1 封板评审与整改任务书](./design/host-v1-sealing-readiness-plan.md) | V1 封板差距、版本与兼容策略、整改包和验收标准 | V1 历史签署，已由 V2 取代 |
| [Plugin SDK 与 UI Profile](./plan-history/host-v1/g3-plugin-sdk-and-ui-profile.md) | V1 基础 SDK、UI Profile、语义资源与样式兼容规则 | V1 历史，已由 V2 取代 |
| [Managed-only 插件加载](./plan-history/host-v1/g4-managed-only-plugin-loading.md) | V1 deps、唯一模块、DI 激活和拒绝诊断 | V1 历史，已由 V2 取代 |
| [显式贡献与 Plugin Registry](./plan-history/host-v1/g5-explicit-contributions-and-plugin-registry.md) | V1 Context/Builder/Registry、贡献所有权和失败原子性 | V1 历史，已由 V2 取代 |
| [宿主 DI 保护与插件注册事务](./plan-history/host-v1/g6-host-di-protection.md) | V1 共享 Host DI 的保护事务 | V1 历史，已由 V2 独立 Provider 取代 |
| [Document V2 持久化设计](./design/document-persistence-v2-design.md) | 六字段格式、异步所有权链、保存提交点、恢复、关闭与 Scope 释放 | 当前 Document 契约，V2 G7 已完成 |
| [Dock 布局快照 V2](./reference/dock-layout-snapshot-v2.md) | 唯一严格字段、四向状态、生命周期可用性门控、整体隔离与 V1 保留边界 | 当前 Layout 契约，V2 G8 已完成 |
| [G9 SDK 事件总线](./plan-history/host-v1/g9-sdk-event-bus.md) | V1 同步事件、订阅令牌和并发语义 | V1 历史；当前已由 V3 G5 的插件私有消息取代 |
| [G10 Host 内部直接协调](./plan-history/host-v1/g10-host-internal-coordination.md) | V1 文件打开、错误状态和 Tool/Dock 协调 | V1 历史，已由 V2 当前架构取代 |
| [G11 低价值 public 面清理](./plan-history/host-v1/g11-low-value-public-surface-cleanup.md) | V1 public 面删除与消费者迁移 | V1 历史，已由 V2 API 基线取代 |
| [G12 统一插件构建、部署与独立发布](./plan-history/host-v1/g12-unified-plugin-build-and-deployment.md) | V1 插件构建、确定性 ZIP 和门禁证据 | V1 历史，已由 V2 构建协议取代 |
| [Plugin SDK API 兼容基线维护指南](./reference/plugin-sdk-api-compatibility.md) | Shipped/Unshipped 生命周期、兼容新增审阅、主版本升级和排错 | 当前 SDK API 长期知识，G13 已完成 |
| [G13 Plugin SDK API 兼容基线](./plan-history/host-v1/g13-plugin-sdk-api-compatibility-baseline.md) | v1 文本基线、成员级变异门禁和验收证据 | V1 历史 API 基线 |
| [G14 Windows 本地发布门禁](./plan-history/host-v1/g14-windows-release-gate.md) | V1 两轮隔离、发布证据比较和失败语义 | V1 历史门禁；V3 G1 不运行发布入口 |
| [G15 宿主诊断脱敏](./plan-history/host-v1/g15-host-diagnostic-redaction.md) | 诊断白名单、固定错误映射、敏感调试开关、SOLID 取舍和专项门禁 | 当前诊断安全边界，G15 已完成 |
| [G16 文档与 v1 基线](./plan-history/host-v1/g16-documentation-and-v1-baseline.md) | 当前文档事实、文档门禁、四插件兼容签署、标签和回退边界 | Managed Plugin v1 最终基线，G16 已完成 |
| [MyAvaloniaManagement 测试说明](./reference/myavalonia-management-tests.md) | 宿主专项测试、覆盖率和 Windows 冒烟门禁 | 当前事实 |
| [Dock 布局快照 V1（历史）](./reference/dock-layout-snapshot-v1.md) | G8 前 `layout-v1.json` 的稳定 ID、迁移和回退历史 | 历史事实；当前 Host 不读取 |
| [主项目内部架构](../Host/MyAvaloniaManagement/docs/design/architecture.md) | `MyAvaloniaManagement` 内部协作者、依赖方向和运行链路 | 当前事实 |
| [主项目设计方法论与取舍](../Host/MyAvaloniaManagement/docs/design/design-methodology-and-tradeoffs.md) | SOLID、设计模式、重构步骤、备选方案与取舍 | 当前决策依据 |
| [主项目兼容约束](../Host/MyAvaloniaManagement/docs/reference/compatibility-contracts.md) | public API、插件、Dock、JSON 和异常语义保护清单 | 当前契约 |

## 设计方法论与探索

以下文档用于解释产品和架构思想，其中部分内容属于研究推论或未来探索，不表示功能已经实现：

- [以注意力为中心的可停靠工作台](./theory/attention-centered-dock-workspace-design.md)：解释 Document、Tool 和 Dock 的产品设计意图。
- [基于活动理论的需求分解方法论](./theory/activity-theory-requirements-decomposition.md)：说明自然语言需求如何拆分到 Document、Tool 和后台服务。
- [工作流执行与可选 AI 规划方案](./design/ai-workflow-plugin-exploration.md)：以临时手工编辑为 MVP 主路径，记录已完成 G0–G4，并给出下载 Action、跨插件 E2E、AI 和持久化的 G5–G10 可行性路线。
- [Workflow Action G0 冻结记录](./plan-history/workflow-action/g0-facts-naming-repositories-sdk-compatibility.md)：冻结 3.0 输入事实、WorkflowStudio 命名、Schema/预算和独立仓库边界，并以真实旧插件包和跨 ALC 夹具签署 SDK 3.1 兼容新增路线；不表示生产 API 已实现。
- [Workflow Action G2 传播记录](./plan-history/workflow-action/g2-sdk-build-external-template-propagation.md)：记录模板 1.1.0、Build 1.1.2 不升版、真实 NuGet/lock、外部双 ALC、非发布门禁历史和正式上传结果。
- [Workflow Action G3 外部 Studio 记录](./plan-history/workflow-action/g3-workflow-studio-fake-action-loop.md)：签署外部仓库提交、Fake 闭环、测试覆盖率、确定性 ZIP 和隔离候选 Host 证据；不表示 G4/G5 真实 Action 已实现。
- [Workflow Action G3.1 协议一致性记录](./plan-history/workflow-action/g3.1-workflow-protocol-consistency.md)：记录共享 Workflow SDK、Host 双 revision、默认 ALC、Studio v2 与静态引用安全；不表示 Host 产品已发布。
- [G3.1 Templates 1.2.0 发布补充记录](./plan-history/workflow-action/g3.1-template-1.2-publication.md)：记录后续范围扩展、SDK 3.2 精确锁定、模板门禁、冻结哈希、上传警告与纯公开源复验。
- [Workflow Action G4 MySmallTools 记录](./plan-history/workflow-action/g4-my-small-tools-nondestructive-encryption-action.md)：记录非破坏性加密合同、真实文件、Studio 双 ZIP、SOLID 与本地非发布门禁。

## V3/V2 已封板基线与后续候选计划

V2 已完成 G0–G14；Host 正式契约、四个真实 V2 业务插件、唯一生产面与发布门禁已经建立：

- [Managed Plugin V3 破坏式架构重构任务书](./design/host-v3-breaking-refactor-plan.md)：
  以修订化 Document 保存、互斥激活、插件私有消息、注册所有权、Workspace/Dock 解耦和 Host Catalog
  分离和全屏租约为目标的 G0–G14 计划；G0–G14 已全部完成，G13 已删除并证明 V2 生产面零残留，
  G14 已冻结 Core/UI 127/45 条 API 并完成两轮隔离发布签署。
- [Managed Plugin V2 破坏式架构重构任务书](./design/host-v2-breaking-refactor-plan.md)：
  以每插件独立 DI、Host Dock Adapter、声明式 Document/Tool 贡献和全新 V2 数据契约为目标的
  G0–G14 已完成的实施与最终签署任务书。
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
- [Managed Plugin V2 G5 声明式贡献目录](./plan-history/host-v2/g5-declarative-contribution-catalog.md)：
  记录一次声明、注册封闭、两阶段冲突隔离、不可变 Registry、Host 内建贡献和非发布门禁证据。
- [Managed Plugin V2 G6 Host Dock Adapter](./plan-history/host-v2/g6-host-dock-adapter.md)：
  记录普通模型、内部 Adapter、View 原子发布、Scope/View 所有权、失败隔离和非发布门禁证据。
- [Managed Plugin V2 G7 Document V2](./plan-history/host-v2/g7-document-v2.md)：
  记录六字段格式、异步创建、保存/恢复/关闭事务、失败矩阵、覆盖率和非发布门禁证据。
- [Managed Plugin V2 G8 布局与生命周期 V2](./plan-history/host-v2/g8-layout-and-lifecycle-v2.md)：
  记录严格 Layout V2、internal 生命周期、可用性门控、退出顺序、测试证据和非发布边界。
- [Managed Plugin V2 G9 MyPlugTest 迁移](./plan-history/host-v2/g9-my-plug-test-v2.md)：
  记录首个真实业务插件的声明式贡献、Document/Tool 所有权、严格内容 Codec、事件释放和确定性测试 ZIP。
- [Managed Plugin V2 G10 DaTang 迁移](./plan-history/host-v2/g10-datang-accounting-help-v2.md)：
  记录双 Document 贡献、窄窗口 Host Port、严格银行对账 schema、关闭所有权和非发布门禁。
- [Managed Plugin V2 G11 MySmallTools 迁移](./plan-history/host-v2/g11-my-small-tools-v2.md)：
  记录四 Document、关闭令牌、全屏端口、SECVID03/LibVLC 资源所有权、真实媒体和确定性测试 ZIP 证据。
- [Managed Plugin V2 G12 BiliDownloader 迁移](./plan-history/host-v2/g12-bili-downloader-v2.md)：
  记录 Document schema 3、readiness、后台关闭、SOLID 责任划分、覆盖率和确定性测试 ZIP 证据。
- [Managed Plugin V2 G13 删除 V1 生产面](./plan-history/host-v2/g13-remove-v1-production-surface.md)：
  记录 Legacy 项目删除、唯一 V2 构建协议、编译负例、依赖白名单与非发布包矩阵。
- [Managed Plugin V2 G14 封板](./plan-history/host-v2/g14-v2-sealing.md)：
  记录正式 API Shipped、两轮隔离 Release 门禁、Windows V2 Smoke、文档签署和回滚边界。
- [外部 Managed Plugin 开发、模板与 NuGet 发布指南](./design/external-managed-plugin-development-and-installation-plan.md)：
  已发布的 Core/UI SDK、Build、Templates，外部项目结构、单 ZIP 打包与 NuGet 维护流程。

## 历史升级与验收记录

### Managed Plugin V3 整改记录

- [G0：冻结 V2 绿色基线](./plan-history/host-v3/g0-green-baseline.md)：以真实保存链复现无修订
  无版本确认的竞争；G2 已以 `AcceptChanges(DocumentRevision)` 修复，并冻结 Host/SDK/四插件的
  非发布测试、覆盖率、API 与确定性包证据。
- [G1：建立 V3 版本与数据边界](./plan-history/host-v3/g1-version-and-data-boundaries.md)：统一产品、SDK
  与四插件 3.0.0 版本线，建立 v3 Unshipped API，同时保留 manifest/Document/layout schema 2 和数据根 v2。
- [G7：分离 Host Catalog 与 Plugin Registry](./plan-history/host-v3/g7-host-catalog-and-plugin-registry.md)：
  记录不可变目录、Host/插件激活边界、Provider/Scope/View 所有权、失败回滚、SOLID 与 448 项非发布证据。
- [G8：建立全屏租约与 Host V3 骨架](./plan-history/host-v3/g8-fullscreen-lease-and-host-v3-skeleton.md)：
  记录租约状态机、窗口/Document/原生表面时序、owner API 删除面、672 项测试和 20 轮资源归零证据。
- [G14：V3 封板](./plan-history/host-v3/g14-v3-sealing.md)：记录最终 API Shipped、两轮隔离 Release
  门禁、四插件专项与确定性制品、Windows Smoke、无外部发布边界和整体回滚方式。

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
- [G5：建立声明式贡献目录](./plan-history/host-v2/g5-declarative-contribution-catalog.md)：
  Host 使用最终 UI SDK 模块入口，贡献经插件局部 Builder 与全局冲突过滤后发布到唯一不可变 Registry。
- [G6：实现 Host Dock Adapter](./plan-history/host-v2/g6-host-dock-adapter.md)：
  只有 Host internal Adapter 继承 Dock；普通模型、预构建 View、Document Scope 与 Tool singleton 各守所有权边界。
- [G7：建立 Document V2](./plan-history/host-v2/g7-document-v2.md)：
  建立唯一异步创建、严格六字段信封、原子保存、恢复另存、关闭重入与 Scope 释放链。
- [G8：建立布局与生命周期 V2](./plan-history/host-v2/g8-layout-and-lifecycle-v2.md)：
  删除布局 V1/Migrator 与 public 生命周期编排面，建立严格快照、Host internal 协调和只读可用性门控。
- [G9：迁移 MyPlugTest](./plan-history/host-v2/g9-my-plug-test-v2.md)：
  迁移 4 个 Document 与 1 个 Tool，删除 Strategy/Dock/Legacy 依赖，并以真实 V2 ZIP 验证加载与组合。
- [G10：迁移 DaTangAccountingHelpPlug](./plan-history/host-v2/g10-datang-accounting-help-v2.md)：
  迁移发票导入和银行余额调节，新增受控窗口端口，并验证严格内容与多 Scope 隔离。
- [G11：迁移 MySmallTools](./plan-history/host-v2/g11-my-small-tools-v2.md)：
  迁移四个 Document、全屏端口与原生资源所有权，并通过真实媒体 Harness 验证关闭释放。
- [G12：迁移 BiliDownloader](./plan-history/host-v2/g12-bili-downloader-v2.md)：
  迁移最后一个业务插件的 Document、Tool、Lifecycle 与 JSON 边界，并通过非发布真实 ZIP 加载验证。
- [G13：删除 V1 生产面](./plan-history/host-v2/g13-remove-v1-production-surface.md)：
  删除 Legacy 项目、兼容适配和过渡构建属性，并以源码、编译、依赖和包矩阵证明零生产残留。

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
