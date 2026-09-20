# 项目文档导航

V16 Document 浮窗关闭与命令面板新建位置已实现，见[实施方案](roadmap/host-v16-document-floating-close-and-creation-target-plan.md)、[专用开发验证](maintenance/host-v16-document-window-verification.md)及[开发记录](archive/records/host-v16/development-acceptance.md)。本地自动化与原生桌面状态分别记录；不使用 AIFLOW、Windows CI 或发布门禁。

V15 羽毛启动窗口与真实插件进度已接入；本次 `D:\data\avalonia` 单文件自包含交付见[专用部署说明](maintenance/host-v15-local-deployment.md)和[实际部署证据](archive/records/host-v15/local-deployment-20260920.json)。

V14 一键自动重启已接入文件菜单和插件看板，见[使用指南](quick-start/restart-host.md)、[行为契约](reference/host-restart.md)、[执行方案](roadmap/host-v14-automatic-restart-plan.md)及[专用开发验证](maintenance/host-v14-automatic-restart-verification.md)。最终本地 verify 结果见[开发记录与证据](archive/records/host-v14/development-acceptance.md)；单文件、自包含安装交付见[桌面工作台部署](maintenance/host-v14-local-deployment.md)，原生桌面体验仍独立验收。本轮不使用 AIFLOW、Windows CI 或正式发布门禁。

V13 插件开关已实现：在[插件看板](quick-start/plugin-status.md)保存下次启动设置，重启生效。见[行为契约](reference/plugin-enablement.md)、[实施计划](roadmap/host-v13-plugin-enablement-plan.md)、[专用验证](maintenance/host-v13-plugin-enablement-verification.md)及[开发记录](archive/records/host-v13/development-acceptance.md)；最终 verify 结果以记录内 JSON 为准，原生桌面待验收。本机单文件、自包含交付见[桌面工作台部署](maintenance/host-v13-local-deployment.md)。

> 用途：主项目唯一总导航。状态：当前；核对日期：2026-09-19。技术基线见[版本与交付边界](reference/platform-baseline.md)。

## 使用工作台

- [启动窗口与插件加载](quick-start/startup.md)：羽毛首屏、真实进度、取消和失败反馈。

- [工作区搜索与功能入口](quick-start/workbench-search.md)：找到功能、切换已有页面、恢复工具、执行命令。
- [功能中心与插件目录](quick-start/plugin-navigation-and-function-center.md)：分类、创建意图、目录偏好。
- [浮动窗口与布局恢复](quick-start/floating-windows-and-layout.md)：工具浮窗位置与分组、关闭、找回和重置。
- [工具中心](quick-start/tool-center.md)：显示、隐藏、收藏和分类。
- [插件看板](quick-start/plugin-status.md)：查看版本、兼容矩阵、可用性、贡献和脱敏诊断。
- [重新启动 Host](quick-start/restart-host.md)：一键关闭并重新启动、保存与取消、应用插件设置。
- [V9 升级边界](quick-start/host-v9-upgrade.md)：旧插件二进制、新模板、外部产物验证。
- [macOS 实验](quick-start/macos-experiment.md)：交叉打包和仍需完成的真机验证。

## 开发插件

从 [Managed Plugin 快速开始](quick-start/README.md) 进入：

1. [创建插件项目](quick-start/create-managed-plugin.md)
2. [添加 Document、Tool 和独立预览工作台](quick-start/add-document-and-tool.md)
3. [插件构建、打包、真实 Host 验收与排错](quick-start/verification-and-troubleshooting.md)
4. [插件图标](quick-start/plugin-icons.md)、[Workflow Action 接入](quick-start/workflow-action-development.md)

命令贡献的当前语义见 [Workbench Command 契约](reference/workbench-commands.md)。生成项目自带的文档随模板交付，不要求外部作者克隆本仓。

## 维护 Host 与 SDK

| 任务 | 权威说明 |
| --- | --- |
| 理解当前 Host 实现与取舍 | [内部架构入口](../Host/MyAvaloniaManagement/docs/README.md) |
| 按 V12 重构 Host 内部职责 | [重构方案](roadmap/host-v12-internal-refactor-plan.md)、[专用开发验证](maintenance/host-v12-refactor-verification.md)、[开发记录](archive/records/host-v12/development-acceptance.md)、[本机部署与手工影响范围](maintenance/host-v12-local-deployment.md) |
| 修改外部可观察行为 | [兼容约束](../Host/MyAvaloniaManagement/docs/reference/compatibility-contracts.md) |
| 修改保存与恢复 | [Document 持久化](reference/document-persistence.md) |
| 修改停靠布局 | [Layout V3](reference/dock-layout-snapshot-v3.md)、[浮窗开发验证](maintenance/floating-layout-verification.md)、[区域回停专项](maintenance/dock-area-fill-verification.md)、[跨窗布局修复](maintenance/dock-cross-window-layout-verification.md) |
| 修改 SDK public API | [API 基线维护](reference/plugin-sdk-api-compatibility.md) |
| 理解产物、报告和兼容判断 | [插件兼容证据契约](reference/plugin-compatibility.md) |
| 验证插件二进制与候选模板 | [插件兼容开发验证](maintenance/plugin-compatibility-verification.md) |
| 修改跨插件调用 | [Workflow Action 契约](reference/workflow-actions.md) |
| 运行验证、理解 seal 条件 | [主仓验证与封板](maintenance/verification.md) |
| 维护六个 NuGet 包与模板 | [NuGet 发布维护](maintenance/nuget-release.md) |

## 未完成工作与历史

- [V15 启动引导与插件加载进度](roadmap/host-v15-startup-splash-plan.md)：实现与专项已接入；见[使用说明](quick-start/startup.md)、[现行契约](reference/host-startup.md)、[专用验证](maintenance/host-v15-startup-verification.md)及[开发记录与最终证据](archive/records/host-v15/development-acceptance.md)。原生桌面、单文件和部署独立记录。
- [V12 Host 内部职责重构方案](roadmap/host-v12-internal-refactor-plan.md)：Registry 校验、UI 刷新调度和工作区查询快照已接入，专项已通过；最终 verify 见[证据](archive/records/host-v12/final-development-evidence.json)，原生桌面验证边界见[记录](archive/records/host-v12/development-acceptance.md)。
- [Document 拖入已有浮窗崩溃修复方案](roadmap/host-document-cross-window-layout-crash-fix-plan.md)：框架补丁与自动化回归已完成，真实桌面验收仍待完成；本地安装状态见[单文件部署说明](maintenance/dock-cross-window-layout-deployment.md)。
- [V11 浮动窗口恢复与 Layout V3 计划](roadmap/host-v11-floating-windows-and-layout-v3-plan.md)：实现已接入，最终开发验证见[证据](archive/records/host-v11/final-development-evidence.json)；工具浮窗恢复位置和分组，Document 支持运行时浮动但不跨启动重开；结果见 [V11 开发记录](archive/records/host-v11/development-acceptance.md)。
- [V10 插件兼容治理与 Host 插件看板计划](archive/plans/host-v10-plugin-compatibility-and-dashboard-plan.md)：已归档的开发计划；结果见 [V10 开发记录](archive/records/host-v10/development-acceptance.md)，业务与真机待办独立保留。
- [待办与验收](roadmap/README.md)：待验收、发布前工作、实验、未来候选和维护决策。
- [历史归档](archive/README.md)：原计划、阶段记录、旧格式及关联证据。历史命令不能当作当前操作入口。

## 理论与设计解释

- [注意力与可停靠工作台](theory/attention-centered-dock-workspace-design.md)
- [活动理论与需求分解](theory/activity-theory-requirements-decomposition.md)
- [约束驱动的软件工业化](theory/约束驱动的软件工业化.md)
- [AI 辅助的可分叉软件基底](theory/ai-enabled-forkable-software-bases.md)

这些文章解释设计意图；涉及本项目的实现映射以当前契约和源码为准，研究建议不自动成为已实施能力。四篇原文及 Host 架构原文同时供应用内帮助读取，路径不可仅为排版方便而改动。

## 可选外部材料

业务插件位于可选的相邻目录 `avalonia_dock_plug_test`。以下链接只在相应仓库已检出时可用；主仓 Gate 不依赖它们。

- [Workflow Studio](../../avalonia_dock_plug_test/myavalonia-workflow-studio/README.md)
- [BiliDownloader](../../avalonia_dock_plug_test/myavalonia-bili-downloader/README.md)
- [VideoSecurityPlayer](../../avalonia_dock_plug_test/myavalonia-video-security-player/README.md)
- [DaTangWork](../../avalonia_dock_plug_test/myavalonia-datang-work/README.md)

## 维护约定

1. 当前文档说明用途、适用范围、事实来源和核对日期；同一契约只保留一份详细权威说明，其他入口使用链接。
2. 修改实现时同步受影响的当前说明、包 README 和模板文档；最低兼容版本不机械替换为最新包号。
3. 测试数量、覆盖率、源码 revision、制品哈希写入有日期的验收记录，日常入口不重复维护。
4. 完成的计划先提取未完成事项，再归档；历史结果只追加勘误，不倒写为当前状态。
5. 搬迁时同步相对链接和关联证据；已清理产物明确标注，不制造一个看似可下载的链接。
6. 现有 Gate 只检查三个 README 的本仓文件链接。其他现行文档、锚点和语义需在变更中额外核对，本轮未扩展校验代码。
