# V19：Host 复杂度收敛开发验证计划

> 用途：验证关闭规则、待发布回滚、命令目录、实时查询和工具入口收敛的行为与结构。
> 状态：P0 完整本地基线已通过，风险复现及实施矩阵正在执行。日期：2026-09-21。逐项结果见[开发记录](../archive/records/host-v19/development-acceptance.md)。
> 设计范围与分阶段要求见 [V19 方案](host-v19-complexity-reduction-plan.md)。本文与待实施方案一同维护在 roadmap；实施后将可复用流程整理到 maintenance，实际结果归入 archive/records。本文只定义本地开发验证，不授予发布资格。

## 1. 执行边界

SOLID 优先，设计模式朴素使用；中文注释与设计思路属于必须审查的交付。测试覆盖可观察行为、实际依赖和资源释放，不用方法名、文件数或行数阈值代替设计判断。

不使用 AIFLOW、Windows CI、seal、发布 Windows Smoke、发布覆盖率或发布重复性门禁；不部署安装目录或发布包。发布阶段再执行原发布政策，不降低阈值或删除发布检查。

本地完整 `verify` 的事实源为 [Gate 执行图](../../tools/MyAvaloniaManagement.Gate/GateExecutionGraph.cs)及[主仓验证](../maintenance/verification.md)：固定补丁准备、locked restore、Release 零警告构建、SDK/Host Unit/Host Plugin/Host Headless UI/MyPlugTest Unit、契约和已发布 API 比较、MyPlugTest 打包及真实 ZIP 验收。开发打包和本地包验收不等于公开发布。verify 不执行 coverage/windows-smoke。

本文“既有落点”指已存在的测试类，**不表示其中已有本行全部断言**。P0 必须逐项核对方法和参数；后续记录每个矩阵编号对应的真实测试或审查证据。没有执行的项目保持待验证。

## 2. 测试方式与失败纪律

1. P0 先跑当前完整基线。两项疑似缺陷使用可控任务和异常注入复现，保留旧实现失败和最小修复后通过的结果；没有复现时修正风险结论，不伪造红灯。
2. 等价重构保护测试在调整前后都应成立；经确认的行为修复单独记录触发条件、旧/新结果及契约依据。
3. 并发使用 TaskCompletionSource、显式闸门和可观察事件，UI 使用真实 Headless Dispatcher 推进。设置有界等待防止挂死，不用任意 Sleep 制造顺序。
4. 资源验证使用真实 Scope/View/模型释放计数、引用身份和登记结果。证明创建次数、释放次数及先后关系，不仅验证一个 bool。
5. 旧入口测试迁移到生产使用的用例路径；删除测试前写出替代断言落点，不能只删旧测试以消除引用。
6. 缺包、零测试、未运行、超时、失败与跳过不得登记为通过。新增必需用例不得跳过；既有跳过须逐项说明且不能覆盖本轮必需矩阵。
7. 复用现有测试夹具和接口；不为测试把 Host internal 类型变成 SDK API，不复制整套生产分支作为“预期值”。

## 3. C：关闭规则与原生协议

既有落点：[DocumentCloseTests](../../Host/MyAvaloniaManagement.Tests/DocumentCloseTests.cs)、[DockWindowCloseTests](../../Host/MyAvaloniaManagement.Tests/DockWindowCloseTests.cs)、[WorkbenchCommandDocumentTargetTests](../../Host/MyAvaloniaManagement.Tests/WorkbenchCommandDocumentTargetTests.cs)、[DocumentWindowV16UiTests.Close](../../Host/MyAvaloniaManagement.UiTests/DocumentWindowV16UiTests.Close.cs)、[HostRestartUiTests](../../Host/MyAvaloniaManagement.UiTests/HostRestartUiTests.cs)。

| 编号 | 必须验证的行为 | 缺口/断言重点 |
| --- | --- | --- |
| C01 | 原本干净的单页等待命令排空，期间出现修改，应保持打开 | 优先复现；与已有范围关闭新增修改用例对照，断言不重试关闭、不授予许可、Scope 存活、后续可重新操作 |
| C02 | 单页选择保存后，排空期间再次修改；明确放弃与未获放弃许可的目标分别处理 | 保存期间和保存结束后分别制造修改，按精确目标验证许可，不把“曾保存”当永久授权 |
| C03 | 单页、范围、应用退出的保存取消/失败、新修订、警告语义一致且有据 | 主文件失败、AcceptChanges 异常、备份失败、新修订分例；保存状态与关闭结果分别断言 |
| C04 | 干净无在途操作保留同步快速路径；等待时只启动一次确认/排空 | 实际同步返回、重复关闭请求计数、一次性授权与重试次数 |
| C05 | 确认、保存、错误提示、重入回调异常均不遗留错误许可或未观察任务 | 页面保持、pending 清理、命令门恢复、原异常/诊断政策 |
| C06 | 首次或重试原生取消及 CanClose 限制保留 | Headless 中原生回调链、内容/窗口引用相同、撤销后可重新关闭 |
| C07 | 单页等待期间变成最后一页只接续原精确许可 | 不重复弹窗、不扩大到多页或应用退出，最终浮窗正常回收 |
| C08 | 主窗、浮窗、单页范围互斥；等待中目标增删/迁移不能错误授权 | 固定目标集合与窗口身份复查；不得因移窗关闭其他内容 |
| C09 | 取消不取消最终 Document 生命周期；最终关闭先撤回 Target 再释放 View/Scope | 区分临时命令关闭 Token 与 Document ClosingToken，验证通知及释放顺序 |
| C10 | 重启、普通退出、最终布局保存和失败撤销沿用原关闭链 | HostRestartTests/UiTests、DocumentOperationShutdownTests、生命周期回归 |
| C11 | 旧 ConfirmWindowCloseAsync/HasDirtyDocuments 入口和仅属于它的状态可删除 | 全仓调用检查、旧测试到现行准备关闭入口的映射；不以永久文本搜索测试锁死私有名称 |

重点已有对照：`DocumentCloseTests.范围关闭等待期间出现新修改则撤销所有许可`、`V16单页已确认许可可接续精确的一页范围且取消会重新开放命令`、`V16单页许可不能扩大为多页范围或应用退出`。这些用例通过不能替代 C01 的单页竞态复现。

## 4. R：待发布文档与回滚

既有落点：[DocumentPersistenceTests](../../Host/MyAvaloniaManagement.Tests/DocumentPersistenceTests.cs)、[WorkspaceSessionAndDockFactoryTests](../../Host/MyAvaloniaManagement.Tests/WorkspaceSessionAndDockFactoryTests.cs)、[DocumentScopeManagerTests](../../Host/MyAvaloniaManagement.PluginTests/DocumentScopeManagerTests.cs)、[DocumentOperationShutdownTests](../../Host/MyAvaloniaManagement.Tests/DocumentOperationShutdownTests.cs)、[DocumentWindowV16UiTests.Creation](../../Host/MyAvaloniaManagement.UiTests/DocumentWindowV16UiTests.Creation.cs)。

| 编号 | 必须验证的行为 | 缺口/断言重点 |
| --- | --- | --- |
| R01 | 坏主文件、有可初始化备份，恢复确认抛异常 | 优先复现；候选不在 Owned/已发布页面及路径/恢复登记中，View/Scope 释放一次，源文件字节不变 |
| R02 | 恢复拒绝立即回收；接受后文档正常发布并要求另存 | 不覆盖原件/备份，RequiresSave、标题和查重一致；成功对象没有被回滚句柄释放 |
| R03 | New/Restore 模型初始化、View 创建/绑定分别失败 | 各阶段都无半成品标签和登记；最终 Scope 释放一次，未创建资源不被虚假计数 |
| R04 | 状态提交、恢复登记、Dock 插入前及部分插入后失败 | 覆盖整个待发布区间；活动 Target、事件订阅和集合最终一致 |
| R05 | 原失败与释放异常同时发生 | 原失败可追溯，清理失败按明确政策报告；不得用 finally 异常悄悄覆盖原始问题 |
| R06 | 恢复确认/初始化等待期间 Host 退出、超时或迟到完成 | DocumentOperationGate 排空证据有效；未结束任务不释放依赖，完成后不重复回收 |
| R07 | 并发打开同一路径、损坏备份及不支持身份保持原结果 | 无重复发布、无无效恢复提示，输入文件不变，路径规范化策略保持 |
| R08 | V16 来源窗口/组失效、V18 多 Intent/同名页面和迟到焦点 | 新建落点与精确 Intent 保持；失败不创建替代页面，旧结果不抢新会话焦点 |

R01 的确认异常不应通过新增产品确认框制造。使用现有交互端口注入异常，资源断言要在 context 最终 Dispose 之前执行，避免退出兜底掩盖泄漏。

## 5. M：目录、Handler 绑定及启动

既有落点：[WorkbenchCommandCatalogExecutorTests](../../Host/MyAvaloniaManagement.Tests/WorkbenchCommandCatalogExecutorTests.cs)、[WorkbenchCommandContextStateTests](../../Host/MyAvaloniaManagement.Tests/WorkbenchCommandContextStateTests.cs)、[ServiceAndModelTests](../../Host/MyAvaloniaManagement.Tests/ServiceAndModelTests.cs)、[HostLifecycleOwnershipTests](../../Host/MyAvaloniaManagement.PluginTests/HostLifecycleOwnershipTests.cs)、[StartupSplashUiTests](../../Host/MyAvaloniaManagement.UiTests/StartupSplashUiTests.cs)、[CommandPaletteV18TargetTests](../../Host/MyAvaloniaManagement.Tests/CommandPaletteV18TargetTests.cs)。

| 编号 | 必须验证的行为 | 缺口/断言重点 |
| --- | --- | --- |
| M01 | 单独构造/合并/校验命令目录不创建 Workspace、窗口、View、模型或 Scope | 真实组合中的创建探针；同时核对 Handler/Provider 未被目录间接解析 |
| M02 | Host/Plugin 重复 ID、缺失/重复 Handler 绑定在工作台交付前失败 | 无最后注册者胜出，保留稳定诊断身份与顺序；空目录和可选 Host 入口也覆盖 |
| M03 | 状态查询和 Executor 使用同一份绑定，Host CanExecute/执行副作用保持 | 每命令调用计数、共享实例身份；不经业务层 IServiceProvider 解析 |
| M04 | 后台目录校验与 UI Handler/工作台创建边界正确 | 实际线程和创建时点；ValidateScopes/ValidateOnBuild、单例/瞬态及 ShutdownParticipants 登记保持 |
| M05 | 早期目录失败与后期 Handler 绑定失败均安全回滚 | 只清理实际创建对象，不为回滚解析 Workspace；已初始化插件逆序关闭，原失败保留 |
| M06 | V18 捕获页面及 revision 过期时拒绝，目标未变仍重查 CanExecute | 菜单/快捷键无显式约束时保持原当前页行为；共享绑定不保存某次 Palette 目标 |
| M07 | 当前插件目标的 owner、实例、租约、取消、完成通知和排空顺序保持 | 关页/切页竞态、目标通知异常和释放后迟到回调；不把目录快照当执行许可 |
| M08 | 菜单发现许可、快捷键冲突治理、Palette 分组/禁用原因/精确操作保持 | WorkbenchCommandProjection/PresentationTests、CommandPaletteV18Tests 与 Headless 交互 |

## 6. Q：按身份查询与展示投影

既有落点：[PluginNavigationTests](../../Host/MyAvaloniaManagement.Tests/PluginNavigationTests.cs)、[CognitiveUxV8Tests](../../Host/MyAvaloniaManagement.Tests/CognitiveUxV8Tests.cs)、[WorkspaceLayoutQuerySnapshotTests](../../Host/MyAvaloniaManagement.Tests/WorkspaceLayoutQuerySnapshotTests.cs)、[CommandPaletteV18Tests](../../Host/MyAvaloniaManagement.Tests/CommandPaletteV18Tests.cs)、[CommandPaletteV18UiTests](../../Host/MyAvaloniaManagement.UiTests/CommandPaletteV18UiTests.cs)。

| 编号 | 必须验证的行为 | 缺口/断言重点 |
| --- | --- | --- |
| Q01 | 创建目录查询直接依赖目录事实，可在无 Workspace 实例时读取 | 组合与纯元数据输入证明，Host/Plugin/不可用项结果一致 |
| Q02 | 功能窄查询按 DocumentTypeId + CreationIntentId 判断 | 同名、多 Intent、无 Intent、无效 Intent、owner 撤回/恢复，不能按显示名路由 |
| Q03 | 页面窄查询按 PageId 检查拥有、发布、挂接、关闭与可用性 | 同类型不同实例、未发布、已释放、分割和浮窗页面、Host 正在退出 |
| Q04 | 窄查询不生成完整分类树/页面展示列表，不写入跨事件缓存 | 代码审查为主，真实创建/事件计数作行为保护；不写镜像实现的测试 |
| Q05 | 展示后/执行前目标变化仍拒绝旧请求，实时重查没有被优化掉 | V18 PageId/Revision、来源窗口和创建目标回归，不能缓存 IsEnabled 授权执行 |
| Q06 | 完整列表排序、分类、非法路径诊断去重、可用性通知与短命快照语义保持 | 长列表、同名来源、多分类、重复查询无模型创建/布局变化/额外通知 |

如果报告性能收益，在同机、同数据、相同预热和采样方式下记录前后耗时及分配；测量前不承诺百分比，也不增加不稳定的墙钟时间硬阈值。无基准结果时只声明删除了哪些全量投影工作。

## 7. T：工具用例与布局提交

既有落点：[ToolCenterTests](../../Host/MyAvaloniaManagement.Tests/ToolCenterTests.cs)、[ToolViewModelTests](../../Host/MyAvaloniaManagement.Tests/ToolViewModelTests.cs)、[ToolCenterUiTests](../../Host/MyAvaloniaManagement.UiTests/ToolCenterUiTests.cs)、[DockToolWindowCloseUiTests](../../Host/MyAvaloniaManagement.UiTests/DockToolWindowCloseUiTests.cs)、[AutoHideRestoreVisualTests](../../Host/MyAvaloniaManagement.UiTests/AutoHideRestoreVisualTests.cs)、[DockLayoutV3UiTests](../../Host/MyAvaloniaManagement.UiTests/DockLayoutV3UiTests.cs)。

| 编号 | 必须验证的行为 | 缺口/断言重点 |
| --- | --- | --- |
| T01 | OpenTool 对 Hidden/Docked/AutoHidden/Floating 产生正确显示、展开或定位 | 同一模型/View；自动收起预览；最小化窗口恢复；V18 动作提示一致 |
| T02 | 设置显隐区分已满足、改变、拒绝、失败 | 原 CanClose、不可用、未注册、未就绪和创建失败原因；未知身份无副作用 |
| T03 | 最后一个浮动工具关闭被原生取消时不报告已隐藏 | 窗口、模型、活动项和原布局保持；再次尝试可以成功 |
| T04 | 恢复遵守仍有效的原位置和现有安全回退 | 主窗、浮窗、自动收起及缺失旧容器；不重建业务实例 |
| T05 | 定位已可见工具仍执行必要焦点与访问记录，取消/失败不伪记成功 | 中心和 Palette 共用结果；每次成功意图只记一次访问 |
| T06 | 单次提交和 HideAll 的通知、部分成功及持久化结果保持 | 观察者计数、逐项结果、失败诊断隔离；无重复保存或丢失成功项 |
| T07 | 隐藏不释放 Tool 业务服务，Runtime 退出仍只释放一次 | PluginContainerIsolation/HostLifecycleOwnership 及实例释放计数 |

## 8. A：跨阶段架构、注释与文档

| 编号 | 必需检查 | 证据 |
| --- | --- | --- |
| A01 | SOLID、依赖与唯一所有权符合方案；新增类型有明确删除的负担 | 差异审查：规则位置、状态集合、依赖边及失败出口的前后说明 |
| A02 | 无新 SDK/public API、格式、身份、版本或插件特判 | HostApiBoundaryTests、PublicApiContractTests、PluginHostBoundaryTests 及完整 verify 契约/API 阶段 |
| A03 | 文档/命令/Workflow 各自排空，Provider 隔离与超时保留不退化 | DocumentOperationShutdownTests、WorkbenchCommandShutdownGateTests、WorkflowActionShutdownGateTests、Plugin 生命周期回归 |
| A04 | 中文注释说明回滚、许可、提交、依赖和时序，过期注释同步清理 | 人工代码审查，不以注释数量或文件长度打分 |
| A05 | 旧测试有效断言迁到现行入口，矩阵完整且没有伪通过 | 矩阵编号 → 实际方法/参数/断言 → 本次 TRX，未执行和未覆盖单列 |
| A06 | 本次新增/修改文档、导航、源码引用、标题锚点和嵌入帮助一致 | 显式链接检查、git diff --check、帮助 Unit/UI、新资源读取与渲染核查 |
| A07 | 最终完整本地 verify 对应最终源码和嵌入 Markdown | HEAD/差异身份、Gate run-id/summary、退出码和各测试组数量；不拼接旧结果 |

## 9. 本地开发命令

以下在主仓根目录执行。P0 基线和 P6 最终完整开发门禁使用：

```powershell
dotnet run --project tools/MyAvaloniaManagement.Gate -- verify
```

本次仅文档交付不提前执行整个实现阶段门禁；运行第 9.3 节的文档专项并检查链接。后续代码实施必须完整执行基线、阶段专项和最终 verify。

### 9.1 专项准备

固定补丁和本次 locked restore 已成功后，专项可以使用 --no-restore；不使用旧 DLL 替代当前构建。所有共享中间目录的构建/测试串行运行，保留 -m:1 和警告检查。每轮使用新目录，以下为命令模板，不是已执行证据。

```powershell
$v19RunId = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$v19Results = "artifacts/host-v19/$v19RunId"
```

### 9.2 阶段专项

```powershell
# P1/P2：关闭、保存、回滚、资源所有权。
dotnet test Host/MyAvaloniaManagement.Tests -c Release --no-restore -m:1 -warnaserror --filter 'FullyQualifiedName~DocumentCloseTests|FullyQualifiedName~DockWindowCloseTests|FullyQualifiedName~DocumentPersistenceTests|FullyQualifiedName~DocumentOperationShutdownTests|FullyQualifiedName~WorkspaceSessionAndDockFactoryTests|FullyQualifiedName~WorkbenchCommandDocumentTargetTests|FullyQualifiedName~HostRestartTests' --logger 'trx;LogFileName=documents-unit.trx' --results-directory "$v19Results/documents-unit"

# P3/P4：目录、组合、实时查询及 V18 目标约束。
dotnet test Host/MyAvaloniaManagement.Tests -c Release --no-restore -m:1 -warnaserror --filter 'FullyQualifiedName~WorkbenchCommand|FullyQualifiedName~ServiceAndModelTests|FullyQualifiedName~HostCatalogPluginRegistryTests|FullyQualifiedName~StartupCoordinatorTests|FullyQualifiedName~PluginNavigationTests|FullyQualifiedName~CognitiveUxV8Tests|FullyQualifiedName~WorkspaceLayoutQuerySnapshotTests|FullyQualifiedName~CommandPaletteV18' --logger 'trx;LogFileName=commands-query-unit.trx' --results-directory "$v19Results/commands-query-unit"

# P5 及全阶段边界检查。
dotnet test Host/MyAvaloniaManagement.Tests -c Release --no-restore -m:1 -warnaserror --filter 'FullyQualifiedName~ToolCenterTests|FullyQualifiedName~ToolViewModelTests|FullyQualifiedName~WorkspaceSessionAndDockFactoryTests|FullyQualifiedName~DockLayoutWorkspaceStateTests|FullyQualifiedName~HostApiBoundaryTests|FullyQualifiedName~PublicApiContractTests|FullyQualifiedName~WorkflowActionShutdownGateTests' --logger 'trx;LogFileName=tools-boundaries-unit.trx' --results-directory "$v19Results/tools-boundaries-unit"

# 真实插件所有权和启动回滚；常规专项排除需要打包输入的验收项。
dotnet test Host/MyAvaloniaManagement.PluginTests -c Release --no-restore -m:1 -warnaserror --filter '(FullyQualifiedName~HostLifecycleOwnershipTests|FullyQualifiedName~DocumentScopeManagerTests|FullyQualifiedName~PluginContainerIsolationTests|FullyQualifiedName~StartupPluginProgressTests|FullyQualifiedName~HostRestartLifecycleTests|FullyQualifiedName~PluginHostBoundaryTests)&Category!=PackageAcceptance' --logger 'trx;LogFileName=ownership-plugin.trx' --results-directory "$v19Results/ownership-plugin"

# 本机 Headless UI：关闭/创建、工具布局、启动及 V18 交互。
dotnet test Host/MyAvaloniaManagement.UiTests -c Release --no-restore -m:1 -warnaserror --filter 'FullyQualifiedName~DocumentWindowV16UiTests|FullyQualifiedName~DockToolWindowCloseUiTests|FullyQualifiedName~ToolCenterUiTests|FullyQualifiedName~AutoHideRestoreVisualTests|FullyQualifiedName~DockLayoutV3UiTests|FullyQualifiedName~HostRestartUiTests|FullyQualifiedName~StartupSplashUiTests|FullyQualifiedName~WorkbenchCommandPresentationUiTests|FullyQualifiedName~CommandPaletteV18UiTests|FullyQualifiedName~CognitiveUxV8UiTests' --logger 'trx;LogFileName=workspace-ui.trx' --results-directory "$v19Results/workspace-ui"
```

按阶段运行对应组，未受影响且已通过的组不为形式重复运行。新增测试放入其他类时同步更新过滤器，并从 TRX 确认新方法确实执行。以上专项不能替代最终完整 verify；真实包验收由 verify 在准备包输入后执行。

Gate 代码或配置未修改时无需重复工具自测；若实际修改其实现/消费关系，追加 `dotnet test tools/MyAvaloniaManagement.Gate.Tests -c Release -m:1`，与正在运行的 Gate 串行执行。

### 9.3 文档与嵌入帮助

```powershell
git diff --check

dotnet test Host/MyAvaloniaManagement.Tests -c Release --no-restore -m:1 -warnaserror --filter 'FullyQualifiedName~HelpContentTests' --logger 'trx;LogFileName=help-content.trx' --results-directory "$v19Results/help-unit"

dotnet test Host/MyAvaloniaManagement.UiTests -c Release --no-restore -m:1 -warnaserror --filter 'FullyQualifiedName~HelpWindowTests' --logger 'trx;LogFileName=help-window.trx' --results-directory "$v19Results/help-ui"
```

不使用 --no-build，确保 Markdown 重新嵌入 Host。若 restore 前提不成立，先完成必要本机依赖准备并记录实际命令。

额外检查本次全部新增/修改 Markdown 的本仓文件链接与标题锚点，验证新方案和验证页作为嵌入资源可读取并能经既有 Markdown renderer 渲染。HelpContentTests/HelpWindowTests 保护现有读取和渲染链，不自动覆盖每个新链接；现有 Gate 只检查三个 README 的文件链接，不能代替显式检查。

## 10. 本机桌面回归边界

以下为实施后受影响行为的针对性本机演练，不属于 Windows CI 或发布 Smoke。Headless 不能证明原生窗口焦点、多屏和桌面实际体验。

| 场景 | 通过标准 |
| --- | --- |
| 单页/浮窗/主窗关闭与取消 | 未保存内容正常询问；取消保留原文档和窗口；重复关闭不重复询问；最后一页关闭无空壳 |
| 保存后仍有修改、恢复及交互失败 | 新修改保留；正常恢复仍需另存；错误提示与实际文件事实一致 |
| 重启前关闭取消或确认 | 取消不退出；通过后沿用原保存/交接；不绕过设置冻结或最终排空 |
| 菜单、快捷键及 Palette | 同一命令作用于正确目标；打开面板后切页不误执行旧项；新建 Intent 与窗口落点保持 |
| 隐藏、自动收起、浮动和最小化工具 | 恢复原实例、正确焦点与位置；取消浮窗关闭后仍可操作 |

V19 实施后的实机回归需单独记录，不能借用当前 Host 人工验收或 V18 自动化结果作为本轮通过证据。项目所有者已[确认当前主程序手工验收通过](../archive/records/host/manual-acceptance-20260921.md)，原 V18 桌面待办已收口；输入法、多屏等矩阵保留作未来改动的回归清单，不预填 V19 结果。

## 11. 完成标准与结果记录

1. C/R/M/Q/T/A 每项均映射到真实测试方法、参数及关键断言，或明确的结构/文档审查。未覆盖项不隐藏在测试总数中。
2. 必需专项发现和执行数量非零、失败为零，新增必需用例无跳过；完整 verify 所有必需阶段通过，包验收输入与实际执行有效。
3. 新旧行为差异仅来自已复现、已说明契约依据的修复；其余取消、通知、身份、目录排序、磁盘提交和资源释放约束保持。
4. SOLID、朴素设计和中文注释审查完成；能列出实际删除的重复规则、状态/入口、无必要依赖或回滚义务。
5. 源码和 Markdown 定稿后取得最终完整 verify；结果保存 HEAD、差异身份、时间、命令、退出码、TRX 数量、Gate run-id 和摘要路径。失败与重跑分别保留。
6. 后续实际开发记录位于 `docs/archive/records/host-v19/development-acceptance.md`，完整证据优先写非嵌入 JSON，文件存在后再链接。当前未创建实施记录，不预填通过数字。
7. 方案、当前契约、内部架构、设计取舍和导航同步；最终 verify 后若改动嵌入 Markdown，重新取得受影响输入的验证。
8. 覆盖率、Windows CI、发布门禁、部署与发布均记录为本轮未执行；开发门禁通过不证明满足发布覆盖率或发布资格。
