# Host 内部文档

> 用途：维护 Host 实现及外部可观察行为。状态：当前；核对日期：2026-09-17。总导航在[项目文档](../../../docs/README.md)。

按修改目的阅读：

1. [内部架构](design/architecture.md)：启动、插件组合、Workspace、Dock、文档、布局和资源所有权。
2. [设计方法与取舍](design/design-methodology-and-tradeoffs.md)：职责边界和内部修改原则。
3. [兼容约束](reference/compatibility-contracts.md)：SDK、身份、磁盘格式、关闭、窗口及诊断的不变量。
4. [V12 内部职责重构方案](../../../docs/roadmap/host-v12-internal-refactor-plan.md)：已接入 Registry 校验、UI 刷新调度与工作区查询快照；测试矩阵和命令见[专用开发验证](../../../docs/maintenance/host-v12-refactor-verification.md)，取舍与结果见[开发记录](../../../docs/archive/records/host-v12/development-acceptance.md)，安装版检查见[本机部署与手工影响范围](../../../docs/maintenance/host-v12-local-deployment.md)。

版本以[集中基线](../../../docs/reference/platform-baseline.md)为准；Document、Layout、Workflow 和 Command 各自的详细契约由[总导航](../../../docs/README.md)进入，不在本入口重复维护。

## 验证与交付

```powershell
dotnet run --project tools/MyAvaloniaManagement.Gate -- verify
```

在仓库根目录执行。Gate 只验证 Host 与 MyPlugTest；外部插件独立验收。正式 seal 的前提、覆盖率与窗口边界见[主仓验证](../../../docs/maintenance/verification.md)。

未完成工作见[待办与验收](../../../docs/roadmap/README.md)；旧计划及其测试数量、提交和签署记录见[历史归档](../../../docs/archive/README.md)。

内部架构原文被应用内帮助直接引用，本目录路径保持稳定；修改链接时同时核对帮助中的正文阅读。
