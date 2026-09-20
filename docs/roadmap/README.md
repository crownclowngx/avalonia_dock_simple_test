# 待办与验收

V16 Document 浮窗关闭与新建位置处于方案阶段，见[实施方案](host-v16-document-floating-close-and-creation-target-plan.md)和[专用开发验证计划](../maintenance/host-v16-document-window-verification.md)。本次只写文档，代码与验收均未执行。

V15 启动引导与插件加载进度已接入；见[实施方案](host-v15-startup-splash-plan.md)、[专用验证](../maintenance/host-v15-startup-verification.md)及[开发记录与最终证据](../archive/records/host-v15/development-acceptance.md)。

V14 一键自动重启已接入，开发结果见[开发记录与最终证据](../archive/records/host-v14/development-acceptance.md)。入口：[执行方案与阶段](host-v14-automatic-restart-plan.md)、[专用开发验证](../maintenance/host-v14-automatic-restart-verification.md)；单文件交付与实际启动检查见[本机部署](../maintenance/host-v14-local-deployment.md)，原生重启往返和桌面体验继续独立验收。

V13 插件开关的实现与专项已完成，原生桌面检查待验收。入口：[计划与阶段](host-v13-plugin-enablement-plan.md)、[专用开发验证](../maintenance/host-v13-plugin-enablement-verification.md)、[开发记录与最终证据](../archive/records/host-v13/development-acceptance.md)。本轮只运行本地开发 verify，不使用 AIFLOW、Windows CI 或发布门禁。

> 用途：集中保留尚未完成的工作。状态：按下表分别跟踪；核对日期：2026-09-19。依据为现有源码与历史验收记录；本页不把未来候选视为已授权实施任务。

| 工作名称 | 状态 / 所属范围 | 事实依据 | 完成条件 |
| --- | --- | --- | --- |
| V16 Document 浮窗关闭与新建位置 | 方案待实施、未验证 / Host 工作区交互 | [方案](host-v16-document-floating-close-and-creation-target-plan.md)、[专用验证计划](../maintenance/host-v16-document-window-verification.md) | 最后文档关闭无空壳、取消保留内容；新建进入发起窗口活动组；C/N 专项、本地 verify 与 M01–M08 分别记录。不使用 AIFLOW、Windows CI 或发布门禁 |
| V15 启动引导与插件加载进度 | 实现与专项已接入，原生桌面待验收 / Host 启动 | [方案](host-v15-startup-splash-plan.md)、[专用验证](../maintenance/host-v15-startup-verification.md)、[开发证据](../archive/records/host-v15/final-development-evidence.json) | 最终本地 verify 见 JSON；原生 M01–M07、外部业务、单文件与部署分别记录，不以 Headless 代替 |
| V14 Host 一键自动重启 | 实现与自动化已接入，原生重启/桌面体验待验收 | [执行方案](host-v14-automatic-restart-plan.md)、[专用验证](../maintenance/host-v14-automatic-restart-verification.md)、[开发证据](../archive/records/host-v14/final-development-evidence.json)、[本机交付](../maintenance/host-v14-local-deployment.md) | 本机单文件产物和启动检查按部署 JSON 记录，不能替代 M01–M07 原生交互及单文件菜单/看板重启往返。不使用 AIFLOW、Windows CI 或本轮正式发布门禁 |
| V12 Host 内部职责重构 | 源码与专项已完成，桌面未执行 / Host 内部 | [V12 方案](host-v12-internal-refactor-plan.md)、[开发记录](../archive/records/host-v12/development-acceptance.md)、[最终证据](../archive/records/host-v12/final-development-evidence.json)、[安装版影响范围](../maintenance/host-v12-local-deployment.md) | 三阶段实现、单元／插件集成／Headless 专项已完成；完整本地 verify 以最终证据为准。原生鼠标、焦点、多屏 DPI 与外部业务未执行，后续按需要独立验收；本轮不使用 AIFLOW、Windows CI 或发布门禁 |
| Document 拖入已有浮窗崩溃 | 框架补丁与自动化已接入，桌面和部署待补 / Host 运行时 | [修复方案](host-document-cross-window-layout-crash-fix-plan.md)、[专项维护](../maintenance/dock-cross-window-layout-verification.md)、[本轮证据](../archive/records/dock-area-fill/cross-window-layout-fix-evidence.json) | 完整 verify 结果以本轮证据为准；补 M01–M03 真实桌面操作及原截图百度网盘场景，然后另行部署 |
| Dock 区域默认居中停靠 | 旧版已部署，跨窗修复已接入但待桌面与新部署 / Host 拖放交互 | [实施计划](host-dock-area-fill-implementation-plan.md)、[专项记录](../archive/records/dock-area-fill/development-acceptance.md)、[最终开发证据](../archive/records/dock-area-fill/development-evidence.json) | 按上述修复专项补真实桌面与新部署；原通过证据不覆盖新增故障。原生遮挡、混合浮窗、标签排序及多屏 DPI 仍需按矩阵独立验收 |
| V11-P2 Tool 浮窗关闭黑框修复 | 源码与自动化已接入，桌面待补 / Host 补丁 | [P2 计划](host-v11-p2-tool-window-close-fix-plan.md)；[修复记录](../archive/records/host-v11/p2-tool-window-close-fix.md)；[最终开发证据](../archive/records/host-v11/p2-final-development-evidence.json) | 补真实 Tool 浮窗右上角关闭、菜单/工具中心隐藏、取消、再次显示与重启；自动化不代替桌面，本补丁部署另行交付 |
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
