# V18：命令面板交互开发记录

> 后续状态（2026-09-21）：最终完整开发 `verify` 已通过，见[原始证据](final-development-evidence.json)（run-id `20260920-235651-0945a5af8b7c`，输入 `0945a5af8b7ce9e5dc95bd3c85efbbbec3e0e260`）；其后完成[本机部署](local-deployment-20260921.json)及[随 V19 方案文档再次部署](local-deployment-with-v19-plan-20260921.json)。项目所有者已[确认当前主程序手工验收通过](../host/manual-acceptance-20260921.md)，桌面待办收口，矩阵留作后续回归。以下正文及 JSON 保留原开发阶段的范围，不倒写当时未执行的实机与发布结果。

> 状态：P0–P3 实现及专项已完成，当前 Markdown 在最终验证前定稿。最终完整 `verify` 状态、输入身份和输出摘要以[非嵌入开发证据](final-development-evidence.json)为准，不预写最终通过结论。日期：2026-09-21。
> 依据：[V18 方案](../../../archive/plans/host-v18-command-palette-interaction-plan.md)、[专用验证矩阵](../../../maintenance/host-v18-command-palette-verification.md)。不使用 AIFLOW、Windows CI 或发布门禁，不包含部署/发布。

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

插件边界、生命周期所有权与 Document Scope 专项在 Release、单进程 MSBuild 和警告视为错误条件下通过 31 项，无失败或跳过，结果为 `artifacts/host-v18/implementation-20260921/p3-plugin`。阶段 Unit/UI 的实际构建配置和命令保留在 JSON，完整 Release 输入由最终 verify 统一重建验证。

开发中保留的失败：XAML 编译绑定缺少明确 DataType；新测试注册使用错误 Placement 前缀导致整插件被拒绝；Headless 初始空文本通知尚未提交导致先前选择被重置。分别修正 XAML 类型、测试声明和会话夹具的明确 Dispatcher 推进。V8 原断言从拼接 Description 改为结构化 IdentityText，目标消失提示同步为新明确文案，原实例及副作用断言保留。未放宽业务检查或引入跳过。

浅色/深色、小窗口、长标题、空结果和 100 入口截图保存在 `artifacts/host-v18/implementation-20260921/screenshots`。人工查看渲染图发现透明局部值覆盖选中样式，修正后增加背景非透明断言并重跑通过。真实输入法与原生多屏 DPI 尚未验证，不能以 Headless 模拟或过去的人工验收代替。

## P4 文档、审查和最终输入

阶段提交：`8781839` 为展示事实/分组排序，`84318cd` 为键盘交互、目标约束及回归。开发在原 `master` 分支进行，没有部署或推送。当前搜索指南、命令契约、Host 架构、设计取舍、兼容说明及导航已同步；方案移到 `docs/archive/plans`，原设计文本保留并补充实施说明。Q/K/E/A 逐项映射见专用验证第 10 节。

SOLID 审查：事实仍归 Workspace/Context/State Query，纯排序只处理不可变行，View 只拥有会话；副作用进入原 Workspace 用例或同一 Executor。预期目标只传值，不持有页面、窗口、模型或 Provider；菜单/快捷键缓存无新增可变目标。未新增服务定位器、规则引擎、插件 ID 分支、SDK API、持久化字段、CI 或发布配置。中文注释覆盖关键排序取舍、状态刷新、预编辑和执行时序。

Markdown 会嵌入 Host，先完成链接/方法映射检查并提交最终输入，再执行完整 `verify` 和必要帮助资源核对。Gate 与专项的真实命令、源码 revision/tree、摘要、TRX 发现/通过/失败/跳过、截图和资源哈希保存于 JSON；最终只更新非嵌入证据。未运行的命令不记为通过，不用旧 DLL 代替新文档构建。

定稿前文档检查覆盖 216 份受跟踪/新增 Markdown 的 1,679 个本仓文件链接，以及专用矩阵中 28 个带完整类名的方法引用，均可定位；可选邻仓链接 40 个单列，不作为构建依赖。本次改动文件没有标题锚点链接，未制造锚点检查数量。原 AIFLOW 文件被路径排除，没有读取其内容。

## 实机体验边界

本轮只完成 Headless 事件、布局、主题与渲染图检查。真实中文输入法候选确认、原生焦点/多屏 DPI 尚未执行，已单独保留在待办；不借用 V17 人工验收。没有执行 AIFLOW、Windows CI、seal、发布 Smoke、发布覆盖率或重复性门禁，没有更新安装目录、上传或公开发布。
