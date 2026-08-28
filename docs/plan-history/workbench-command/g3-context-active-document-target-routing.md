# Workbench Command G3：Context v1 与活动 Document Target 路由实施记录

> 状态：已完成（2026-08-28；完整非发布门禁通过）。
>
> 输入提交：`0f49440a3d63bdc6bdefff9728b2b3ab44468473`
>
> 输入 Git tree：`cfd3cb166657402ec76de9573d1f6fb10f93752a`
>
> 前置：[G2 无 UI Catalog 与 Executor](./g2-command-catalog-executor.md)
>
> 总设计：[Workbench Command 引入评审与实施任务书](../../design/workbench-command-introduction-plan.md#g3建立-context-v1-与活动-document-target-路由)

## 1. 实施边界

G3 只完成 Host internal 的活动 Document 事实、Context v1、命令状态查询、当前实例 Target 路由和
Document 安全关闭协调。没有迁移 `MainWindowViewModel` 的打开/保存方法、`MenuView.axaml` 或
`MainWindow.axaml` 的 `Ctrl+S`；这些 UI 入口在 G4 前仍沿用旧路径。

本阶段没有修改 Core/UI SDK public API、API baseline、版本、NuGet 包内容、模板或外部 WorkflowStudio、
ClassicGame。SDK 保持 3.2.0，Host 保持 3.0.0，manifest/Document/layout schema 保持 2，布局文件仍为
`layout-v2.json`，数据根仍为 `v2`。没有引入 AIFLOW、Windows CI/Smoke、Release Acceptance 或发布门禁。

## 2. 设计思路与源码变化

活动事实只有一条来源：`HostDockFactory.OnActiveDockableChanged` 精确转交 `WorkspaceSession`，Session 从
主 DocumentDock 读取语义上的活动 `ManagedDocumentDockable`，按 Adapter 引用去重后发布独立
`ActiveDocumentChanged`。首次布局提交、布局替换、标签切换、活动 Document 关闭和 Session 退出都经过
同一提交点；Tool 激活不会改变活动 Document，也不会借用 `LayoutChanged` 误报。

`WorkbenchContextSnapshot` 是只含五个字段的不可变纯值：`HasActiveDocument`、
`ActiveDocumentTypeId`、`ActiveDocumentOwnerId`、`IsActiveDocumentPersistable`、`Revision`。空上下文从
revision 0 开始，只在 Adapter 实例引用变化时递增。Adapter、Target 与 Adapter ClosingToken 仅存在于
独立的 `WorkbenchContextCapture`，不进入快照、SDK、Catalog 或磁盘格式。

统一状态链为：

```text
Command Catalog
    → owner availability
    → 当前 Context owner / DocumentType
    → 当前实例 Target 能力
    → Host Handler 或 Target.CanExecute
    → CommandNotFound / OwnerUnavailable / TargetUnavailable / Disabled / Enabled
```

Host Open Handler 始终 Enabled；Host Save Handler 只在活动 Document 可持久化时 Enabled。插件状态异常
映射为 Disabled 并写脱敏诊断。状态查询不缓存 `CanExecute`，也不复制 Host Handler 的业务判断。

状态层每次切换先在锁内更新 revision 与当前 Target，使旧代次立即失效，再在锁外退订旧 Target、订阅新
Target。工作线程事件只有 sender、revision、owner、DocumentType 与 CommandId 全部仍匹配时才发布定向
刷新；未知命令、旧实例和迟到事件直接丢弃。插件自定义事件访问器和刷新观察者抛错均被隔离并诊断。

`WorkbenchCommandExecutor` 每次调用重新解析 Catalog、owner、当前捕获、owner/type、Target 和
`CanExecute`，取得按 Adapter 引用计数的命令租约后再次确认实例仍为当前目标。插件执行链接调用者取消、
单 Document 关闭取消、Adapter ClosingToken 和 Host shutdown token，并真实 `await ExecuteAsync`。
`CanExecute=false` 返回新增 `CommandDisabled`；插件异常只返回固定脱敏文案，非关联
`OperationCanceledException` 仍按失败处理。执行后仅在捕获实例仍为当前目标时发布定向刷新。

`WorkbenchDocumentCommandLeaseStore` 只维护 Adapter 引用、在途计数、关闭取消和排空任务。Document 获准
关闭后先拒绝新调用并取消在途命令；同步 Dock 关闭在尚未排空时返回拒绝，最后一个协作调用退出后由
`DocumentCloseCoordinator` 自动重试。没有强制释放超时；不协作命令会让标签保持打开。Dock 基类最终
拒绝时恢复入口，真正关闭时移除租约。排空完成前不会释放 View、Adapter、ClosingToken 或 Scope；最终
仍沿用“控件缓存 → View/Adapter → ClosingToken/Scope”的既有释放顺序。

HostRuntime 的工作台命令退出仍使用 G2 的独立 10 秒全局门控；超时保留对象图。单 Document 引用计数
租约没有与全局 shutdown 或 Workflow Action Run Manager 合并。

## 3. SOLID 与朴素模式

| 原则 | G3 做法 |
| --- | --- |
| SRP | Workspace 只发布活动事实；Context Store 只维护快照/捕获；State Query 只判定状态；Executor 只执行；租约只保护关闭 |
| OCP | 新路由继续消费既有 Catalog、Availability ReadModel 与 SDK Target；没有增加插件类型分支 |
| LSP | Host Open/Save 使用同一窄 Handler 状态/执行契约；G1 Target 契约没有变形 |
| ISP | Snapshot 只有五个字段；Handler、关闭参与者和捕获对象只暴露各自所需事实 |
| DIP | Executor 依赖 State Query 与租约；状态层依赖 Catalog/ReadModel/Context，不定位 Provider 或 Scope |

采用的模式只有不可变 Snapshot、原子 Capture、Catalog 查询、显式 Handler Adapter 和按 Adapter 引用计数
的 Lease。没有引入 MediatR、CQRS、事件总线、事件溯源、反射发现、字符串条件、重试、队列、通用任务
运行时或第二套 Workflow Action Runtime。新增类型以及并发、revision、取消、退订、所有权和回滚判断均有
中文 XML/设计注释。

## 4. 测试、覆盖率与兼容证据

专项入口为：

```powershell
dotnet test Host/MyAvaloniaManagement.Tests/MyAvaloniaManagement.Tests.csproj `
  -c Release --no-restore --filter FullyQualifiedName~WorkbenchCommand

pwsh -NoProfile -File .\scripts\Test-WorkbenchCommandG3.ps1 -Configuration Release
```

Workbench Command 定向测试实际通过 **47/47**。新增覆盖无/Host/普通/可持久化 Document、首次布局、标签
切换、同实例去重、同类型多实例、Tool 激活、关闭/替换；Target 缺失、owner/type 不匹配、CanExecute
true/false/抛错、工作线程/未知/迟到事件、订阅访问器失败；当前实例执行、切换竞态、执行前重查、四令牌
链接、异常脱敏、非关联取消、定向刷新、shutdown 排空；以及干净/脏关闭、取消、在途排空、Scope 时点、
新调用拒绝、Dock 拒绝恢复和最终释放顺序。

Host Unit 274、UI 65、Plugin 212，Host 三层共 **551/551**；行覆盖率 **86.51%**、分支覆盖率 **71.76%**，
均不低于 G2 的 86.12% / 71.4%。G3 关键文件聚合行覆盖率为：

| 文件 | 行覆盖率 |
| --- | ---: |
| `WorkbenchContextSnapshot.cs` | 100.00% |
| `WorkbenchContextStore.cs` | 96.43% |
| `WorkbenchCommandStateQuery.cs` | 93.78% |
| `WorkbenchCommandExecutor.cs` | 93.85% |
| `WorkbenchDocumentCommandLeaseStore.cs` | 99.03% |
| `DocumentCloseCoordinator.cs` | 94.89% |

四插件专项门禁聚合通过 **3537** 项：MyPlugTest 644、DaTangAccountingHelpPlug 695、MySmallTools 835、
BiliDownloader 1363。门禁执行 locked restore、Release 零警告构建、Host 三层、SDK/API、四插件真实包、
定向测试、覆盖率、结构扫描和文档门禁。专项目录归档 27 份 TRX 与 46 份覆盖率证据，摘要位于
`artifacts/test-results/WorkbenchCommandG3/summary.json`。

最终摘要中的 `baseGateReused=true` 表示：同一最终工作树的完整 G7 已在紧邻的前一步全部通过；G3 后置
断言首次仅因脚本内 Core Unshipped 哈希常量少写两个字符而停止。修正常量后复用了刚生成的 G7 原始证据，
重新执行 47 项定向测试、API 哈希、关键文件覆盖率、结构、文档和证据归档；生产源码在两次之间没有变化。

结构门禁证明 Snapshot 没有 UI/Dock/Provider/Scope/对象字典，Context 捕获不接触 Dock Framework 或
服务定位，`DocumentScopeManager` 没有 `GetService`。Core Shipped/Unshipped 127/91、UI
Shipped/Unshipped 45/66 及 G1 四份 SHA-256 均保持不变。

## 5. 非发布事实与整体回滚

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

G3 没有读取或使用 AIFLOW，没有调用 Windows CI/Smoke、Release Acceptance、Host Release Gate、上传、
签名、tag 或发布命令。完整回滚单位为活动 Document 通知、Context/State/Executor/租约实现、关闭协调接入、
诊断、测试、覆盖率门槛、`Test-WorkbenchCommandG3.ps1` 和本记录；失败时整体回到输入 G2，不保留
`LayoutChanged`/`ActiveDocumentChanged` 双状态源、未消费 Snapshot 或半成品 Target 路由。
