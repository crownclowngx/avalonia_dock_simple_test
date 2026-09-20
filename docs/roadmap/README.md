# 待办与候选

> 用途：集中保留尚未完成的工作。状态：按下表分别跟踪；核对日期：2026-09-20。事实源：当前实现、历史开发/部署记录及项目所有者确认。

当前 Host 的人工验收已由项目所有者实际手工使用后[确认通过](../archive/records/host/manual-acceptance-20260920.md)。原工具中心、搜索、看板、V11–V17 与 Dock 修复的重复 Host 桌面待办已收口，完成计划移入[历史归档](../archive/README.md)。历次本机部署见[部署索引](../maintenance/local-deployment.md)。

## 已确认的开发计划

| 事项 | 状态 / 范围 | 依据与完成条件 |
| --- | --- | --- |
| V18 命令面板分组与操作意图表达 | 方案已确认，尚未实施 / Host internal 展示与键盘会话 | [V18 方案](host-v18-command-palette-interaction-plan.md)、[专用开发验证](../maintenance/host-v18-command-palette-verification.md)：保留四类结果及单列表键盘路径，明确来源、实例、动作和命令目标；按 Q/K/E/A 矩阵补齐测试并执行本地开发 verify，不使用 AIFLOW、Windows CI 或发布门禁 |

V18 当前只完成设计和验证规范文档，不代表功能、自动化或人工体验验收已完成。此前 Host 人工验收结论不覆盖尚未实施的 V18 行为。

## 发布与外部验证

| 事项 | 状态 / 范围 | 依据与完成条件 |
| --- | --- | --- |
| 发布 Smoke 适配 Layout V3 | 发布前待完成 / Gate | [主仓验证](../maintenance/verification.md)：当前 GateChecks 仍检查 V2 文件与 schema；正式发布前适配 V3，运行工具自测和当次发布验证 |
| 已发布 API 文本分类与 Host seal 政策 | 发布政策待明确 / SDK、Host | [API 维护](../reference/plugin-sdk-api-compatibility.md)：公开包事实和二进制比较已有证据；分类、Unshipped 和 seal 政策在发布阶段处理 |
| Host 安装程序公开发布 | 发布前待完成 / Host 交付 | [NuGet 发布范围](../archive/records/host-v9/nuget-unified-3.4.1-release.md)：本机单文件交付已完成；公开发布仍需当次产物、有效 seal 条件及备份/回退验证 |
| 外部插件专有业务回归 | 按插件独立跟踪 / 外部仓库 | [兼容验证边界](../maintenance/plugin-compatibility-verification.md)：账号、数据库、下载、视频/WebView、后台任务和原生依赖由对应业务仓库记录，Host 整体验收不生成这些逐项业务结果 |
| macOS 运行实验 | 实验待验证 / Host、MyPlugTest | [实验指南](../quick-start/macos-experiment.md)：真机验证启动、插件、帮助、停靠、文件选择和示例功能；交叉编译不能代替 |

## 后续候选

| 候选 | 范围与依据 |
| --- | --- |
| 清单解析、工作区关闭与 Workflow 编排继续改善可读性 | [V17 已归档方案](../archive/plans/host-v17-readability-refactor-plan.md)中未纳入实施的候选，先明确独立目标与行为保护 |
| Workflow 下载与跨插件业务闭环 | [原始 Workflow 方案](../archive/plans/ai-workflow-plugin-exploration.md)：先确认实际插件现状和责任仓库，再验证真实下载、后续加密的取消/失败及源文件保留 |
| AI 候选规划、破坏性能力、定义持久化 | 同一原始方案中的独立产品候选，分别确认需求与边界，不作为当前手工编排的前提 |

## 状态更新规则

- 实现、自动验证、人工验收、部署和公开发布分别记录；新问题按新证据进入本页。
- 同一事项更新证据链接，不创建重复待办；完成后链接带日期结果并从活跃列表移至历史。
- 未来候选不自动获得实施授权；外部事项仅说明主仓集成边界。
- 原 Workflow 方案的“下载 Action G5”与后来的“双角色治理 G5”范围不同，按名称和实际结果判断完成。
