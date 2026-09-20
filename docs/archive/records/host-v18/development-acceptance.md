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

## P2–P3 键盘会话、目标约束与回归

View 使用同一虚拟化列表；组标题不进入候选集合，双击仅在结果正文生效。默认选择首个可用项，状态刷新沿用原顺序、身份和滚动位置。底部效果与错误行分离；预编辑文本存在时把 Enter、方向键和 Esc 留给输入控件。

共享 Executor 接收本次调用的 PageId/ContextRevision 约束。页面命令和 Host 保存携带约束，全局入口不携带；约束未写入共享 Command Store。View 先核对同一候选，关闭前核对目标，Executor 最后重查；两个同名页面、关闭面板瞬间切页、切回同页但代次变化均有无副作用拒绝测试。普通菜单/快捷键调用继续路由当前实例。

阶段 Unit 122 项、Headless UI 103 项通过，无失败或跳过。Unit 包含新增目标测试及 V8/WorkbenchCommand/工具相关保护；UI 包含 V18、V8、V16、工具中心、展示和刷新调度。结果分别为 `artifacts/host-v18/implementation-20260921/p3-unit`、`p2-ui-final`。四种工具布局、两种原始创建 Intent、同名页面、未知禁用原因、100 项虚拟化均有行为断言。

开发中保留的失败：XAML 编译绑定缺少明确 DataType；新测试注册使用错误 Placement 前缀导致整插件被拒绝；Headless 初始空文本通知尚未提交导致先前选择被重置。分别修正 XAML 类型、测试声明和会话夹具的明确 Dispatcher 推进。V8 原断言从拼接 Description 改为结构化 IdentityText，目标消失提示同步为新明确文案，原实例及副作用断言保留。未放宽业务检查或引入跳过。

浅色/深色、小窗口、长标题、空结果和 100 入口截图保存在 `artifacts/host-v18/implementation-20260921/screenshots`。人工查看渲染图发现透明局部值覆盖选中样式，修正后增加背景非透明断言并重跑通过。真实输入法与原生多屏 DPI 尚未验证，不能以 Headless 模拟或过去的人工验收代替。

最终完整开发验证及当前文档同步仍待 P4，不预写最终通过结论。
