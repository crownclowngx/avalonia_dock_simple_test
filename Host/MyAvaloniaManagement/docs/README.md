# Host 内部文档

> 用途：维护 Host 实现及外部可观察行为。状态：当前；核对日期：2026-09-20。事实源为本项目源码及测试；主仓入口见[总导航](../../../docs/README.md)。

当前实现包含插件开关、自动重启、启动窗口、Document 浮窗关闭与命令面板创建目标，以及 V17 的诊断、命令展示和服务注册整理。Host 人工验收已由项目所有者[确认通过](../../../docs/archive/records/host/manual-acceptance-20260920.md)。

## 按修改目的阅读

| 目的 | 文档 |
| --- | --- |
| 理解启动、组合、Workspace、Dock 与资源所有权 | [内部架构](design/architecture.md) |
| 理解 SOLID、职责边界和取舍 | [设计方法](design/design-methodology-and-tradeoffs.md) |
| 修改 SDK 边界、身份、磁盘格式、窗口或关闭行为 | [兼容约束](reference/compatibility-contracts.md) |
| 查阅版本、支持平台与包职责 | [集中基线](../../../docs/reference/platform-baseline.md) |
| 运行自动化与定位专项回归 | [主仓验证与专项索引](../../../docs/maintenance/verification.md) |
| 构建和交付本机安装版 | [本机部署](../../../docs/maintenance/local-deployment.md) |
| 查阅 V11–V17 方案、设计取舍和原始结果 | [历史归档](../../../docs/archive/README.md) |

Document、Layout、Workflow、Command、启动和重启的详细契约从[总导航](../../../docs/README.md)进入，本入口不重复维护。

## 验证与维护

在仓库根目录执行：

```powershell
dotnet run --project tools/MyAvaloniaManagement.Gate -- verify
```

Gate 验证 Host 与 MyPlugTest；专项测试、人工验收、本机部署及正式发布分别记录。仍未完成的事项见[待办](../../../docs/roadmap/README.md)。

内部架构原文由应用内帮助直接引用，路径保持稳定；编辑与搬迁遵循[文档维护规则](../../../docs/maintenance/documentation.md)，重新核对帮助读取和链接。
