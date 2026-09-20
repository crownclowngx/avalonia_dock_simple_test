# Host 内部文档

V16 Document 浮窗关闭与新建位置目前处于方案阶段，见[实施方案](../../../docs/roadmap/host-v16-document-floating-close-and-creation-target-plan.md)和[专用开发验证计划](../../../docs/maintenance/host-v16-document-window-verification.md)。SOLID 为首要约束；本次仅文档，未修改代码或执行验证。

V15 通过唯一 App 提前显示矢量羽毛启动窗口，后台插件组合报告真实进度，UI 线程完成工作台交接；见[现行契约](../../../docs/reference/host-startup.md)、[专用验证](../../../docs/maintenance/host-v15-startup-verification.md)及[开发记录](../../../docs/archive/records/host-v15/development-acceptance.md)。

V14 一键自动重启已接入，见[现行契约](../../../docs/reference/host-restart.md)、[执行方案](../../../docs/roadmap/host-v14-automatic-restart-plan.md)、[专用验证](../../../docs/maintenance/host-v14-automatic-restart-verification.md)及[开发记录](../../../docs/archive/records/host-v14/development-acceptance.md)。复用原关闭所有权与一次性进程交接；最终开发、桌面、单文件与部署状态分别记载。

V13 插件开关已接入：在看板保存下次启动设置，在 DLL 加载前过滤。见[行为契约](../../../docs/reference/plugin-enablement.md)、[实施计划](../../../docs/roadmap/host-v13-plugin-enablement-plan.md)、[专用验证](../../../docs/maintenance/host-v13-plugin-enablement-verification.md)及[开发记录](../../../docs/archive/records/host-v13/development-acceptance.md)。

> 用途：维护 Host 实现及外部可观察行为。状态：当前；核对日期：2026-09-19。总导航在[项目文档](../../../docs/README.md)。

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
