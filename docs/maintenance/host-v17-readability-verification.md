# V17：Host 可读性重构专用开发验证

> 用途：验证诊断文件拆分、命令展示文件拆分和服务注册方法提取保持原行为。
> 状态：P0 基线、行为补测及 P1–P3 专项已执行；实际结果见[开发记录](../archive/records/host-v17/development-acceptance.md)，最终完整 verify 以该记录关联 JSON 为准。日期：2026-09-20。
> 范围和设计依据见 [V17 重构计划](../roadmap/host-v17-readability-refactor-plan.md)。本页只定义开发验证，不授予发布资格。

## 1. 执行边界

当前已完成代码实施与阶段验证。以下命令保留为维护入口，实际运行目录、数量及修正过程由开发记录和非嵌入 JSON 保存；示例目录不冒充实际运行路径。

实施时从仓库根目录运行本地测试与既有 `verify`。不使用 AIFLOW、Windows CI、`seal`、发布 Windows Smoke、发布覆盖率或发布重复性门禁，不部署安装目录或发布包。不删除、绕过或放宽已有发布条件。

依据 [Gate 执行图](../../tools/MyAvaloniaManagement.Gate/GateExecutionGraph.cs)，`verify` 包含固定补丁准备、locked restore、Release 零警告构建、SDK/Host Unit/Host Plugin/Host Headless UI/MyPlugTest Unit、契约与已发布 API 比较、MyPlugTest 打包和真实 ZIP 验收；没有 coverage 和 windows-smoke 阶段。运行前提见 [主仓验证](verification.md)。开发打包与本地包验收不表示上传或发布。

## 2. 测试选择与新增原则

1. 将下表逐项映射到既有测试的真实方法、参数组合和断言，再判断缺口；表中类名是已有入口，不代表整行要求已经覆盖。
2. P1/P2 优先复用行为测试，不为搬文件添加文件名或类型所在路径测试。
3. P3 如缺少延迟创建、别名身份或显式传入实例的保护，在现有 `ServiceAndModelTests`、`HostCatalogPluginRegistryTests` 或 `HostLifecycleOwnershipTests` 内补相应行为用例。
4. 新测试先在提取前实现上成立，再验证提取后实现；不复制生产注册代码构造另一份“预期实现”，不锁死私有辅助方法名。
5. 线程与通知测试使用实际 Headless Dispatcher、明确队列推进或可控任务；不用任意 Sleep 猜测时序。
6. 类型可见性、接口/程序集边界由现有架构测试保护；SOLID、方法表达和中文注释由代码审查验证，不用行数阈值替代判断。

## 3. D：诊断拆分矩阵

| 编号 | 必须保持的行为 | 已有测试落点 / 核查要求 |
| --- | --- | --- |
| D01 | Session/Sequence、内存记录与逐条 JSONL 输出一致；关闭后行为和重复释放保持 | `HostDiagnosticsTests` 的会话记录用例；核对是否缺少释放边界断言 |
| D02 | 内存、JSONL、默认镜像不含异常正文、路径或非法结构字段；固定消息和受控阶段/耗时保持 | `HostDiagnosticsTests` 的敏感正文、白名单转换与生命周期失败用例；`WorkbenchCommandDiagnosticTests` |
| D03 | 敏感开关只接受精确启用值，原始异常只走既有独立输出 | `HostDiagnosticsTests` 的显式开关与非精确开关值用例 |
| D04 | 目录不可用时退化为内存诊断并继续接收；保留最近会话数量与顺序 | `HostDiagnosticsTests` 的目录不可用、最近二十次会话用例 |
| D05 | 分类优先级、错误码、启动决策和共享契约异常优先映射不变 | `HostDiagnosticsTests` 的失败策略参数化用例、加载异常映射用例 |
| D06 | 关闭诊断出口等待在途报告，迟到取消异常不再访问已结束会话 | Plugin `HostLifecycleOwnershipTests` 中诊断出口与迟到取消用例 |

D 系列不得把生成时间、随机 SessionId 或真实路径写死成脆弱快照；比较受控字段、相对顺序及脱敏事实。

## 4. C：命令展示拆分矩阵

| 编号 | 必须保持的行为 | 已有测试落点 / 核查要求 |
| --- | --- | --- |
| C01 | 菜单按组与稳定身份排序，分隔符无悬空；Hide/Disable 与当前目标一致 | Unit `WorkbenchCommandProjectionTests` 的共享位置、Hide/Disable 用例 |
| C02 | Host 快捷键优先，冲突插件快捷键全部禁用，菜单命令仍保留 | Unit `WorkbenchCommandProjectionTests` 的快捷键冲突用例 |
| C03 | 同 CommandId 的适配器共享，未知/释放后访问拒绝，缓存只释放一次 | Unit `WorkbenchCommandProjectionTests` 的 CommandStore 用例；UI 的菜单与 Ctrl+S 绑定用例 |
| C04 | Menu/Command 在原 UI 同步范围刷新，Palette 始终排队；后台通知回 UI、合并和重入保持 | UI `WorkbenchCommandPresentationUiTests`、`UiRefreshSchedulerTests` |
| C05 | 异常观察者不阻断后续观察者；释放中已捕获事件快照与迟到通知保持原语义 | UI `WorkbenchCommandPresentationUiTests` 的观察者释放、异常观察者用例；Unit 三种 Projection 重复释放用例 |
| C06 | Owner 撤回/恢复、Palette 去重/排序/搜索、当前 Document 命令路由保持 | Unit `WorkbenchCommandProjectionTests`、`WorkbenchCommandDocumentTargetTests`；UI `WorkbenchCommandPresentationUiTests` |
| C07 | Presentation 仍按原创建/释放顺序拥有 Store、Menu、KeyBinding、Palette，无重复订阅或第二缓存 | 核对原构造函数/Dispose 差异，结合 C03–C06 的真实通知与释放断言；只在行为缺口处补测试 |

既有诊断失败隔离、锁范围与事件快照不可因“去重复”一起改写。保持 Palette 的独立排队政策，不把三个投影测试合并成只检查最终列表的一个用例。

## 5. S：服务组合与所有权矩阵

| 编号 | 必须保持的行为 | 已有测试落点 / 核查要求 |
| --- | --- | --- |
| S01 | 注册描述符不提前创建 Workflow/Workspace/插件对象；原来允许的早期解析仍允许 | Plugin `HostLifecycleOwnershipTests.插件组合间接创建Workflow管理器_回滚使用已登记实例而不创建Workspace`；必要时补注册阶段零创建探针 |
| S02 | ValidateScopes/ValidateOnBuild 下可解析；MainWindowViewModel 瞬态、Workspace 单例保持 | Unit `ServiceAndModelTests.宿主服务可在作用域和构建验证开启时解析`，扩展遗漏的别名身份断言 |
| S03 | 显式传入 Builder/ProviderOwner/ScopeRegistry/ShutdownParticipants 原实例继续被使用；默认路径可用；可选诊断服务回退保持 | 核对现有组合测试，针对缺口在原测试类加入实际解析/释放探针，不只检查辅助方法是否被调用 |
| S04 | 关闭接口和实现共用对象；Session.DockFactory 与 DI Factory 相同；回调只挂接原 Session | Unit `WorkspaceSessionAndDockFactoryTests` 及现有组合夹具；通过引用相同、回调与登记行为验证 |
| S05 | 只登记已创建对象，部分初始化失败按原对象集合回滚；不为清理临时创建 Workspace | Plugin `HostLifecycleOwnershipTests` 的间接创建、部分初始化失败、启动失败与回滚异常用例 |
| S06 | Document Scope 独立、Tool singleton、Host/插件 Provider 隔离与精确工厂保持 | Plugin `DocumentScopeManagerTests`、`PluginContainerIsolationTests`；Unit `HostCatalogPluginRegistryTests` |
| S07 | 正常关闭、取消、在途任务排空、超时保留和异常聚合不变 | Plugin `HostLifecycleOwnershipTests`；Unit `DocumentOperationShutdownTests`、`WorkbenchCommandShutdownGateTests`、`WorkflowActionShutdownGateTests` |
| S08 | 后台组合/UI 工作台解析边界及重启入口保持，不提前触发 Avalonia 控件或命令目录创建 | Unit `StartupCoordinatorTests`、`HostRestartTests`；Plugin `StartupPluginProgressTests`、`HostRestartLifecycleTests`；UI `StartupSplashUiTests`、`HostRestartUiTests` |

注册顺序和描述符方式还需静态逐段核对：以提取前源码为参照，检查每个原注册位置被同一组私有调用替换；不以运行一次启动成功作为全部证明。不要通过在测试中复制一整套注册清单永久绑定无关实现细节。

## 6. 跨阶段检查

| 检查 | 要求 |
| --- | --- |
| 架构/API | `HostApiBoundaryTests`、`PublicApiContractTests`、`PluginHostBoundaryTests` 及完整 verify 的契约/API 比较保持通过 |
| 开发回归 | 最终 verify 执行全部既有测试集合，覆盖 SDK、Host、MyPlugTest 与包验收；专项过滤器不能替代 |
| SOLID | 状态与资源所有者不变，组合根之外无新增 Provider 解析，无多余接口/基类/全局状态 |
| 中文注释 | 诊断收窄、适配器共享、释放顺序、延迟创建和实际实例登记均有设计理由；注释与实现一致 |
| 可读性 | 可从文件名找到主要类型，从注册入口看清步骤，辅助方法表达完整职责；不设武断行数上限 |
| 文档 | 新旧源码引用、导航、相对链接、实际阶段状态与命令一致；历史证据不倒写 |

### 6.1 覆盖率基线文件的路径维护

[coverage-baseline.json](../../Host/MyAvaloniaManagement.Tests/coverage-baseline.json) 原先将 `Business/Presentation/Commands/WorkbenchCommandProjection.cs` 列为关键文件，值为 `90.0`。现已映射到 `WorkbenchPresentationCommandStore.cs`、`WorkbenchMenuProjection.cs`、`WorkbenchKeyBindingProjection.cs`、`WorkbenchCommandPresentation.cs`，四项均保持 `90.0`；其他条目与整体阈值未改动。

检查 JSON 可解析、新路径真实存在、旧逻辑全部有对应目标、无无故删除或降低门槛。纯接口/记录文件是否具有可执行行应按实际代码说明，不为了填表制造不可执行的覆盖率条目。

这是基线清单维护，不是运行覆盖率门禁。当前 `verify` 不采集覆盖率，不能据此声称达到 90%；实际发布覆盖率仍由发布阶段按当次工具和政策验证。本轮不修改 Gate 的覆盖率阶段或加入替代发布流程。

## 7. 本地开发命令

下面命令供实施时执行。按依赖顺序串行运行，`-m:1` 避免关联工程同时改写共享中间目录；每次保留证据使用独立结果目录。

### 7.1 P0 基线与 P4 最终完整验证

```powershell
dotnet run --project tools/MyAvaloniaManagement.Gate -- verify
```

P0 记录原始结果，P4 在最终代码与 Markdown 定稿后运行。基线失败需保留原因和受影响范围；不删除断言、修改 API 基线、放宽阈值或使用旧 DLL 获得绿灯。

下面专项的 `--no-restore` 以前述基线已完成 locked restore 为前提。新增依赖不属于本轮范围；若前提不成立，应先解决基线准备问题。

```powershell
$v17RunId = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$v17Results = "artifacts/host-v17/$v17RunId"
```

### 7.2 P1 诊断专项

```powershell
dotnet test Host/MyAvaloniaManagement.Tests -c Release --no-restore -m:1 -warnaserror --filter 'FullyQualifiedName~HostDiagnosticsTests|FullyQualifiedName~WorkbenchCommandDiagnosticTests' --logger 'trx;LogFileName=p1-unit.trx' --results-directory "$v17Results/p1-unit"

dotnet test Host/MyAvaloniaManagement.PluginTests -c Release --no-restore -m:1 -warnaserror --filter 'FullyQualifiedName~HostLifecycleOwnershipTests&Category!=PackageAcceptance' --logger 'trx;LogFileName=p1-plugin.trx' --results-directory "$v17Results/p1-plugin"
```

### 7.3 P2 展示专项

```powershell
dotnet test Host/MyAvaloniaManagement.Tests -c Release --no-restore -m:1 -warnaserror --filter 'FullyQualifiedName~WorkbenchCommandProjectionTests|FullyQualifiedName~WorkbenchCommandPresentationTests|FullyQualifiedName~WorkbenchCommandDocumentTargetTests|FullyQualifiedName~WorkbenchCommandDiagnosticTests' --logger 'trx;LogFileName=p2-unit.trx' --results-directory "$v17Results/p2-unit"

dotnet test Host/MyAvaloniaManagement.UiTests -c Release --no-restore -m:1 -warnaserror --filter 'FullyQualifiedName~WorkbenchCommandPresentationUiTests|FullyQualifiedName~UiRefreshSchedulerTests' --logger 'trx;LogFileName=p2-ui.trx' --results-directory "$v17Results/p2-ui"
```

### 7.4 P3 组合专项

```powershell
dotnet test Host/MyAvaloniaManagement.Tests -c Release --no-restore -m:1 -warnaserror --filter 'FullyQualifiedName~ServiceAndModelTests|FullyQualifiedName~HostCatalogPluginRegistryTests|FullyQualifiedName~WorkspaceSessionAndDockFactoryTests|FullyQualifiedName~DocumentOperationShutdownTests|FullyQualifiedName~WorkbenchCommandShutdownGateTests|FullyQualifiedName~WorkflowActionShutdownGateTests|FullyQualifiedName~StartupCoordinatorTests|FullyQualifiedName~HostRestartTests|FullyQualifiedName~HostApiBoundaryTests|FullyQualifiedName~PublicApiContractTests|FullyQualifiedName~V17' --logger 'trx;LogFileName=p3-unit.trx' --results-directory "$v17Results/p3-unit"

dotnet test Host/MyAvaloniaManagement.PluginTests -c Release --no-restore -m:1 -warnaserror --filter '(FullyQualifiedName~HostLifecycleOwnershipTests|FullyQualifiedName~DocumentScopeManagerTests|FullyQualifiedName~PluginContainerIsolationTests|FullyQualifiedName~StartupPluginProgressTests|FullyQualifiedName~HostRestartLifecycleTests|FullyQualifiedName~PluginHostBoundaryTests)&Category!=PackageAcceptance' --logger 'trx;LogFileName=p3-plugin.trx' --results-directory "$v17Results/p3-plugin"

dotnet test Host/MyAvaloniaManagement.UiTests -c Release --no-restore -m:1 -warnaserror --filter 'FullyQualifiedName~StartupSplashUiTests|FullyQualifiedName~HostRestartUiTests|FullyQualifiedName~WorkbenchCommandPresentationUiTests' --logger 'trx;LogFileName=p3-ui.trx' --results-directory "$v17Results/p3-ui"
```

若实施时增加了新测试类，先检查测试发现并更新上述过滤器与矩阵，再执行；不能因为过滤器中的旧类通过就推断新类被执行。完整 verify 会执行原有测试项目，仍须从 TRX 单独核对新增测试的实际结果。

常规 Plugin 专项排除 `PackageAcceptance`；完整 verify 负责准备真实包输入并执行包验收。不能把缺包导致的跳过算作通过。

### 7.5 Gate 工具自测的适用条件

本轮计划不改 Gate 实现。若实施确实修改了工具实现、配置消费或测试证据处理，追加以下自测，再执行最终 verify；没有该类修改时无需为形式重复运行。

```powershell
dotnet test tools/MyAvaloniaManagement.Gate.Tests -c Release -m:1 -warnaserror --logger 'trx;LogFileName=gate-tool.trx' --results-directory "$v17Results/gate-tool"
```

Gate 主程序与工具自测不得并行运行。专项已通过后，不在没有新修改、失败或未决问题的情况下反复全量测试；最终完整 verify 必须对应最终输入。

## 8. 通过标准与证据

1. 每条 D/C/S 要求都有实际测试方法/参数组合或明确的结构审查记录；不足之处补齐后才能标记完成。
2. 每次必需测试运行的发现/执行数量非零，失败为零；必需项不得跳过，其他既有跳过说明原因与影响。新增测试须单独核对执行数量。
3. Release 构建与最终 verify 成功，相关代码无新增警告，不能仅根据某条过滤命令的退出码认定全部通过。
4. Headless 验证只证明所测对象、线程和交互链行为；不宣称完成原生窗口、鼠标、焦点或多屏体验验收。本轮无 UI 行为改变，不新增一轮完整桌面验收作为文件整理的前置条件；发现真实交互回归则另行记录并验证。
5. 记录源码 HEAD、工作树差异身份、实际命令、时间、退出码、TRX 发现/通过/失败/跳过数、Gate run-id 与 summary 路径；多次结果分别保存，不拼接成一次完整通过。
6. 本地开发结果与部署、发布分开记录。未执行的覆盖率、Windows CI、发布门禁和安装部署明确为未执行。

已创建[开发记录](../archive/records/host-v17/development-acceptance.md)，记录实际拆分映射、SOLID 取舍、测试和文档审查；关联的非嵌入 `final-development-evidence.json` 保存基线、专项和最终运行事实。最终结果只在运行结束后写入，不预填通过结论。

## 9. 文档检查与收口顺序

1. 核对计划、专用验证、总导航、待办、Host 入口和主仓验证的相对链接。
2. 实施后定向更新内部架构与设计取舍的源码定位，核对拆分源文件在当前文档和配置中的引用；历史文档保留原始事实。
3. 核对命令中的项目路径、真实测试类、过滤条件和结果目录；执行 `git diff --check`。
4. Gate 内置链接检查范围有限，本次新增的计划与专用验证正文须额外检查，不能只依赖三个 README 的门禁检查。
5. 先定稿嵌入帮助的 Markdown，再执行最终完整 verify，最后写非嵌入 JSON。最终 verify 后若再次修改 Markdown 或源码，应重新取得对应输入的验证。

## 10. 实际测试映射

下表列出关键方法；各专项仍执行第 7 节完整类过滤器及所有参数组合。结构比对使用 Roslyn 语法标记，忽略注释与格式，不替代运行期行为测试。

| 编号 | 实际测试方法或结构证据 | 执行组 |
| --- | --- | --- |
| D01 | `HostDiagnosticsTests.V17会话序号与文件顺序一致且释放后拒绝新增记录`、`会话记录写入内存和可逐条解析的JsonLines日志` | P0 补测、P1 Unit |
| D02 | `HostDiagnosticsTests.默认诊断的内存JsonLines和镜像均不包含异常敏感正文`、`白名单转换丢弃路径和非法结构字段并保留受控阶段耗时`；`WorkbenchCommandDiagnosticTests` 全类 | P1 Unit |
| D03 | `HostDiagnosticsTests.显式敏感开关只把异常原文写入临时输出`、`非精确开关值不会开启敏感输出` | P1 Unit |
| D04 | `HostDiagnosticsTests.启动时只保留包含当前文件在内的最近二十次会话`、`日志目录不可用时退化为内存诊断且继续接收记录` | P1 Unit |
| D05 | `HostDiagnosticsTests.失败策略按阶段和错误码给出稳定启动决策`、`加载异常映射优先识别共享契约版本冲突` | P1 Unit |
| D06 | `HostLifecycleOwnershipTests.关闭诊断出口等待正在报告的调用_关闭后不访问已结束会话`、`迟到取消异常在出口关闭后只被观察_不再写入日志` | P1 Plugin |
| C01 | `WorkbenchCommandProjectionTests.四个共享位置按组顺序稳定投影且分隔符没有悬空`、`Hide和Disable随当前Target与CanExecute使用同一状态事实` | P2 Unit |
| C02 | `WorkbenchCommandProjectionTests.Host快捷键优先且跨插件冲突双禁用但菜单命令保留` | P2 Unit |
| C03 | `WorkbenchCommandProjectionTests.CommandStore拒绝未知和释放后访问且唯一缓存只释放一次`；`WorkbenchCommandPresentationUiTests.文件菜单和CtrlS绑定同一稳定保存命令且设计数据保持纯内存` | P2 Unit/UI |
| C04 | `WorkbenchCommandPresentationUiTests.菜单与命令同步刷新而面板始终排队并合并`；`UiRefreshSchedulerTests.后台连续请求合并且回调在UI线程并在锁外执行`、`发布期间的新失效保留各自重入顺序` | P2 UI |
| C05 | `WorkbenchCommandPresentationUiTests.观察者中释放仍完成本轮事件快照但后续失效不再发布`、`定向非相关全量通知正确且异常观察者不阻断后续刷新`；`WorkbenchCommandProjectionTests.三种Projection重复释放后拒绝读取且迟到可用性通知安全` | P2 Unit/UI |
| C06 | `WorkbenchCommandProjectionTests.Owner不可用时菜单和快捷键同步移除并在恢复后重建` 及 Palette 方法；`WorkbenchCommandDocumentTargetTests.同类型多个Document只执行当前活动实例` | P2 Unit/UI |
| C07 | 10 个类型语法标记一致，包含组合对象构造与 Dispose；C03–C06 的行为回归 | P2 结构、Unit/UI |
| S01 | `ServiceAndModelTests.V17注册和容器验证保持共享输入身份且不提前创建关闭参与者`；`HostLifecycleOwnershipTests.插件组合间接创建Workflow管理器_回滚使用已登记实例而不创建Workspace` | P0 补测、P3 Unit/Plugin |
| S02 | `ServiceAndModelTests.宿主服务可在作用域和构建验证开启时解析`、`V17关闭端口和工作区Factory复用实际登记实例` | P3 Unit |
| S03 | `ServiceAndModelTests.V17注册和容器验证保持共享输入身份且不提前创建关闭参与者` 的两组参数；`HostDiagnosticsTests.V17看板诊断解析保持具体会话优先和接口回退` 的四组参数 | P0 补测、P3 Unit |
| S04 | `ServiceAndModelTests.V17关闭端口和工作区Factory复用实际登记实例`；`WorkspaceSessionAndDockFactoryTests.DockFactory未绑定和重复绑定均快速失败`、`DockFactory仅转发框架协议并建立规范Locator`；原挂接语句顺序比对 | P3 Unit/结构 |
| S05 | `HostLifecycleOwnershipTests.初始化部分成功后解析下一项失败_回滚只关闭已成功项`、`启动后半程失败_真实Runtime回滚调用已启动项并保留原始异常`、`回滚Shutdown及诊断再次失败_不得覆盖启动异常或释放容器` | P3 Plugin |
| S06 | `DocumentScopeManagerTests.每个Document拥有独立Scope且Lease释放幂等`；`PluginContainerIsolationTests.每插件DocumentScope使用本插件服务并可独立关闭`；`WorkspaceSessionAndDockFactoryTests.多个主窗口ViewModel共享唯一Session布局且不重复创建Tool`；`HostCatalogPluginRegistryTests` 全类 | P3 Unit/Plugin |
| S07 | `HostLifecycleOwnershipTests.正常关闭先停生命周期再释放两个真实容器_并发请求共用结果`、`业务调用未排空时不关闭生命周期也不释放Provider`、`Shutdown持续挂起_宽限后保留且迟到成功不自动释放`；三类关闭 Gate 单测 | P3 Unit/Plugin |
| S08 | `StartupCoordinatorTests.工作线程隔离同步阻塞并在Windows保留STA入口`、`首帧之前取消不执行任何插件工厂`；`StartupPluginProgressTests`、`HostRestartTests`、`HostRestartLifecycleTests`、`StartupSplashUiTests`、`HostRestartUiTests` 全类；工厂未提前调用的结构比对 | P3 Unit/Plugin/UI |

本页提供方法定位；通过数量、失败修正和最终输入身份统一查阅开发记录及实际 JSON/TRX。
