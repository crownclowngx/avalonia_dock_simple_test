# V18：命令面板交互专用开发验证

> 用途：验证四类结果分组、明确动作、当前目标提示和无额外操作负担的命令面板改造。
> 状态：验证规范已建立；V18 功能实施、新增测试和完整开发验收尚未执行。日期：2026-09-20。
> 设计依据：[V18 交互方案](../roadmap/host-v18-command-palette-interaction-plan.md)。当前实际行为见[工作区搜索](../quick-start/workbench-search.md)。本文不授予部署或发布资格。

## 1. 验证范围与执行边界

SOLID、详细中文注释、单一事实源和原有执行路径是首要审查条件。以可观察行为组织单元测试和 Headless UI 测试，不以私有方法名、类数量、文件行数或复制生产实现的断言代替验证。

本次仅交付设计文档。当前只要求文档链接、格式及嵌入帮助相关构建/测试检查；以下 V18 行为矩阵和完整 `verify` 供后续实施执行，未执行不得写为通过。

后续开发允许本机 `dotnet test` 和既有 `verify`。**不使用 AIFLOW、Windows CI、`seal`、发布 Windows Smoke、发布覆盖率、发布重复性门禁，不部署或发布产物。** 本机在 Windows 上运行单元测试或 Headless 测试不等同于 Windows CI；不改动或放宽发布政策。

[Gate 执行图](../../tools/MyAvaloniaManagement.Gate/GateExecutionGraph.cs) 中 `verify` 只包含补丁准备、restore、build、tests、contracts、packages 和 package-acceptance；coverage 与 windows-smoke 只属于 Seal。开发阶段的 MyPlugTest 打包及本地包验收不是公开发布。

## 2. 测试落点和补测原则

实施前逐条核对以下矩阵与现有测试的真实方法、参数组合和断言。下表中的已有测试类是检查入口，不代表它已经覆盖 V18 行为；缺口必须补测。

| 范围 | 已有测试入口 | V18 补测方向 |
| --- | --- | --- |
| 查询、身份、展示与排序 | Unit：`WorkbenchCommandProjectionTests`、`DockWorkspaceNavigationTests`、`PluginNavigationTests`、`ToolCenterTests` | 四组连续、组相关性、同名/多 Intent、不初始化业务、状态原因 |
| 命令状态及执行 | Unit：`WorkbenchCommandContextStateTests`、`WorkbenchCommandDocumentTargetTests`、`WorkbenchCommandCatalogExecutorTests`、`WorkbenchCommandHostHandlerTests` | 当前页提示和提交目标一致、禁用、无副作用失败、共享执行路径 |
| 键盘和生命周期 | UI：`WorkbenchCommandPresentationUiTests`、`UiRefreshSchedulerTests`、`ToolCenterUiTests`、`DocumentWindowV16UiTests` | 非候选标题、跨组导航、稳定选择、目标消失、工具恢复、跨窗及迟到回调 |
| 边界及设计数据 | Unit：`HostApiBoundaryTests`、`PublicApiContractTests`；Plugin：`PluginHostBoundaryTests`；UI：`WorkbenchCommandPresentationUiTests` | public API 不变，生产/设计投影一致，View 不持有业务对象 |
| 帮助文档 | Unit：`HelpContentTests`；UI：`HelpWindowTests` | Markdown 嵌入、读取、相对链接和现有渲染/窗口行为 |

如果现有测试类已过大，可新增主题明确的 `CommandPaletteV18Tests` / `CommandPaletteV18UiTests`，以上名字是建议而非已存在事实。新增后必须核对测试发现并更新过滤器，不能凭 `FullyQualifiedName~V18` 返回成功就认定新测试实际执行。

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

dotnet test Host/MyAvaloniaManagement.Tests -c Release --no-restore -m:1 -warnaserror --filter 'FullyQualifiedName~V18|FullyQualifiedName~WorkbenchCommand|FullyQualifiedName~DockWorkspaceNavigationTests|FullyQualifiedName~PluginNavigationTests|FullyQualifiedName~ToolCenterTests|FullyQualifiedName~HostApiBoundaryTests|FullyQualifiedName~PublicApiContractTests' --logger 'trx;LogFileName=palette-unit.trx' --results-directory "$v18Results/unit"

dotnet test Host/MyAvaloniaManagement.UiTests -c Release --no-restore -m:1 -warnaserror --filter 'FullyQualifiedName~V18|FullyQualifiedName~WorkbenchCommandPresentationUiTests|FullyQualifiedName~UiRefreshSchedulerTests|FullyQualifiedName~ToolCenterUiTests|FullyQualifiedName~DocumentWindowV16UiTests' --logger 'trx;LogFileName=palette-ui.trx' --results-directory "$v18Results/ui"

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

尚未完成的桌面项单独标注，不借用 V17 或 Host 此前整体人工验收作为 V18 的逐项证据。

## 9. 通过标准、证据与文档同步

1. Q/K/E/A 每项映射到实际测试方法、参数组合或明确审查记录；未覆盖不能只标“有测试类”。
2. 必需专项的发现/执行数量非零，失败为零，新增必需测试无跳过；其他既有跳过逐项解释。只看退出码不足以判断有效通过。
3. Release 构建、相关警告检查及最终完整 verify 对应最终输入；专项通过不能冒充完整门禁通过。开发 verify 不采集覆盖率，不据此声称满足某个覆盖率比例。
4. 记录源码 HEAD、工作树差异身份、时间、真实命令、退出码、TRX 的发现/通过/失败/跳过数量、Gate run-id 和 summary 路径；失败及重跑各自保留，不拼接成一次通过。
5. 后续实施的实际开发结果放入有日期记录，非嵌入 JSON 保存完整运行证据；文件存在后再加入导航。最终 verify 后再修改嵌入 Markdown，应重新取得受影响输入的验证。
6. 同步方案、专用验证、搜索指南、命令契约、Host 内部设计和导航；将计划行为转换为“当前行为”必须有代码及验证依据。
7. AIFLOW、Windows CI、发布门禁、部署和公开发布均不属于本次完成条件，记录为未执行，不写成失败或已通过。

本页当前只定义验证规范，不包含 V18 功能的通过记录。文档交付检查的实际结果与后续代码实施证据分别保存。
