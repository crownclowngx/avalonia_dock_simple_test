# V18：命令面板交互开发记录

> 状态：实施进行中；本记录区分阶段结果与最终验收。日期：2026-09-21。
> 依据：[V18 方案](../../../roadmap/host-v18-command-palette-interaction-plan.md)、[专用验证矩阵](../../../maintenance/host-v18-command-palette-verification.md)。不使用 AIFLOW、Windows CI 或发布门禁，不包含部署/发布。

## P0 基线与行为缺口

起点 `71034f7cd338`，工作树干净。本地完整 `verify` 通过，Gate run-id 为 `20260920-232052-71034f7cd338`（目录使用 UTC）；构建零警告、零错误。SDK 91、Host Unit 616、Host Plugin 292、Host UI 277、MyPlugTest Unit 11、真实包验收 1 项全部通过，无跳过；API/契约比较与开发包验收通过。

新增 `CommandPaletteV18Tests.同组弱匹配页面仍连续排列而不会被同名创建入口穿插` 复现原排序的页面/创建入口穿插。首次夹具错误使用仅支持插件创建的 UnitTestDockableFactory 创建 Host 页面，失败原因不是业务回归；改用已有 DocumentTestContext 后，断言在原实现上按预期失败。两次失败分别保存在 `artifacts/host-v18/implementation-20260921/p0-red` 和 `p0-behavior-red`。

## P1 展示事实与排序

四类身份保持，展示记录与纯排序函数放入 `WorkbenchPalettePresentation.cs`。组内最佳匹配决定组顺序，同等级采用固定类型顺序；弱匹配留在原组。记录同时提供动作、来源、实例、目标及不可用原因；设计数据覆盖四组和两个同名实例。

状态/目标通过同次路由捕获，预期目标仅保存 PageId/ContextRevision 值。没有新增 SDK API、业务状态缓存或命令执行器。实际执行校验和 View 交互仍在 P2/P3 实施。

P1 相关 Unit 54 项、命令展示 UI 18 项通过，无失败或跳过。旧测试中禁用保存位于命令组首的顺序断言已按“同等级可执行优先”调整，原身份、状态、快捷键和发现许可断言保留。结果目录分别为 `artifacts/host-v18/implementation-20260921/p1-green` 和 `p1-ui`。

最终完整开发验证、V18 桌面体验检查及当前文档同步尚未完成，不沿用此前 Host 人工验收作为本轮结论。
