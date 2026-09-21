# Host 内部文档

> 用途：维护 Host 实现及外部可观察行为。状态：当前；核对日期：2026-09-21。事实源为本项目源码及测试；主仓入口见[总导航](../../../docs/README.md)。

当前实现包含插件开关、自动重启、启动窗口、Document 浮窗关闭与命令面板创建目标、V17 可读性整理、V18 命令面板交互，以及 V19 关闭、回滚、命令目录、即时查询和工具入口收敛。V19 开发与本机交付分别见[开发记录](../../../docs/archive/records/host-v19/development-acceptance.md)和[部署证据](../../../docs/archive/records/host-v19/local-deployment-20260921.json)。既有[手工验收确认](../../../docs/archive/records/host/manual-acceptance-20260921.md)保持原始范围；[V20](../../../docs/archive/plans/host-v20-layout-retirement-plan.md)已退役 Layout V2 并删除旧分组查询，实际开发验证见[本轮记录](../../../docs/archive/records/host-v20/development-acceptance.md)，未执行本机部署或发布。

## 按修改目的阅读

| 目的 | 文档 |
| --- | --- |
| 理解启动、组合、Workspace、Dock 与资源所有权 | [内部架构](design/architecture.md) |
| 理解 SOLID、职责边界和取舍 | [设计方法](design/design-methodology-and-tradeoffs.md) |
| 修改 SDK 边界、身份、磁盘格式、窗口或关闭行为 | [兼容约束](reference/compatibility-contracts.md) |
| 查阅版本、支持平台与包职责 | [集中基线](../../../docs/reference/platform-baseline.md) |
| 运行自动化与定位专项回归 | [主仓验证与专项索引](../../../docs/maintenance/verification.md) |
| 查阅命令面板分组与目标一致性设计 | [V18 交互方案](../../../docs/archive/plans/host-v18-command-palette-interaction-plan.md)、[专用开发验证](../../../docs/maintenance/host-v18-command-palette-verification.md) |
| 维护收敛后的关闭、回滚、命令和工具边界 | [V19 归档方案](../../../docs/archive/plans/host-v19-complexity-reduction-plan.md)、[专用验证](../../../docs/maintenance/host-v19-complexity-verification.md)、[开发记录](../../../docs/archive/records/host-v19/development-acceptance.md) |
| 理解 Layout V2 退役、测试迁移与现行入口收敛 | [V20 归档方案](../../../docs/archive/plans/host-v20-layout-retirement-plan.md)、[专用开发验证](../../../docs/maintenance/host-v20-layout-retirement-verification.md) |
| 审视开发门禁、测试职责和验证成本 | [V21 待实施方案](../../../docs/roadmap/host-v21-gate-and-test-efficiency-plan.md)、[专用开发验证计划](../../../docs/roadmap/host-v21-gate-and-test-efficiency-verification.md) |
| 构建和交付本机安装版 | [本机部署](../../../docs/maintenance/local-deployment.md) |
| 查阅 V11–V19 方案、设计取舍和原始结果 | [历史归档](../../../docs/archive/README.md) |

Document、Layout、Workflow、Command、启动和重启的详细契约从[总导航](../../../docs/README.md)进入，本入口不重复维护。

## 验证与维护

在仓库根目录执行：

```powershell
dotnet run --project tools/MyAvaloniaManagement.Gate -- verify
```

Gate 验证 Host 与 MyPlugTest；专项测试、人工验收、本机部署及正式发布分别记录。仍未完成的事项见[待办](../../../docs/roadmap/README.md)。

内部架构原文由应用内帮助直接引用，路径保持稳定；编辑与搬迁遵循[文档维护规则](../../../docs/maintenance/documentation.md)，重新核对帮助读取和链接。
