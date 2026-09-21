# 项目文档导航

> 用途：主项目唯一总导航。状态：当前；核对日期：2026-09-21。版本事实见[集中基线](reference/platform-baseline.md)，分类与维护规则见[文档维护](maintenance/documentation.md)。

当前源码已实施 V20 历史布局退役与入口收敛，见[开发记录](archive/records/host-v20/development-acceptance.md)。V20 单文件自包含交付见[本机部署](maintenance/local-deployment.md)及其部署证据；自动验证、既有[人工验收](archive/records/host/manual-acceptance-20260921.md)、本机交付和公开发布分别留证。

## 使用工作台

- [启动窗口与插件加载](quick-start/startup.md)：首屏、真实进度、取消和失败反馈。
- [工作区搜索与功能入口](quick-start/workbench-search.md)：新建页面、切换已有页面、恢复工具、执行命令。
- [功能中心与插件目录](quick-start/plugin-navigation-and-function-center.md)：分类、创建意图、目录偏好。
- [浮动窗口与布局恢复](quick-start/floating-windows-and-layout.md)：浮动、回停、关闭、找回和重置。
- [工具中心](quick-start/tool-center.md)：显示、隐藏、收藏和分类。
- [插件看板](quick-start/plugin-status.md)：兼容矩阵、可用性、诊断及下次启动开关。
- [重新启动 Host](quick-start/restart-host.md)：保存与取消、自动重启、应用插件设置。
- [macOS 实验](quick-start/macos-experiment.md)：交叉打包与独立真机验证。

## 开发插件

从 [Managed Plugin 快速开始](quick-start/README.md)进入：

1. [创建插件项目](quick-start/create-managed-plugin.md)
2. [添加 Document、Tool 和独立预览工作台](quick-start/add-document-and-tool.md)
3. [构建、打包、真实 Host 验收与排错](quick-start/verification-and-troubleshooting.md)
4. [插件图标](quick-start/plugin-icons.md)、[Workflow Action 接入](quick-start/workflow-action-development.md)
5. [旧插件与 V9 升级边界](quick-start/host-v9-upgrade.md)

命令贡献见 [Workbench Command 契约](reference/workbench-commands.md)。生成项目自带文档随模板交付，外部作者无需克隆本仓。

## 维护 Host 与 SDK

| 任务 | 权威入口 |
| --- | --- |
| 理解实现、职责和资源所有权 | [Host 内部文档](../Host/MyAvaloniaManagement/docs/README.md) |
| 修改外部可观察行为 | [兼容约束](../Host/MyAvaloniaManagement/docs/reference/compatibility-contracts.md) |
| 启动与插件进度 | [启动契约](reference/host-startup.md) |
| 插件开关与重启 | [开关契约](reference/plugin-enablement.md)、[重启契约](reference/host-restart.md) |
| Document 保存与恢复 | [Document 持久化](reference/document-persistence.md) |
| 工具布局及浮窗 | [Layout V3](reference/dock-layout-snapshot-v3.md)、[浮窗指南](quick-start/floating-windows-and-layout.md) |
| 命令及跨插件调用 | [Workbench Command](reference/workbench-commands.md)、[Workflow Action](reference/workflow-actions.md) |
| SDK public API 与产物兼容 | [API 基线](reference/plugin-sdk-api-compatibility.md)、[兼容证据](reference/plugin-compatibility.md) |
| 运行日常验证与专项回归 | [主仓验证与专项索引](maintenance/verification.md) |
| 部署 Host 与维护六个 NuGet 包 | [本机部署](maintenance/local-deployment.md)、[NuGet 发布](maintenance/nuget-release.md) |
| 维护文档和归档证据 | [文档维护规则](maintenance/documentation.md) |

## 当前结论、待办与历史

- [V20 历史布局退役与现行入口收敛](archive/plans/host-v20-layout-retirement-plan.md)：已实施并归档；[专用开发验证](maintenance/host-v20-layout-retirement-verification.md)、[测试去向](archive/records/host-v20/test-matrix.md)与[开发记录](archive/records/host-v20/development-acceptance.md)。Layout V2 运行能力及旧查询退出，现行行为由 V3 路径验证。
- [V19 Host 复杂度收敛方案](archive/plans/host-v19-complexity-reduction-plan.md)：已实施并归档；[专用回归验证](maintenance/host-v19-complexity-verification.md)、[逐项测试映射](archive/records/host-v19/test-matrix.md)与[开发记录](archive/records/host-v19/development-acceptance.md)记录关闭、回滚、命令目录、实时查询和工具入口的收敛。
- [Host 人工验收确认](archive/records/host/manual-acceptance-20260921.md)：项目所有者确认当前主程序通过，专项矩阵保留供后续回归。
- [V18 命令面板交互方案](archive/plans/host-v18-command-palette-interaction-plan.md)：已实施四类分组、动作和目标校验；配套[专用开发验证](maintenance/host-v18-command-palette-verification.md)及[开发记录](archive/records/host-v18/development-acceptance.md)。
- [待办与候选](roadmap/README.md)：保留当前开发计划、发布前事项、外部业务边界、实验和未来候选。
- [历史归档](archive/README.md)：V1–V20 方案、阶段结果、原失败和各次部署/发布证据；历史命令不作为当前操作入口。
- [本轮文档整理记录](archive/records/documentation/reorganization-20260921.md)：目录归位、验收与部署状态修正、应用内帮助同步和验证结果。

## 理论与设计解释

- [注意力与可停靠工作台](theory/attention-centered-dock-workspace-design.md)
- [活动理论与需求分解](theory/activity-theory-requirements-decomposition.md)
- [约束驱动的软件工业化](theory/约束驱动的软件工业化.md)
- [AI 辅助的可分叉软件基底](theory/ai-enabled-forkable-software-bases.md)

文章解释设计意图；实现映射以当前契约和源码为准，研究建议不自动成为已实施能力。四篇理论原文与 Host 架构原文供应用内帮助读取，保持原路径和章节身份。

## 可选外部材料

业务插件位于可选相邻目录 `avalonia_dock_plug_test`。以下链接仅在相应仓库检出时可用，主仓 Gate 不依赖它们；专有业务验收由各仓库负责。

- [Workflow Studio](../../avalonia_dock_plug_test/myavalonia-workflow-studio/README.md)
- [BiliDownloader](../../avalonia_dock_plug_test/myavalonia-bili-downloader/README.md)
- [VideoSecurityPlayer](../../avalonia_dock_plug_test/myavalonia-video-security-player/README.md)
- [DaTangWork](../../avalonia_dock_plug_test/myavalonia-datang-work/README.md)

## 维护约定

遵循[文档维护规则](maintenance/documentation.md)：当前契约集中维护；测试数量和产物身份进入有日期记录；完成计划提取剩余事项后归档；搬迁同步链接，历史结果只追加后续说明。
