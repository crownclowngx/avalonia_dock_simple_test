# V18：命令面板交互专用开发验证

> 用途：验证四类结果分组、明确动作、当前目标提示和无额外操作负担的命令面板改造。
> 状态：V18 实现、专项及最终完整开发验证已完成，见[开发记录](../archive/records/host-v18/development-acceptance.md)与其非嵌入 JSON。项目所有者已[确认当前主程序手工验收通过](../archive/records/host/manual-acceptance-20260921.md)，矩阵保留供后续回归。日期：2026-09-21。
> 设计依据：[V18 交互方案](../archive/plans/host-v18-command-palette-interaction-plan.md)。当前实际行为见[工作区搜索](../quick-start/workbench-search.md)。本文不授予部署或发布资格。

## 1. 验证范围与执行边界

SOLID、详细中文注释、单一事实源和原有执行路径是首要审查条件。以可观察行为组织单元测试和 Headless UI 测试，不以私有方法名、类数量、文件行数或复制生产实现的断言代替验证。

本页维护可复用行为矩阵和实际方法映射，不把阶段测试数量重复作为当前基线。第 10 节映射已经落地的测试与审查；V18 最终完整 `verify` 已在当时代码和嵌入 Markdown 定稿后通过，输入身份与结果单独存入开发证据。后续修改按实际影响重新验证。

开发允许本机 `dotnet test` 和既有 `verify`。**不使用 AIFLOW、Windows CI、`seal`、发布 Windows Smoke、发布覆盖率、发布重复性门禁，不部署或发布产物。** 本机在 Windows 上运行单元测试或 Headless 测试不等同于 Windows CI；不改动或放宽发布政策。

[Gate 顺序计划](../../tools/MyAvaloniaManagement.Gate/GateExecutionPlan.cs) 中 `verify` 只包含补丁准备、restore、build、contracts、tests、packages 和 package-acceptance；coverage 与 windows-smoke 只属于 Seal。开发阶段的 MyPlugTest 打包及本地包验收不是公开发布。

## 2. 测试落点和补测原则

后续修改时逐条核对以下矩阵与现有测试的真实方法、参数组合和断言。下表列出检查入口，已落地映射见第 10 节；新行为缺口按实际风险补测。

| 范围 | 已有测试入口 | V18 补测方向 |
| --- | --- | --- |
| 查询、身份、展示与排序 | Unit：`WorkbenchCommandProjectionTests`、`DockWorkspaceNavigationTests`、`PluginNavigationTests`、`ToolCenterTests` | 四组连续、组相关性、同名/多 Intent、不初始化业务、状态原因 |
| 命令状态及执行 | Unit：`WorkbenchCommandContextStateTests`、`WorkbenchCommandDocumentTargetTests`、`WorkbenchCommandCatalogExecutorTests`、`WorkbenchCommandHostHandlerTests` | 当前页提示和提交目标一致、禁用、无副作用失败、共享执行路径 |
| 键盘和生命周期 | UI：`WorkbenchCommandPresentationUiTests`、`UiRefreshSchedulerTests`、`ToolCenterUiTests`、`DocumentWindowV16UiTests` | 非候选标题、跨组导航、稳定选择、目标消失、工具恢复、跨窗及迟到回调 |
| 边界及设计数据 | Unit：`HostApiBoundaryTests`、`PublicApiContractTests`；Plugin：`PluginHostBoundaryTests`；UI：`WorkbenchCommandPresentationUiTests` | public API 不变，生产/设计投影一致，View 不持有业务对象 |
| 帮助文档 | Unit：`HelpContentTests`；UI：`HelpWindowTests` | Markdown 嵌入、读取、相对链接和现有渲染/窗口行为 |

新增测试位于 `CommandPaletteV18Tests`、`CommandPaletteV18TargetTests`、`CommandPaletteV18UiTests`，并补充既有 `ToolCenterUiTests`。过滤器包含 V8 既有行为保护；检查 TRX 实际发现的名称和数量，不能只凭零退出码认定执行。

用本仓 Host/MyPlugTest 或测试夹具模拟 Bili 多 Intent、小说控制和游戏多实例，验证注册形状、目标和执行次数即可，不复制邻仓业务。外部插件的网络、账号、模型、下载、加密等专有业务不属于 Host 本轮门禁。

## 3. Q：发现、分组与匹配矩阵

| 编号 | 必须验证的可观察行为 |
| --- | --- |
| Q01 | 四种原身份映射到四组；无匹配组不出现；组标题没有 StableKey、Command 或执行能力 |
| Q02 | 空查询组序为页面、创建入口、工具、命令；组内稳定，注册遍历顺序改变不影响最终结果 |
| Q03 | 非空查询以组内最佳 MatchRank 排组，同等级固定组序；一组结果连续，不恢复逐行混排 |
| Q04 | 精确命令“保存”在只有辅助字段命中的页面组之前；两组同为精确匹配时采用固定组序 |
| Q05 | 组内按 Rank、同等级可执行优先、SearchName、StableKey 排序；较弱行留在其组内的取舍有显式断言 |
| Q06 | 名称精确/前缀/包含/辅助包含、首尾空白和英文大小写规则保持；界面动作前缀不污染原 SearchName |
| Q07 | 同名不同来源、两个真实页面、同类型不同 Intent 均保留；重复菜单声明的同 CommandId 仍只出现一次 |
| Q08 | 当前页序号在筛选中稳定且关闭后不复用；标题重名时 PageId 仍决定执行目标 |
| Q09 | 命令仍仅按既有发现许可出现；Owner/Target 不成立时过滤，业务 Disabled 时保留；局部 ICommand/Workflow Action 不自动暴露 |
| Q10 | 查询、匹配、分组、状态提示和图标回退均不创建 Document、Tool、Scope 或执行业务；用创建/执行探针证明 |
| Q11 | 有可信来源名时展示和匹配它；无名称时受控回退，绝不从 ID 猜名称；未知元数据不丢掉合法结果 |

## 4. K：键盘、选择与展示矩阵

| 编号 | 必须验证的可观察行为 |
| --- | --- |
| K01 | 输入后默认选中第一个可执行结果；全部禁用时选第一条并展示原因；零结果时无选中项、无可执行 Enter 提示 |
| K02 | 上下键连续跨组，只选真实结果；首尾保持原有边界行为；组标题不能获得列表选择或执行 |
| K03 | 普通成功路径仍为输入、必要的上下选择、Enter；不强制点击分类、按钮、Tab、右箭头或二级菜单 |
| K04 | 选中页、创建入口、工具和命令后，底部提示分别对应真实动作；选择改变立即更新，无旧目标文案残留 |
| K05 | 禁用项可被显式选中并读取说明；Enter 不执行它，也不执行别的可用项；双击同样遵循禁用规则 |
| K06 | 状态刷新保留 StableKey、焦点和必要滚动位置；单纯 Enabled/布局变化不把操作中的选择重置或突然搬动 |
| K07 | 选中目标消失或失效后，本次 Enter 终止并反馈；补选项只能由下一次明确提交执行，不能沿用旧按键 |
| K08 | 重复打开仍沿用原会话规则；Esc 正常关闭空闲面板并恢复原焦点；关闭后的迟到通知不复活面板 |
| K09 | 错误反馈、忙状态和 Enter 帮助文案职责分离；错误不被普通选择提示遮盖，运行期间拒绝重复提交 |
| K10 | 长中文/英文标题、插件标识、实例号、快捷键及原因不会互相覆盖；空组、仅一组、全部禁用及设计数据均可渲染 |
| K11 | 分组和动作能通过文本及可访问名称理解，不只依赖颜色/图标；高 DPI、小窗口和主题实际体验单独检查 |
| K12 | 中文输入法确认候选字不会误执行结果。Headless 能模拟的事件链自动验证，平台组合输入另做本机人工检查，不以模拟替代真机结论 |

## 5. E：动作、目标和生命周期矩阵

| 编号 | 必须验证的可观察行为 |
| --- | --- |
| E01 | 两个同类页面的操作目标提示精确对应当前实例；另一个实例内容、修改状态、撤销栈和执行计数不变 |
| E02 | 旧提示生成后切换活动页，在最终处理器调用前重查并拒绝旧目标提交；不按同名/同类型选别的页，也不主动切回 |
| E03 | Host 保存绑定当前页；全局帮助/中心入口不附加无关页面约束；无活动页时按各自原 CanExecute 处理 |
| E04 | 目标说明与预期身份来自一致快照；UI 刷新与执行间发生的目标变化不会出现“显示 A、执行 B” |
| E05 | 预期目标值属于本次调用，不写入共享命令缓存；两个窗口/会话之间不会覆盖目标或污染菜单与快捷键 |
| E06 | 工具隐藏后恢复同实例；自动收起被展开；停靠/浮动时聚焦原工具及实际承载窗口，不重复构造 |
| E07 | 已有页面切换保持内容和未保存修改；浮窗最小化时恢复原窗口，不创建替代页面 |
| E08 | 多 Intent 创建传入精确原身份；多个来源同名不误路由；已打开同名页面不能把明确的新建操作改成切换 |
| E09 | 新建保持 V16 的发起窗口/文档组捕获及原安全回退；初始化期间切窗或开新会话时，旧完成不抢回焦点 |
| E10 | 新建失败保留查询、选择和本次错误，支持重试；执行中重复 Enter 不重复创建，关闭/取消语义保持 |
| E11 | 命令继续复用 State Query、Command Store 和 Executor；菜单、快捷键、Palette 的业务状态和副作用一致 |
| E12 | 插件不可用、文档关闭中、Host 退出和命令在途排空维持原限制；未完成任务不被展示重排或释放截断 |
| E13 | 模拟运行/暂停/取消状态通知，确认准确名称和可执行状态变化；不能将预览标成导出完成、将延后暂停标成立即取消 |
| E14 | 工具真实原因、Host 已知原因及插件仅布尔状态的通用原因分别验证；未知原因和异常不得伪造成具体业务结论 |
| E15 | 后台通知经原 UI 调度合并，异常观察者隔离；Dispose 和迟到回调不造成重复订阅或保留插件对象 |
| E16 | 有效快捷键仍来自冲突治理投影；冲突键不展示为可用，命令本身可通过正常结果执行 |

涉及并发时使用可控 Task、明确 Dispatcher 推进和执行探针，不用任意 Sleep 猜测时序。针对 E02/E04 必须制造“展示后、调用前”的真实竞态，不只测试两个静态列表。

## 6. A：架构、注释与兼容审查

| 编号 | 审查要求 | 证据方式 |
| --- | --- | --- |
| A01 | 投影负责展示事实与排序，View 负责输入会话，既有执行者负责副作用；无第二目录/执行器/状态缓存 | 代码审查、原有边界测试、执行探针 |
| A02 | 四类身份及多 Intent 组合保持；纯值展示不持有 Provider、Scope、Document 模型、Dock 或窗口对象 | 类型边界审查及资源/弱引用回归 |
| A03 | 不引入插件 ID 特判、动词猜测、反射 ICommand 扫描、通用规则引擎；Host 少量说明集中显式声明 | SOLID 与依赖方向审查 |
| A04 | SDK public API、版本、持久化 schema、菜单许可和快捷键政策不变 | 既有 API/契约测试、差异审查、完整 verify |
| A05 | 生产和设计投影均满足分组/选择约束，设计数据不初始化真实 Workspace | 设计数据测试及 Headless 绑定验证 |
| A06 | 新核心类型/方法/分支有详细中文注释，说明排序取舍、目标一致性、状态原因边界、所有权和迟到回调理由 | 人工代码审查，不以注释行数计分 |
| A07 | 不增加常见路径的必需点击/按键；业务本来需要的选择器和确认仍归原用例 | K 系列及针对性桌面演练 |
| A08 | 相邻插件只作语义参考，不成为主仓门禁依赖；Workflow 与局部操作不被误纳入 | Q09、夹具注册检查及独立仓库边界审查 |

## 7. 本地开发命令

以下均在主仓根目录执行。共享中间目录的构建和测试串行运行，保留 `-m:1`。执行前核对[主仓验证前提](verification.md)，不要通过旧 DLL、放宽警告或删除断言取得通过。

### 7.1 P0 基线与 P4 最终开发门禁

```powershell
dotnet run --project tools/MyAvaloniaManagement.Gate -- verify
```

P0 记录原始结果；P4 在最终源码和 Markdown 定稿后运行。`verify` 是日常开发门禁，不运行 Windows CI/Smoke 或发布覆盖率。若基线失败，记录原因与影响后修复实际前置问题，不把失败改为跳过。V18 文档交付本身不要求提前执行整个实现阶段门禁。

### 7.2 专项测试

下面 `--no-restore` 以本次 locked restore 已成功、固定补丁已就绪为前提；新增依赖不属于 V18 计划范围。结果目录每次新建，示例路径不表示已经执行。

```powershell
$v18RunId = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$v18Results = "artifacts/host-v18/$v18RunId"

dotnet test Host/MyAvaloniaManagement.Tests -c Release --no-restore -m:1 -warnaserror --filter 'FullyQualifiedName~V18|FullyQualifiedName~CognitiveUxV8Tests|FullyQualifiedName~WorkbenchCommand|FullyQualifiedName~DockWorkspaceNavigationTests|FullyQualifiedName~PluginNavigationTests|FullyQualifiedName~ToolCenterTests|FullyQualifiedName~HostApiBoundaryTests|FullyQualifiedName~PublicApiContractTests' --logger 'trx;LogFileName=palette-unit.trx' --results-directory "$v18Results/unit"

dotnet test Host/MyAvaloniaManagement.UiTests -c Release --no-restore -m:1 -warnaserror --filter 'FullyQualifiedName~V18|FullyQualifiedName~CognitiveUxV8UiTests|FullyQualifiedName~WorkbenchCommandPresentationUiTests|FullyQualifiedName~UiRefreshSchedulerTests|FullyQualifiedName~ToolCenterUiTests|FullyQualifiedName~DocumentWindowV16UiTests' --logger 'trx;LogFileName=palette-ui.trx' --results-directory "$v18Results/ui"

dotnet test Host/MyAvaloniaManagement.PluginTests -c Release --no-restore -m:1 -warnaserror --filter '(FullyQualifiedName~PluginHostBoundaryTests|FullyQualifiedName~HostLifecycleOwnershipTests|FullyQualifiedName~DocumentScopeManagerTests)&Category!=PackageAcceptance' --logger 'trx;LogFileName=palette-plugin.trx' --results-directory "$v18Results/plugin"
```

专项按实际变更分阶段执行，最后仍需完整 `verify`。测试类若调整，更新过滤器并检查新测试实际出现在 TRX 中。常规 Plugin 专项排除 PackageAcceptance；完整 verify 准备真实包后执行，缺包或零测试不能算通过。

### 7.3 文档与嵌入帮助验证

```powershell
git diff --check

dotnet test Host/MyAvaloniaManagement.Tests -c Release --no-restore -m:1 -warnaserror --filter 'FullyQualifiedName~HelpContentTests' --logger 'trx;LogFileName=help-content.trx' --results-directory "$v18Results/help-unit"

dotnet test Host/MyAvaloniaManagement.UiTests -c Release --no-restore -m:1 -warnaserror --filter 'FullyQualifiedName~HelpWindowTests' --logger 'trx;LogFileName=help-window.trx' --results-directory "$v18Results/help-ui"
```

这里不使用 `--no-build`，确保 Markdown 重新嵌入 Host。额外检查本次新增及改动 Markdown 的本仓文件链接和标题锚点；可选邻仓链接单列。现有帮助测试证明已有读取/渲染链及窗口行为，不自动证明每个新增页面的所有链接；必须结合显式链接检查和新资源可读取检查。

若仅文档交付时 restore 前提不成立，可以先准备本机必要依赖再执行上述专项；记录实际命令，不能把未运行的计划命令当作证据。Gate 工具未修改则无需为形式运行其自测；若后续实际修改 Gate，实现方追加工具自测并与 Gate 主进程串行执行。

## 8. 针对性桌面验收

该表用于实施后的本机体验检查，不是 Windows CI 或发布 Smoke。Headless 通过不代替输入法、原生窗口和多屏体验结论。

| 场景 | 通过标准 |
| --- | --- |
| 欢迎页混合搜索 | 两个已有实例和两个新开入口按组连续显示，来源/序号一眼可辨；普通 Enter 和上下选择即可完成 |
| 精确命令搜索 | 输入“保存”等名称时对应组靠前，不被弱匹配页面/工具淹没；禁用时原因可读且不误执行 |
| 当前页面命令 | 两个同类页面之间切换，提示和执行目标一致；打开 Palette 后发生上下文变化也不串实例 |
| 工具恢复 | 隐藏、自动收起、浮窗三种状态的文案对应实际效果；无额外模式选择 |
| 中文输入与小窗口 | 输入法确认不触发命令；长名称、主题、高 DPI 下重要信息和底部提示不被截掉 |
| 创建失败/异步切窗 | 失败保留查询可重试；等待期间切窗或开启新会话后旧结果不抢焦点 |

项目所有者已于 2026-09-21 确认当前主程序整体手工验收通过，原桌面待办收口。本表保留作后续改动的回归清单；确认没有逐项环境日志，不据此补写输入法型号、屏幕配置或每个矩阵编号的执行结果。

## 9. 通过标准、证据与文档同步

1. Q/K/E/A 每项映射到实际测试方法、参数组合或明确审查记录；未覆盖不能只标“有测试类”。
2. 必需专项的发现/执行数量非零，失败为零，新增必需测试无跳过；其他既有跳过逐项解释。只看退出码不足以判断有效通过。
3. Release 构建、相关警告检查及最终完整 verify 对应最终输入；专项通过不能冒充完整门禁通过。开发 verify 不采集覆盖率，不据此声称满足某个覆盖率比例。
4. 记录源码 HEAD、工作树差异身份、时间、真实命令、退出码、TRX 的发现/通过/失败/跳过数量、Gate run-id 和 summary 路径；失败及重跑各自保留，不拼接成一次通过。
5. 后续实施的实际开发结果放入有日期记录，非嵌入 JSON 保存完整运行证据；文件存在后再加入导航。最终 verify 后再修改嵌入 Markdown，应重新取得受影响输入的验证。
6. 同步方案、专用验证、搜索指南、命令契约、Host 内部设计和导航；将计划行为转换为“当前行为”必须有代码及验证依据。
7. AIFLOW、Windows CI、发布门禁、部署和公开发布均不属于本次完成条件，记录为未执行，不写成失败或已通过。

阶段通过、原失败、重跑和最终门禁分别保存在开发记录与 JSON；以下映射描述自动化覆盖，整体人工确认另见页首记录。

## 10. 实际方法映射与审查结果

以下均是已存在的方法。Unit 类位于 `Host/MyAvaloniaManagement.Tests`，UI 类位于 `Host/MyAvaloniaManagement.UiTests`；参数组合以源码及 TRX 为准。

| 代号 | 类型与实际方法 |
| --- | --- |
| S1 | Unit `CommandPaletteV18Tests.组相关性优先且同组保持连续并以身份稳定打破同名平局` |
| S2 | Unit `CommandPaletteV18Tests.同等级可用项优先但状态刷新保留原顺序而新查询应用新顺序` |
| S3 | Unit `CommandPaletteV18Tests.禁用原因无事实时使用通用说明且命令名称不被推断为完成结果`：预览导出、延后暂停、取消、持续创作四组 |
| S4 | Unit `CommandPaletteV18Tests.四类实际投影包含身份动作和真实原因且重复查询不创建页面` |
| S5 | Unit `CommandPaletteV18Tests.同组弱匹配页面仍连续排列而不会被同名创建入口穿插` |
| X1 | Unit `CommandPaletteV18TargetTests.旧页面约束拒绝且同一共享命令的普通调用仍执行当前页` |
| X2 | Unit `CommandPaletteV18TargetTests.目标相同仍重查业务禁用且不因为已有约束绕过CanExecute` |
| K1 | UI `CommandPaletteV18UiTests.默认跨禁用项选择可用结果且全部禁用和空查询结果不误执行` |
| K2 | UI `CommandPaletteV18UiTests.组标题不增加键盘步骤或执行入口且中文预编辑回车只交给输入控件` |
| K3 | UI `CommandPaletteV18UiTests.状态刷新保留身份顺序和滚动而查询更改采用新的默认项` |
| K4 | UI `CommandPaletteV18UiTests.选中身份在执行前消失时本次回车不执行自动补选项且错误不被底部提示覆盖` |
| K5 | UI `CommandPaletteV18UiTests.页面命令使用显示的目标且关闭面板瞬间切页也不会误执行`：提交前切页、CloseRequested 内切页两组 |
| K6 | UI `CommandPaletteV18UiTests.真实保存携带页面约束而全局入口无页面约束并保留创建意图` |
| K7 | UI `CommandPaletteV18UiTests.同类型两个创建入口按原意图各自新建而不会复用同名已有页面`：link、profile 两组 |
| K8 | UI `CommandPaletteV18UiTests.长名称多实例主题截图保留序号来源和完整可访问目标`：浅色 800、深色 640 宽；均为 Headless DIP |
| R1 | Unit `CognitiveUxV8Tests.查询按名称精确前缀包含说明匹配排序且不做模糊猜测` |
| R2 | Unit `CognitiveUxV8Tests.功能目录与工具目录共用匹配优先级且同名不同来源和意图不合并` |
| R3 | Unit `CognitiveUxV8Tests.同名页面按运行时身份切换且关闭旧项绝不替换成同名新页面`；`页面标题快照刷新但身份序号不变且观察者异常不影响发布释放` |
| R4 | Unit `CognitiveUxV8Tests.页面与工具重复查询不发布通知或改变实例且退出后重新检查`；`功能发现使用创建目录无需Command并且重复查询不初始化页面` |
| R5 | Unit `WorkbenchCommandProjectionTests.Palette普通命令只按菜单许可发现且Host保存以Disabled显示`；`Palette目标成立后按CommandId去重并实时保留业务Disabled`；`Palette重复名称以稳定Id排序且只显示冲突治理后的快捷键` |
| R6 | UI `WorkbenchCommandPresentationUiTests.CtrlShiftP打开聚焦且重复打开保留查询Escape恢复焦点和快捷键`；`原生浮窗使用同一保存命令且面板会话跨窗口互斥` |
| R7 | UI `CognitiveUxV8UiTests.搜索异步失败保留查询选择并阻止重复输入和窗口假取消且重试成功后交还焦点`；`同名页面选择按实例切换并保留编辑内容且消失目标不新建` |
| R8 | UI `ToolCenterUiTests.命令面板工具使用独立身份打开隐藏或自动收起项并记录访问`：新增 Hidden、Docked、AutoHidden、Floating 提示及退出原因断言 |
| R9 | UI `DocumentWindowV16UiTests.N01主窗和两个浮窗的新建互不串组`；`N05初始化或串行门等待期间切窗不改变落点`；`N06创建提交前原组移走或来源关闭按规则回退`；`N10迟到焦点恢复不能覆盖切页关页或新面板会话`；`N11辅助窗口不作为来源且跨窗已有页只定位原实例` |
| R10 | Unit `WorkbenchCommandDocumentTargetTests.关闭先取消并排空Target再释放Scope且拒绝迟到调用`；`HostShutdown取消插件Target并排空全局调用` |
| R11 | UI `WorkbenchCommandPresentationUiTests.工作线程状态变化切回UI线程且释放后不再刷新`；`定向非相关全量通知正确且异常观察者不阻断后续刷新`；`Palette实时隐藏目标并保留Disabled且异常执行只产生脱敏诊断` |
| R12 | UI `WorkbenchCommandPresentationUiTests.文件菜单和CtrlS绑定同一稳定保存命令且设计数据保持纯内存`；`保存菜单随活动目标更新且真实CtrlS只保存当前可持久化Document` |
| R13 | UI `CognitiveUxV8UiTests.百入口长名称与不同主题可滚动且查询不创建插件`；`搜索定位分割区域页面后文档目标和输入焦点一致` |

| 矩阵项 | 实际依据 |
| --- | --- |
| Q01 / Q02 / Q03 | S1、S4、S5、K2；组标题为 XAML TextBlock，不具有候选身份 |
| Q04 / Q05 | S1、S2、S5；明确组最优等级而非逐行全局排序 |
| Q06 | R1、R5；动作前缀保持在 LeadingAction，匹配使用原名称；主程序来源补充显式字段 |
| Q07 / Q08 | R2、R3、R5、K5、K7；不按展示名合并 |
| Q09 | R5、K6；代码审查确认普通命令仍由菜单声明许可，四种身份没有新增 Workflow/局部扫描 |
| Q10 / Q11 | S4、R4、R13、K6、K8；来源使用权威名称或原 OwnerId，不拆词猜测 |
| K01 / K03 | K1、K2、K7；一个输入框与一个结果列表 |
| K02 / K04 | K2、K6、K7、R8；标题和正文双击绑定分离 |
| K05 | K1；双击正文和 Enter 共用 ExecuteSelectionAsync 的禁用检查 |
| K06 / K07 | S2、K3、K4、K5、R7 |
| K08 / K09 | R6、R7、K4；错误行与选中项提示独立 |
| K10 | K1、K8、R12、R13；截图经过人工查看，选中色修复有非透明背景断言 |
| K11 | K8、R13 覆盖文本、主题和小窗口；原开发阶段未执行原生多屏 DPI，后续整体人工确认见页首，未补写逐屏日志 |
| K12 | K2 覆盖公开 PreeditText 事件链；原开发阶段未执行真实 Windows 中文输入法，后续整体人工确认见页首，未补写逐项输入法日志 |
| E01 / E02 / E04 | X1、K5、R7；旧提交和关闭瞬间切页都不执行新实例 |
| E03 | K6、R12；保存属于页面命令，全局入口无预期页面 |
| E05 | X1、R6、R9；预期目标只作为调用值，无共享可变字段 |
| E06 / E07 | R8、R7、R9、R13；工具恢复和页面定位使用原用例；原生平台效果仍见第 8 节 |
| E08 / E09 / E10 | K7、R7、R9；真实 Intent 和原创建落点、失败/迟到回调 |
| E11 / E12 | X1、X2、R5、R10、R12；Executor 的状态重查、租约、取消、排空链保留 |
| E13 / E14 | S3、K6、R8、R11；声明名称不变，布尔状态通知不被解释成具体业务状态 |
| E15 / E16 | R5、R11；原调度器、事件隔离和快捷键冲突治理 |
| A01 / A02 | 展示快照与纯排序、View 会话、Executor 分责；ExpectedTarget 仅 PageId/Revision；R4、R10、X1 验证查询和所有权 |
| A03 / A06 | 代码审查：仅 Host 已知 CommandId 提示映射，插件无 ID 特判；中文注释覆盖排序、快照、调用约束、预编辑和迟到回调 |
| A04 | SDK/版本/schema/CI/Gate 无修改；既有 HostApiBoundaryTests、PublicApiContractTests 和完整 verify 的 contracts 阶段保护 |
| A05 / A07 | R12、K2、K7、K8；设计数据纯内存，常规操作路径无新增步骤；整体人工确认独立留证 |
| A08 | R5、K6、K7；两 Intent 与页面命令用主仓夹具模拟，不引入邻仓或外部业务依赖 |

Headless 输出包括 `palette-v18-light.png`、`palette-v18-dark.png` 以及 V8 长列表、空结果和失败截图。截图路径及 SHA256 由非嵌入 JSON 保存；不把截图、模拟输入或测试名字中的“原生浮窗”当作 Windows 桌面实机执行记录。
