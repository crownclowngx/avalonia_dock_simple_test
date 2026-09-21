# V16 Document 浮窗关闭与新建位置：专用开发验证

> 当前状态（2026-09-21）：Host 整体人工验收由项目所有者手工使用后确认通过，见[人工验收确认](../archive/records/host/manual-acceptance-20260921.md)。本文保留专项命令和后续回归矩阵；原阶段自动化结果以关联记录/JSON 为准，当前交付见[本机部署](local-deployment.md)。

> 用途：定义 V16 的本地自动化矩阵、原生桌面验收、门禁和证据要求。日期：2026-09-20。
> 状态：实现与专项已落地；实际结果见[开发记录](../archive/records/host-v16/development-acceptance.md)，最终完整门禁见同目录非嵌入 JSON。原阶段 M01–M08 未执行；当前 Host 人工验收见页首确认。目标规则见 [V16 实施方案](../archive/plans/host-v16-document-floating-close-and-creation-target-plan.md)。
> 不使用 AIFLOW、Windows CI、`seal`、发布 Smoke、发布覆盖率或重复性门禁；不部署安装目录、不发布包。下文命令可复现本地验证，不授予发布资格。

## 1. 验证层次与通过规则

| 层次 | 范围 | 可以证明什么 |
| --- | --- | --- |
| U：单元与协议 | 目标捕获/重验、范围关闭、许可、失败回滚、候选释放 | 可控时序下的选择、关闭与所有权不变量 |
| P：插件集成 | 真实注册/工厂/Scope，异步初始化，创建失败，原有插件生命周期 | 目标策略没有绕开真实创建流程，没有多建或误释放业务实例 |
| UI：Headless | 实际 XAML 标签关闭按钮、快捷键、搜索执行、窗口模型及视觉树 | 输入经过真实宿主入口后，窗口登记、布局、面板及页面归属正确 |
| R：关联回归 | Tool P2、分割/回停、布局重置/恢复、保存取消、应用退出/重启 | 本轮修复保持既有约束，不引入相关路径回归 |
| G：本地门禁 | Gate 自测及完整 `verify` | 本仓构建、测试、契约、插件包验收闭环，不授予发布资格 |
| M：原生桌面 | 原生窗口存活、黑框、输入焦点、保存对话框、多窗口交互 | 实际桌面体验；不能被 Headless、源码审查或截图中“暂时看不见”替代 |

所有必需专项必须发现并执行预期用例。零匹配、未说明的跳过、缺失 TRX 或仅构建成功均不能算通过。缺陷修复前先记录正式失败测试，再记录对应通过结果；不为文案、类名或代码结构镜像编写无行为价值的测试。

异步用例使用可控制的调度队列、TaskCompletionSource 或现有测试夹具，显式控制初始化、保存、命令排空及关闭重试；不用固定 sleep 作为成功依据。测试拥有窗口和临时数据目录，结束时清理自身资源，不操作用户安装目录或真实业务文件。

## 2. 必需自动化矩阵

### 2.1 关闭与窗口回收

| 编号 | 场景 | 必须断言 | 层次 |
| --- | --- | --- | --- |
| V16-C01 | 通过真实标签 × 关闭唯一干净 Document | 原生窗口对象收到一次 Closed；窗口不可见；Root.Windows、Factory.HostWindows 无残留；窗口模型引用解除；Document/Scope 仅释放一次 | UI、P |
| V16-C02 | 通过实际关闭命令及标签菜单关闭最后 Document | 与 C01 同一结果；不能只用 Window.Close 代替入口 | UI、U |
| V16-C03 | 最后文档有未保存修改，分别保存成功、放弃、取消 | 前两者关闭；取消保留原 Document/View/Scope、活动项及窗口；确认不重复 | U、UI、P |
| V16-C04 | 保存失败、保存对话框取消、保存期间再次修改 | 原窗口及文档仍可用，不清空内容、不报告关闭成功、不永久冻结命令 | U、UI |
| V16-C05 | CanClose、CanCloseLastDockable 或 DockableClosing 拒绝 | 在拆除前拒绝；解除限制后可再关闭；不因整窗接管绕过能力或重复触发同轮事件 | U、UI |
| V16-C06 | 原生 Closing 外部监听者取消或最终重试被拒绝 | 原对象图完整，窗口仍登记；撤销许可后可以重新操作 | UI |
| V16-C07 | 单组多文档、多组最后一页、混合 Tool/Document | 仅整个浮窗已无业务内容时回收；仍有内容的窗口保留；空组与分隔条清理正确 | UI |
| V16-C08 | 主窗口最后文档关闭 | 主窗口和主文档骨架保留，后续新建成功 | UI、U |
| V16-C09 | 正在执行文档命令、重复关闭、单页关闭转整窗、原生最终清理重入 | 先排空后释放；无互相等待、递归请求、重复确认/释放；迟到回调不触及已销毁对象 | U、P、UI |
| V16-C10 | 确认期间增减/迁移内容，或窗口被布局转移移除 | 原请求及许可失效；不误关新内容、不撤销无关的有效操作；旧请求不能完成新请求 | U、UI |
| V16-C11 | 整个混合浮窗关闭后重新显示 Tool | Document 释放，Tool 原实例/View 保留，位置和分组恢复；Tool P2 原能力与取消检查仍通过 | P、UI |
| V16-C12 | 拖走最后 Document、合并浮窗、重置布局、关闭主窗口及重启准备 | 转移不释放文档；真正退出按既有所有权释放；无空壳、悬挂许可或重复收尾 | U、P、UI |

C01 至少有一个修改前正式失败的 Headless 测试，并补原生桌面复现。若黑框只在原生平台出现，保留实际复现证据，同时用 Headless 明确覆盖导致残留的取消/引用清理顺序，不能伪造修改前失败。

### 2.2 新建位置与异步稳定性

| 编号 | 场景 | 必须断言 | 层次 |
| --- | --- | --- | --- |
| V16-N01 | 主窗与两个文档浮窗分别按 Ctrl+Shift+P 新建 | 新实例仅出现在发起窗口，其他窗口文档数不变；至少覆盖真实快捷键、结果选择和执行 | UI、P |
| V16-N02 | 同窗口分割多文档组，焦点分别位于各组 | 新页进入操作前活动组；打开面板后搜索框焦点不改变目标 | U、UI |
| V16-N03 | 焦点在同窗口 Tool、从未记录活动组、记录已失效 | 优先该窗口最近有效文档组，否则按稳定布局顺序；不采用其他窗口全局活动文档 | U、UI |
| V16-N04 | 纯工具浮窗中新建，主窗存在多个文档组 | 使用主窗口活动组或默认组；不自动改造工具浮窗布局 | U、UI |
| V16-N05 | 在串行门排队或插件初始化暂停时切换 A→B | A 发起的新页仍进入 A 原组；不能在 await 后按 B 重取目标 | U、P、UI |
| V16-N06 | 等待期间原组移除、原组迁到其他窗口、来源窗开始关闭或失效 | 先同源窗口有效组，再主窗口；不追随迁走的组、不选无关浮窗、不复活旧窗 | U、UI |
| V16-N07 | 应用退出/重启准备或 Session 停止接受创建 | 不发布迟到页面；已创建但未发布的候选及 Scope 仅清理一次，状态结果可观察 | U、P |
| V16-N08 | 初始化异常、插入失败、重复发布、回退仍无目标 | 无部分标签/登记泄漏；文档未先在主窗闪现；失败在原面板可见，允许合法重试 | U、P、UI |
| V16-N09 | 多来源请求、面板会话切换、相同功能重复请求 | 目标只属于该次请求；共享命令对象不串窗；原串行门和面板 Busy 规则有效 | U、UI |
| V16-N10 | 成功后目标页尚未挂视觉树、之后切页/关页/切窗 | 实际新页选中；迟到焦点回调重查本次结果，不访问已释放 View，不抢回用户后来切换的窗口 | UI |
| V16-N11 | 辅助对话框成为活动 TopLevel，搜索已有页/Tool/普通命令 | 辅助窗不承载 Document；已有页只定位原实例；Tool 和普通命令的目标规则不变 | U、UI |
| V16-N12 | 功能中心、插件目录、欢迎页、打开文件、恢复/启动入口 | 共用 API 变化后仍采用各自明确默认策略；路径去重、原子初始化、保存格式和启动行为不变 | U、P、UI |

N01 必须先通过浮窗命令面板的真实路径证明现有版本落入主窗口，再用同一行为断言验证修复。不能只直接调用一个新增 PublishDocument 重载来宣称快捷键链路已修复。

### 2.3 组合与资源断言

至少完成“浮动原文档 → 同窗新建第二页 → 关闭其中一页 → 关闭最后一页”的完整流程，另覆盖关闭最后页时取消、同窗多组及混合 Tool。每个阶段核对页面索引、活动目标、窗口归属、View 身份和 Scope 释放计数。

重复开关浮窗后，窗口上下文注销、事件退订及在途请求集合应回到预期状态。优先断言显式登记/释放事实，不以不稳定的 GC 时机作为唯一泄漏证据。

## 3. 测试落点与现有回归

新增专项位于原测试项目，`DocumentWindowTestContext` 使用真实插件注册、Scope、模型与 View，只替换保存选择、可控初始化等待和存储边界。`DocumentWindowV16UiTests` 的源文件按 Close/Creation 分开，测试方法保留 C/N 编号便于检索。

| 矩阵 | 实际测试类与方法前缀（参数组合均须执行） |
| --- | --- |
| C01 | UI `DocumentWindowV16UiTests.C01最后文档标签按钮关闭后回收浮窗和文档资源` |
| C02 | UI `C02关闭入口回收同一原生浮窗`：关闭命令、实际标签菜单绑定、Window.Close |
| C03、C04 | UI `C03C04保存决策只在成功后拆除文档`：保存、放弃、取消、保存取消/失败/新增修订；Unit `DocumentCloseTests` |
| C05、C06 | UI `C05单页和整窗关闭均遵守能力及可取消事件`、`C06原生首次或重试取消后仍可重新关闭` |
| C07、C08 | UI `C07其他文档组或工具仍在时仅回收空组`、`C08主窗最后文档关闭后可重新新建` |
| C09 | UI 两个 `C09` 方法（命令排空及单页转整窗）；Unit `DocumentCloseTests` 两个 `V16` 方法（精确接续、禁止扩大范围/退出） |
| C10 | UI `C10确认期间新增页面使旧关闭请求失效`；Unit `DockWindowCloseTests` 的内容变化、移除后迟到确认、重试失败/释放 |
| C11 | UI `C11整窗关闭混合内容后工具原实例可恢复而文档只释放一次`；`DockToolWindowCloseUiTests` 全类 |
| C12 | 既有 UI `DockAreaFillUiTests` 的内容合并/源空窗、`DockCrossWindowLayoutUiTests`、`DockLayoutV3UiTests` 的回停/重置/拒绝回滚；Unit 退出及重启相关测试 |
| N01 | UI 两个 `N01` 方法：主窗/双浮窗独立新建，以及同窗新建后逐页关闭完整链 |
| N02、N03 | UI `N02N03多分组与工具焦点使用同窗最近活动文档组`、`N02点击另一组已选中页面仍更新新建落点`；Unit `DocumentCreationTargetTests.N02N03最近文档组按窗口隔离且工具激活不覆盖记录` |
| N04 | UI `N04纯工具浮窗新建回到主窗活动组且不改造工具窗口`；Unit `N04N06来源失效只回退主窗有效组而不选其他浮窗` |
| N05 | UI `N05初始化或串行门等待期间切窗不改变落点`：初始化等待、串行门排队，均断言归属与不抢焦点 |
| N06 | UI `N06创建提交前原组移走或来源关闭按规则回退`；Unit `N06原组失效或迁往别窗时优先同来源其他组` 及 `N04N06` |
| N07、N08 | Unit `N07没有安全目标时拒绝且相同标识不能替代来源身份`、`N07N08初始化失败或等待期间退出均不发布并仅释放一次`、`N08目标插入后失败撤销部分写入且重复发布不移动原页面`；UI `N08浮窗初始化失败不新增标签且原查询可重试` |
| N09 | UI `N09同一共享动作的并发请求分别保留目标`（同创建用例并发）、`N09跨窗切换面板会话不会沿用上次来源`；`CognitiveUxV8UiTests` 的 Busy/重复 Enter/关闭保护 |
| N10 | UI `N10迟到焦点恢复不能覆盖切页关页或新面板会话`、N05 切窗；`CognitiveUxV8UiTests` 成功后焦点回交 |
| N11 | UI `N11辅助窗口不作为来源且跨窗已有页只定位原实例`；`CognitiveUxV8UiTests` 及 `WorkbenchCommandPresentationUiTests` 的工具/普通命令 |
| N12 | Unit `DocumentCreationTargetTests.N12无显式目标的旧创建入口保持主默认组`；`DocumentPersistenceTests`、`CognitiveUxV8Tests` 及 UI 的功能中心/目录/欢迎入口；PluginTests 的初始化与 Scope 生命周期 |

N06 窗口失效用例调用与面板相同的生产创建用例，以便在可控初始化等待时关闭来源窗口；真实面板 Busy 会禁止关闭自身，不能为测试绕过这一保护。N01/N05/N08/N09 的面板测试另外覆盖真实快捷键与会话链。C12 的既有测试通过最终完整 verify 一并执行，不将其描述为本轮新增测试。

| 责任 | 既有落点或回归范围 |
| --- | --- |
| 创建目标与发布 | Host Unit：`WorkspaceSessionAndDockFactoryTests`、`DocumentPersistenceTests`、`DockWorkspaceNavigationTests`；新增目标策略/异步发布用例 |
| 关闭许可与退出 | Host Unit：`DocumentCloseTests`、`DockWindowCloseTests`、`DocumentOperationShutdownTests`、`WorkbenchCommandShutdownGateTests` |
| 真实 UI | Host UI：`WorkbenchCommandPresentationUiTests`；新增 Document 浮窗关闭与创建目标专项 |
| 工具与布局 | Host UI：`DockToolWindowCloseUiTests`、`DockCrossWindowLayoutUiTests`、`DockLayoutV3UiTests`；Host Plugin：`DockFloatingPolicyTests`、`DockFourWayLayoutTests` |
| 插件资源 | Host Plugin：`DocumentScopeManagerTests`、`HostLifecycleOwnershipTests`；按真实测试工厂补可控初始化和释放计数 |
| 门禁工具 | `MyAvaloniaManagement.Gate.Tests`；门禁实现与发布阈值保持原样 |

不存在的类型、过时的过滤器或零发现必须在实际执行前修正。仅靠旧类的过滤器不能覆盖新增专项；实施后先列出新增测试并核对 C/N 编号，再运行完整项目及最终 verify。

## 4. 本地开发命令

在仓库根目录按顺序运行。新增专项入口如下，必须检查非零发现；关联基线命令随后列出。`-m:1` 避免不同测试工程同时改写共享中间目录。

```powershell
dotnet test Host/MyAvaloniaManagement.Tests -c Release -m:1 --filter 'FullyQualifiedName~DocumentCreationTargetTests|FullyQualifiedName~DocumentCloseTests|FullyQualifiedName~DockWindowCloseTests'
dotnet test Host/MyAvaloniaManagement.UiTests -c Release -m:1 --filter 'FullyQualifiedName~DocumentWindowV16UiTests|FullyQualifiedName~CognitiveUxV8UiTests|FullyQualifiedName~DockToolWindowCloseUiTests'
```

```powershell
dotnet test Host/MyAvaloniaManagement.Tests -c Release -m:1 --filter 'FullyQualifiedName~DocumentCloseTests|FullyQualifiedName~DockWindowCloseTests|FullyQualifiedName~WorkspaceSessionAndDockFactoryTests|FullyQualifiedName~DocumentPersistenceTests|FullyQualifiedName~DocumentOperationShutdownTests|FullyQualifiedName~WorkbenchCommandShutdownGateTests|FullyQualifiedName~DockWorkspaceNavigationTests'
dotnet test Host/MyAvaloniaManagement.UiTests -c Release -m:1 --filter 'FullyQualifiedName~DockToolWindowCloseUiTests|FullyQualifiedName~DockCrossWindowLayoutUiTests|FullyQualifiedName~DockLayoutV3UiTests|FullyQualifiedName~WorkbenchCommandPresentationUiTests'
dotnet test Host/MyAvaloniaManagement.PluginTests -c Release -m:1 --filter '(FullyQualifiedName~DocumentScopeManagerTests|FullyQualifiedName~HostLifecycleOwnershipTests|FullyQualifiedName~DockFloatingPolicyTests|FullyQualifiedName~DockFourWayLayoutTests)&Category!=PackageAcceptance'
```

新专项完成后执行完整受影响项目，确保它们被实际发现；这也覆盖仅运行上述旧类过滤器的遗漏：

```powershell
dotnet test Host/MyAvaloniaManagement.Tests -c Release -m:1 --logger 'trx;LogFileName=host-unit.trx' --results-directory artifacts/v16/tests/unit
dotnet test Host/MyAvaloniaManagement.UiTests -c Release -m:1 --logger 'trx;LogFileName=host-ui.trx' --results-directory artifacts/v16/tests/ui
dotnet test Host/MyAvaloniaManagement.PluginTests -c Release -m:1 --filter 'Category!=PackageAcceptance' --logger 'trx;LogFileName=host-plugin.trx' --results-directory artifacts/v16/tests/plugin
dotnet test tools/MyAvaloniaManagement.Gate.Tests -c Release -m:1 --logger 'trx;LogFileName=gate-tool.trx' --results-directory artifacts/v16/tests/gate-tool
dotnet run --project tools/MyAvaloniaManagement.Gate -- verify
```

按失败定位、修复及最终收口选择执行顺序，不要求每次微调都重复全部项目。最终完整 verify 不可由专项替代；若最后已经修改源码或嵌入帮助文档，旧 verify 不作为最终输入的通过证据。不要同时运行 Gate 自测和 Gate 主工具，避免重建正在执行的工具。

`verify` 依据 [Gate 顺序计划](../../tools/MyAvaloniaManagement.Gate/GateExecutionPlan.cs)，包含固定 Avalonia/Dock 补丁准备、locked restore、Release 零警告构建、契约/API、已有测试集合、MyPlugTest 打包及真实 ZIP 验收。它不添加 coverage 或 windows-smoke 阶段。补丁准备的详细前提见 [主仓验证说明](verification.md)；不通过修改包缓存代替准备步骤。

单独运行 PluginTests 时排除 `PackageAcceptance`，完整 verify 负责提供真实包输入并执行包验收；不以专项排除项为由删掉最终包验收。SDK、MyPlugTest 及契约检查继续由最终 verify 覆盖。

## 5. 原生桌面验收矩阵

本节是本地交互验收，不是 Windows CI 或发布 Smoke。使用与所记源码匹配的开发产物，记录版本和数据目录；不得把已有安装版当成新修复产物。

| 编号 | 操作 | 完成证据 |
| --- | --- | --- |
| V16-M01 | 拖出一个真实 Document，点标签 ×；另测右键关闭、原生标题栏关闭和 Alt+F4 | 无黑框或可见空壳；主窗口仍可继续新建，记录窗口消失和输入状态 |
| V16-M02 | 最后文档有修改，分别保存、放弃、取消和保存失败 | 对话框归属正确；取消/失败保留原内容，后续编辑、保存与关闭可用 |
| V16-M03 | 主窗与至少两个浮窗切换，各窗 Ctrl+Shift+P 新建 | 面板与实际新标签位于预期窗口，窗口内多分组落点正确 |
| V16-M04 | 在可控慢初始化期间切换窗口，及允许的目标分组变化 | 结果归属遵守发起目标/回退规则，无迟到焦点抢夺或失效窗口复活 |
| V16-M05 | 工具获得焦点、纯工具浮窗、混合浮窗关闭最后文档 | 同窗最近文档组和主窗回退符合方案；仍有 Tool 时保留窗口，恢复工具实例正常 |
| V16-M06 | 浮窗中再新建一页，逐页关闭；重复浮动/回停、重置布局 | 最后一页才回收整个空窗，拖放与重置不误关或释放业务页面 |
| V16-M07 | 主窗退出/重启取消及成功，配合未保存文档 | 原关闭语义保持；没有晚到新页、孤儿窗口、重复确认或重复释放 |
| V16-M08 | 浅深主题、常用 DPI、多屏间激活，检查标签关闭与输入焦点 | 无主题掩盖的空窗，焦点与窗口归属正确；记录实际覆盖的显示环境 |

未执行项逐项标记“未执行”并说明后续验证范围，不填写推测结果。自动化全部通过时可以写“开发自动化通过、原生桌面待验收”，不能写“黑框问题已在真实桌面验证解决”。

## 6. 文档检查与证据收口

本轮核对新增/变更 Markdown 的相对链接、状态表述、命令与现有工程名称，以及 `git diff --check`。C/N 专项、Host Unit 全量与 Gate 自测已执行，最终完整 verify 在帮助文档冻结后执行；明细见开发记录与非嵌入证据。

`docs/archive/records/host-v16/development-acceptance.md` 与非嵌入的 `final-development-evidence.json` 按以下边界记录结果：

- 源码 HEAD、工作树差异身份、固定补丁身份，确认测试对应实际修改后的输入。
- 修改前缺陷证据与修改后 C/N 编号到实际测试的映射。
- 实际命令、配置、时间、退出码、TRX 的发现/通过/失败/跳过数和产物路径。
- Gate run-id、summary 及必要摘要；失败结果也保留，不拼接多次专项计数冒充一次完整运行。
- M01–M08 的实测、未执行或失败状态，截图/视频或实际观察记录，以及所用产物身份。
- 已同步的文档与链接检查；未改变 SDK、磁盘格式、门禁阈值及发布状态的事实。
- 部署和发布单独记为本轮未执行，不借用历史安装版或发布记录填补。

文档修改会进入应用内帮助的构建输入。先完成现行指南和开发记录正文，再运行最终完整 verify，最后写非嵌入 JSON 证据；若此后修改嵌入 Markdown，重新取得对应最终输入的验证。既有 Gate 文档链接检查只覆盖少量 README，本专项正文的相对链接与内容仍需额外核对。

本地开发通过不等于发布通过。正式发布时使用当次适用的完整发布门禁，继续保留既有 API、覆盖率、Windows Smoke 等待办，本方案不授权或自动执行那些操作。
