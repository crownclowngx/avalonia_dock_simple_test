# V18：命令面板分组与操作意图表达方案

> 实施后说明（2026-09-21）：P0 基线及 P1–P3 实现/专项已完成，当前指南已同步。实际交付与最终开发门禁见[开发记录](../../archive/records/host-v18/development-acceptance.md)；原生输入法和多屏 DPI 留在[待办](../../roadmap/README.md)。以下保留原设计阶段的目标与约束，“尚未实施”“仅文档”表示原方案形成时的状态，不作为当前行为结论。

> 用途：明确命令面板下一轮交互改造、工程边界和实施步骤。
> 状态：方案已由项目所有者确认；本文是设计交付，功能代码、专项补测和开发验收尚未实施。日期：2026-09-20。
> 调研基线：`53ea76521bc5a2ad80fa4c37c28c69c8a788c45a`；实施前重新记录 HEAD 和工作树状态。
> V18 是 Host 改造序号，不代表产品、程序集、SDK、NuGet 或持久化格式版本升级。
> 配套：[V18 专用开发验证](../../maintenance/host-v18-command-palette-verification.md)。当前实际行为仍以[工作区搜索指南](../../quick-start/workbench-search.md)为准，不能将本文的目标行为视为已经交付。

## 1. 目标与首要约束

用户输入“欢迎”时，现有列表交错展示已有页面和创建入口，名称、图标接近，动作却放在最右侧。用户需要反复比较名称、来源和动作，才能判断回车会切换还是新建。目标是在保持“输入 → 上下选择 → Enter”路径的前提下，让目标、动作和影响对象直接可见。

已确认的交互方向：保留一份连续结果列表，按四种结果分组；一行对应一个明确动作；组标题只帮助阅读，不成为新的操作步骤。多个真实实例和多个创建意图分别保留，不按同名折叠。

实施必须遵守以下规定：

1. **SOLID 是首要规定。** 状态、排序、展示、执行和资源所有权的职责必须清晰，不能为了新布局建立第二套命令状态或执行系统。
2. 设计模式朴素使用。复用已有投影、命令适配器和显式组合；允许小型不可变记录、枚举、普通方法或纯排序函数，不引入通用搜索框架、规则引擎、反射扫描、服务定位器或多层策略工厂。
3. 新增或实质修改的核心类型、方法和关键分支使用详细中文注释，解释职责、输入输出、所有权、时序和设计理由，不逐行翻译代码。
4. 单元测试、Headless UI 测试、必要的插件边界回归及本地开发门禁必须齐全；矩阵和实际执行证据分开维护，未执行不得标为通过。
5. 同步维护使用指南、契约、导航和专用开发验证文档。当前文档任务只新增方案及验证规范、更新入口，不修改运行行为。
6. **不使用 AIFLOW**，不读取或更新其上下文、任务记录及更新候选。
7. **不使用 Windows CI 或发布门禁。** 后续实施只运行本地专项与既有 `verify`；不运行 `seal`、发布 Windows Smoke、发布覆盖率或重复性门禁，不修改 CI、不部署安装目录、不上传包。发布阶段再执行当次发布门禁，现有阈值和政策不放宽。

## 2. 工程盘点与类型边界

当前 [WorkbenchPaletteIdentity](../../../Host/MyAvaloniaManagement/Business/Presentation/Commands/WorkbenchPaletteIdentity.cs) 只有 Page、Function、Tool、Command 四种身份。[Palette 投影](../../../Host/MyAvaloniaManagement/Business/Presentation/Commands/WorkbenchCommandPaletteProjection.cs) 汇合四类只读数据，以已声明菜单贡献作为普通命令的发现许可。[执行适配](../../../Host/MyAvaloniaManagement/Business/Presentation/Commands/WorkspacePaletteActions.cs) 区分页面激活、创建入口和工具操作；普通命令沿用共享 Executor。

| 工程事实 | V18 展示和处理 | 不应做的合并或推断 |
| --- | --- | --- |
| 本次运行已发布页面有独立 PageId 和稳定序号 | “已打开的页面”；保留标题、来源、页面序号、修改状态 | 不因相同标题或 Document 类型合并；序号不是文件编号，不跨启动恢复 |
| Function 身份由 DocumentTypeId 和可选 CreationIntentId 共同确定 | “新开页面”；每个合法创建入口一行 | 不把同一 Document 的不同 Intent 合并 |
| BiliDownloader 有“链接下载”“个人内容来源”两个创建入口 | 两个结果均新建，并传入各自原始 Intent | 不根据显示名称重新构造 Intent |
| Tool 可隐藏、停靠、自动收起或位于浮窗 | “工具面板”；分别显示“显示”“展开”“定位”及真实布局状态 | 不将布局状态增加为新结果类型；不重建工具实例 |
| Host 有打开文件、保存、帮助、功能中心、工具中心、插件看板和重启命令 | “命令”；名称和说明体现实际动作，已知入口可给出明确 Enter 提示 | 不把“打开窗口”误标成“新开工作区页面” |
| Workflow Studio、经典游戏、小说插件注册当前 Document 命令 | “命令”；补充“作用于：页面标题 · 页面序号” | 不在每个已开实例下面复制一套命令；不替用户切到其他实例再执行 |
| 小说插件有打开项目、持续创作、暂停、取消、生成和导出预览 | 保留声明的准确动词；以真实 CanExecute 和上下文控制可用性 | “打开小说项目”不是新建页面；“本章完成后暂停”不是立即取消；预览不是完成导出 |
| Workflow Action 有单独注册、参数 Schema、Run、授权及调用治理 | 不增加为 Palette 直接执行结果；继续由 Workflow Studio/原 Consumer 配置调用 | 不把 Action 当作无参数 Command，不建立第二套 Workflow Runtime |
| 下载记录重试、选中项导出、插件开关等是局部 UI 操作 | 继续留在拥有其选择状态的原界面 | 不扫描所有 ICommand 自动暴露到全局面板 |
| 文件、设置、帮助章节、下载任务和业务记录不是现有独立 Palette 数据源 | 已声明的相关入口仍可作为命令或页面出现 | 不扩展为全盘搜索、业务记录检索或任意窗口枚举 |

因此，V18 仍然只有四个主要分组。作用域、布局、运行状态、是否进入选择器是每行的展示信息，不增加模式选择器或第五类可直接执行的工作流结果。

## 3. 用户可见交互

### 3.1 一个搜索框、一份连续列表

不增加前置下拉框、分类 Tab、逐行操作按钮、必须展开的子菜单或必学查询语法。“打开”“切换”等词无需由用户输入；用户搜索对象名称，界面说明执行结果。保留既有上下键、Enter、Esc 和双击路径，不增加完成原操作所必需的按键。

搜索“欢迎”的目标示意如下；名称、序号和来源由当前数据决定：

```text
命令面板
[ 欢迎                                      ]

已打开的页面
> ↩ 欢迎                    主程序 · 页面 1
  ↩ 欢迎主程序              主程序 · 页面 2

新开页面
  ＋ 欢迎                    测试插件
  ＋ 欢迎主程序              主程序

↑↓ 选择    Enter 回到“欢迎”（页面 1）    Esc 关闭
```

组标题无焦点、无候选身份，上下键从上一组最后一条直接进入下一组第一条。不得增加“加载更多”或展开步骤来隐藏本来可以直接选择的创建入口或实例。

### 3.2 每行的信息职责

| 位置 | 展示职责 |
| --- | --- |
| 左侧及主标题 | 动作标记和对象名称。页面使用返回标记，新建使用加号；工具显示“显示 / 展开 / 定位”；命令保留其动作名称 |
| 名称旁边或同一阅读区 | 人能理解的来源、实例序号、当前页标记；不要将唯一的区分信息放到最右侧 |
| 副说明 | 页面修改状态、命令作用对象、真实不可用原因或入口说明 |
| 右侧 | 已治理的有效快捷键及必要状态，不逐行重复“执行命令” |
| 固定底部 | 当前选中项的 Enter 结果和基础键盘提示；操作错误另行保留，不能被帮助文案覆盖 |

文本是主要依据，颜色和图标只辅助。复用现有图标体系，保留无图标回退。来源优先使用已有可信展示名称；没有友好名称时回退到已知来源标识，不能按 PluginId 拆词猜测插件名称。长标题可以省略，但来源和实例标识不能同时消失，选中项底部或可访问名称应保留完整目标。

### 3.3 工具面板

工具的实际布局来自 [ToolWorkspaceReadModel](../../../Host/MyAvaloniaManagement/Business/Workspace/ToolWorkspaceReadModel.cs)，操作继续走原 ToolCenterActions：

| 当前状态 | 标题动作 | Enter 效果 |
| --- | --- | --- |
| 已隐藏 | 显示 文件系统浏览器 | 恢复并聚焦原工具实例 |
| 自动收起 | 展开 文件系统浏览器 | 展开自动收起区域并聚焦原工具 |
| 已停靠 | 定位 文件系统浏览器 | 聚焦已有工具 |
| 浮动窗口 | 定位 文件系统浏览器 | 激活承载窗口并聚焦原工具 |
| 未就绪或不可用 | 保留工具名和真实原因 | 不执行；不能为修复展示而创建实例 |

“显示 / 展开 / 定位”仅是准确描述同一既有工具用例，不增加三套执行逻辑。隐藏工具不等于取消其后台任务。

### 3.4 命令及作用目标

```text
命令
> 运行当前工作流
  作用于：图片处理流程 · 页面 3

  取消当前工作流
  作用于：图片处理流程 · 页面 3 · 当前状态下不可用

↑↓ 选择    Enter 运行“图片处理流程”    Esc 关闭
```

插件 Document 命令只在原发现许可及当前目标成立时出现；保留原来对 OwnerUnavailable、TargetUnavailable 等状态的过滤。当前目标成立但业务 CanExecute 为 false 时保留禁用项。Host 保存也作用于当前页面，不能仅按“Host 注册”认定为全局动作。

已知 Host 入口可用显式、局部的展示映射解释“选择文件…”“打开功能中心”“显示插件看板”等结果。插件命令默认使用其已声明名称、说明和真实目标，不按中文动词、CommandId 片段或描述文字猜测副作用。未提供精确结果语义时，底部使用“Enter 执行：命令名称”，不要承诺尚未发生的保存、导出或任务完成。

`…` 只沿用真实需要后续输入的入口语义，不机械追加到所有命令。重启、文件选择、导出预览继续进入原用例及原交互，不增加或绕过已有确认。Palette 不接管任务进度和业务取消协议。

### 3.5 不可用原因

工具已提供 UnavailableReason，可以直接投影。Host 保存等已知用例可以依据权威状态给出明确原因。现有插件目标只返回 `CanExecute(CommandId)` 布尔值，未提供统一原因契约；只能显示“当前状态下不可用”，不能推测“正在运行”“缺少文件”等具体原因。

本轮不为原因提示扩展 SDK public API。不可用项可以用上下键选中以读取说明，但默认选择优先可执行项；Enter 不执行禁用项，也不偷偷改为执行下一项。

## 4. 分组、排序与选择规则

### 4.1 确定性的分组排序

复用当前纯文本匹配等级：名称精确、名称前缀、名称包含、辅助字段包含。忽略首尾空白和英文大小写，不新增拼音、纠错、自然语言意图识别、使用历史预测或学习排序。新增的动作前缀、底部文案和组标题不替代原 SearchName，也不成为隐式命令语法。

处理顺序固定为：收集原有合法候选 → 匹配原名称与明确的搜索元数据 → 按身份归组 → 排序 → 生成只读展示快照。主程序和插件来源名称可作为明确的辅助搜索字段；结构化展示信息不得通过拼接整段 UI 文案重新反向解析。

1. 空查询时，组序固定为“已打开的页面 → 新开页面 → 工具面板 → 命令”。
2. 非空查询时，每组取组内最好的 MatchRank 作为组相关性；先比较组相关性，再以上述固定组序打破平局。没有匹配项的组不出现。
3. 组内先按 MatchRank，再按同等级可执行优先、原搜索名称和 StableKey 稳定排序。不同组不会因各自名称逐条穿插。
4. 分组连续性优先于把所有结果逐条按 Rank 混排。一组因精确命中排在前面后，其余匹配行仍留在组内；此取舍必须通过测试锁定，不能同时宣称全局逐项严格按 Rank 排序。
5. 如果只有命令“保存”精确命中，而页面只在说明中包含“保存”，命令组先出现；如果两组都精确命中，则固定组序决定先后。仅含禁用项的组仍可展示，默认选择另外的可执行结果。

每条真实候选仅出现一次，不在顶部再复制一份“智能推荐”。正常输入后选择排序结果中的第一个可执行项；全部禁用时选中第一条以显示原因，但 Enter 无效。没有结果时清空选择和 Enter 提示。

### 4.2 状态变化与键盘稳定性

- 用户更改查询时重新排序并采用上述默认选择；仅收到状态刷新时优先保留 StableKey 和滚动位置，不能重选第一条。
- 同一查询期间仅 Enabled、工具布局或标题状态变化时，避免因可用性变化突然搬动正在操作的条目；可执行优先的重新排列在下一次查询/新会话应用。实际新增、删除、重命名则更新结果，并继续按身份保留选择。
- 目标消失时更新列表并说明目标已不可用；触发本次执行的 Enter 立即终止，不把该次按键转交给自动补选项。随后用户可以用新的 Enter 执行明确显示的新选择。
- 命令目标标题、页面身份和上下文代次应由同一次一致快照生成。提交执行前重新核对；在展示之后切到另一实例，不能沿用旧提示却作用于新实例。
- 跨实例目标变化应刷新提示，并拒绝仍携带旧目标约束的提交；不要恢复旧窗口或主动切回旧页面“替用户完成”。全局命令不绑定无关的页面约束。
- 键盘焦点保持现有搜索输入路径；中文输入组合期间用于确认候选字的 Enter 不能同时执行结果。实施时核对 Avalonia 当前输入事件行为，专项和真实桌面分别验证。

## 5. 工程职责与 SOLID 落地

| 原则 | V18 落地要求 |
| --- | --- |
| SRP | Workspace、Catalog、State Query 仍拥有事实；Palette 投影拥有发现、展示元数据和排序；View 只处理渲染、选择与键盘会话；执行仍交给既有用例 |
| OCP | 继续从已有声明发现不同插件的四类贡献；新增业务插件无需在 View 增加插件 ID 分支。已知 Host 入口的少量说明集中声明，不创建通用策略系统 |
| LSP | 生产投影与设计数据实现提供相同分组/选择语义；不同来源同类条目共享同一交互，保留失败、取消、线程与释放约束 |
| ISP | 复用并按需收窄 Host internal 展示接口；View 不获取 Catalog、Document 模型或 Provider；不为每个类机械新增接口 |
| DIP | 状态和执行通过已有窄端口及构造依赖连接；服务解析留在组合根；不把 IServiceProvider、Dock 树或插件对象装入展示记录 |

建议改动落点如下，具体类型名由实施时的最小职责决定，不要求制造额外层级：

| 现有位置 | 拟调整责任 | 必须保留 |
| --- | --- | --- |
| WorkbenchCommandPaletteProjection / Entry | 组标识、动作提示、来源、目标说明、禁用原因；分组排序可提取为纯方法 | 原发现许可、只读查询、UI 调度和唯一事实源 |
| WorkbenchPaletteIdentity | 继续使用四类稳定身份 | PageId、CommandId、ToolTypeId、DocumentTypeId + IntentId，不按名字去重 |
| CommandPaletteView.axaml / code-behind | 组标题、行布局、底部提示及稳定选择 | 一个结果列表，标题不进入候选集合，原焦点恢复、忙状态和错误反馈 |
| WorkbenchCommandPresentationDesignData | 补齐四组、多实例、长文本、禁用和空结果示例 | 纯内存设计数据，不创建插件或运行时 Workspace |
| Workspace / Context 的只读展示边界 | 如现有快照不足，提供标题、实例序号、当前标记和预期目标值 | 只导出值，不暴露 Page/Adapter/模型对象；不复制所有权 |
| Presentation Command / 共享 Executor | 如需要，增加 Host internal 的预期目标校验入口 | 仍调用同一 Executor；预期目标随本次提交传递，不存入共享命令缓存 |

当前 CommandPalette 的普通命令使用共享 ICommand 入口。目标一致性若需要补充能力，应在 Host internal 路径显式传递预期 PageId/ContextRevision 等必要值，并在共享执行链调用真实处理器前重查；不得在 View 直接调用插件目标，也不得把关闭面板后的焦点恢复当作目标验证。

设计中文注释重点解释：为何创建入口不能按标题去重；为何先分组再排序；为何状态刷新保留选择；为何禁用原因只能来自真实状态；为何页面命令需要一致目标快照；为何视图不拥有插件实例；为何迟到异步完成不能抢回用户焦点。注释与测试描述都写具体行为，不使用空泛“遵循 SOLID”替代设计理由。

## 6. 保持的执行与兼容边界

1. SDK public API、产品/包版本、manifest、Document envelope、Layout V3 和插件注册协议不变。
2. 搜索和排序不初始化页面、不执行插件业务、不读文件内容或启动网络请求。查询不得创建新的 Document/Tool/Scope。
3. 普通命令与菜单、有效快捷键共用状态及 Executor；快捷键冲突结果继续沿用已有治理，不显示已失效的快捷键。
4. 新建保留 V16 捕获的来源窗口和文档组；原组消失和窗口关闭时继续采用已有安全回退。已有页激活保留内容、修改状态及承载窗口。
5. 创建期间屏蔽重复提交；成功关闭面板，失败保留查询和错误；普通命令继续沿用原关闭面板后执行时序。预提交校验失败时保留面板，不假装命令已执行。
6. 未保存保护、关闭等待、Owner 可用性、迟到回调、线程调度、观察者异常隔离和资源排空保持。状态展示不改变取消语义或命令生命周期。
7. Workflow Action、局部 ICommand、窗口租约、下载任务和业务记录不扩充为本轮新数据源；外部插件用于核对语义，主仓验证不依赖相邻仓库存在。

## 7. 实施阶段与完成标准

| 阶段 | 工作 | 退出条件 |
| --- | --- | --- |
| V18-P0 | 记录源码/工作树，运行本地开发基线；将专项矩阵映射到已有真实测试，补充行为缺口用例 | 已有回归结果明确；新行为的缺口被真实失败断言定位，不改旧预期掩盖回归 |
| V18-P1 | 四类展示元数据、身份保护、分组排序及设计数据 | 纯查询、排序、多实例、多 Intent 和同名来源测试通过 |
| V18-P2 | 分组列表、底部动作、键盘及状态刷新 | Headless 覆盖连续导航、禁用项、空组、稳定选择、长文本和焦点会话 |
| V18-P3 | 命令目标一致性、工具状态、创建与失败链回归 | 当前实例、跨窗、异步和关闭场景通过，原 Executor/Workspace 责任保持 |
| V18-P4 | 同步当前文档，完成 SOLID/中文注释审查、本地完整 verify 和有针对性的桌面验证 | 专项矩阵逐项有证据；实现、自动测试、人工体验、部署及发布分别标记 |

详细测试、命令和证据格式只在[V18 专用开发验证](../../maintenance/host-v18-command-palette-verification.md)维护。本次文档提交不运行 P0–P4 的完整开发门禁，也不预填实现或测试通过结论。

## 8. 文档同步与验收

本次新增本方案及专用验证，更新总导航、待办、Host 内部入口、主仓验证和相关当前指南的计划链接。当前使用指南和契约正文仍描述现有实现，仅说明 V18 尚待实施。

实施完成时同步：

- `docs/quick-start/workbench-search.md`：分组、排序、默认选择、动作预告、实例及目标说明。
- `docs/reference/workbench-commands.md`：命令目标一致性、状态原因边界和与 Workflow Action 的区别。
- `Host/MyAvaloniaManagement/docs/design/architecture.md`、`design/design-methodology-and-tradeoffs.md`：实际职责变化及取舍；按实际改动更新兼容约束。
- 专用验证的测试映射与实际结果；有日期的开发记录和非嵌入 JSON 单独保留。记录准备就绪后再创建其链接，不留下尚不存在的验收文件入口。
- 本方案完成后按文档惯例归档，提取尚未完成事项；V1–V17 原记录和此前人工验收不倒写。

由于仓库 Markdown 会嵌入 Host，文档定稿后检查本仓链接及标题锚点、运行 `git diff --check`，并重新构建和运行帮助内容/窗口专项。文档检查通过只证明文档交付，不等于 V18 功能实现通过。

## 9. 调研依据

主仓事实源：[搜索指南](../../quick-start/workbench-search.md)、[命令契约](../../reference/workbench-commands.md)、[Workflow Action 契约](../../reference/workflow-actions.md)、[当前页面快照](../../../Host/MyAvaloniaManagement/Business/Workspace/OpenWorkspacePage.cs)、[创建目录](../../../Host/MyAvaloniaManagement/Business/Workspace/DocumentCreationDirectory.cs)、[命令状态查询](../../../Host/MyAvaloniaManagement/Business/Commands/State/WorkbenchCommandStateQuery.cs)、[Command 描述契约](../../../Host/MyAvaloniaManagement.PluginSdk.UI/WorkbenchCommandDescriptors.cs)、[Document/Tool 描述契约](../../../Host/MyAvaloniaManagement.PluginSdk.UI/ContributionDescriptors.cs)。

下列为可选相邻仓库的调研依据，只在相应项目检出时可打开；主仓测试必须使用本仓夹具，不将这些文件变为构建或门禁依赖：

- [BiliDownloader 创建入口](../../../../avalonia_dock_plug_test/myavalonia-bili-downloader/src/BiliDownloaderPlugin.Plugin/Plugin/BiliDownloaderPluginModule.cs)：一个文档类型具有多个创建 Intent。
- [Workflow Studio 注册](../../../../avalonia_dock_plug_test/myavalonia-workflow-studio/src/WorkflowStudio.Plugin/Plugin/WorkflowStudioModule.cs)：验证、运行、取消当前工作流。
- [经典游戏注册](../../../../avalonia_dock_plug_test/myavalonia-classic-game/src/ClassicGamePlugin.Plugin/Plugin/ClassicGamePluginModule.cs)：当前实例的重新开始和撤销，不伪造不支持的动作。
- [小说命令定义](../../../../avalonia_dock_plug_test/myavalonia-novel-generate/src/NovelGeneratePlugin.Plugin/Plugin/NovelCommands.cs)、[执行适配](../../../../avalonia_dock_plug_test/myavalonia-novel-generate/src/NovelGeneratePlugin.Plugin/Features/Main/MainDocument.Workbench.cs)：打开项目、持续创作、延后暂停、取消和导出预览的语义不同。
- [归档 Workflow Action](../../../../avalonia_dock_plug_test/myavalonia-layer-unpack/src/LayerUnpackPlugin.Plugin/Workflow/ArchiveWorkflowActions.cs)：带参数的独立工作流能力不等同于用户命令。
