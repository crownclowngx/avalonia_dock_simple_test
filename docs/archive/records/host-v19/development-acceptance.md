# V19 Host 复杂度收敛开发记录

> 用途：记录 V19 实施、行为修复、专项验证及最终开发门禁的实际证据。日期：2026-09-21。状态：实施中；未进行本轮实机人工验收、安装目录部署或公开发布。
> 方案：[复杂度收敛计划](../../../roadmap/host-v19-complexity-reduction-plan.md)；矩阵：[专项验证](../../../roadmap/host-v19-complexity-verification.md)。

## P0：固定输入与基线

- 实施起点：`b38c6526aa8c410ec56390a7e234a4b29315e6f8`，工作树干净；分支 `codex/host-v19-complexity-reduction`。
- 本地命令：`dotnet run --project tools/MyAvaloniaManagement.Gate -- verify`；运行 ID：`20260921-010831-b38c6526aa8c`，证据位于 `artifacts/gate/20260921-010831-b38c6526aa8c/summary.json`。
- Release 编译 0 警告、0 错误；SDK 91、Host Unit 626、Host Plugin 292、Host Headless UI 288、MyPlugTest Unit 11、真实 ZIP 验收 1，全部通过，无跳过。契约及已发布 API 比较通过。
- 本轮仅使用本地开发验证；未使用 AIFLOW、Windows CI、seal、发布覆盖率、发布 Windows Smoke 或重复性发布门禁。

实际调用核对：主窗通过 `PrepareWindowCloseAsync → WorkspaceSession.PrepareApplicationCloseAsync → DocumentCloseCoordinator.PrepareRangeCloseAsync` 准备许可；旧 `ConfirmWindowCloseAsync` 与 `HasDirtyDocuments` 仅剩转发和测试调用。单页与范围关闭的用户决策及排空后检查存在重复与缺口；备份创建后等待恢复确认处没有覆盖异常的局部回滚保护。

保存警告需分开记录：主文件已经原子提交不因 `AcceptChanges` 或备份写入异常变为保存失败；关闭许可仍需复核排空后的实时脏状态。旧单页路径与范围路径对此不一致，实施时作为明确的行为修复处理，不伪装成等价重构。

## 后续阶段

P0 两项风险均已通过确定性测试复现：`DocumentCloseTests.V19干净单页排空期间出现修改必须撤销关闭并重新开放命令` 在旧实现实际重试 1 次（预期 0）；`DocumentPersistenceTests.V19恢复确认异常必须立即释放候选且不改动输入文件` 在旧实现释放 0 次（预期 1）。红灯证据：`artifacts/host-v19/p0-risk-reproduction/risks-red.trx`，2 项均因行为断言失败，非编译失败。

恢复确认缺口先以局部 `try/finally` 修复，保留原始确认异常及原件/备份内容；P2 再统一创建到发布的回滚义务。该测试还断言 Scope 立即释放、生命周期取消先于模型释放、未发布和零写入。

P1–P6 正在执行。各项真实测试方法、红绿结果、设计取舍和最终验证在完成对应工作后追加；本记录不提前声明覆盖或通过。

恢复最小修复验证：DocumentPersistenceTests 21/21 通过，证据 artifacts/host-v19/p0-risk-reproduction/recovery-minimal-green.trx。

## P1：关闭规则收敛

`RequestDecisionAsync` 是唯一确认/保存规则，`VerifyFinalStateAsync` 是单页和范围排空后的共同许可条件。同步干净快速路径、一次性许可和精确单页接续浮窗范围均保留。删除 Coordinator、Session、MainWindowViewModel 的旧 `ConfirmWindowCloseAsync`、`HasDirtyDocuments` 及 `_windowRequestPending`；原测试迁移到 `PrepareRangeCloseAsync(isApplicationExit: true)` 或主窗 `PrepareWindowCloseAsync`。

新增方法覆盖：`V19干净单页排空期间出现修改必须撤销关闭并重新开放命令`（C01/C09）；`V19单页保存后排空又修改必须保留_明确放弃可继续`（C02）；`V19所有关闭入口按最终脏状态区分确认修订警告与备份警告` 六组参数（C03）；`V19同步取消回调修改文档并排空也不能走干净快速关闭`（C04）；`V19范围放弃许可不能覆盖确认时干净而后来变脏的页面`（C02/C08）。原保存中新修订测试改为等待关闭收尾事件，消除“错误提示到达即认为 pending 已清理”的竞态；新增测试初稿误读展示脏状态也已改为读取真实模型事实。失败 TRX 原样保留。

文档相关 Unit 99/99、关闭/浮窗/重启 Headless UI 75/75 通过，无跳过。证据：`artifacts/host-v19/p1-close/documents-final.trx`、`close-ui.trx`。既有测试继续验证原生首次/重试否决、CanClose、单页接续不扩大许可、目标集合变化、Scope 顺序和重启最终布局。保存警告行为修复已同步 `docs/reference/document-persistence.md`。
