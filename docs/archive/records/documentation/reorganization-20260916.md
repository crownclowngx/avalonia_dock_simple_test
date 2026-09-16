# 2026-09-16 主项目文档整理记录

> 本次仅整理文档与归档证据；不修改产品、Gate、测试、API、配置或外部仓库，不使用 AIFLOW，不发布或部署。

## 基线与范围

- 源码基线：`80e4129ef78f766c167a583010963bae1a37da50`，整理前主仓工作树干净。
- 原始受跟踪 Markdown：151 份。每份去向见下表；该表是本次处置快照，不作为日常文档目录。
- 保持应用内八个帮助章节、四篇理论原文和 Host 架构原文的路径；历史证据 JSON 与图片只移动，不改内容。
- 综合发布指南的当前步骤归入维护指南，版本/发布日期快照归入 plugin-packaging 记录；开发步骤由快速开始承接。G3.1 发布短文合并进协议一致性历史记录。

## 验证

- 临时只读扫描覆盖整理后的 157 份 Markdown，文件链接与 Markdown 标题锚点均通过；模板内部链接没有逃逸生成项目目录。扫描脚本仅保存在系统临时目录，未加入仓库。
- 原始 151 份 Markdown 全部有下表处置记录。归档正文对照原文，仅允许归档说明、链接重算/勘误和明确的合并补充；当时的版本、结果及失败结论未改写。
- 52 处旧外部目录引用已重算；58 处已清理产物引用改为文字说明；3 处 Legacy 类型链接映射到当前声明与所有权实现；另修正 5 处历史标题锚点。
- 12 份归档 JSON/图片按 SHA-256 逐一验证，字节完全一致；其余 647 份原非 Markdown 跟踪文件不变。八篇应用内帮助正文和 Phase4 原始证据不变，四篇理论与 Host 架构保持原路径和章节身份。
- 相邻 11 个外部插件仓库的工作树状态与整理前一致；原有外部修改未触碰。最终范围检查没有代码、配置、测试、API 基线或外部仓库改动。
- `git diff --check` 通过。

### 主仓 Gate

在本仓根执行 `dotnet run --project tools/MyAvaloniaManagement.Gate -- verify`，一轮 `scope=all` 通过；locked restore、Release 零警告/零错误构建、契约检查、打包及真实 ZIP 验收均通过。

| 测试组 | 通过 | 失败 | 跳过 |
| --- | --- | --- | --- |
| host-plugin | 218 | 0 | 0 |
| host-ui | 117 | 0 | 0 |
| host-unit | 401 | 0 | 0 |
| my-plug-test | 11 | 0 | 0 |
| my-plug-test-package | 1 | 0 | 0 |
| sdk | 91 | 0 | 0 |
| 合计 | 839 | 0 | 0 |

- 运行 ID：`20260916-020248-80e4129ef78f`；完成时间：`2026-09-16T02:04:18.6034192+00:00`。
- 原始证据位置：`artifacts/gate/20260916-020248-80e4129ef78f/summary.json` 及相邻日志、TRX。它们属于可清理的本地构建产物，本记录保留结论而不把下载链接作为长期证据入口。
- summary SHA-256：`b815ca6c92c0034ffbf8cc1a15cb49372b0b80ee330264032202513b25782633`。
- Gate 记录的工作树摘要：`5524FCE6CAED565FC9F94EFF0A8F49F048497372B75126E1958C652C6589C70A`。验证对象为含文档整理的未提交工作树，不能只用基线 HEAD 代表全部输入。
- 未运行 seal、覆盖率或真实 Windows Smoke；Host 发布资格仍为 false，没有公共发布、安装目录部署或外部业务验收。

### 最后事实修正后的帮助回归

全量 Gate 后继续修正理论文章中的 Scope、Tool 与 Layout 当前实现映射，随后执行：

```powershell
dotnet test Host/MyAvaloniaManagement.Tests/MyAvaloniaManagement.Tests.csproj -c Release --no-restore -m:1 -warnaserror --filter FullyQualifiedName~HelpContentTests --results-directory artifacts/documentation-audit/20260916 --logger 'trx;LogFileName=help-content.trx'
```

依赖及 Host 帮助资源重新编译通过，10 项帮助内容测试全部通过，失败/跳过为零，覆盖八章节读取、四篇理论原文及架构原文嵌入、跨文档链接和渲染。TRX 位于 `artifacts/documentation-audit/20260916/help-content.trx`。最终再运行全量路径/锚点和范围扫描。

本节在验证完成后回填，不把验收记录自身的新增文字倒写成已参与先前 Gate 的输入。

## 整理与删除摘要

- 84 份原阶段记录迁入 records，15 份原方案/评审迁入 plans，Document V1 与 Layout V1 两份旧格式迁入 specifications。归档入口与详细去向见下表。
- Document V2 与主仓测试说明分别迁到 reference 与 maintenance，并按当前事实维护。
- 原外部插件综合指南删除，开发内容由[创建插件指南](../../../quick-start/create-managed-plugin.md)承接，发布内容由[NuGet 维护](../../../maintenance/nuget-release.md)承接，独有版本/发布日期保存在[分发快照](../plugin-packaging/distribution-baseline-20260828.md)。
- 原 G3.1 SDK 发布短文删除，全文合并为[协议一致性记录的 SDK 发布补充](../workflow-action/g3.1-workflow-protocol-consistency.md#sdk-发布补充)。除此之外不因“过时”删除原方案或阶段结论。
- 最终 157 份 Markdown 中，104 份位于 archive；其余包括当前指南、包/模板说明、应用帮助、理论与原位 Phase4 记录。总文件数包含新增导航和处置记录；整理目标是缩小日常维护入口。

## 原始文档处置清单

| 原路径（基线位置） | 处置 | 当前入口 / 承接位置 |
| --- | --- | --- |
| `Host/MyAvaloniaManagement.Icons/README.md` | 保留原位，按当前事实维护 | [Host/MyAvaloniaManagement.Icons/README.md](../../../../Host/MyAvaloniaManagement.Icons/README.md) |
| `Host/MyAvaloniaManagement.PluginSdk.UI/README.md` | 保留原位，按当前事实维护 | [Host/MyAvaloniaManagement.PluginSdk.UI/README.md](../../../../Host/MyAvaloniaManagement.PluginSdk.UI/README.md) |
| `Host/MyAvaloniaManagement.PluginSdk.Workflow/README.md` | 保留原位，按当前事实维护 | [Host/MyAvaloniaManagement.PluginSdk.Workflow/README.md](../../../../Host/MyAvaloniaManagement.PluginSdk.Workflow/README.md) |
| `Host/MyAvaloniaManagement.PluginSdk/README.md` | 保留原位，按当前事实维护 | [Host/MyAvaloniaManagement.PluginSdk/README.md](../../../../Host/MyAvaloniaManagement.PluginSdk/README.md) |
| `Host/MyAvaloniaManagement/HelpContent/activity.md` | 保留原位 | [Host/MyAvaloniaManagement/HelpContent/activity.md](../../../../Host/MyAvaloniaManagement/HelpContent/activity.md) |
| `Host/MyAvaloniaManagement/HelpContent/architecture.md` | 保留原位 | [Host/MyAvaloniaManagement/HelpContent/architecture.md](../../../../Host/MyAvaloniaManagement/HelpContent/architecture.md) |
| `Host/MyAvaloniaManagement/HelpContent/attention.md` | 保留原位 | [Host/MyAvaloniaManagement/HelpContent/attention.md](../../../../Host/MyAvaloniaManagement/HelpContent/attention.md) |
| `Host/MyAvaloniaManagement/HelpContent/forkable.md` | 保留原位 | [Host/MyAvaloniaManagement/HelpContent/forkable.md](../../../../Host/MyAvaloniaManagement/HelpContent/forkable.md) |
| `Host/MyAvaloniaManagement/HelpContent/product.md` | 保留原位 | [Host/MyAvaloniaManagement/HelpContent/product.md](../../../../Host/MyAvaloniaManagement/HelpContent/product.md) |
| `Host/MyAvaloniaManagement/HelpContent/production.md` | 保留原位 | [Host/MyAvaloniaManagement/HelpContent/production.md](../../../../Host/MyAvaloniaManagement/HelpContent/production.md) |
| `Host/MyAvaloniaManagement/HelpContent/references.md` | 保留原位 | [Host/MyAvaloniaManagement/HelpContent/references.md](../../../../Host/MyAvaloniaManagement/HelpContent/references.md) |
| `Host/MyAvaloniaManagement/HelpContent/workbench.md` | 保留原位 | [Host/MyAvaloniaManagement/HelpContent/workbench.md](../../../../Host/MyAvaloniaManagement/HelpContent/workbench.md) |
| `Host/MyAvaloniaManagement/docs/README.md` | 保留原位，按当前事实维护 | [Host/MyAvaloniaManagement/docs/README.md](../../../../Host/MyAvaloniaManagement/docs/README.md) |
| `Host/MyAvaloniaManagement/docs/design/architecture.md` | 保留原位，按当前事实维护 | [Host/MyAvaloniaManagement/docs/design/architecture.md](../../../../Host/MyAvaloniaManagement/docs/design/architecture.md) |
| `Host/MyAvaloniaManagement/docs/design/design-methodology-and-tradeoffs.md` | 保留原位，按当前事实维护 | [Host/MyAvaloniaManagement/docs/design/design-methodology-and-tradeoffs.md](../../../../Host/MyAvaloniaManagement/docs/design/design-methodology-and-tradeoffs.md) |
| `Host/MyAvaloniaManagement/docs/reference/compatibility-contracts.md` | 保留原位，按当前事实维护 | [Host/MyAvaloniaManagement/docs/reference/compatibility-contracts.md](../../../../Host/MyAvaloniaManagement/docs/reference/compatibility-contracts.md) |
| `Packaging/MyAvaloniaManagement.Plugin.Build/README.md` | 保留原位，按当前事实维护 | [Packaging/MyAvaloniaManagement.Plugin.Build/README.md](../../../../Packaging/MyAvaloniaManagement.Plugin.Build/README.md) |
| `Packaging/MyAvaloniaManagement.Plugin.Templates/README.md` | 保留原位，按当前事实维护 | [Packaging/MyAvaloniaManagement.Plugin.Templates/README.md](../../../../Packaging/MyAvaloniaManagement.Plugin.Templates/README.md) |
| `Packaging/MyAvaloniaManagement.Plugin.Templates/content/myavalonia-plugin/README.md` | 保留原位，按当前事实维护 | [Packaging/MyAvaloniaManagement.Plugin.Templates/content/myavalonia-plugin/README.md](../../../../Packaging/MyAvaloniaManagement.Plugin.Templates/content/myavalonia-plugin/README.md) |
| `Packaging/MyAvaloniaManagement.Plugin.Templates/content/myavalonia-plugin/docs/README.md` | 保留原位，按当前事实维护 | [Packaging/MyAvaloniaManagement.Plugin.Templates/content/myavalonia-plugin/docs/README.md](../../../../Packaging/MyAvaloniaManagement.Plugin.Templates/content/myavalonia-plugin/docs/README.md) |
| `Packaging/MyAvaloniaManagement.Plugin.Templates/content/myavalonia-plugin/docs/deployment-and-release.md` | 保留原位，按当前事实维护 | [Packaging/MyAvaloniaManagement.Plugin.Templates/content/myavalonia-plugin/docs/deployment-and-release.md](../../../../Packaging/MyAvaloniaManagement.Plugin.Templates/content/myavalonia-plugin/docs/deployment-and-release.md) |
| `Packaging/MyAvaloniaManagement.Plugin.Templates/content/myavalonia-plugin/docs/plugin-icons.md` | 保留原位，按当前事实维护 | [Packaging/MyAvaloniaManagement.Plugin.Templates/content/myavalonia-plugin/docs/plugin-icons.md](../../../../Packaging/MyAvaloniaManagement.Plugin.Templates/content/myavalonia-plugin/docs/plugin-icons.md) |
| `Packaging/MyAvaloniaManagement.Plugin.Templates/content/myavalonia-plugin/docs/project-and-window-responsibilities.md` | 保留原位，按当前事实维护 | [Packaging/MyAvaloniaManagement.Plugin.Templates/content/myavalonia-plugin/docs/project-and-window-responsibilities.md](../../../../Packaging/MyAvaloniaManagement.Plugin.Templates/content/myavalonia-plugin/docs/project-and-window-responsibilities.md) |
| `Packaging/MyAvaloniaManagement.Plugin.Templates/content/myavalonia-plugin/docs/workbench-commands.md` | 保留原位，按当前事实维护 | [Packaging/MyAvaloniaManagement.Plugin.Templates/content/myavalonia-plugin/docs/workbench-commands.md](../../../../Packaging/MyAvaloniaManagement.Plugin.Templates/content/myavalonia-plugin/docs/workbench-commands.md) |
| `Packaging/MyAvaloniaManagement.Plugin.Templates/content/myavalonia-plugin/docs/workflow-actions.md` | 保留原位，按当前事实维护 | [Packaging/MyAvaloniaManagement.Plugin.Templates/content/myavalonia-plugin/docs/workflow-actions.md](../../../../Packaging/MyAvaloniaManagement.Plugin.Templates/content/myavalonia-plugin/docs/workflow-actions.md) |
| `README.md` | 保留原位，按当前事实维护 | [README.md](../../../../README.md) |
| `TestResults/Phase4/NO-GO.md` | 保留原位 | [TestResults/Phase4/NO-GO.md](../../../../TestResults/Phase4/NO-GO.md) |
| `docs/README.md` | 保留原位，按当前事实维护 | [docs/README.md](../../../README.md) |
| `docs/design/ai-workflow-plugin-exploration.md` | 归档保留 | [docs/archive/plans/ai-workflow-plugin-exploration.md](../../plans/ai-workflow-plugin-exploration.md) |
| `docs/design/avalonia-dock-upgrade-assessment-20260916.md` | 归档保留 | [docs/archive/plans/avalonia-dock-upgrade-assessment-20260916.md](../../plans/avalonia-dock-upgrade-assessment-20260916.md) |
| `docs/design/document-persistence-v1-design.md` | 归档保留 | [docs/archive/specifications/document-persistence-v1-design.md](../../specifications/document-persistence-v1-design.md) |
| `docs/design/document-persistence-v2-design.md` | 迁移并修正 | [docs/reference/document-persistence.md](../../../reference/document-persistence.md) |
| `docs/design/external-managed-plugin-development-and-installation-plan.md` | 拆分合并后删除原文件 | [docs/maintenance/nuget-release.md](../../../maintenance/nuget-release.md) |
| `docs/design/host-plugin-architecture-review.md` | 归档保留 | [docs/archive/plans/host-plugin-architecture-review.md](../../plans/host-plugin-architecture-review.md) |
| `docs/design/host-v1-sealing-readiness-plan.md` | 归档保留 | [docs/archive/plans/host-v1-sealing-readiness-plan.md](../../plans/host-v1-sealing-readiness-plan.md) |
| `docs/design/host-v2-breaking-refactor-plan.md` | 归档保留 | [docs/archive/plans/host-v2-breaking-refactor-plan.md](../../plans/host-v2-breaking-refactor-plan.md) |
| `docs/design/host-v3-breaking-refactor-plan.md` | 归档保留 | [docs/archive/plans/host-v3-breaking-refactor-plan.md](../../plans/host-v3-breaking-refactor-plan.md) |
| `docs/design/host-v4-breaking-refactor-plan.md` | 归档保留 | [docs/archive/plans/host-v4-breaking-refactor-plan.md](../../plans/host-v4-breaking-refactor-plan.md) |
| `docs/design/host-v5-lifecycle-ownership-repair-plan.md` | 归档保留 | [docs/archive/plans/host-v5-lifecycle-ownership-repair-plan.md](../../plans/host-v5-lifecycle-ownership-repair-plan.md) |
| `docs/design/host-v6-plugin-navigation-and-function-center-plan.md` | 归档保留 | [docs/archive/plans/host-v6-plugin-navigation-and-function-center-plan.md](../../plans/host-v6-plugin-navigation-and-function-center-plan.md) |
| `docs/design/host-v6.1-extensible-icon-contributions-plan.md` | 归档保留 | [docs/archive/plans/host-v6.1-extensible-icon-contributions-plan.md](../../plans/host-v6.1-extensible-icon-contributions-plan.md) |
| `docs/design/host-v7-tool-center-and-visibility-plan.md` | 归档保留 | [docs/archive/plans/host-v7-tool-center-and-visibility-plan.md](../../plans/host-v7-tool-center-and-visibility-plan.md) |
| `docs/design/host-v8-cognitive-ux-convergence-plan.md` | 归档保留 | [docs/archive/plans/host-v8-cognitive-ux-convergence-plan.md](../../plans/host-v8-cognitive-ux-convergence-plan.md) |
| `docs/design/host-v9-avalonia-dock-upgrade-plan.md` | 归档保留 | [docs/archive/plans/host-v9-avalonia-dock-upgrade-plan.md](../../plans/host-v9-avalonia-dock-upgrade-plan.md) |
| `docs/design/plugin-status-window.md` | 归档保留 | [docs/archive/plans/plugin-status-window.md](../../plans/plugin-status-window.md) |
| `docs/design/workbench-command-introduction-plan.md` | 归档保留 | [docs/archive/plans/workbench-command-introduction-plan.md](../../plans/workbench-command-introduction-plan.md) |
| `docs/plan-history/host-v1/g0-green-baseline.md` | 归档保留 | [docs/archive/records/host-v1/g0-green-baseline.md](../host-v1/g0-green-baseline.md) |
| `docs/plan-history/host-v1/g1-support-boundary-and-version-lines.md` | 归档保留 | [docs/archive/records/host-v1/g1-support-boundary-and-version-lines.md](../host-v1/g1-support-boundary-and-version-lines.md) |
| `docs/plan-history/host-v1/g10-host-internal-coordination.md` | 归档保留 | [docs/archive/records/host-v1/g10-host-internal-coordination.md](../host-v1/g10-host-internal-coordination.md) |
| `docs/plan-history/host-v1/g11-low-value-public-surface-cleanup.md` | 归档保留 | [docs/archive/records/host-v1/g11-low-value-public-surface-cleanup.md](../host-v1/g11-low-value-public-surface-cleanup.md) |
| `docs/plan-history/host-v1/g12-unified-plugin-build-and-deployment.md` | 归档保留 | [docs/archive/records/host-v1/g12-unified-plugin-build-and-deployment.md](../host-v1/g12-unified-plugin-build-and-deployment.md) |
| `docs/plan-history/host-v1/g13-plugin-sdk-api-compatibility-baseline.md` | 归档保留 | [docs/archive/records/host-v1/g13-plugin-sdk-api-compatibility-baseline.md](../host-v1/g13-plugin-sdk-api-compatibility-baseline.md) |
| `docs/plan-history/host-v1/g14-windows-release-gate.md` | 归档保留 | [docs/archive/records/host-v1/g14-windows-release-gate.md](../host-v1/g14-windows-release-gate.md) |
| `docs/plan-history/host-v1/g15-host-diagnostic-redaction.md` | 归档保留 | [docs/archive/records/host-v1/g15-host-diagnostic-redaction.md](../host-v1/g15-host-diagnostic-redaction.md) |
| `docs/plan-history/host-v1/g16-documentation-and-v1-baseline.md` | 归档保留 | [docs/archive/records/host-v1/g16-documentation-and-v1-baseline.md](../host-v1/g16-documentation-and-v1-baseline.md) |
| `docs/plan-history/host-v1/g2-host-api-surface.md` | 归档保留 | [docs/archive/records/host-v1/g2-host-api-surface.md](../host-v1/g2-host-api-surface.md) |
| `docs/plan-history/host-v1/g3-plugin-sdk-and-ui-profile.md` | 归档保留 | [docs/archive/records/host-v1/g3-plugin-sdk-and-ui-profile.md](../host-v1/g3-plugin-sdk-and-ui-profile.md) |
| `docs/plan-history/host-v1/g4-managed-only-plugin-loading.md` | 归档保留 | [docs/archive/records/host-v1/g4-managed-only-plugin-loading.md](../host-v1/g4-managed-only-plugin-loading.md) |
| `docs/plan-history/host-v1/g5-explicit-contributions-and-plugin-registry.md` | 归档保留 | [docs/archive/records/host-v1/g5-explicit-contributions-and-plugin-registry.md](../host-v1/g5-explicit-contributions-and-plugin-registry.md) |
| `docs/plan-history/host-v1/g6-host-di-protection.md` | 归档保留 | [docs/archive/records/host-v1/g6-host-di-protection.md](../host-v1/g6-host-di-protection.md) |
| `docs/plan-history/host-v1/g7-document-envelope-v1.md` | 归档保留 | [docs/archive/records/host-v1/g7-document-envelope-v1.md](../host-v1/g7-document-envelope-v1.md) |
| `docs/plan-history/host-v1/g8-document-content-persistence-contract.md` | 归档保留 | [docs/archive/records/host-v1/g8-document-content-persistence-contract.md](../host-v1/g8-document-content-persistence-contract.md) |
| `docs/plan-history/host-v1/g9-sdk-event-bus.md` | 归档保留 | [docs/archive/records/host-v1/g9-sdk-event-bus.md](../host-v1/g9-sdk-event-bus.md) |
| `docs/plan-history/host-v2/g0-green-baseline.md` | 归档保留 | [docs/archive/records/host-v2/g0-green-baseline.md](../host-v2/g0-green-baseline.md) |
| `docs/plan-history/host-v2/g1-version-and-data-boundaries.md` | 归档保留 | [docs/archive/records/host-v2/g1-version-and-data-boundaries.md](../host-v2/g1-version-and-data-boundaries.md) |
| `docs/plan-history/host-v2/g13-remove-v1-production-surface.md` | 归档保留 | [docs/archive/records/host-v2/g13-remove-v1-production-surface.md](../host-v2/g13-remove-v1-production-surface.md) |
| `docs/plan-history/host-v2/g14-v2-sealing.md` | 归档保留 | [docs/archive/records/host-v2/g14-v2-sealing.md](../host-v2/g14-v2-sealing.md) |
| `docs/plan-history/host-v2/g2-plugin-sdk-rebuild.md` | 归档保留 | [docs/archive/records/host-v2/g2-plugin-sdk-rebuild.md](../host-v2/g2-plugin-sdk-rebuild.md) |
| `docs/plan-history/host-v2/g3-manifest-v2-and-build-protocol.md` | 归档保留 | [docs/archive/records/host-v2/g3-manifest-v2-and-build-protocol.md](../host-v2/g3-manifest-v2-and-build-protocol.md) |
| `docs/plan-history/host-v2/g4-per-plugin-containers.md` | 归档保留 | [docs/archive/records/host-v2/g4-per-plugin-containers.md](../host-v2/g4-per-plugin-containers.md) |
| `docs/plan-history/host-v2/g5-declarative-contribution-catalog.md` | 归档保留 | [docs/archive/records/host-v2/g5-declarative-contribution-catalog.md](../host-v2/g5-declarative-contribution-catalog.md) |
| `docs/plan-history/host-v2/g6-host-dock-adapter.md` | 归档保留 | [docs/archive/records/host-v2/g6-host-dock-adapter.md](../host-v2/g6-host-dock-adapter.md) |
| `docs/plan-history/host-v2/g7-document-v2.md` | 归档保留 | [docs/archive/records/host-v2/g7-document-v2.md](../host-v2/g7-document-v2.md) |
| `docs/plan-history/host-v2/g8-layout-and-lifecycle-v2.md` | 归档保留 | [docs/archive/records/host-v2/g8-layout-and-lifecycle-v2.md](../host-v2/g8-layout-and-lifecycle-v2.md) |
| `docs/plan-history/host-v2/g9-my-plug-test-v2.md` | 归档保留 | [docs/archive/records/host-v2/g9-my-plug-test-v2.md](../host-v2/g9-my-plug-test-v2.md) |
| `docs/plan-history/host-v3/g0-green-baseline.md` | 归档保留 | [docs/archive/records/host-v3/g0-green-baseline.md](../host-v3/g0-green-baseline.md) |
| `docs/plan-history/host-v3/g1-version-and-data-boundaries.md` | 归档保留 | [docs/archive/records/host-v3/g1-version-and-data-boundaries.md](../host-v3/g1-version-and-data-boundaries.md) |
| `docs/plan-history/host-v3/g13-remove-v2-production-surface.md` | 归档保留 | [docs/archive/records/host-v3/g13-remove-v2-production-surface.md](../host-v3/g13-remove-v2-production-surface.md) |
| `docs/plan-history/host-v3/g14-v3-sealing.md` | 归档保留 | [docs/archive/records/host-v3/g14-v3-sealing.md](../host-v3/g14-v3-sealing.md) |
| `docs/plan-history/host-v3/g2-revisioned-document-save.md` | 归档保留 | [docs/archive/records/host-v3/g2-revisioned-document-save.md](../host-v3/g2-revisioned-document-save.md) |
| `docs/plan-history/host-v3/g3-exclusive-document-activation.md` | 归档保留 | [docs/archive/records/host-v3/g3-exclusive-document-activation.md](../host-v3/g3-exclusive-document-activation.md) |
| `docs/plan-history/host-v3/g4-plugin-registration-ownership.md` | 归档保留 | [docs/archive/records/host-v3/g4-plugin-registration-ownership.md](../host-v3/g4-plugin-registration-ownership.md) |
| `docs/plan-history/host-v3/g5-plugin-private-messaging.md` | 归档保留 | [docs/archive/records/host-v3/g5-plugin-private-messaging.md](../host-v3/g5-plugin-private-messaging.md) |
| `docs/plan-history/host-v3/g6-workspace-session-and-dock-factory.md` | 归档保留 | [docs/archive/records/host-v3/g6-workspace-session-and-dock-factory.md](../host-v3/g6-workspace-session-and-dock-factory.md) |
| `docs/plan-history/host-v3/g7-host-catalog-and-plugin-registry.md` | 归档保留 | [docs/archive/records/host-v3/g7-host-catalog-and-plugin-registry.md](../host-v3/g7-host-catalog-and-plugin-registry.md) |
| `docs/plan-history/host-v3/g8-fullscreen-lease-and-host-v3-skeleton.md` | 归档保留 | [docs/archive/records/host-v3/g8-fullscreen-lease-and-host-v3-skeleton.md](../host-v3/g8-fullscreen-lease-and-host-v3-skeleton.md) |
| `docs/plan-history/host-v3/g9-my-plug-test-v3-acceptance.md` | 归档保留 | [docs/archive/records/host-v3/g9-my-plug-test-v3-acceptance.md](../host-v3/g9-my-plug-test-v3-acceptance.md) |
| `docs/plan-history/host-v4/g0-v3-source-baseline.md` | 归档保留 | [docs/archive/records/host-v4/g0-v3-source-baseline.md](../host-v4/g0-v3-source-baseline.md) |
| `docs/plan-history/host-v4/g1-remove-dead-host-surface.md` | 归档保留 | [docs/archive/records/host-v4/g1-remove-dead-host-surface.md](../host-v4/g1-remove-dead-host-surface.md) |
| `docs/plan-history/host-v4/g2-strongly-typed-identity-and-use-case-entry.md` | 归档保留 | [docs/archive/records/host-v4/g2-strongly-typed-identity-and-use-case-entry.md](../host-v4/g2-strongly-typed-identity-and-use-case-entry.md) |
| `docs/plan-history/host-v4/g3-layout-responsibility-alignment.md` | 归档保留 | [docs/archive/records/host-v4/g3-layout-responsibility-alignment.md](../host-v4/g3-layout-responsibility-alignment.md) |
| `docs/plan-history/host-v4/g4-document-control-recycling-ownership.md` | 归档保留 | [docs/archive/records/host-v4/g4-document-control-recycling-ownership.md](../host-v4/g4-document-control-recycling-ownership.md) |
| `docs/plan-history/host-v4/g5-domain-helper-migration.md` | 归档保留 | [docs/archive/records/host-v4/g5-domain-helper-migration.md](../host-v4/g5-domain-helper-migration.md) |
| `docs/plan-history/host-v4/g6-file-system-path-and-presentation-model.md` | 归档保留 | [docs/archive/records/host-v4/g6-file-system-path-and-presentation-model.md](../host-v4/g6-file-system-path-and-presentation-model.md) |
| `docs/plan-history/host-v4/g7-four-plugins-harness-documentation-regression.md` | 归档保留 | [docs/archive/records/host-v4/g7-four-plugins-harness-documentation-regression.md](../host-v4/g7-four-plugins-harness-documentation-regression.md) |
| `docs/plan-history/host-v4/g8-v4-sealing.md` | 归档保留 | [docs/archive/records/host-v4/g8-v4-sealing.md](../host-v4/g8-v4-sealing.md) |
| `docs/plan-history/host-v5/lifecycle-ownership-repair-acceptance.md` | 归档保留 | [docs/archive/records/host-v5/lifecycle-ownership-repair-acceptance.md](../host-v5/lifecycle-ownership-repair-acceptance.md) |
| `docs/plan-history/host-v6.1/external-plugin-icons-upgrade.md` | 归档保留 | [docs/archive/records/host-v6.1/external-plugin-icons-upgrade.md](../host-v6.1/external-plugin-icons-upgrade.md) |
| `docs/plan-history/host-v6.1/icon-contributions-and-resources-acceptance.md` | 归档保留 | [docs/archive/records/host-v6.1/icon-contributions-and-resources-acceptance.md](../host-v6.1/icon-contributions-and-resources-acceptance.md) |
| `docs/plan-history/host-v6/plugin-navigation-and-function-center-acceptance.md` | 归档保留 | [docs/archive/records/host-v6/plugin-navigation-and-function-center-acceptance.md](../host-v6/plugin-navigation-and-function-center-acceptance.md) |
| `docs/plan-history/host-v7/tool-center-and-visibility-acceptance.md` | 归档保留 | [docs/archive/records/host-v7/tool-center-and-visibility-acceptance.md](../host-v7/tool-center-and-visibility-acceptance.md) |
| `docs/plan-history/host-v8/cognitive-ux-convergence-acceptance.md` | 归档保留 | [docs/archive/records/host-v8/cognitive-ux-convergence-acceptance.md](../host-v8/cognitive-ux-convergence-acceptance.md) |
| `docs/plan-history/host-v8/local-deployment-20260912.md` | 归档保留 | [docs/archive/records/host-v8/local-deployment-20260912.md](../host-v8/local-deployment-20260912.md) |
| `docs/plan-history/host-v9/development-acceptance.md` | 归档保留 | [docs/archive/records/host-v9/development-acceptance.md](../host-v9/development-acceptance.md) |
| `docs/plan-history/host-v9/generated-artifacts-cleanup.md` | 归档保留 | [docs/archive/records/host-v9/generated-artifacts-cleanup.md](../host-v9/generated-artifacts-cleanup.md) |
| `docs/plan-history/host-v9/nuget-unified-3.4.1-release.md` | 归档保留 | [docs/archive/records/host-v9/nuget-unified-3.4.1-release.md](../host-v9/nuget-unified-3.4.1-release.md) |
| `docs/plan-history/net10/phase-0-baseline.md` | 归档保留 | [docs/archive/records/net10/phase-0-baseline.md](../net10/phase-0-baseline.md) |
| `docs/plan-history/net10/phase-1-governance.md` | 归档保留 | [docs/archive/records/net10/phase-1-governance.md](../net10/phase-1-governance.md) |
| `docs/plan-history/net10/phase-2-net10-foundation.md` | 归档保留 | [docs/archive/records/net10/phase-2-net10-foundation.md](../net10/phase-2-net10-foundation.md) |
| `docs/plan-history/net10/phase-3-plugin-dependencies.md` | 归档保留 | [docs/archive/records/net10/phase-3-plugin-dependencies.md](../net10/phase-3-plugin-dependencies.md) |
| `docs/plan-history/net10/phase-4-avalonia12-libvlc-gate.md` | 归档保留 | [docs/archive/records/net10/phase-4-avalonia12-libvlc-gate.md](../net10/phase-4-avalonia12-libvlc-gate.md) |
| `docs/plan-history/plugin-status/window-acceptance.md` | 归档保留 | [docs/archive/records/plugin-status/window-acceptance.md](../plugin-status/window-acceptance.md) |
| `docs/plan-history/workbench-command/g0-facts-semantics-public-api.md` | 归档保留 | [docs/archive/records/workbench-command/g0-facts-semantics-public-api.md](../workbench-command/g0-facts-semantics-public-api.md) |
| `docs/plan-history/workbench-command/g1-command-contracts-registration-declarations.md` | 归档保留 | [docs/archive/records/workbench-command/g1-command-contracts-registration-declarations.md](../workbench-command/g1-command-contracts-registration-declarations.md) |
| `docs/plan-history/workbench-command/g10-cross-repository-integration-sealing.md` | 归档保留 | [docs/archive/records/workbench-command/g10-cross-repository-integration-sealing.md](../workbench-command/g10-cross-repository-integration-sealing.md) |
| `docs/plan-history/workbench-command/g2-command-catalog-executor.md` | 归档保留 | [docs/archive/records/workbench-command/g2-command-catalog-executor.md](../workbench-command/g2-command-catalog-executor.md) |
| `docs/plan-history/workbench-command/g3-context-active-document-target-routing.md` | 归档保留 | [docs/archive/records/workbench-command/g3-context-active-document-target-routing.md](../workbench-command/g3-context-active-document-target-routing.md) |
| `docs/plan-history/workbench-command/g4-host-open-save-presentation-loop.md` | 归档保留 | [docs/archive/records/workbench-command/g4-host-open-save-presentation-loop.md](../workbench-command/g4-host-open-save-presentation-loop.md) |
| `docs/plan-history/workbench-command/g5-declarative-menu-keybinding-projection.md` | 归档保留 | [docs/archive/records/workbench-command/g5-declarative-menu-keybinding-projection.md](../workbench-command/g5-declarative-menu-keybinding-projection.md) |
| `docs/plan-history/workbench-command/g6-sdk-candidate-template-independent-consumption.md` | 归档保留 | [docs/archive/records/workbench-command/g6-sdk-candidate-template-independent-consumption.md](../workbench-command/g6-sdk-candidate-template-independent-consumption.md) |
| `docs/plan-history/workbench-command/g7-workflow-studio-three-real-commands.md` | 归档保留 | [docs/archive/records/workbench-command/g7-workflow-studio-three-real-commands.md](../workbench-command/g7-workflow-studio-three-real-commands.md) |
| `docs/plan-history/workbench-command/g8-classic-game-multi-instance-commands.md` | 归档保留 | [docs/archive/records/workbench-command/g8-classic-game-multi-instance-commands.md](../workbench-command/g8-classic-game-multi-instance-commands.md) |
| `docs/plan-history/workbench-command/g9-minimal-command-palette.md` | 归档保留 | [docs/archive/records/workbench-command/g9-minimal-command-palette.md](../workbench-command/g9-minimal-command-palette.md) |
| `docs/plan-history/workflow-action/g0-facts-naming-repositories-sdk-compatibility.md` | 归档保留 | [docs/archive/records/workflow-action/g0-facts-naming-repositories-sdk-compatibility.md](../workflow-action/g0-facts-naming-repositories-sdk-compatibility.md) |
| `docs/plan-history/workflow-action/g1-host-workflow-action-kernel.md` | 归档保留 | [docs/archive/records/workflow-action/g1-host-workflow-action-kernel.md](../workflow-action/g1-host-workflow-action-kernel.md) |
| `docs/plan-history/workflow-action/g2-sdk-build-external-template-propagation.md` | 归档保留 | [docs/archive/records/workflow-action/g2-sdk-build-external-template-propagation.md](../workflow-action/g2-sdk-build-external-template-propagation.md) |
| `docs/plan-history/workflow-action/g3-workflow-studio-fake-action-loop.md` | 归档保留 | [docs/archive/records/workflow-action/g3-workflow-studio-fake-action-loop.md](../workflow-action/g3-workflow-studio-fake-action-loop.md) |
| `docs/plan-history/workflow-action/g3.1-template-1.2-publication.md` | 归档保留 | [docs/archive/records/workflow-action/g3.1-template-1.2-publication.md](../workflow-action/g3.1-template-1.2-publication.md) |
| `docs/plan-history/workflow-action/g3.1-workflow-protocol-consistency.md` | 归档保留 | [docs/archive/records/workflow-action/g3.1-workflow-protocol-consistency.md](../workflow-action/g3.1-workflow-protocol-consistency.md) |
| `docs/plan-history/workflow-action/g5-dual-role-governance.md` | 归档保留 | [docs/archive/records/workflow-action/g5-dual-role-governance.md](../workflow-action/g5-dual-role-governance.md) |
| `docs/quick-start/README.md` | 保留原位，按当前事实维护 | [docs/quick-start/README.md](../../../quick-start/README.md) |
| `docs/quick-start/add-document-and-tool.md` | 保留原位，按当前事实维护 | [docs/quick-start/add-document-and-tool.md](../../../quick-start/add-document-and-tool.md) |
| `docs/quick-start/create-managed-plugin.md` | 保留原位，按当前事实维护 | [docs/quick-start/create-managed-plugin.md](../../../quick-start/create-managed-plugin.md) |
| `docs/quick-start/host-v9-upgrade.md` | 保留原位，按当前事实维护 | [docs/quick-start/host-v9-upgrade.md](../../../quick-start/host-v9-upgrade.md) |
| `docs/quick-start/macos-experiment.md` | 保留原位，按当前事实维护 | [docs/quick-start/macos-experiment.md](../../../quick-start/macos-experiment.md) |
| `docs/quick-start/plugin-icons.md` | 保留原位，按当前事实维护 | [docs/quick-start/plugin-icons.md](../../../quick-start/plugin-icons.md) |
| `docs/quick-start/plugin-navigation-and-function-center.md` | 保留原位，按当前事实维护 | [docs/quick-start/plugin-navigation-and-function-center.md](../../../quick-start/plugin-navigation-and-function-center.md) |
| `docs/quick-start/plugin-status.md` | 保留原位，按当前事实维护 | [docs/quick-start/plugin-status.md](../../../quick-start/plugin-status.md) |
| `docs/quick-start/tool-center.md` | 保留原位，按当前事实维护 | [docs/quick-start/tool-center.md](../../../quick-start/tool-center.md) |
| `docs/quick-start/verification-and-troubleshooting.md` | 保留原位，按当前事实维护 | [docs/quick-start/verification-and-troubleshooting.md](../../../quick-start/verification-and-troubleshooting.md) |
| `docs/quick-start/workbench-search.md` | 保留原位，按当前事实维护 | [docs/quick-start/workbench-search.md](../../../quick-start/workbench-search.md) |
| `docs/quick-start/workflow-action-development.md` | 保留原位，按当前事实维护 | [docs/quick-start/workflow-action-development.md](../../../quick-start/workflow-action-development.md) |
| `docs/quick-start/workflow-sdk-publication.md` | 合并后删除原文件 | [docs/archive/records/workflow-action/g3.1-workflow-protocol-consistency.md](../workflow-action/g3.1-workflow-protocol-consistency.md#sdk-发布补充) |
| `docs/reference/dock-layout-snapshot-v1.md` | 归档保留 | [docs/archive/specifications/dock-layout-snapshot-v1.md](../../specifications/dock-layout-snapshot-v1.md) |
| `docs/reference/dock-layout-snapshot-v2.md` | 保留原位，按当前事实维护 | [docs/reference/dock-layout-snapshot-v2.md](../../../reference/dock-layout-snapshot-v2.md) |
| `docs/reference/myavalonia-management-tests.md` | 迁移并修正 | [docs/maintenance/verification.md](../../../maintenance/verification.md) |
| `docs/reference/plugin-sdk-api-compatibility.md` | 保留原位，按当前事实维护 | [docs/reference/plugin-sdk-api-compatibility.md](../../../reference/plugin-sdk-api-compatibility.md) |
| `docs/theory/activity-theory-requirements-decomposition.md` | 保留原位，按当前事实维护 | [docs/theory/activity-theory-requirements-decomposition.md](../../../theory/activity-theory-requirements-decomposition.md) |
| `docs/theory/ai-enabled-forkable-software-bases.md` | 保留原位，按当前事实维护 | [docs/theory/ai-enabled-forkable-software-bases.md](../../../theory/ai-enabled-forkable-software-bases.md) |
| `docs/theory/attention-centered-dock-workspace-design.md` | 保留原位，按当前事实维护 | [docs/theory/attention-centered-dock-workspace-design.md](../../../theory/attention-centered-dock-workspace-design.md) |
| `docs/theory/约束驱动的软件工业化.md` | 保留原位，按当前事实维护 | [docs/theory/约束驱动的软件工业化.md](../../../theory/约束驱动的软件工业化.md) |

## 关联证据迁移

以下文件只移动，SHA-256 与整理前完全一致：

| 原路径 | 归档位置 |
| --- | --- |
| `docs/plan-history/host-v6/images/function-center-dark.png` | [docs/archive/records/host-v6/images/function-center-dark.png](../host-v6/images/function-center-dark.png) |
| `docs/plan-history/host-v6/images/function-center-light.png` | [docs/archive/records/host-v6/images/function-center-light.png](../host-v6/images/function-center-light.png) |
| `docs/plan-history/host-v6/images/tool-legacy-light.png` | [docs/archive/records/host-v6/images/tool-legacy-light.png](../host-v6/images/tool-legacy-light.png) |
| `docs/plan-history/host-v6/images/tool-tree-dark.png` | [docs/archive/records/host-v6/images/tool-tree-dark.png](../host-v6/images/tool-tree-dark.png) |
| `docs/plan-history/host-v6/images/tool-tree-light.png` | [docs/archive/records/host-v6/images/tool-tree-light.png](../host-v6/images/tool-tree-light.png) |
| `docs/plan-history/host-v8/development-evidence.json` | [docs/archive/records/host-v8/development-evidence.json](../host-v8/development-evidence.json) |
| `docs/plan-history/host-v8/images/palette-failure.png` | [docs/archive/records/host-v8/images/palette-failure.png](../host-v8/images/palette-failure.png) |
| `docs/plan-history/host-v8/images/palette-long-dark.png` | [docs/archive/records/host-v8/images/palette-long-dark.png](../host-v8/images/palette-long-dark.png) |
| `docs/plan-history/host-v8/images/tool-center-light.png` | [docs/archive/records/host-v8/images/tool-center-light.png](../host-v8/images/tool-center-light.png) |
| `docs/plan-history/host-v9/development-evidence.json` | [docs/archive/records/host-v9/development-evidence.json](../host-v9/development-evidence.json) |
| `docs/plan-history/host-v9/generated-artifacts-cleanup.json` | [docs/archive/records/host-v9/generated-artifacts-cleanup.json](../host-v9/generated-artifacts-cleanup.json) |
| `docs/plan-history/host-v9/nuget-unified-3.4.1-evidence.json` | [docs/archive/records/host-v9/nuget-unified-3.4.1-evidence.json](../host-v9/nuget-unified-3.4.1-evidence.json) |

## 后续事项

V7/V8/V9 人工与业务验收、Host 安装发布、macOS 实验、Workflow 候选和 API 分类政策统一由[集中待办](../../../roadmap/README.md)维护，不因文档归档而关闭。
