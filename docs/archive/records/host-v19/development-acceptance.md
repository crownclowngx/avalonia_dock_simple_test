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
