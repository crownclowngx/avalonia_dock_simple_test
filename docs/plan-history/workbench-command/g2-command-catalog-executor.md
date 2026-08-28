# Workbench Command G2：无 UI Catalog 与 Executor 实施记录

> 状态：已完成（2026-08-28；完整非发布门禁通过）。
>
> 输入提交：`e4278fec31271c72467a52c5f309af984bb53354`
>
> 输入 Git tree：`e8344fe04fa430ea7f92a216b1a2c80e9bfefe08`
>
> 前置：[G1 兼容契约与注册声明](./g1-command-contracts-registration-declarations.md)
>
> 总设计：[Workbench Command 引入评审与实施任务书](../../design/workbench-command-introduction-plan.md#g2建立无-ui-command-catalog-与-executor)

## 1. 实施边界

G2 只建立 Host internal 无 UI 内核：Host/Plugin 合并 Catalog、打开/保存 Handler、统一 Executor、稳定结果、
诊断和 10 秒关闭门控。插件 Command 已可通过稳定身份查询，但在 G3 建立活动 Document Context 与实例 Target
前返回 `TargetUnavailable`，不会解析插件 Provider 或模型。

本阶段没有修改 `MainWindowViewModel.OpenDocument/SaveDocument`、`MenuView.axaml` 或 `MainWindow.axaml`
的 `Ctrl+S`，也没有创建 `MenuItem`、`KeyBinding`、Palette 或 Avalonia Command Adapter。Core/UI SDK public
API、3.2.0 源码版本、3.0.0 产品版本、manifest/envelope/layout schema 2、`layout-v2.json` 和数据根 `v2`
保持不变。

## 2. 实现与设计思路

无 UI 执行链为：

```text
CommandId
    → WorkbenchCommandExecutor
    → WorkbenchCommandCatalog
        ├─ HostWorkbenchCommandCatalog → Host Handler
        └─ PluginRegistry → owner availability → TargetUnavailable（G3 前）
```

`HostWorkbenchCommandCatalog` 显式冻结打开、保存描述符和 Handler，不取得根 Provider。
`WorkbenchCommandCatalog` 合并 Host 与插件不可变事实，并在 UI 启动前拒绝最终 ID 碰撞；可用性不缓存到
Catalog，而由 Executor 每次执行前重新查询。插件目录项只保存 Owner、Descriptor 和目标 DocumentTypeId，
不保存 Target、模型、Scope、Provider、Control、Dock 或 `ICommand`。

两个 Host Handler 直接复用 `DocumentPersistenceCoordinator` 和唯一 `DocumentOperationState`。文件选择取消、
无活动 Document、保存取消不覆盖旧错误；保存警告或失败继续进入现有错误条。Executor 把未知 ID、owner
不可用、Target 不可用、shutdown rejection、取消和异常映射为稳定结果；只有未处理异常写脱敏诊断，异常
正文、路径和 Payload 不返回调用者。

Executor 的锁只维护 accepting、在途计数和排空信号。`BeginShutdown` 在锁外传播取消，避免取消回调重入；
`WorkbenchCommandShutdownGate` 固定等待 10 秒。HostRuntime 只有在 Command 排空后才释放 Workspace 与
Document Scope，并在 Command/Workflow Action 均排空后释放 Lifecycle、插件 Provider 和 Host Provider。
超时不会强杀同进程代码，也不会继续不安全释放。

## 3. SOLID 与朴素模式

| 原则 | G2 做法 |
| --- | --- |
| SRP | Catalog 冻结事实，Executor 管理调用与排空，Handler 只适配现有打开/保存用例，Shutdown Gate 只做安全判定 |
| OCP | Host 增加命令只显式登记 Handler；插件命令继续来自 G1 Registry，不修改既有插件分支 |
| LSP | 打开、保存实现同一窄 Handler 契约；旧插件无 Command 时合并目录仍正常工作 |
| ISP | Handler 只有可等待执行；关闭参与者只有宽限、停止入口和排空查询 |
| DIP | Executor 依赖 Catalog、Availability ReadModel 与诊断端口，不接触 Provider、Scope 或 Dock |

模式只使用不可变 Catalog、Executor、Adapter Handler 和窄 Shutdown Gate。没有引入 MediatR、CQRS、事件溯源、
反射发现、服务定位器、通用命令总线、Run Manager、Schema、授权、重试、队列或 invocation scope。新增代码
均使用详细中文 XML/设计注释解释所有权、并发、取消、异常和释放原因。

## 4. 测试、覆盖率与兼容证据

定向入口为：

```powershell
dotnet test Host/MyAvaloniaManagement.Tests/MyAvaloniaManagement.Tests.csproj `
  -c Release --no-restore --filter FullyQualifiedName~WorkbenchCommand

pwsh -NoProfile -File .\scripts\Test-WorkbenchCommandG2.ps1 -Configuration Release
```

完整门禁实际通过 Workbench Command 定向 **31/31**；其中 G2 新增 Catalog/Executor、Host Handler、
Shutdown Gate 和诊断测试 20 项。Host 三层为 Unit 258、UI 65、Plugin 212，共 **535/535**，
行覆盖率 **86.12%**、分支覆盖率 **71.4%**。Command 关键文件聚合行覆盖率均不低于 90%，其中合并
Catalog 100%、Host Handler 100%、ExecutionResult 100%、Executor 96.77%、Shutdown Gate 100%。

完整 G2 门禁已执行 locked restore、Release 零警告构建、Host 三层、SDK/API、四插件真实包、覆盖率、
结构扫描和文档门禁。四插件专项门禁聚合通过 **3473** 项；文档门禁检查 96 份文档、531 个本地链接、
189 个脚本路径和 48 个项目路径。专项摘要归档 27 份 TRX 与 46 份覆盖率证据，入口为
`artifacts/test-results/WorkbenchCommandG2/summary.json`。这些数量全部来自本次实际门禁结果，不使用计划值。

API 已由完整门禁证明保持：Core Shipped/Unshipped 127/91，UI Shipped/Unshipped 45/66；G1 四份 API
哈希不变，没有 v4 baseline、新 public 成员、版本或磁盘协议变化。

## 5. 非发布边界与回滚

```text
aiflow=false
windowsCi=false
windowsSmoke=false
releaseAcceptance=false
releaseGate=false
publishable=false
published=false
uploaded=false
tagCreated=false
```

G2 不读取或使用 AIFLOW，不调用 Windows CI/Smoke、Release Acceptance、Host Release Gate、上传、签名、
tag 或发布命令。回滚单位是 `Business/Commands`、组合/关闭接入、新诊断、专项测试、覆盖率门槛、G2 脚本
和本文档整体；回滚后恢复为 G1 只有候选契约与冻结注册事实，不留下未消费 Handler 或空运行入口。
