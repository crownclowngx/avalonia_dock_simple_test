# V12 Host 内部重构开发验证

> 当前状态（2026-09-20）：Host 整体人工验收由项目所有者手工使用后确认通过，见[人工验收确认](../archive/records/host/manual-acceptance-20260920.md)。本文保留专项命令和后续回归矩阵；原阶段自动化结果以关联记录/JSON 为准，当前交付见[本机部署](local-deployment.md)。

> 用途：为 [V12 重构方案](../archive/plans/host-v12-internal-refactor-plan.md)提供专项测试矩阵、开发门禁与证据规范。
> 状态：P0–P3 专项验证已执行；结果见[开发记录](../archive/records/host-v12/development-acceptance.md)，完整 verify 见[最终开发证据](../archive/records/host-v12/final-development-evidence.json)。核对日期：2026-09-20。
> 事实源：现有 Host 测试、[Gate 执行图](../../tools/MyAvaloniaManagement.Gate/GateExecutionGraph.cs)、[Gate 配置](../../tools/MyAvaloniaManagement.Gate/gate.config.json)与[主仓验证](verification.md)。

## 1. 验证边界

SOLID、朴素设计和详细中文注释与行为正确性一起验收。自动化负责可执行行为，代码审查负责职责、依赖方向及注释是否说明了真实约束。

本轮只运行本地开发检查。不使用 AIFLOW、Windows CI、seal、发布 Windows Smoke、发布覆盖率或发布重复性门禁；不调整发布配置、阈值或 API 基线以获取通过结果。

本地 verify 包含 MyPlugTest 打包与真实 ZIP 加载验收，这是开发兼容测试，不上传、不发布。缺插件目录、零测试、跳过或缺报告不能用来证明通过。

## 2. P1：Registry 校验与提交

| 编号 | 必须覆盖的行为 | 现有测试基础／补充方向 |
| --- | --- | --- |
| R01 | 不同声明顺序合法；未登记类型不被发现；读取和校验不创建模型、View 或 Scope | ExplicitContributionAndPluginRegistryTests、WorkbenchCommandRegistrationTests |
| R02 | Document/Tool/Workflow/Command/Placement/Icon 归属；精确命名空间；多 Owner 和多 Lifecycle 的既有失败 | PluginRegistrationOwnershipTests、IconRegistrationTests、WorkflowActionKernelTests；补齐新校验器数据用例 |
| R03 | 插件内 ID、模型、View 映射、Handler、Placement 和 Gesture 重复按原规则拒绝；角色冲突不漏报 | ExplicitContributionAndPluginRegistryTests、WorkbenchCommandRegistrationTests |
| R04 | 全局冲突排除全部相关 Owner 及所有贡献，保留无冲突候选；同 Owner 与跨 Owner 判定不混淆 | IdentityAndRegistryTests、WorkbenchCommandRegistrationTests、IconRegistrationTests |
| R05 | Add／Seal／Import／Build 各自验证时机、封闭后写入、重复 Build 和防御性复制保持 | WorkbenchCommandRegistrationTests、ExplicitContributionAndPluginRegistryTests |
| R06 | 诊断码、阶段、Owner、StableId、贡献程序集及顺序保持；诊断端口抛错不被新层吞掉 | 补受控诊断 Sink，比较有序诊断与失败后的可见状态 |
| R07 | Registry 构造或诊断失败不提前提交；冲突 Provider 释放，成功 Scope 所有权只在提交后可用 | PluginContainerIsolationTests、DocumentScopeManagerTests、HostLifecycleOwnershipTests；补故障注入 |
| R08 | Host Catalog 与插件 Registry 仍分离；跨 Runtime 不共享声明和 Provider | HostCatalogPluginRegistryTests、ExplicitContributionAndPluginRegistryTests |

新增 PluginContributionValidatorTests、PluginConflictAnalyzerTests 和 PluginRegistryRefactorContractTests 均位于既有 Host Unit 工程。局部规则、全局事实与原入口失败边界分层验证，不新增测试框架。

## 3. P2：UI 刷新调度

| 编号 | 必须覆盖的行为 | 断言重点 |
| --- | --- | --- |
| U01 | Menu／KeyBinding／Command 在 UI 线程收到失效立即发布，后台失效切回 UI | 回调线程与调用先后；不能只断言最终数量 |
| U02 | Palette 在 UI 线程也排队；同一事务多次失效只触发一次刷新 | 入队前没有通知，推进队列后一次通知 |
| U03 | 后台连续通知合并；刷新后新通知可再次发布 | 不丢后续刷新，不新增延时节流 |
| U04 | 排队后 Dispose、重复 Dispose、释放后请求和读取保持既有语义 | 迟到回调失效、订阅解除、原异常／返回行为 |
| U05 | 通知中再次失效、通知中释放、多个观察者及异常观察者 | 重入和事件快照语义保持，后续观察者不被意外阻断 |
| U06 | 各消费者原诊断行为保持 | Menu／Palette、KeyBinding、Command 分别断言，不用统一吞异常掩盖差异 |
| U07 | Command 定向／全量失效，执行前目标变化与 CanExecute 变化 | 过滤逻辑不进入调度器；执行仍重新捕获当前目标 |
| U08 | 组合对象释放顺序、多个窗口／Runtime 隔离 | 一个调度器不串扰其他消费者，无新增全局刷新状态 |

现有基础为 WorkbenchCommandPresentationTests、WorkbenchCommandProjectionTests、WorkbenchCommandPresentationUiTests 和 CognitiveUxV8UiTests。新增 UiRefreshSchedulerTests 位于 Headless 工程，直接验证实际 Dispatcher；不为测试新增生产调度接口。WorkbenchCommandPresentationUiTests 补齐四种真实消费者在通知中释放的事件快照语义。

竞态用独立工作线程、受控任务与队列推进复现，不用固定等待时间。需要在 UI 队列推进前完成后台投递时，使用独立 Thread 并断言 CheckAccess 为 false；不把可能被同步等待内联的 Task.Run 当作后台线程证据。测试遵循现有 Avalonia 测试串行配置，不让并发测试污染同一 UI Dispatcher。

## 4. P3：工作区查询快照

| 编号 | 必须覆盖的行为 | 断言重点 |
| --- | --- | --- |
| Q01 | 主树、多层 DocumentDock、多个浮窗及分组 | 页面成员与浮窗归属等于原遍历语义 |
| Q02 | 同标题页面、相同 Dock Id 的不同窗口、重复节点引用 | 引用身份不被字符串合并；首个匹配及去重顺序不变 |
| Q03 | Hidden、四向 Pinned、Docked、Floating、Active 组合 | 工具状态判定优先级与现有说明保持 |
| Q04 | 未创建／激活失败／不可用工具，关闭中页面，退出及空布局 | 完整目录仍可解释；CanOpen／CanActivate／错误说明保持 |
| Q05 | 页面标题和 Dirty 变化、发布序号、关闭后同名新页面 | 稳定身份与排序保持，不按名称切换到其他实例 |
| Q06 | 连续查询间移动、隐藏、恢复、关闭、重置布局 | 新查询看到新关系；旧快照不被复用 |
| Q07 | 查询无副作用 | 模型／View／Scope 创建释放计数不变；无磁盘写入、布局改变或业务通知 |
| Q08 | 查询结果生成后目标被关闭、插件撤回或 Host 退出 | 执行入口重新检查，不信任旧查询结果 |
| Q09 | 主树查询不会命中浮窗同名骨架；布局恢复后再次查询 | DockTreeNavigator 原单窗口入口不改含义 |
| Q10 | 有限规模的大布局对照 | 与独立简单遍历参照结果一致；性能采样单独记录，不使用机器毫秒硬阈值 |

现有基础为 DockWorkspaceNavigationTests、CognitiveUxV8Tests、ToolCenterTests、WorkspaceSessionAndDockFactoryTests、DockLayoutWorkspaceStateTests，以及 ToolCenterUiTests、CognitiveUxV8UiTests、DockLayoutV3UiTests。

新增 WorkspaceLayoutQuerySnapshotTests 放入 Host Unit 工程。参照算法只存在于测试，夹具表达预期结构；不复制一套长期运行的生产查询实现。

## 5. 跨阶段回归与结构审查

| 检查 | 必须保持的事实 |
| --- | --- |
| HostApiBoundaryTests、PluginHostBoundaryTests、SDK/API 比较 | 无新增 Host 导出类型，插件仍只依赖 SDK，已发布签名不变 |
| PluginRegistrationOwnershipTests、PluginContainerIsolationTests | 插件私有 Provider 和保留端口边界不变 |
| DocumentCloseTests、DocumentOperationShutdownTests、HostLifecycleOwnershipTests | 关闭取消、在途排空、失败回滚和保留政策不变 |
| HostDockAdapterTests、DocumentControlRecyclingTests、跨窗布局回归 | 查询与刷新不重建业务模型或 View，不改变 Scope 寿命 |
| HostDiagnosticsTests、WorkbenchCommandDiagnosticTests | 固定诊断与脱敏规则不变 |
| SOLID 审查 | 协作者职责单一，依赖方向清晰，没有第二状态源、任意 Provider 解析或单实现接口堆积 |
| 中文注释审查 | P1 的提交顺序、P2 的时机与竞态、P3 的快照寿命均有设计理由，注释与测试一致 |

完整 verify 继续运行原测试套件，阶段过滤器不能替代这些跨阶段回归。

## 6. 本地执行命令

以下命令为本轮已使用的开发入口。全部从仓库根目录运行，按阶段串行执行；`-m:1` 避免相关项目并发写入相同中间目录。

### 6.1 基线与最终完整开发门禁

```powershell
dotnet run --project tools/MyAvaloniaManagement.Gate -- verify
```

该入口准备固定 Avalonia／Dock 补丁，执行 locked restore、Release 零警告构建、SDK／Host Unit／Plugin／Headless UI／MyPlugTest Unit、契约及已发布 API 比较、MyPlugTest 打包和真实 ZIP 验收。不启动 Windows Smoke，不授予发布资格。

新增测试放入已有工程后由完整 verify 自动发现，不新增 scope 或跳过开关。首次补丁与公共 API 基线准备的工具和下载条件遵循 [主仓验证](verification.md)。记录失败原因，不改用旧 DLL 绕过。

### 6.2 P1 专项

```powershell
dotnet test Host/MyAvaloniaManagement.Tests -c Release --no-restore -m:1 -warnaserror --filter 'FullyQualifiedName~PluginContributionValidatorTests|FullyQualifiedName~PluginConflictAnalyzerTests|FullyQualifiedName~PluginRegistryRefactorContractTests|FullyQualifiedName~ExplicitContributionAndPluginRegistryTests|FullyQualifiedName~PluginRegistrationOwnershipTests|FullyQualifiedName~WorkbenchCommandRegistrationTests|FullyQualifiedName~IdentityAndRegistryTests|FullyQualifiedName~IconRegistrationTests|FullyQualifiedName~HostCatalogPluginRegistryTests|FullyQualifiedName~WorkflowActionKernelTests' --logger 'trx;LogFileName=p1-unit.trx' --results-directory artifacts/host-v12/p1-unit
dotnet test Host/MyAvaloniaManagement.PluginTests -c Release --no-restore -m:1 -warnaserror --filter '(FullyQualifiedName~PluginContainerIsolationTests|FullyQualifiedName~DocumentScopeManagerTests|FullyQualifiedName~HostLifecycleOwnershipTests)&Category!=PackageAcceptance' --logger 'trx;LogFileName=p1-plugin.trx' --results-directory artifacts/host-v12/p1-plugin
```

### 6.3 P2 专项

```powershell
dotnet test Host/MyAvaloniaManagement.Tests -c Release --no-restore -m:1 -warnaserror --filter 'FullyQualifiedName~WorkbenchCommandPresentationTests|FullyQualifiedName~WorkbenchCommandProjectionTests|FullyQualifiedName~WorkbenchCommandContextStateTests|FullyQualifiedName~WorkbenchCommandDiagnosticTests|FullyQualifiedName~WorkbenchCommandDocumentTargetTests' --logger 'trx;LogFileName=p2-unit.trx' --results-directory artifacts/host-v12/p2-unit
dotnet test Host/MyAvaloniaManagement.UiTests -c Release --no-restore -m:1 -warnaserror --filter 'FullyQualifiedName~UiRefreshSchedulerTests|FullyQualifiedName~WorkbenchCommandPresentationUiTests|FullyQualifiedName~CognitiveUxV8UiTests' --logger 'trx;LogFileName=p2-ui.trx' --results-directory artifacts/host-v12/p2-ui
```

### 6.4 P3 专项

```powershell
dotnet test Host/MyAvaloniaManagement.Tests -c Release --no-restore -m:1 -warnaserror --filter 'FullyQualifiedName~WorkspaceLayoutQuerySnapshotTests|FullyQualifiedName~DockWorkspaceNavigationTests|FullyQualifiedName~CognitiveUxV8Tests|FullyQualifiedName~ToolCenterTests|FullyQualifiedName~WorkspaceSessionAndDockFactoryTests|FullyQualifiedName~DockLayoutWorkspaceStateTests' --logger 'trx;LogFileName=p3-unit.trx' --results-directory artifacts/host-v12/p3-unit
dotnet test Host/MyAvaloniaManagement.UiTests -c Release --no-restore -m:1 -warnaserror --filter 'FullyQualifiedName~ToolCenterUiTests|FullyQualifiedName~CognitiveUxV8UiTests|FullyQualifiedName~DockLayoutV3UiTests|FullyQualifiedName~DockCrossWindowLayoutUiTests|FullyQualifiedName~DockToolWindowCloseUiTests|FullyQualifiedName~DockToolSplitUiTests|FullyQualifiedName~DocumentControlRecyclingTests' --logger 'trx;LogFileName=p3-ui.trx' --results-directory artifacts/host-v12/p3-ui
```

示例输出目录用于单次排错；正式保留多次运行证据时，为每次使用独立子目录，避免覆盖旧 TRX。新增测试类现已接入；组合过滤器仍可能因已有测试通过而返回成功，因此最终 JSON 额外记录新增类实际执行数量与 R/U/Q 对应测试。不能仅凭退出码。专项命令的 --no-restore 前提是本轮基线 verify 已完成 locked restore。

常规 Plugin 测试排除 PackageAcceptance；真实包验收交给完整 verify 准备输入并要求恰好通过一项。不要在未提供包输入时单独运行该类并把跳过记为通过。

### 6.5 Gate 工具自测

本计划不要求修改 Gate 生产代码。若实施发现确需调整测试发现或证据处理，应先明确差异范围，保留现有发布政策，并串行运行：

```powershell
dotnet test tools/MyAvaloniaManagement.Gate.Tests -c Release -m:1 -warnaserror
```

未修改工具时不为形式重复运行工具自测；最终完整 verify 仍是必需项。

## 7. 通过标准与证据

1. 所有新增必需用例被发现并实际执行；阶段用例无失败、无未解释跳过、无零测试。完整 verify 成功，构建零警告。
2. 不删除旧有效断言，不降低阈值，不通过清空 public API 基线、修改 Category 或屏蔽测试取得绿灯。
3. 记录日期、源码 revision、未提交差异身份、命令、实际测试类及通过／失败／跳过数、TRX 和 Gate run-id；失败也保留证据。
4. 用 R01–R08、U01–U08、Q01–Q10 对照实际测试名称与 TRX，不把本矩阵的文字当成已覆盖证明。
5. 实施后创建 V12 专用开发记录；在 Markdown 定稿后运行最终完整 verify，将最终源码和 DLL 身份写入非嵌入 JSON。存在未提交文件时只记录 HEAD 不足以唯一标识验证源码。
6. 若最终测试后修改了代码或嵌入 Markdown，按影响重新执行验证；旧证据继续保留为历史，不能改写为新产物通过。
7. 性能记录附布局规模、采样方法和前后结果；未测量明确写“未测”，不把复杂度推断写成实测百分比。

V12 的“开发完成”与“发布资格”分开。本轮不收集或判定发布覆盖率，不调用任何发布门禁。

## 8. 文档与实际交互检查

本轮已同步新文档、总导航、待办、归档、Host 文档入口、主仓验证入口、内部架构与设计取舍。最终定稿检查链接、状态及真实协作者调用关系。

Gate 当前只检查三个 README 的本仓文件链接。V12 专项文档和开发记录需要额外核对相对链接、锚点、源码路径、测试类与命令；执行 `git diff --check` 检查空白问题。新增文件也必须纳入链接检查，不能只读取已跟踪 diff。

实施后的本地交互建议覆盖：打开菜单与命令面板、切换同名页面、浮窗最小化后定位、隐藏／显示工具、关闭取消和退出。Headless 可以验证窗口协议，但不证明原生鼠标、焦点、多屏 DPI 或外部业务。

真实桌面项目逐项记录已观察／失败／未执行及原因；缺环境时保留待验收，不启动发布 Smoke 替代。当前 Host 人工验收结论见页首确认，历史 V11 待办已统一收口；外部插件业务验证仍不作为本仓开发构建的隐含前提。

## 9. 本轮矩阵与实际测试对应

以下列出每项的具体断言入口；完整相关测试类仍按第 6 节执行，表内方法不代替全套回归。所有条目已在阶段 TRX 中核对存在且通过。完整 verify 会再次核对最终源码中的这些方法；最终 JSON 保存匹配到的实际案例（包括 Theory 参数）、来源 TRX 和报告哈希。

| 编号 | 已执行的具体用例 | 阶段 TRX |
| --- | --- | --- |
| R01 | `PluginContributionValidatorTests.Command和Placement可先于Document声明且校验没有激活副作用`<br>`ExplicitContributionAndPluginRegistryTests.未登记类型不会因存在于程序集而进入Registry且构建不激活模型` | p1-unit |
| R02 | `PluginRegistrationOwnershipTests.Document和Tool必须属于精确PluginId及正确贡献种类命名空间`<br>`WorkbenchCommandRegistrationTests.Seal区分CommandTarget和Placement的所有权越界`<br>`PluginRegistryRefactorContractTests.多所有者且多生命周期保留原失败边界` | p0-unit、p1-unit |
| R03 | `ExplicitContributionAndPluginRegistryTests.Tool模型角色View映射和多生命周期冲突在插件内一次汇总`<br>`WorkbenchCommandRegistrationTests.Seal拒绝重复Command跨类型Placement和本插件Gesture`<br>`PluginContributionValidatorTests.声明关系错误与重复快捷键分别报告且保留规则顺序` | p1-unit |
| R04 | `WorkbenchCommandRegistrationTests.全局CommandId冲突整体排除两个Owner及其全部贡献`<br>`PluginConflictAnalyzerTests.图标同Owner重复仍拒绝而普通全局身份只比较跨Owner`<br>`PluginConflictAnalyzerTests.跨DocumentTool精确模型冲突返回两个Owner并保留View来源` | p1-unit |
| R05 | `PluginContributionValidatorTests.集合快照不随Builder追加改变且不能通过强转改写`<br>`WorkbenchCommandRegistrationTests.Seal和Build后均不能继续写入`<br>`PluginRegistryRefactorContractTests.局部组合异常保持排序并且失败导入不留下部分贡献` | p0-unit、p1-unit |
| R06 | `PluginRegistryRefactorContractTests.全局诊断保持贡献种类和所有者发现顺序且不激活View`<br>`PluginRegistryRefactorContractTests.诊断抛错立即停止且尚未提交Provider所有权` | p0-unit、p1-unit |
| R07 | `PluginRegistryRefactorContractTests.Registry索引构造失败仍未提交Provider且Builder保持封闭`<br>`PluginContainerIsolationTests.每插件DocumentScope使用本插件服务并可独立关闭`<br>`PluginContainerIsolationTests.插件Provider按规范PluginId建立并在宿主之前逆序释放` | p0-unit、p1-plugin、p1-unit |
| R08 | `ExplicitContributionAndPluginRegistryTests.两个组合根拥有独立且发布后不可写的Registry`<br>`HostCatalogPluginRegistryTests.WorkspaceCatalog合并Host与插件View但不合并Provider所有权` | p1-unit |
| U01 | `WorkbenchCommandPresentationUiTests.菜单与命令同步刷新而面板始终排队并合并`<br>`WorkbenchCommandPresentationUiTests.Owner不可用时View移除生成对象且恢复后不产生重复项`<br>`UiRefreshSchedulerTests.后台连续请求合并且回调在UI线程并在锁外执行` | p0-ui、p2-ui |
| U02 | `UiRefreshSchedulerTests.UI线程同步模式与排队模式保持不同的合并行为`<br>`WorkbenchCommandPresentationUiTests.菜单与命令同步刷新而面板始终排队并合并` | p0-ui、p2-ui |
| U03 | `UiRefreshSchedulerTests.后台连续请求合并且回调在UI线程并在锁外执行`<br>`UiRefreshSchedulerTests.UI线程同步模式与排队模式保持不同的合并行为` | p2-ui |
| U04 | `UiRefreshSchedulerTests.排队后释放和释放后的请求均不再通知且重复释放安全`<br>`WorkbenchCommandProjectionTests.三种Projection重复释放后拒绝读取且迟到可用性通知安全` | p2-ui、p2-unit |
| U05 | `UiRefreshSchedulerTests.发布期间的新失效保留各自重入顺序`<br>`WorkbenchCommandPresentationUiTests.观察者中释放仍完成本轮事件快照但后续失效不再发布`<br>`WorkbenchCommandPresentationUiTests.定向非相关全量通知正确且异常观察者不阻断后续刷新` | p2-ui |
| U06 | `WorkbenchCommandPresentationUiTests.Owner不可用时View移除生成对象且恢复后不产生重复项`<br>`WorkbenchCommandPresentationUiTests.定向非相关全量通知正确且异常观察者不阻断后续刷新`<br>`WorkbenchCommandPresentationUiTests.Palette实时隐藏目标并保留Disabled且异常执行只产生脱敏诊断` | p2-ui |
| U07 | `WorkbenchCommandPresentationUiTests.定向非相关全量通知正确且异常观察者不阻断后续刷新`<br>`WorkbenchCommandDocumentTargetTests.同类型多个Document只执行当前活动实例` | p2-ui、p2-unit |
| U08 | `UiRefreshSchedulerTests.回调中释放抑制后续请求并且其他消费者不受影响`<br>`WorkspaceSessionAndDockFactoryTests.MainWindow订阅可独立释放而不影响其他窗口投影` | p2-ui、p3-unit |
| Q01 | `WorkspaceLayoutQuerySnapshotTests.浮窗嵌套与窗口回边仍按原窗口顺序寻找承载者`<br>`WorkspaceLayoutQuerySnapshotTests.相同标题和Id不合并实例且重复成员保留首个分组` | p3-unit |
| Q02 | `WorkspaceLayoutQuerySnapshotTests.相同标题和Id不合并实例且重复成员保留首个分组`<br>`WorkspaceLayoutQuerySnapshotTests.窗口声明顺序优先于工作区DFS首次到达的窗口` | p3-unit |
| Q03 | `WorkspaceLayoutQuerySnapshotTests.隐藏四向固定可见和活动关系分别捕获且查询不修复重叠`<br>`WorkspaceLayoutQuerySnapshotTests.工具读取保留隐藏优先级并在下一次查询看到恢复与固定` | p3-unit |
| Q04 | `WorkspaceLayoutQuerySnapshotTests.空布局没有文档窗口或工具关系`<br>`CognitiveUxV8Tests.关闭等待和工作区退出均阻止旧搜索项定位或创建`<br>`ToolCenterUiTests.可用性撤回保留解释禁用打开并且关闭会话不再订阅`<br>`DockLayoutV3UiTests.全隐藏或插件缺失的浮窗不创建空窗口但保留恢复记录` | p0-unit、p3-ui、p3-unit |
| Q05 | `CognitiveUxV8Tests.页面标题快照刷新但身份序号不变且观察者异常不影响发布释放`<br>`CognitiveUxV8Tests.同名页面按运行时身份切换且关闭旧项绝不替换成同名新页面` | p0-unit、p3-unit |
| Q06 | `WorkspaceLayoutQuerySnapshotTests.下一次捕获看到移动隐藏和移除而旧关系保持不变`<br>`DockLayoutV3UiTests.内容全屏阻止迁移且重置保留文档实例并关闭浮窗` | p3-ui、p3-unit |
| Q07 | `CognitiveUxV8Tests.页面与工具重复查询不发布通知或改变实例且退出后重新检查`<br>`ToolCenterUiTests.一百个工具虚拟化搜索不重复创建视图且图标保持Owner` | p0-unit、p3-ui、p3-unit |
| Q08 | `CognitiveUxV8Tests.同名页面按运行时身份切换且关闭旧项绝不替换成同名新页面`<br>`CognitiveUxV8Tests.页面与工具重复查询不发布通知或改变实例且退出后重新检查`<br>`ToolCenterUiTests.可用性撤回保留解释禁用打开并且关闭会话不再订阅` | p0-unit、p3-ui、p3-unit |
| Q09 | `DockWorkspaceNavigationTests.浮窗与嵌套浮窗可定位且窗口回边不会无限遍历`<br>`DockLayoutV3UiTests.工具浮窗恢复复用原模型和View且关闭后按原组只显示目标工具` | p3-ui、p3-unit |
| Q10 | `WorkspaceLayoutQuerySnapshotTests.有限大布局与原查询一致并记录关系查询采样` | p3-unit |
