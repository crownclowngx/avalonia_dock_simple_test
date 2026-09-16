# 待办与验收

> 用途：集中保留尚未完成的工作。状态：按下表分别跟踪；核对日期：2026-09-16。依据为现有源码与历史验收记录；本页不把未来候选视为已授权实施任务。

| 工作名称 | 状态 / 所属范围 | 事实依据 | 完成条件 |
| --- | --- | --- | --- |
| V11-P1 Tool 上下分割修复 | 源码与自动化已接入，桌面待补 / Host 补丁 | [P1 计划](host-v11-p1-tool-split-fix-plan.md)；[修复记录](../archive/records/host-v11/p1-tool-split-fix.md)；[最终开发证据](../archive/records/host-v11/p1-final-development-evidence.json) | 最终 verify 结果独立记录；实际补验截图位置上下投放、连续操作、浮窗回停与重启恢复。Headless 不代替桌面；安装目录部署为后续交付 |
| V11 浮动窗口与工具布局保存恢复 | 实现已接入，开发结果见证据 / 主程序 Host | [V11 实施计划](host-v11-floating-windows-and-layout-v3-plan.md)；[阶段记录](../archive/records/host-v11/development-acceptance.md) | 实现与专项测试已接入；最终 [verify 结果](../archive/records/host-v11/final-development-evidence.json) 独立记录。补真实鼠标拖放、跨屏 DPI、原生选择器、视频/WebView/后台业务；Headless 不替代真人验收 |
| 工具中心真实桌面与后台业务联调 | 待验收 / Host 与对应插件 | [V7 记录](../archive/records/host-v7/tool-center-and-visibility-acceptance.md) | 记录真实停靠、主窗口取消关闭、在途业务任务下隐藏与恢复；证明没有误释放模型或改变业务寿命 |
| 工作区交互用户任务观察 | 待验收 / Host 使用体验 | [V8 记录](../archive/records/host-v8/cognitive-ux-convergence-acceptance.md) | 实际观察找到功能、切换同名页面、恢复工具、失败重试；记录误操作、提示及焦点体验。自动化和本地部署不能替代 |
| 新框架真实 Windows、看板交互与旧插件业务回归 | 待验收 / Host V9–V10 与插件 | [V9 记录](../archive/records/host-v9/development-acceptance.md)、[V10 记录](../archive/records/host-v10/development-acceptance.md) | 补拖拽、Esc、失活、多屏 DPI、看板原生文件选择与键盘体验、原生视频/全屏、账号/数据库/下载及后台任务；不把 Headless 或逐插件加载通过当作业务和真机通过 |
| V11 发布 Smoke 格式适配 | 发布前待完成 / Gate | [专项维护指南](../maintenance/floating-layout-verification.md) | 实际发布前将 GateChecks 的布局 Smoke 文件名与 schema 断言适配为 V3，并运行工具自测及当次发布门禁；本轮不执行、不放宽阈值 |
| Host 安装程序发布 | 发布前待完成 / Host 交付 | [V9 原计划](../archive/plans/host-v9-avalonia-dock-upgrade-plan.md)、[NuGet 发布范围](../archive/records/host-v9/nuget-unified-3.4.1-release.md) | 在当次发布授权下完成前述验收、有效 seal 前提、完整产物备份与回退验证，分别记录安装部署和包发布状态 |
| macOS 运行实验 | 实验待验证 / Host 与 MyPlugTest | [实验指南](../quick-start/macos-experiment.md) | 真机验证空 Host、插件发现、帮助、停靠、文件选择及示例功能；记录架构、系统和日志，不以交叉编译成功代替 |
| Workflow 下载与跨插件业务闭环 | 未来候选 / 外部业务插件及 Studio | [原始工作流方案](../archive/plans/ai-workflow-plugin-exploration.md) | 先按实际插件现状确认需求和责任仓库；若实施，再验证真实下载及后续加密的取消、失败和源文件保留 |
| AI 候选规划、破坏性能力、定义持久化 | 未来候选 / Workflow 产品需求 | [原始工作流方案](../archive/plans/ai-workflow-plugin-exploration.md) | 分别确认需求与边界后再立项，不作为当前手工编排的前置条件 |
| 已发布 API 的文本分类与 Host seal 政策 | 发布政策待明确 / SDK 与 Host 发布 | [API 维护说明](../reference/plugin-sdk-api-compatibility.md)、[V10 记录](../archive/records/host-v10/development-acceptance.md) | 发布事实快照、文本映射与开发 API 比较已完成；正式分类及 seal 政策留到发布阶段，不删除签名、不改写历史承诺、不放宽门禁 |

## 状态更新规则

- 实现完成、自动验证完成、人工验收完成、已部署、已公开发布分别记录，不能互相代替。
- 同一事项在新版本继续存在时更新本表的证据链接，不创建重复待办；完成后链接到带日期的结果，并从活跃列表移到历史。
- 原 Workflow 方案的“下载 Action G5”与后来的“双角色治理 G5”不同；按名称和范围判断完成。
- 此处外部插件事项只说明主仓的集成边界，不代替外部仓库自己的实施计划。
