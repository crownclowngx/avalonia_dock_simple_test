# V19 Host 复杂度收敛开发记录

> 用途：记录 V19 实施、行为修复、专项验证及最终开发门禁的实际证据。日期：2026-09-21。状态：实施中；未进行本轮实机人工验收、安装目录部署或公开发布。
> 方案：[复杂度收敛计划](../../../roadmap/host-v19-complexity-reduction-plan.md)；矩阵：[专项验证](../../../roadmap/host-v19-complexity-verification.md)。

## P0：固定输入与基线

- 实施起点：`b38c6526aa8c410ec56390a7e234a4b29315e6f8`，工作树干净；分支 `codex/host-v19-complexity-reduction`。
- 本地命令：`dotnet run --project tools/MyAvaloniaManagement.Gate -- verify`；运行 ID：`20260921-010831-b38c6526aa8c`，证据位于 `artifacts/gate/20260921-010831-b38c6526aa8c/summary.json`。
- Release 编译 0 警告、0 错误；SDK 91、Host Unit 626、Host Plugin 292、Host Headless UI 288、MyPlugTest Unit 11、真实 ZIP 验收 1，全部通过，无跳过。契约及已发布 API 比较通过。
- 本轮仅使用本地开发验证；未使用 AIFLOW、Windows CI、seal、发布覆盖率、发布 Windows Smoke 或重复性发布门禁。

实际调用核对：主窗通过 `PrepareWindowCloseAsync → WorkspaceSession.PrepareApplicationCloseAsync → DocumentCloseCoordinator.PrepareRangeCloseAsync` 准备许可；旧 `ConfirmWindowCloseAsync` 与 `HasDirtyDocuments` 仅剩转发和测试调用。单页与范围关闭的用户决策及排空后检查存在重复与缺口；备份创建后等待恢复确认处没有覆盖异常的局部回滚保护。

保存警告需分开记录：主文件已经原子提交不因 `AcceptChanges` 或备份写入异常变为保存失败；关闭许可仍需复核排空后的实时脏状态。旧单页路径与范围路径对此不一致，实施时作为明确的行为修复处理，不伪装成等价重构。

## 后续阶段

P0 两项风险均已通过确定性测试复现：`DocumentCloseTests.V19干净单页排空期间出现修改必须撤销关闭并重新开放命令` 在旧实现实际重试 1 次（预期 0）；`DocumentPersistenceTests.V19恢复确认异常必须立即释放候选且不改动输入文件` 在旧实现释放 0 次（预期 1）。红灯证据：`artifacts/host-v19/p0-risk-reproduction/risks-red.trx`，2 项均因行为断言失败，非编译失败。

恢复确认缺口先以局部 `try/finally` 修复，保留原始确认异常及原件/备份内容；P2 再统一创建到发布的回滚义务。该测试还断言 Scope 立即释放、生命周期取消先于模型释放、未发布和零写入。

P1–P6 正在执行。各项真实测试方法、红绿结果、设计取舍和最终验证在完成对应工作后追加；本记录不提前声明覆盖或通过。

恢复最小修复验证：DocumentPersistenceTests 21/21 通过，证据 artifacts/host-v19/p0-risk-reproduction/recovery-minimal-green.trx。

## P1：关闭规则收敛

`RequestDecisionAsync` 是唯一确认/保存规则，`VerifyFinalStateAsync` 是单页和范围排空后的共同许可条件。同步干净快速路径、一次性许可和精确单页接续浮窗范围均保留。删除 Coordinator、Session、MainWindowViewModel 的旧 `ConfirmWindowCloseAsync`、`HasDirtyDocuments` 及 `_windowRequestPending`；原测试迁移到 `PrepareRangeCloseAsync(isApplicationExit: true)` 或主窗 `PrepareWindowCloseAsync`。

新增方法覆盖：`V19干净单页排空期间出现修改必须撤销关闭并重新开放命令`（C01/C09）；`V19单页保存后排空又修改必须保留_明确放弃可继续`（C02）；`V19所有关闭入口按最终脏状态区分确认修订警告与备份警告` 六组参数（C03）；`V19同步取消回调修改文档并排空也不能走干净快速关闭`（C04）；`V19范围放弃许可不能覆盖确认时干净而后来变脏的页面`（C02/C08）。原保存中新修订测试改为等待关闭收尾事件，消除“错误提示到达即认为 pending 已清理”的竞态；新增测试初稿误读展示脏状态也已改为读取真实模型事实。失败 TRX 原样保留。

文档相关 Unit 99/99、关闭/浮窗/重启 Headless UI 75/75 通过，无跳过。证据：`artifacts/host-v19/p1-close/documents-final.trx`、`close-ui.trx`。既有测试继续验证原生首次/重试否决、CanClose、单页接续不扩大许可、目标集合变化、Scope 顺序和重启最终布局。保存警告行为修复已同步 `docs/reference/document-persistence.md`。

## P2：待发布回滚义务

新增 `PendingWorkspaceDocument`，仅持有候选身份和回调 Session 的回滚义务；不直接释放模型/View/Scope。创建、读取路径登记、恢复确认和发布串成同一个局部 using 生命周期，不再逐层约定 nullable pending 的转移。Session 统一撤销部分插入、释放和清除持久化/恢复登记；清理异常只记录诊断，保留原始失败。成功发布后义务解除，重复 Dispose 无副作用。

新增 `V19恢复确认等待期间退出仍由会话持有候选_发布拒绝后清除全部状态`（R01/R03/R05/R07）、`V19候选发布成功后重复释放义务不释放已发布Scope`（R01/R04）及确认异常测试的清理异常参数（R06）。原 `N07N08初始化失败或等待期间退出均不发布并仅释放一次`、`N08目标插入后失败撤销部分写入且重复发布不移动原页面` 继续覆盖失败阶段和目标语义（R02/R04/R08）。

Unit 61/61、Plugin 所有权 56/56、Document/Restart Headless UI 73/73 通过，无跳过。证据目录 `artifacts/host-v19/p2-rollback`，结果分别为 `rollback-final.trx`、`ownership-plugin.trx`、`rollback-ui.trx`。编译阶段发现的测试属性名与命名空间错误已修正，未将编译失败登记为行为红灯。

## P3：命令目录与执行绑定

Host 与合并 Catalog 只含纯描述，显式 Handler 映射放在组合根和 `HostWorkbenchCommandBindings`。背景启动提前校验目录，UI AttachWorkbench 校验绑定后才交给 Shell。绑定拒绝缺失、重复与多余 ID；State Query 捕获同一个 Handler 到 Route，Executor 不再从目录读取执行对象。原容器生命周期、参与者登记、V18 目标代次和共享 Command Store 均保持。

`V19纯命令目录可在后台解析且不创建工作区执行绑定或视图` 使用会抛错的 DI 边界证明无连带构造（M01）；`V19缺失重复和多余绑定在组合时携带准确身份失败` 三组参数验证诊断（M02）；`V19状态查询与执行使用同一个冻结Handler且查询不会执行命令` 验证身份、冻结和调用计数（M03）。原命令、Context、目标、关闭门、Palette 及投影测试覆盖 M06–M08；启动 UI 边界与参与者登记按生产代码审查，正常/失败启动及所有权在最终完整 verify 再回归。

Unit 125/125、Command/Palette/Startup Headless UI 31/31 通过，无跳过。证据：`artifacts/host-v19/p3-catalog/commands-final.trx`、`commands-ui.trx`。同步启动契约、命令契约及架构说明。

## P4：按身份读取必要事实

创建菜单查询改为直接依赖 WorkspaceCatalog，删除 Session 的全目录转发；已有目录测试迁至真实目录入口。新增精确创建入口检查和页面可激活检查，Palette 可用性与焦点恢复不再调用 ReadDirectory/GetOpenPages。仍按实时 Registry 可用性、发布引用、实际 Dock 挂接、关闭和退出事实判断；执行前复核不复用展示快照，也不增加长期缓存。

`V19指定功能查询严格区分默认入口和声明意图且不创建页面` 覆盖默认/明确/未知/跨类型意图及零创建（Q02/Q05）；`V19指定页面查询与真实展示一致并在关闭撤销移除和退出后即时重查` 覆盖成员及关闭变化、拒绝旧执行（Q03/Q05）；后台目录 DI 边界测试增加创建菜单读取（Q01）。原重复查询测试增加窄查询，仍验证零通知、零持久化、身份及创建释放次数不变。Q04 按调用链审查确认不构建其他页面展示；Q06 保留原目录分类、非法路径回退、排序和去重诊断测试。窄身份判定本身不产生分类诊断，诊断仍在真实目录读取时发生。

Unit 181/181、相关 Headless UI 37/37 通过，无跳过；`artifacts/host-v19/p4-query/query-final.trx`、`query-ui.trx`。真实插件包目录验收已迁移到 WorkspaceCatalog，最终 verify 在生成本轮包后执行。

## P5：工具意图与提交

删除 Session.ShowTool、TrySetToolVisibility 和 ToolDockCoordinator.ShowTool；相关 Unit/UI 测试迁移到 OpenTool/SetToolVisibility 的结构化结果。Session 共用就绪、身份、可用性及原实例查找；恢复统一走浮窗位置恢复和 Dock 回退。OpenTool 保留可见时定位、自动收起预览、最小化恢复；SetToolVisibility 保留 CanClose、已满足与原生取消判断。底层恢复布尔值只描述 Dock 协议步骤，不再充当业务结果。

`V19重复打开仍定位并记录一次访问_重复显隐不通知不记访问`（T05/T06）精确断言布局、访问和焦点通知计数；`V19批量隐藏保留逐项失败和成功且仅通知一次`（T02/T06）断言部分成功、逐项结果、原实例与一次通知。Unit 65/65、Tool/布局/Document 浮窗 Headless UI 124/124 通过，无跳过；`artifacts/host-v19/p5-tools/tools-final.trx`、`tools-ui.trx`。现有 UI 继续验证 T01/T03/T04 的自动收起、浮窗取消和原位置恢复，隐藏不释放 Tool 的生命周期政策保持。工具使用指南的可观察行为未改变，不加入内部实现细节。
