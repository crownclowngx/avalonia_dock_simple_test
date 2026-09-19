# V14 一键自动重启：专用开发验证

> 用途：定义 V14 的自动化测试、真实进程交接、桌面检查、本地开发门禁和证据规范。
> 状态：实现和自动化已接入；矩阵到实际测试的映射见第 10 节，最终判定/计数见[开发证据](../archive/records/host-v14/final-development-evidence.json)。原生桌面和单文件样本未执行。日期：2026-09-19。
> 关联：[V14 执行方案](../roadmap/host-v14-automatic-restart-plan.md)、[当前主仓验证](verification.md)、[插件开关契约](../reference/plugin-enablement.md)。

## 1. 范围与证据原则

只使用本地单元、生命周期集成、Headless UI、真实子进程测试及完整 `verify`。不使用 AIFLOW，不使用 Windows CI，不运行 `seal`、发布 Windows Smoke、发布覆盖率或发布重复性门禁，不部署、不发布。本地 Windows 进程测试属于开发验证，不因此要求执行发布流程。

必须分别证明“请求被接受”“允许最终关闭”“资源清理成功”“旧进程已退出”“新进程已创建”和“新进程初始化完成”。只断言按钮变灰、Close 被调用或 Process.Start 返回，均不足以证明重启成功。

测试优先复用现有测试项目和夹具。新增最小子进程资产只用于观测真实进程与锁；不向生产 Host 添加重置缓存、执行任意代码或跳过保存确认的测试后门。若确需新增测试项目，由原测试工程依赖纳入完整 verify，不能藏在只会手动运行的脚本中。

下列矩阵是必需覆盖面，可以用参数化或组合测试承载，不要求每行新增一个类。实施时逐项填写实际测试名、报告和状态，不写只检查源码字符串或中文注释存在的伪测试。

## 2. C：重启协调与可撤销准备

归属 `Host/MyAvaloniaManagement.Tests` 的 `HostRestartTests`，结合现有 `DocumentCloseTests` 和 `DocumentOperationShutdownTests`，窗口集成由 `HostRestartUiTests` 覆盖。

| 编号 | 场景 | 必需断言 |
| --- | --- | --- |
| C01 | 主菜单、看板及快速重复请求 | 共用一份忙碌状态和一次请求；只有一次准备与助手启动，没有第二个 Host |
| C02 | 全部文档干净、没有文档 | 均可正常准备；仍执行命令排空、布局保存和关闭协议 |
| C03 | 多个脏文档保存/放弃/取消 | 完成全部决定后才拆除 UI；任一取消不先关闭其他文档，成功保存的文件不回滚 |
| C04 | 保存失败、另存选择器取消、保存期间再次修改 | 重启停止，文档保留；错误可见，不能使用过期 revision 授权关闭 |
| C05 | 布局最终提交失败 | 撤销关闭许可并恢复布局调度，旧布局与工作区仍可使用，不能拉起新进程 |
| C06 | 原生最终 Closing 拒绝 | 即使助手已 Ready 也必须 Abort；本次意图不被下一次普通退出消费 |
| C07 | 插件设置写入/重读在途 | 服务层阻止新冲突写入并等待已接受操作；失败停止重启，取消后恢复写入能力 |
| C08 | 看板关闭而保存继续 | 是否可重启依据服务事实；看板销毁不丢保存，晚到结果不访问已释放控件 |
| C09 | 普通退出与重启并发、重入关闭回调 | 只有一个关闭所有者；普通退出不意外升级为重启，退出中不接受新请求 |
| C10 | 取消后再次重启 | 许可、布局冻结、助手和忙碌状态完整解除，新请求可正常完成 |
| C11 | 从 Workbench Command 发起 | Handler 释放自身执行租约后才进入最终排空；不会等待自己，异常由协调器观察 |
| C12 | 准备期间创建文档、执行命令或关闭浮窗 | 既有操作门和可撤销许可正确协调，不新增未被确认的文档，不误释放其他实例 |

取消与失败用例需要断言原 Document、Tool、View、Scope 身份及仍可操作性，不只检查集合数量。对关闭阶段未完成的正常用户决定不设置会丢数据的强制自动通过。

## 3. L/H：启动描述与助手协议

启动规则位于 `HostRestartTests`，助手协议位于 `HostRestartHandoffTests`，均在 Host Unit 项目；真实管道和进程等待另由 X 系列证明。

| 编号 | 场景 | 必需断言 |
| --- | --- | --- |
| L01 | apphost、dotnet DLL 启动 | 可执行文件、入口 DLL、参数数组和工作目录正确；不重新调用 dotnet run/build |
| L02 | 自包含/单文件启动描述 | 使用实际进程路径，不依赖 Assembly.Location；纯规则测试与真实样本覆盖分别记录 |
| L03 | 中文、空格、引号和类似 shell 元字符的参数 | 参数边界完整，无 shell 插值；不会拆成多个参数或执行其他命令 |
| L04 | 自定义数据根和必要环境 | 新 Host 使用同一有效数据根；内部助手参数、一次性令牌、测试探针标志不扩散 |
| L05 | 文件不存在、不可执行、入口未知或工作目录无效 | 关闭 UI 前可诊断失败；不猜测其他程序，不丢弃必要参数勉强启动 |
| H01 | 助手早期分流 | 不创建 HostRuntime、插件 ALC、主窗口、布局 Writer 或正常 Host 日志会话 |
| H02 | Ready、CleanExitReady、成功退出的顺序 | 任一条件缺失都不启动；Ready 不授予启动许可，CleanExitReady 不跳过等待 |
| H03 | 最终关闭拒绝及 Abort | 助手结束、连接解除；迟到信号和重复信号不重新激活请求 |
| H04 | 旧进程退出/崩溃但未发最终许可 | 无论 PID 不存在或退出码为 0，都不自动重启 |
| H05 | 最终许可后旧进程失败退出或不退出 | 不拉起；超时退出助手，不强杀旧 Host |
| H06 | PID 复用、进程身份错误、连接前父进程已退出 | 使用已验证的目标进程等待对象，不误认另一个进程；失败关闭通道 |
| H07 | 版本/会话身份不符、截断、过长消息、连接断开 | 拒绝协议，不构造启动操作；错误脱敏、等待有界 |
| H08 | 助手启动失败或 Ready/确认超时 | 可撤销阶段恢复工作区；不可撤销阶段只报告失败，不假称恢复原实例 |
| H09 | 重复 CleanExitReady、重复退出通知及重复 Dispose | 至多启动一次，句柄和连接只按所有权释放，不发生循环 |
| H10 | 新进程创建失败、助手错误反馈 | 独立中文反馈及可定位日志；不访问旧窗口、不加载插件、不无限重试 |
| H11 | 一次性信息寿命 | 普通下次启动没有可消费的残留指令；不会把会话令牌、敏感参数写入诊断 |

时间相关单元测试使用可控时间源、明确握手和完成信号。真实进程测试使用有限总时限，不能靠固定 sleep 后比较 PID。所有测试启动的子进程都登记归属，失败清理仅处理本测试拥有的进程及临时目录，不能扫描并结束用户正在运行的 Host。

## 4. R：退出与资源所有权

`Host/MyAvaloniaManagement.PluginTests/HostRestartLifecycleTests` 复用真实 Shutdown 结果，结合 `HostLifecycleOwnershipTests` 和 `PluginLifecycleCoordinatorTests`；命令、文档、工作流门的单元回归继续放在原项目。

| 编号 | 场景 | 必需断言 |
| --- | --- | --- |
| R01 | 正常重启退出 | 原关闭链仅执行一次；窗口/View、Scope、插件生命周期、Provider 和锁的释放次序符合现有契约 |
| R02 | 命令、文档初始化、工作流或取消回调未结束 | 维持原排空/资源保留政策；无 CleanExitReady，不释放仍被使用的 Provider |
| R03 | 插件 Shutdown 失败、Provider.Dispose 失败 | Failures 非空即不能拉起；ResourcesRetained=false 不掩盖 Dispose 错误 |
| R04 | Runtime 已释放后的进程交接 | 交接对象仍可用且不解析已释放容器，不保留插件/View 引用，退出后资源完整回收 |
| R05 | 普通退出与启动回滚 | 均无自动重启；重启请求不会把启动失败循环拉起 |
| R06 | 正常诊断关闭失败或后续退出异常 | 不误发最终成功许可或返回成功退出状态；保留原始错误，不被交接错误覆盖 |
| R07 | 生命周期回调及 Provider 重复清理请求 | 沿用原幂等/共享完成结果；新增协调不导致二次 Dispose 或重复 Shutdown |

不能因资源排空困难改用强制退出，也不能为测试把关闭宽限改为无限等待。安全保留资源的历史测试必须继续通过。

## 5. U：真实绑定与 Headless UI

`Host/MyAvaloniaManagement.UiTests/HostRestartUiTests` 验证窗口关闭接入，同时回归 `ApplicationAndWindowTests`、`PluginEnablementUiTests`、`PluginStatusWindowTests` 和 `DockLayoutV3UiTests`。

| 编号 | 场景 | 必需断言 |
| --- | --- | --- |
| U01 | 主菜单和看板真实控件点击 | 实际绑定到共同用例；不只直接调用私有方法 |
| U02 | 已保存待生效、改回原设置、无变更 | 看板入口状态准确；主菜单无变更也可重启；不伪造当前插件状态 |
| U03 | 保存中、准备中、连续点击及关闭看板 | 忙碌原因明确，无重复执行、无晚到 UI 访问；窗口关闭不取消已接受设置保存 |
| U04 | 文档确认取消、布局失败、原生 Closing 否决 | 窗口及原工作区保留，按钮可恢复，错误可见 |
| U05 | 最终关闭、浮窗、模态窗口和焦点所有者 | 重用现有窗口上下文，保存对话框归属正确，不残留浮窗或隐藏阻塞窗口 |
| U06 | 窄窗口、深浅主题、键盘访问 | 中文入口和状态可读可达，没有新增默认快捷键冲突 |

按现有 Headless 串行约定推进 Dispatcher，验证真实事件次序。Headless 不执行真实的生产进程拉起，用系统端口替身验证 UI 边界；跨进程事实由 X 系列独立证明。

## 6. X：真实子进程与状态恢复

Plugin 测试中的 `HostRestartProcessTests` 编排隔离 Controls、临时数据根与真实旧/新进程。`TestAssets/RestartHarness` 直接调用生产 `Program.Run`，保留 HostRuntime、窗口关闭、真实管道和助手；仅注入 Avalonia.Headless 桌面启动及测试错误收据。测试资产由 Plugin 工程依赖、构建和复制，自动进入完整 verify；没有生产测试参数开关或假 Host 协议副本。

| 编号 | 场景 | 必需断言 |
| --- | --- | --- |
| X01 | 旧 Host→助手→新 Host | 独立 PID/进程身份、Ready/最终许可、旧进程退出和新进程启动的可观察顺序；恰好一个新 Host |
| X02 | 锁交接 | 旧 Host 持有测试数据根 Writer 锁期间新 Host 不获取该锁；旧进程退出后新进程可获得，无交接窗口争用 |
| X03 | 启用→禁用→再启用 | 两次真实自动重启采用同一数据根；禁用时无 DLL 加载/Configure/生命周期，重新启用恢复；不是单纯读写 JSON |
| X04 | 工具布局、收藏、Document | 按既有规则恢复工具和偏好，业务文件保留，Document 不自动重开 |
| X05 | 崩溃、强杀、无最终许可的正常退出 | 无后继 Host；助手在有限时间内回收，不能变为常驻守护 |
| X06 | 父进程退出延迟、断开握手、新进程创建失败 | 不提前启动、不无限等待；有错误证据和独立反馈 |
| X07 | 连续多轮重启及随后普通退出 | 每轮助手释放，无进程/句柄持续增长，最后普通退出没有后继实例 |
| X08 | 实际 apphost 和 dotnet DLL 入口 | 均经过真实启动及交接，中文/空格路径、参数、工作目录、数据根保持正确 |
| X09 | 现有单文件/自包含样本 | 有匹配源码的可用样本时验证真实启动形式；无样本标记未执行，纯规则测试不能替代，不为此启动发布门禁 |

X01–X08 为必需自动化。真实 Host 的驱动若需要新增测试侧适配器，复用现有内部测试访问和 Avalonia 测试设施，不给普通用户入口增加可执行任意操作的测试参数。若受原生桌面环境限制未能执行，明确标记受阻，不能把协议夹具通过当作完整 X01 通过。

进程创建成功与新 Host 初始化结果分开记录。新 Host 插件初始化失败时保留既有启动错误行为；不要求助手承担持续健康监控。单文件实机覆盖不足必须列为独立限制，后续发布样本验证另行完成。

## 7. 本地开发命令

以下命令从主仓根目录串行执行；过滤器中的 `HostRestart` 对应已落地类名。实际轮次报告见最终 JSON，不覆盖旧失败报告。

### 7.1 基线与最终完整门禁

```powershell
dotnet run --project tools/MyAvaloniaManagement.Gate -- verify
```

沿用当前固定 Avalonia/Dock 补丁准备、locked restore、Release 零警告构建、SDK/Host Unit/Plugin/Headless UI/MyPlugTest Unit、契约和 API 比较、MyPlugTest 打包及真实 ZIP 验收。它不运行发布 Windows Smoke，也不授予发布资格。

实施前运行基线，所有代码及嵌入 Markdown 定稿后运行最终完整 verify。专项不能代替完整门禁，不更改 scope、阈值、API 基线或排除新增测试换取成功。

### 7.2 协调、启动、交接与相关单元测试

```powershell
dotnet test Host/MyAvaloniaManagement.Tests -c Release --no-restore -m:1 -warnaserror --filter 'FullyQualifiedName~HostRestart|FullyQualifiedName~DocumentCloseTests|FullyQualifiedName~DocumentOperationShutdownTests|FullyQualifiedName~WorkbenchCommandHostHandlerTests|FullyQualifiedName~WorkbenchCommandShutdownGateTests|FullyQualifiedName~WorkflowActionShutdownGateTests|FullyQualifiedName~PluginEnablementServiceTests|FullyQualifiedName~DockLayoutV3StoreTests' --logger 'trx;LogFileName=v14-unit.trx' --results-directory artifacts/host-v14/unit
```

### 7.3 生命周期、真实子进程及插件开关往返

```powershell
dotnet test Host/MyAvaloniaManagement.PluginTests -c Release --no-restore -m:1 -warnaserror --filter '(FullyQualifiedName~HostRestart|FullyQualifiedName~HostLifecycleOwnershipTests|FullyQualifiedName~PluginLifecycleCoordinatorTests|FullyQualifiedName~PluginEnablementRestartTests|FullyQualifiedName~PluginEnablementLoadingTests)&Category!=PackageAcceptance' --logger 'trx;LogFileName=v14-plugin.trx' --results-directory artifacts/host-v14/plugin
```

PackageAcceptance 由完整 verify 准备真实包输入。常规专项排除该类别，不能把缺包导致的跳过计作通过。

### 7.4 Headless UI 及布局回归

```powershell
dotnet test Host/MyAvaloniaManagement.UiTests -c Release --no-restore -m:1 -warnaserror --filter 'FullyQualifiedName~HostRestart|FullyQualifiedName~ApplicationAndWindowTests|FullyQualifiedName~PluginEnablementUiTests|FullyQualifiedName~PluginStatusWindowTests|FullyQualifiedName~DockLayoutV3UiTests|FullyQualifiedName~DockToolWindowCloseUiTests' --logger 'trx;LogFileName=v14-ui.trx' --results-directory artifacts/host-v14/ui
```

`--no-restore` 的前提是本轮已完成 locked restore。输出目录按实际轮次加唯一子目录保存，不能覆盖先前失败证据。组合过滤器可能只命中旧测试；必须从 TRX 核对新增类及矩阵，退出码为 0 不代表新增能力已经验证。

### 7.5 工具自测与文档校验

不为 V14 主动重构 Gate。只有确实修改其执行、测试资产纳入或证据校验逻辑时，才同步工具测试并串行执行：

```powershell
dotnet test tools/MyAvaloniaManagement.Gate.Tests -c Release -m:1 -warnaserror
```

检查所有本轮 Markdown，包括新增未跟踪文件的本仓相对链接、锚点、命令、类名及状态；运行 `git diff --check`。现有 Gate 的三个 README 检查不能覆盖全部新文档。SOLID、中文注释、协议提交点和可撤销边界采用人工代码审查。

## 8. 原生桌面检查

在隔离开发数据根完成，逐项记录已观察、失败或未执行及原因。此处不是 Windows CI、发布 Smoke 或安装目录部署。

| 编号 | 人工检查 |
| --- | --- |
| M01 | 主菜单点击重启，窗口短暂关闭并自动重新出现；工具布局恢复，Document 不自动重开 |
| M02 | 看板禁用插件并重启，入口按配置消失；再启用重启后恢复；用户数据保留 |
| M03 | 有脏文档时保存、放弃、取消；另存选择器取消和保存失败均不误退出 |
| M04 | 浮窗、多个屏幕、全屏内容或模态窗口下重启，确认提示归属、窗口收尾及焦点体验 |
| M05 | 快速连点、准备期间关闭看板、设置写入在途、取消后再次重启 |
| M06 | 助手启动失败或新 Host 无法创建时，用户可以看到明确中文错误而非程序无声消失 |
| M07 | 新 Host 初始化失败，显示既有启动错误，不反复自动拉起 |

外部真实视频、下载、数据库及账号任务仍由对应插件验收；主仓假插件的生命周期测试不代表所有业务任务都已正确收尾。未验证项不阻止如实记录自动化结果，也不能被标记为桌面全部通过。

## 9. 实施记录与最终证据

已创建[开发记录](../archive/records/host-v14/development-acceptance.md)，记录设计取舍、P0–P5、SOLID 审查与原生桌面状态。最终验证后写入同目录 `final-development-evidence.json`，并接入归档导航；不预填成功。

证据至少包含：日期、HEAD、未提交差异身份、阶段提交、实际命令及退出码、测试通过/失败/跳过数、TRX 路径和摘要、Gate run-id/summary、C/L/H/R/U/X 到实际测试的映射。子进程证据记录旧/助手/新进程身份、有效信号、退出码、启动次序、数据根的脱敏标识及失败清理结果；不记录会话令牌、敏感用户参数或业务内容。

Markdown 嵌入 Host，先定稿再跑最终完整 verify；最终计数和产物身份写入非嵌入 JSON。若后续修改影响源码或嵌入资源，旧结果不能冒充新产物验证，需按影响重新验证。

开发完成要求：所有必需自动化实际执行并通过、完整 verify 成功、原有效断言及兼容政策未削弱、SOLID 与详细中文注释审查完成、文档一致。缺测试、零命中、缺报告、必需项跳过或失败均不满足完成条件。开发通过、原生桌面通过、单文件样本通过、部署与发布分别标记。

## 10. 实际测试映射与观察边界

下表描述实际实现的自动化覆盖；计数和运行状态以最终 TRX/JSON 为准。复用测试保护原关闭协议，新增测试保护重启与该协议的接合点，不复制一套保存实现。

| 矩阵 | 实际测试/核对 |
| --- | --- |
| C01、C02、C10、C11 | `HostRestartTests.重复请求只准备一次_取消恢复设置入口且下一次仍可关闭`、`普通关闭不创建助手且准备取消后不残留请求`；`HostRestartUiTests.菜单重启释放自身命令租约_助手就绪后才关闭` |
| C03、C04 | `DocumentCloseTests.窗口关闭覆盖干净_放弃_保存取消和重复请求`、`窗口保存期间出现新修订_保持打开且再次保存后允许关闭`、`范围关闭取消或文件选择取消保持整组与生命周期`；重启复用此调用链 |
| C05、C06、C09、C12、U04、U05 | `DockLayoutV3UiTests.最终布局写入失败保留窗口与命令且重试成功后可以退出`、`主窗等待干净文档命令后被原生取消会恢复命令与创建入口`、`主窗口退出先保存可见浮窗再拆除且重启不恢复文档`；`HostRestartUiTests` 最终否决和迟到继续关闭测试 |
| C07、C08、U02、U03 | `HostRestartTests` 在途操作成功/失败、冻结取消和双重冻结；`PluginEnablementServiceTests` 真实存储冻结；`PluginEnablementUiTests` 保存期间关窗、改回设置和冲突重读 |
| L01–L05 | `HostRestartTests.启动参数逐项保留_助手参数不污染普通启动`，含缺可执行/入口 DLL/目录；`HostRestartProcessTests` 两种启动形式完整往返；操作系统创建失败在协议测试注入，真实单文件仅实现路径规则，X09 未执行 |
| H01–H05、H08、H09、H11 | `HostRestartHandoffTests` 许可/退出/取消/未知助手/重复许可；`HostRestartProcessTests.无许可或未成功退出不能产生后继Host` 覆盖 ordinary、cancel、crash、kill、bad-exit；`最终许可已确认但旧进程仍存活时不能提前拉起`；DI 借用与早分流经代码审查 |
| H06、H07 | `HostRestartHandoffTests.父进程身份不符或指向自身时在连接前拒绝`；`HostRestartTests` 截断/不匹配身份及固定字节读取；真实会话使用 PID+启动时间及保留句柄，未强造系统 PID 复用 |
| H10、X06 | `HostRestartHandoffTests.父进程等待取消不启动也不强杀`、`新进程创建失败不重试`；真实进程延迟退出及取消断管。90 秒超时用确定性取消验证；创建失败采用系统副作用替身，原生错误窗口待 M06 |
| R01–R05、R07 | `HostRestartLifecycleTests` 的有/无释放异常、幂等、Scope 失败保留；`HostLifecycleOwnershipTests` 正常关闭、挂起/取消/排空及启动回滚；`PluginLifecycleCoordinatorTests`；真实进程失败退出无后继 |
| R06 | Program 顺序审查：Shutdown 后关闭诊断并检查新增记录，再发送最终结果；`HostDiagnosticsTests` 诊断容错与脱敏、`HostLifecycleOwnershipTests` 关闭诊断/迟到异常及 `bad-exit` 回归。未通过真实磁盘故障强制注入日志 Dispose 失败 |
| U01、U06 | `HostRestartUiTests` 真实菜单投影；进程 Harness 点击看板实际 XAML 按钮 Command；`WorkbenchCommandProjectionTests`、`WorkbenchCommandPresentationUiTests` 精确命令集合和无默认快捷键；既有看板窄窗主题回归。原生键盘/可读性待 M 项 |
| X01–X04、X07、X08 | `HostRestartProcessTests.新进程禁用再启用并交接布局锁_两种启动形式`：三个 Host、两个助手、实际 MyPlugTest 启动策略、独占布局锁、额外页面不重开、参数/目录/数据根；`PluginEnablementLoadingTests` 验证禁用前置过滤无加载，`PluginEnablementRetentionTests` 验证工具/偏好保留 |
| X05 | `无许可或未成功退出不能产生后继Host` 的 crash、kill 与普通退出；身份限定的进程清理 |
| X09、M01–M07 | 未执行，分别保留单文件/自包含和原生桌面检查；不作为已通过的自动化计数 |

测试输出目录 `TestResults/v14-restart/<随机标识>` 保留测试进程收据；最终 JSON 只汇总测试标识、进程角色/身份、结果和报告哈希，不收录会话令牌、原始参数或用户数据路径。旧安装版和本轮工作树结果不能混用。
