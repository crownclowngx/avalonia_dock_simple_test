# 待办与候选

> 用途：集中保留尚未完成的工作。状态：按下表分别跟踪；核对日期：2026-09-24。事实源：当前实现、历史开发/部署记录及项目所有者确认。

项目所有者于 2026-09-21 [确认当前主程序已完成手工验收](../archive/records/host/manual-acceptance-20260921.md)。当前 V18 与此前 Host 桌面体验的验收待办已收口，专项矩阵保留供后续回归；确认记录没有逐项环境日志，不补写输入法或多屏配置。历次本机部署见[部署索引](../maintenance/local-deployment.md)。

## 当前开发计划与后续体验

| 事项 | 状态 / 范围 | 依据与完成条件 |
| --- | --- | --- |
| V23 本地 ZIP 插件安装与更新 | 当前源码已实施，方案归档；部署、人工体验及发布未执行 | [当前契约](../reference/plugin-installation.md)、[开发记录](../archive/records/host-v23/development-acceptance.md)、[专用回归](../maintenance/host-v23-local-zip-installation-verification.md)。外部正式包实测、大包/原生文件选择体验保留为交付前观察项；不恢复 V22 在线分发 |
| V22 Host 内置插件源与安装升级 | 已暂停（尚未实施）；项目所有者于 2026-09-22 要求停止推进，保留方案与专用验证计划 | [V22 方案](host-v22-plugin-distribution-plan.md)、[专用开发验证](host-v22-plugin-distribution-verification.md)：保留 Gitee Raw 默认地址及空内容视为零插件的约定；明确恢复后重新确认自动下载、ZIP 托管方式及实施范围；暂停期间不推进实现、托管选型或专项验证 |
| V19 后续实机体验 | 代码实施及本机交付已完成；人工体验与交付分别留证 | [开发记录](../archive/records/host-v19/development-acceptance.md)、[部署证据](../archive/records/host-v19/local-deployment-20260921.json)、[回归矩阵](../maintenance/host-v19-complexity-verification.md)：后续按实际改动需要核对主窗/浮窗、保存和恢复、自动收起及多屏体验，不用部署结果扩展既有人工验收范围 |

V19 方案已[归档](../archive/plans/host-v19-complexity-reduction-plan.md)，本机单文件交付已完成，实际源码与产物身份以部署 JSON 为准。V20 实现与专项已完成，方案已[归档](../archive/plans/host-v20-layout-retirement-plan.md)，最终开发结果见[记录](../archive/records/host-v20/development-acceptance.md)。Layout V2 已退役，后续实机体验仍按实际需要留证；本轮未部署或发布。

V21 已实施并[归档方案](../archive/plans/host-v21-gate-and-test-efficiency-plan.md)；夹具裁剪、条件等待、Gate 证据与单测归并见[开发记录](../archive/records/host-v21/development-acceptance.md)和[专用开发验证](../maintenance/host-v21-gate-and-test-efficiency-verification.md)。最终自动验证以记录中的 JSON 为准，没有新增部署或人工验收结论。

## 发布与外部验证

| 事项 | 状态 / 范围 | 依据与完成条件 |
| --- | --- | --- |
| 发布 Smoke 实跑验证 Layout V3 | V20 已完成代码适配与工具自测；真实进程链待发布阶段 / Gate | [V20 开发记录](../archive/records/host-v20/development-acceptance.md)：开发仅验证共享产物检查与 V3 实际读写；正式发布再按[原门禁](../maintenance/verification.md)执行真实 Windows Smoke |
| 已发布 API 文本分类与 Host seal 政策 | 发布政策待明确 / SDK、Host | [API 维护](../reference/plugin-sdk-api-compatibility.md)：公开包事实和二进制比较已有证据；分类、Unshipped 和 seal 政策在发布阶段处理 |
| Host 安装程序公开发布 | 发布前待完成 / Host 交付 | [NuGet 发布范围](../archive/records/host-v9/nuget-unified-3.4.1-release.md)：本机单文件交付已完成；公开发布仍需当次产物、有效 seal 条件及备份/回退验证 |
| 外部插件专有业务回归 | 按插件独立跟踪 / 外部仓库 | [兼容验证边界](../maintenance/plugin-compatibility-verification.md)：账号、数据库、下载、视频/WebView、后台任务和原生依赖由对应业务仓库记录，Host 整体验收不生成这些逐项业务结果 |
| macOS 运行实验 | 实验待验证 / Host、MyPlugTest | [实验指南](../quick-start/macos-experiment.md)：真机验证启动、插件、帮助、停靠、文件选择和示例功能；交叉编译不能代替 |

## 后续候选

| 候选 | 范围与依据 |
| --- | --- |
| Workflow BeginShutdown 取消锁边界调查 | V20 之后优先独立调查；验证注释与实际锁内取消的差异、回调与 Run 释放竞态，确认缺陷才最小修复；不纳入 V20 完成条件，不全面重构 Workflow |
| PluginManifestReader 最小整理 | 在上述调查之后独立安排；优先只提取声明解释步骤，保留首次失败、错误码、异常范围及 JSON 生命周期；背景见[V17 候选](../archive/plans/host-v17-readability-refactor-plan.md) |
| Command Palette 按来源分段 | 有新的展示修改需求时再评估；当前保持四类来源的线性流程及 V18 目标复查，不新增 Provider 框架或拆分 WorkspaceSession |
| Workflow 下载与跨插件业务闭环 | [原始 Workflow 方案](../archive/plans/ai-workflow-plugin-exploration.md)：先确认实际插件现状和责任仓库，再验证真实下载、后续加密的取消/失败及源文件保留 |
| AI 候选规划、破坏性能力、定义持久化 | 同一原始方案中的独立产品候选，分别确认需求与边界，不作为当前手工编排的前提 |

## 状态更新规则

- 实现、自动验证、人工验收、部署和公开发布分别记录；新问题按新证据进入本页。
- 同一事项更新证据链接，不创建重复待办；完成后链接带日期结果并从活跃列表移至历史。
- 未来候选不自动获得实施授权；外部事项仅说明主仓集成边界。
- 原 Workflow 方案的“下载 Action G5”与后来的“双角色治理 G5”范围不同，按名称和实际结果判断完成。
