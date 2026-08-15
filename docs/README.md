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
| [Managed Plugin v1 封板评审与整改任务书](./design/host-v1-sealing-readiness-plan.md) | 主程序封板差距、版本与兼容策略、删除清单、独立整改包和最终验收标准 | 当前整改计划，完成前不得认定宿主已封板 |
| [Plugin SDK 与 UI Profile](./plan-history/host-v1/g3-plugin-sdk-and-ui-profile.md) | 基础 SDK 包、可选 UI 依赖 Profile、宿主语义资源与插件样式兼容规则 | 当前包与样式契约，G3 已完成 |
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

## 历史升级与验收记录

### Managed Plugin v1 整改记录

- [G0：绿色基线恢复](./plan-history/host-v1/g0-green-baseline.md)：测试契约漂移修复、SOLID/Test Stub 设计意图、Release 构建、三层测试、覆盖率和 Windows Smoke 证据。
- [G1：支持边界与版本线冻结](./plan-history/host-v1/g1-support-boundary-and-version-lines.md)：集中版本事实、v1 数据根隔离、版本政策门禁和正式支持范围。
- [G2：Host 实现面收口](./plan-history/host-v1/g2-host-api-surface.md)：Host 零自有导出类型、构造注入启动链、显式设计数据、friend 测试边界和 265 项绿色证据。
- [G3：Plugin SDK 与 UI Profile](./plan-history/host-v1/g3-plugin-sdk-and-ui-profile.md)：正式基础包、可选 UI Profile、14 个语义资源、共享程序集边界和 275 项绿色证据。

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
