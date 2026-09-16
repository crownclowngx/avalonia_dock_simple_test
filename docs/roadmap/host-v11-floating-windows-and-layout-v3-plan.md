# MyAvaloniaManagement V11：浮动窗口恢复与 Layout V3 实施计划

> 用途：约束主程序浮动窗口、工具布局保存恢复及配套验证的实施范围。状态：计划待实施；本次仅编写计划并同步导航，没有实施下列 G 阶段。编写日期：2026-09-16。
> 核查基线：`ec8f0ae`；已结合 `9bceb07` 文档重组、V10 开发完成后的目录与源码重新核查，不沿用旧 `docs/design`、`docs/plan-history` 路径或早期 Host 生命周期假设。
> 用户已确定：工具浮窗恢复位置和分组；Document 支持运行时浮动，重启不自动重开。本文中的 V11 是改造顺序，Layout V3 是独立磁盘协议版本，均不代表产品或 NuGet 包升到相同版本。
> 首要规定：满足 SOLID；朴素使用设计模式；新增及实质修改的核心代码使用详细中文注释，解释设计思路、所有权和失败语义；单元测试、集成测试、本地开发门禁及专项文档齐全。
> 不使用 AIFLOW，不初始化、读取或维护 `.aiflow` 流程上下文。本轮不使用 Windows CI、发布门禁、`seal`、发布 Windows Smoke、发布覆盖率或发布重复性流程，不上传包、不打发布标签、不部署用户安装目录。发布阶段另行执行相应流程。

## 1. 目标、范围与完成定义

### 1.1 用户能够观察到的结果

1. Tool、Document 及其合法标签组可以拖出为独立桌面窗口，并能拖回主窗口或其他兼容停靠目标。
2. 浮动期间复用同一个业务模型、View 和 Document Scope；下载、编辑等会话状态不因移动窗口而重建。
3. 工具中心正确区分停靠、浮动、自动隐藏和隐藏；页面列表仍能定位浮动 Document，重复打开同一文件激活已有实例。
4. 浮窗中的保存快捷键、命令面板、文件选择、关闭确认和内容全屏遵守同一工作台语义。
5. 正常退出后，下一次启动恢复工具分组、顺序、显隐、浮窗位置和尺寸；Document 不自动重开，纯 Document 浮窗不恢复。
6. 显示器断开、缩放改变、插件暂不可用或布局文件损坏时，工作台仍可进入可操作状态，原有效布局有恢复依据。

### 1.2 范围边界

| 项目 | 本轮决定 |
| --- | --- |
| 浮动类型 | 使用独立桌面浮窗；不新增主窗口内部模拟浮窗模式 |
| 工作区骨架 | 主窗口 Root、Workspace 与稳定主文档入口不能被整体拖走；允许实际内容与合法组浮动 |
| Document 持久化 | 保持现有 Document 文件及修订保存语义；布局中不保存文档 ID、文件路径、标题、内容或重开清单 |
| Tool 持久化 | 保存稳定工具身份、布局结构和恢复位置，不保存业务 payload |
| 普通辅助窗口 | 工具中心、功能中心、插件看板、帮助窗口不成为 Dock Tool，也不纳入工具布局树 |
| 自动隐藏 | 保留主窗口四向自动隐藏；浮窗本轮不提供自动隐藏入口，先停靠回主窗口再使用该功能；从自动隐藏状态浮动时先退出该状态并保留主窗口回停位置 |
| 插件与 SDK | 默认不修改 public SDK、不要求业务插件统一重编译；Dock 继续是 Host 内部实现 |
| 框架和版本 | 基于当前 Avalonia `12.1.2`、Dock `12.1.0.6`；本轮不同时升级框架、产品或六包版本 |
| 数据根 | 继续使用当前 `v2` 数据根及 `MYAVALONIA_DATA_DIRECTORY` 完整目录覆盖语义；只升级布局文件格式 |
| 产品扩展 | 不增加布局云同步、多套命名布局、布局分享、Document 会话恢复或热卸载 |

### 1.3 完成定义

实现完成、自动验证完成、真实桌面验收完成分别记录。G0–G8 的必需本地自动验证全部通过后，才可标记开发自动化完成；真实拖动、跨屏和原生资源未验收时必须明确保留待办，不能把 Headless 结果写成完整桌面功能通过。开发完成不授予发布资格。

## 2. 当前源码事实与改造影响

| 当前入口 | 已核查事实 | V11 必须处理 |
| --- | --- | --- |
| [HostDockFactory](../../Host/MyAvaloniaManagement/Business/Docking/HostDockFactory.cs) | 四个 Float override 是空实现；根能力禁止 Float；已注册原生 HostWindow Locator；Tool 关闭默认隐藏 | 恢复框架协议，纳入浮窗创建、关闭和状态通知；不能只删除一个开关 |
| [ManagedDocumentDockable](../../Host/MyAvaloniaManagement/Business/Docking/ManagedDocumentDockable.cs)、[ManagedToolDockable](../../Host/MyAvaloniaManagement/Business/Docking/ManagedToolDockable.cs) | 两个 Adapter 均设置 `CanFloat=false`；Document 有运行期 PageId，Tool 使用稳定贡献 ID | 同步能力策略，保留模型和 View 所有权；PageId 不转成跨启动身份 |
| [DockWorkspaceBuilder](../../Host/MyAvaloniaManagement/Business/Layout/DockWorkspaceBuilder.cs) | 外层 Root 与内层 Workspace 均应用禁浮动策略 | 区分固定主布局骨架和可浮动内容；新浮窗根也要具有一致能力 |
| [DockTreeNavigator](../../Host/MyAvaloniaManagement/Business/Layout/DockTreeNavigator.cs) | 主要沿 VisibleDockables 递归，不统一处理各 Root 的 Windows | 建立按窗口限定与整个工作区两种明确查询，覆盖隐藏、Pinned、浮窗根并防重复 |
| [ToolDockCoordinator](../../Host/MyAvaloniaManagement/Business/Layout/ToolDockCoordinator.cs) | 恢复工具依赖原 Owner；Top/Bottom 归一化依赖稳定主布局节点 | 浮窗不能误走查找 WorkspaceRows/Columns 的主窗口分支；保留真实浮窗分组 |
| [WorkspaceSession.Pages](../../Host/MyAvaloniaManagement/Business/Workspace/WorkspaceSession.Pages.cs)、[ToolWorkspaceReadModel](../../Host/MyAvaloniaManagement/Business/Workspace/ToolWorkspaceReadModel.cs) | 页面存在性、激活、工具显隐依赖主树；现有工具状态只有 Hidden/Docked/AutoHidden | 更新跨窗定位与浮动状态，避免页面消失、误隐藏或重复创建 |
| [MainWindow](../../Host/MyAvaloniaManagement/Views/MainWindow.axaml.cs) | 快捷键、命令面板、内容全屏与布局保存挂在主窗口；Opened 应用布局，Closing 保存 | 提取可复用窗口协作者，并在退出拆除浮窗前捕获布局 |
| [DocumentCloseCoordinator](../../Host/MyAvaloniaManagement/Business/Documents/DocumentCloseCoordinator.cs) | 同步取消、异步保存、一次性关闭许可和命令排空已经存在；窗口确认按应用退出表达 | 增加有范围的浮窗关闭协调，不能直接把关闭子窗当作应用退出 |
| [HostRuntimeShutdown](../../Host/MyAvaloniaManagement/Business/Composition/HostRuntimeShutdown.cs)、[HostShutdownParticipants](../../Host/MyAvaloniaManagement/Business/Composition/HostShutdownParticipants.cs) | 记录已创建参与者；先停止入口并等待 Command/Document/Workflow，再安全释放或保留资源 | 沿用重构后的唯一关闭所有者；不从浮窗直接 Dispose Runtime/Provider，也不在失败回滚时解析新服务 |
| [DocumentControlRecycling](../../Host/MyAvaloniaManagement/Business/Docking/DocumentControlRecycling.cs)、[指针保护](../../Host/MyAvaloniaManagement/Behaviors/DockTabPointerCaptureGuard.cs) | 已处理正文单实例、同 Presenter 复用、捕获移交与残留状态 | 保留近期修复，补跨 TopLevel 迁移和释放回归，不删除保护层换取浮动 |
| [布局生命周期](../../Host/MyAvaloniaManagement/Business/Layout/DockLayoutLifecycle.cs)、[Mapper](../../Host/MyAvaloniaManagement/Business/Layout/DockLayoutSnapshotMapper.cs)、[Store](../../Host/MyAvaloniaManagement/Business/Layout/DockLayoutStore.cs) | V2 只保存四向 Pane 与 Tool；保存已有原子事务；未知或不可用工具会拒绝整份布局 | 引入严格 V3、只读 V2 转换、可用项恢复和自动保存；旧 Store.Load 具有隔离副作用，不能直接作为只读迁移入口 |
| [Directory.Version.props](../../Directory.Version.props)、[VersionPolicyTests](../../Host/MyAvaloniaManagement.PluginTests/VersionPolicyTests.cs)、[GateChecks](../../tools/MyAvaloniaManagement.Gate/GateChecks.cs) | 含 V2 文件名及版本断言；发布 Smoke 也硬编码 V2 | 更新当前布局事实与开发契约测试；发布 Smoke 适配作为发布前待办，不在开发阶段运行 |
| [Host 项目](../../Host/MyAvaloniaManagement/MyAvaloniaManagement.csproj) | `docs/**/*.md` 和 Host 内部文档嵌入程序 | 文档改变也会改变 Host 产物；最终验证应在文档定稿后进行，V10 旧产物证据不自动适用于 V11 |

当前细节以 [Layout V2 契约](../reference/dock-layout-snapshot-v2.md)、[内部架构](../../Host/MyAvaloniaManagement/docs/design/architecture.md)、[设计取舍](../../Host/MyAvaloniaManagement/docs/design/design-methodology-and-tradeoffs.md)和源码为准。本文描述的 V3 均为拟实施目标，不提前改写当前契约。

## 3. SOLID 与实现规范

SOLID 是首要验收项。不得用一个新的万能 WindowManager、LayoutManager 或服务定位器吞并已经拆开的职责。

| 原则 | 具体规定 | 验收依据 |
| --- | --- | --- |
| SRP | Session 管实例；Factory 适配框架；窗口协作者管窗口寿命；Mapper 管结构；Validator 管约束；Store 管 I/O；保存调度只管合并与提交时序 | 构造依赖与方法职责可解释，各失败边界能独立测试 |
| OCP | 插件工具按稳定贡献 ID 进入布局；新增工具无需改窗口代码；平台窗口与屏幕差异放在适配边界 | 不出现按业务插件名称分支；未知工具有明确兼容语义 |
| LSP | 恢复 Float 后保留 Dock 基类通知和关闭取消协议；隐藏 Tool 不释放模型；移动 Document 不触发最终关闭 | 单项/整组、取消/重入、浮动/回停共用行为测试 |
| ISP | 只在实际平台、I/O、调度或窗口交互边界使用窄接口；窗口不获得整个 Runtime/Provider | 不增加通用 GetService/Resolve 或全局静态 Current |
| DIP | 布局校验、屏幕位置计算、迁移、空节点清理依赖数据；窗口、文件、调度在组合根注入 | 纯数据单元测试不启动真实桌面，不访问业务插件 |

### 3.1 朴素设计与职责分配

- 沿用构造器注入、MVVM、Host Adapter、记录类型、枚举和普通协作者；不引入事件总线、规则引擎、通用状态机库或通用事务框架。
- 优先扩展已有职责清晰的类型。确需新增的边界限于浮窗生命周期、窗口交互绑定、布局保存调度和纯数据位置/结构策略；名称可按实施中的实际依赖调整。
- `WorkspaceSession` 仍唯一拥有 Document/Tool 集合；窗口映射仅登记 Window 与窗口 ID，不建立第二份业务实例目录。
- `HostDockFactory` 不读取 JSON、不直接写文件、不创建第二个 DI Scope；窗口不自行释放插件 Provider。
- 保留主窗口局部结构查询。不得把所有 `FindDockById` 无差别改成全工作区搜索，否则可能把主窗口的稳定停靠点解析到浮窗。
- Host 与插件窗口端口保持既有 SDK 形状；平台 Owner 的选择在 Host 内部完成。没有必要不修改 public API 文本或包依赖。

### 3.2 中文注释要求

新增及实质修改的核心类和入口使用中文 XML 注释；复杂分支添加中文行内注释。注释至少解释：

1. 为什么存在这一边界，它拥有和借用什么对象。
2. 哪些操作必须在 UI 线程，什么数据允许交给后台写入。
3. 浮动是视图迁移、隐藏是保留实例、关闭是最终释放，三者如何区分。
4. 关闭取消、重复回调、保存失败、恢复回滚和迟到任务如何保持一致。
5. 像素与逻辑尺寸如何换算，旧格式为何只读，缺失工具为何保留记录。

注释说明设计思路和不变量，不逐句翻译代码；调整行为时同步删除“全部禁止浮动”“只存在主窗口”等过时表述。

## 4. 浮窗运行时与关闭协议

### 4.1 工作区遍历与工具恢复

按窗口返回 Root、可见节点、隐藏项和自动隐藏项；工作区查询再合并各 Root 的 Windows，使用引用去重防止回边或重复枚举。不要沿 Owner/OriginalOwner 任意递归。

工具浮动后，原主窗口停靠位置与当前浮窗位置分别维护。关闭工具浮窗隐藏组内工具；窗口无可见内容时释放窗口对象，但保留工具的结构恢复记录。重新显示一个工具时只显示它，其他隐藏工具继续隐藏；已存在原浮窗时加入原组，否则按保存位置重建窗口。回停位置失效时回退到主窗口对应方向的稳定 ToolDock。

Top/Bottom 归一化只作用于需要稳定四向结构的主窗口；浮窗使用自己的合法分割树。主文档入口全部浮动后仍保留可接收新文档、可拖回内容的中心停靠目标。

### 4.2 窗口交互

- 浮窗使用 Dock 自带窗口与 Factory 扩展点，由 Host 统一创建、注册、激活和退订；不机械恢复 V9 已删除的旧 HostWindow 样式。
- 点击工具中心或页面列表时，先还原并激活实际承载窗口，再设置该窗口根的活动项与焦点。工具获得焦点不擅自替换已有文档命令目标。
- 主窗口和浮窗共用现有 Command Catalog/Context/State/Executor，每个窗口拥有独立 KeyBinding 实例和订阅；一次按键只能执行一次。
- 命令面板在调用窗口显示，同一时刻只维持一个工作台面板会话；关闭后恢复该窗口焦点，避免并发面板共享交互状态。
- [Host 文件选择](../../Host/MyAvaloniaManagement/Business/Storage/AvaloniaHostStorageService.cs)、[插件窗口交互](../../Host/MyAvaloniaManagement/Business/Presentation/AvaloniaPluginWindowInteraction.cs)、[关闭对话框](../../Host/MyAvaloniaManagement/Business/Documents/DocumentInteractionService.cs)统一采用 Host 内部 Owner 选择规则：有明确目标时使用目标窗口，否则使用当前活动工作台窗口，最后回退主窗口。开始异步交互时固定 Owner；目标关闭后不向失效窗口提交结果。
- 浮窗接入既有 [内容全屏租约](../../Host/MyAvaloniaManagement/Business/Presentation/WindowContentFullscreenSession.cs)。有活动租约时暂不允许迁移该内容，先退出全屏再浮动/回停；最终关闭仍幂等释放。不能将独立窗口全屏和插件内容全屏混为同一状态。
- 原生视频、WebView 等跨窗口迁移是单独验收场景。普通 View 测试不能证明原生句柄迁移正确；若发现问题，记录具体插件与控件边界，不默认强制重建业务模型规避。

### 4.3 有范围的关闭

| 动作 | 应有结果 |
| --- | --- |
| 关闭单个 Tool 或纯 Tool 浮窗 | 隐藏目标工具，保留模型及恢复位置；不停止后台业务 |
| 关闭单个 Document | 沿用保存修订、取消、命令排空、一次性许可与幂等释放 |
| 关闭含多个 Document 的浮窗 | 固定本次目标集合；先汇总确认和保存，再排空目标命令，全部允许后提交关闭 |
| 关闭过程中取消或保存失败 | 在提交前保持整组页面和工具；撤销本次关闭许可与 closing 状态，恢复命令入口 |
| 关闭主窗口 | 覆盖全部窗口中的 Document，完成确认及关闭协调；捕获退出布局后关闭浮窗，再交回 Runtime 原有关闭链 |

浮窗关闭不能调用表达 `isApplicationExit=true` 的主窗口确认路径冒充应用退出。范围关闭需兼容无脏数据但命令仍在运行的 Document；取消关闭不得提前取消最终生命周期令牌或释放 Scope。

目标集合变化、另一个关闭请求和迟到回调通过同一协调入口串行处理。保存成功的文件不会因后来用户取消整组关闭而回滚；“保持整组”指页面与实例尚未拆除。若框架在最终提交中异常或拒绝部分操作，报告实际结果并保留未关闭实例，不能宣称已释放对象可被事务回滚复活。

主窗口确认取消时，浮窗保持可用；主窗口正常退出时，不因“最后一个浮窗仍开着”留下无主窗口进程。不得绕开 `HostRuntimeShutdown` 对在途业务和资源保留的判断。

## 5. Layout V3 数据设计

### 5.1 文件与版本

- 新主文件：`layout-v3.json`，`schemaVersion=3`；上一份有效文件采用同目录 `layout-v3.json.bak`。
- 默认仍在 `%LOCALAPPDATA%\MyAvaloniaManagement\v2\`；覆盖环境变量仍表示完整数据根。
- `layout-v1.json` 不读取、不迁移、不覆盖、不隔离；V2 仅作为首次升级的只读输入。
- manifest 与 Document envelope 保持 schema 2。版本属性应以明确的当前 Layout 属性表达 3；不能把名为 `MyAvaloniaV2Layout*` 的属性直接填成 3 制造语义错位。旧属性是否保留仅依据实际消费者，不能为此扩张 SDK。
- 不直接序列化 Dock 运行时对象、CLR 类型名、Context、View、Provider 或插件 DTO。

### 5.2 最小结构模型

采用有界、强类型的布局记录。以下是字段职责，最终精确 JSON 字段集合与示例在 G1 冻结到 V3 专项契约；不得先依赖宽松反序列化再补校验。

| 数据 | 必需语义 |
| --- | --- |
| 根记录 | schemaVersion、主窗口布局、浮窗记录、工具状态；写入顺序确定 |
| 主窗口 | 正常位置/尺寸、最大化状态、工具布局及一个稳定的主文档区域占位 |
| 布局节点 | 有限类型：分割、工具标签组、主文档区域占位；分割保存方向和子项比例；工具组保存稳定组 ID、有序工具 ID 与活动工具 ID |
| 浮窗 | 稳定窗口 ID、正常位置/尺寸、最大化状态、所属工具布局；浮窗不保存文档区域占位 |
| 工具状态 | 稳定贡献 ID、显示/隐藏/自动隐藏状态，以及主窗口回停位置；当前组由工具组的有序成员列表唯一表达 |
| 恢复位置 | 窗口/组身份、顺序、主窗口方向；允许保存已全部隐藏组的结构，重新显示时使用 |
| 屏幕参考 | 保存时屏幕工作区、缩放和可用匹配信息；只作为恢复提示，不将显示器标识视为永远稳定 |

一个工具只能有一个权威归属位置；工具组有序 ID 列表表达顺序，工具状态不再维护另一份可冲突的当前组映射。回停位置是备用位置，不参与当前占用判断。组和窗口 ID 由 Host 分配并随布局保留，不依赖标题或本次 Dock 自动生成的临时 ID。

主窗口占位只表示“保留一个可接收文档的中心区域”，不保存当前文档数、文档拆分、PageId 或恢复路径。Document 的任意分割在本轮不跨启动恢复。工具与文档共存的结构只投影工具和必要的主窗口占位；纯文档浮窗被删除，空分割归并并重新归一化比例。

全部隐藏或暂不可用工具的结构可以保存在快照中，但恢复时不创建无可见内容的原生窗口。由此区分“保留恢复记录”和“显示空白浮窗”。

### 5.3 严格校验

必须在修改真实 Dock 树前验证：

- 精确字段集合、类型、枚举、版本；拒绝重复/未知/缺失字段、大小写错误、注释及尾随逗号。
- 窗口/组/节点身份唯一、引用有效、工具不重复占位、节点无环、父子关系合法。
- 数字有限，尺寸为正，比例合法；分割比例归一化。主文档占位只在主窗口且恰好一个。
- 活动工具属于相应组并可见；自动隐藏只适用于主窗口合法方向，不与隐藏或浮动状态冲突。
- 文件大小、节点深度、窗口/节点/工具数量有合理上限；具体上限在 G1 固定并覆盖边界测试。
- 多屏坐标允许负数，不能把负 X/Y 当坏文件；合法但位于屏幕外的坐标由位置策略修正。
- 缺插件属于运行时可用性问题，不能放宽损坏 JSON、重复身份或非法结构的校验。

## 6. 迁移、备份与可用项恢复

### 6.1 读取优先级与旧版回退

1. V3 存在时优先严格读取；主文件缺失或损坏但存在 V3 备份时，先检查并使用有效 V3 备份。
2. 首次尚无 V3 及其历史恢复证据时，才读取并校验 V2，将四向 Pane、顺序、显隐、Pinned 和活动项转换成 V3；已有退役内置工具规则在转换中继续精确应用。
3. V2 原件保持字节不变，读取失败也不对它重命名或覆盖；不能直接调用具有 Quarantine 副作用的旧 Store.Load。
4. 已升级后 V3 损坏且无有效备份，保留坏文件并使用默认布局；识别已有 V3 的备份/隔离记录，避免每次启动重新导入过期 V2。
5. 没有历史布局时沿用默认布局，工具默认隐藏。V3 写入成功后不再双写 V2。

旧 Host 继续读取原 V2，所以回滚得到升级前的工具布局；V11 中的新增分组和浮窗不会反向同步给旧版本。遇到更新程序写入的未知未来 schema，保留文件并以只读默认布局启动，不把它认作损坏文件或覆盖成 V3。

### 6.2 损坏与暂时不可用分开处理

结构损坏保留 `.invalid.bak` 诊断副本；先校验备份，再恢复或使用默认值。加载和隔离操作也遵守文件写入归属；只读实例不隔离、不修复、不覆盖文件。

与当前 V2 “缺一个工具拒绝整份布局”不同，V3 对格式合法但未安装、生命周期不可用或激活失败的工具保留原始布局记录，只恢复当前可用项。临时移除这些项得到运行时有效树，纯空窗不显示；正常保存将保留项与用户实际修改后的可用项合并，不能把没创建出来的工具当作用户主动删除。

保留项不创建模型、不调用不可用插件、不持有 Provider。按稳定 ID 去重，已退休的已知内置工具按明确迁移规则删除；恢复失败和用户明确重置布局是不同操作。下次插件重新可用时恢复其保留位置。

### 6.3 文件事务与多实例

复用 [AtomicFileTransaction](../../Host/MyAvaloniaManagement/Business/Storage/AtomicFileTransaction.cs) 的同目录写入、刷新与原子替换。备份更新、主文件提交和失败清理需有明确顺序，不把单文件原子写误称为跨文件事务；只用已验证的有效文件更新 `.bak`。任何写入失败至少保留上一份有效主文件或备份，首次迁移失败继续保留 V2。

每个规范化数据根只允许一个布局写入者，可用该目录专用锁文件的独占句柄表达；锁仅约束布局，不限制第二个 Host 的业务运行。未取得锁的实例读取快照并保持本次会话布局，不自动接管，也不保存、迁移或隔离。进程退出释放句柄，遗留锁文件本身不代表仍被占用；同进程双实例、跨进程竞争与崩溃后释放均需验证。

## 7. 自动保存、恢复事务与屏幕位置

### 7.1 保存时机与竞态

- 在浮动/回停/分组/标签顺序/工具显隐/自动隐藏、分割比例、窗口移动/尺寸/最大化状态真正提交后标记变化。
- 用约 750 ms 的合并延迟保存；拖动进行中、恢复中、布局重置事务中不捕获半成品。初始值可按交互验收微调，测试使用可控调度，不依赖真实 Sleep。
- UI 线程捕获不可变快照，后台只做序列化与 I/O；后台线程不得读取 Dock、Control 或 Screens。
- 单一写入队列按递增修订提交；新快照可合并尚未开始的旧任务，已运行任务与后续任务串行，避免旧结果覆盖新状态。
- 原生窗口在 Normal 时更新正常位置和尺寸；最大化单独记标记，最小化不覆盖正常值，也不跨启动恢复最小化状态。
- 保存错误记录脱敏诊断并显示一次可理解的非模态提示；布局仍能使用，下次修改可重试，不能无限弹窗或静默宣称成功。

### 7.2 退出顺序

主窗口退出请求先完成所有窗口的确认与操作协调；确认取消则恢复正常使用，不提前关闭工具或浮窗。批准退出后停止新的布局变化，捕获并提交最终快照，再拆除原生浮窗；拆除阶段的 Hidden/Closed 通知不再触发保存。

最终写入采用异步完成后继续关闭，避免 UI 线程阻塞等待需要 Dispatcher 的工作；有界失败或写入错误报告为未保存本次布局，保留上一有效文件，不改变文档保存判断。保存任务、事件订阅和窗口对象的清理由实际创建者配对执行；Runtime 失败回滚不创建窗口或临时解析存储服务。

### 7.3 恢复事务

顺序：获取布局读写模式 → 严格读取与迁移 → 计算可恢复投影 → 创建默认工作区和唯一实例 → 主窗口及屏幕信息就绪 → 应用结构 → 创建非空浮窗 → 校正位置 → 统一显示并恢复活动工具 → 解除保存抑制。

应用之前完成结构验证；应用中窗口创建或挂载失败，关闭本次已创建的临时窗口并恢复工具到安全主布局。不能让半成品窗口成为已保存状态，不能以重新创建全部插件模型来回滚。启动失败和运行期重置分开处理，后者必须保留已打开 Document 的实例与修改状态。

### 7.4 多屏和恢复入口

位置数据明确区分屏幕像素坐标与逻辑尺寸；保存屏幕参考，恢复时按当前工作区和缩放换算。优先匹配原屏幕，其次按交叠/最近位置选择，最后回退主窗口所在屏幕。限制最小和最大尺寸适应小屏幕，确保标题栏及主要客户区可见；处理负坐标、任务栏工作区、显示器移除和 DPI 变化。

新增 Host 命令并接入现有 Catalog/Projection：

- **找回屏幕外窗口**：仅移动不可见浮窗到当前可见工作区，不重置分组或业务内容。
- **重置布局**：先做局部确认，备份当前有效布局；已打开 Document 原实例移回中心区，工具恢复默认隐藏，移除浮窗与旧工具恢复位置，再保存新布局。该确认针对用户布局重置，不是开发执行的额外审批。

## 8. 分阶段实施清单

阶段按依赖顺序推进，普通类名、测试组织和内部拆分由实施者决定。不得跳过跨窗口与关闭保护而先交付裸 Float 开关。

### G0：核实框架与建立开发基线

- [ ] 记录实施时 HEAD、工作树状态、实际框架版本，复查本文源码入口是否漂移。
- [ ] 运行完整本地 verify，建立本轮自己的基线，不复用 V10 测试数量或产物报告。
- [ ] 对锁定 Dock 版本核实 Float/FloatAll、窗口根登记、原生关闭回调、拖动事件和 InitLayout 时序，以最小 Host 测试证明，不只引用最新版在线示例。
- [ ] 新建 V11 专项开发记录，明确实现、自动化、真机与发布状态。

通过条件：基线结果可追溯，框架扩展点和关键关闭时序有事实依据；如需框架升级，单独列明范围变化，不混入本轮默认方案。

### G1：冻结 V3 契约与纯数据规则

- [ ] 新建 V3 专项契约，确定字段集合、ID、比例、数量上限、缺失项保留、坐标与隐藏组恢复语义。
- [ ] 实现 DTO、严格 JSON、纯校验、V2 只读转换和过滤文档后的树归并。
- [ ] 建立正常、坏格式、旧格式、隐藏浮窗和缺失工具夹具及往返测试。

通过条件：无需 UI 即可证明 V2→V3 和 V3 往返，Document 内容不进入线格式，旧文件保持原样。

### G2：跨窗口工作区查询与工具协调

- [ ] 区分主布局局部查找和全工作区查询，覆盖 Windows/Hidden/Pinned。
- [ ] 更新工具状态、页面列表、文件激活、Owner 判断和活动文档发布。
- [ ] 修正 Top/Bottom 归一化的作用域及工具回停、隐藏分组恢复。

通过条件：内存多窗口夹具中的工具与页面不丢失、不重复，主骨架不会误解析为浮窗节点。

### G3：浮窗生命周期与关闭安全

- [ ] 开放内容及合法组浮动，创建 Host 浮窗适配与窗口登记。
- [ ] 完成范围关闭、整组确认、目标命令排空、取消恢复和一次性关闭许可。
- [ ] 接入主窗口退出与现有 Runtime 关闭链，保持幂等释放和失败保留语义。
- [ ] 补 View 迁移、事件退订、缓存与 Scope 释放测试。

通过条件：浮动/回停复用原实例；取消不拆组；关闭子窗不结束 Runtime；关闭全部窗口无残余订阅与错误释放。

### G4：跨窗口命令与原生交互

- [ ] 复用窗口快捷键、命令面板、Owner 选择与全屏租约逻辑。
- [ ] 工具中心和页面列表激活实际承载窗口，最小化时先恢复。
- [ ] 验证指针捕获、Esc、失活、拖出再拖回和窗口主题，不回退近期正文复用修复。

通过条件：浮窗命令目标正确、一次触发一次执行、窗口关闭后的迟到通知不重新绑定失效对象。

### G5：持久化、自动保存与恢复事务

- [ ] 接入 V3 Store、备份、独占写入、只读实例和未来 schema 保护。
- [ ] 接入保存调度、UI 快照、串行写入和退出前最终提交。
- [ ] 接入可用项投影恢复、保留项合并、非空浮窗显示与失败回滚。
- [ ] 更新当前 Layout 版本属性、VersionPolicyTests 与实际受影响的开发契约引用。

通过条件：重启工具布局稳定，文档不重开，旧数据保留，文件故障和实例竞争不会覆盖有效布局。

### G6：屏幕适配与用户恢复入口

- [ ] 实现并测试位置计算、DPI 换算、正常尺寸/最大化状态。
- [ ] 接入找回窗口与重置布局命令，保留文档实例和修改状态。
- [ ] 进行普通本地真实桌面验收并记录环境；不能运行发布 Smoke 代替交互观察。

通过条件：位置规则自动测试通过；真实多屏、鼠标和原生资源结果按已验收/待验收分别记录。

### G7：回归与本地开发门禁

- [ ] 更新现有禁止浮动测试和快照断言，增加第 9 节全部必要行为测试。
- [ ] 新增测试进入现有本仓工程和 verify 范围；更新 Gate 时独立运行工具自测。
- [ ] 运行完整 verify，验证 SDK 边界、MyPlugTest 真实 ZIP、Host Unit/Plugin/Headless UI 和既有 V10 功能。

通过条件：要求执行的自动验证无失败、无意外跳过或零测试，不降低已有断言、警告策略和发布阈值。

### G8：同步专项文档与开发交接

- [ ] 按第 10 节同步当前指南、专项契约、验证说明、Host 架构和 V11 实际记录。
- [ ] 检查全体改动文档的文件链接、标题锚点、帮助嵌入和状态描述。
- [ ] 文档定稿后运行最终验证；将最终产物身份写入非嵌入证据摘要，避免修改嵌入 MD 后继续使用旧产物身份。
- [ ] 在 roadmap 合并未完成真机/业务事项，并登记发布 Smoke 的 V3 适配及实际发布门禁待办；归档计划时重算链接。

通过条件：实现、开发自动化、真实桌面、外部业务、部署、发布六类状态分别可追溯，没有预填通过或隐藏未验收项。

## 9. 单元测试、集成测试与本地门禁

### 9.1 行为验收矩阵

| 编号 | 必测行为 | 主要承接位置 |
| --- | --- | --- |
| T01 | 单 Tool、Document、整组浮动及回停；骨架不浮动；原分组可继续接收文档 | HostDockAdapterTests、WorkspaceSessionAndDockFactoryTests、替换 DockFloatingDisabledTests |
| T02 | 多 Root/嵌套 Windows/Hidden/Pinned、去重与局部查找；同名页面稳定身份 | Navigator 专项单元测试、ToolCenterTests、页面测试 |
| T03 | 四向停靠、浮窗内 Top/Bottom 分割、组顺序、隐藏最后工具后重建原窗 | DockFourWayLayoutTests、ToolCenterUiTests |
| T04 | 浮窗中文档重复文件打开、页面列表激活、最小化恢复、活动工具不覆盖文档目标 | Workspace 与 WorkbenchCommandDocumentTargetTests、Headless UI |
| T05 | 同一 View/模型/Scope 跨窗迁移；同 Presenter 不自清空；关闭后引用可释放 | HostDockAdapterUiTests、DockSplitVisualRegressionTests、回收器测试 |
| T06 | 单文档/整窗保存、放弃、取消、保存失败、新修订出现、框架拒绝和重复关闭 | DocumentCloseTests、范围关闭集成测试 |
| T07 | 干净文档在途命令排空、目标组变化、主窗和浮窗同时关闭、迟到回调 | DocumentOperationShutdownTests、命令关闭及 HostLifecycleOwnershipTests |
| T08 | Ctrl+S 等快捷键一次执行、活动目标正确、命令面板与焦点恢复、窗口退订 | WorkbenchCommandPresentationUiTests、窗口绑定测试 |
| T09 | 文件选择与关闭确认 Owner、全屏租约、窗口取消关闭、失效目标迟到结果 | 窗口交互单元测试、Headless UI、实际桌面 |
| T10 | V3 正常往返、组 ID 稳定、主文档占位、纯文档空窗剔除、比例归并 | V3 Mapper/Validator 单元测试 |
| T11 | 未知/重复/缺失字段、未来版本、重复工具、坏引用、环、深度和数量边界、NaN/无效尺寸 | 严格 JSON 与快照校验测试 |
| T12 | V2 合法迁移、退役工具、Pinned、原字节不变、V1 不接触、升级后不反复导入旧 V2 | DockLayoutStoreTests、迁移临时目录测试 |
| T13 | 主文件坏而备份好、双坏、权限失败、备份失败、临时写失败、替换失败、文件占用 | Store 与原子写入集成测试 |
| T14 | 缺插件/初始化失败/激活失败，仅恢复可用项；保存保留项，插件回来恢复原位置 | DockLayoutAvailabilityTests、生命周期集成测试 |
| T15 | 连续拖动合并、恢复抑制、旧新修订、退出前 flush、拆窗通知不覆盖、调度释放 | 可控调度单元测试、布局生命周期集成测试 |
| T16 | 双实例只有一个写入者、只读实例不隔离或迁移、不同数据根独立、崩溃释放锁 | 文件句柄测试与最小进程测试 |
| T17 | 单/多屏、负坐标、不同 DPI、移除屏幕、小工作区、最大化/最小化、找回窗口 | 位置策略纯函数测试与真实桌面 |
| T18 | 重置布局保留脏文档及 Scope、工具默认隐藏、取消与失败、空窗清理 | Workspace、Headless UI |
| T19 | 保存 JSON 无文档路径/标题/payload，诊断不输出文件原文、插件异常正文或屏幕敏感信息 | 布局格式测试、HostDiagnosticsTests |
| T20 | 主窗口退出、启动失败及资源未排空时不误释放 Provider；浮窗没有遗留强订阅 | HostLifecycleOwnershipTests、RuntimeShutdown 与 UI 资源测试 |
| T21 | MyPlugTest 实际包打开 Tool/Document 后浮动、回停、关闭；SDK 边界保持；V10 看板可继续打开 | PackageAcceptance 与 Host/SDK/UI 现有测试工程 |
| T22 | 当前文件名与 schema 属性一致、文档锚点、帮助嵌入、旧格式标识没有机械替换 | VersionPolicyTests、HelpContentTests、文档扫描和必要 Gate 自测 |

测试断言可观察行为和失败结果，不机械镜像实现；浮动需要真实 HostWindow 路径的测试，不能全部用直接设置 Owner 的夹具代替。既有 Adapter、MyPlugTestV3UiTests 和窗口样式测试中的禁浮动断言也需同步审计，不只修改名称最明显的一份测试。

### 9.2 本轮允许的开发命令

在主仓根目录运行完整开发入口：

```powershell
dotnet run --project tools/MyAvaloniaManagement.Gate -- verify
```

当前 verify 执行 locked restore、Release 零警告构建、SDK/Host Unit/Host Plugin/Host Headless UI/MyPlugTest Unit、契约及已发布 API 比较、MyPlugTest 打包与真实 ZIP 验收。API 比较是开发兼容检查，不是公共发布动作；它不启动 Windows Smoke，也不授予发布资格。完整语义见[主仓验证说明](../maintenance/verification.md)。

阶段排错使用现有测试项目，不替代最终 verify：

```powershell
dotnet test Host/MyAvaloniaManagement.Tests -c Release -m:1 -warnaserror
dotnet test Host/MyAvaloniaManagement.PluginTests -c Release -m:1 -warnaserror --filter 'Category!=PackageAcceptance'
dotnet test Host/MyAvaloniaManagement.UiTests -c Release -m:1 -warnaserror
dotnet test Host/MyAvaloniaManagement.PluginSdk.Tests -c Release -m:1 -warnaserror
```

修改 Gate 后单独运行工具自测，不能与正在运行的 Gate 相互重建：

```powershell
dotnet test tools/MyAvaloniaManagement.Gate.Tests -c Release -m:1 -warnaserror
```

帮助专项用于文档或嵌入路径变化后的检查：

```powershell
dotnet test Host/MyAvaloniaManagement.Tests -c Release -m:1 -warnaserror --filter FullyQualifiedName~HelpContentTests
```

结果必须记录实际命令、源码与未提交输入身份、通过/失败/跳过数量及 TRX。缺输入、零测试、未执行或通过过滤器绕开必要测试均不能记为成功。PackageAcceptance 由 Gate 准备实际 ZIP 输入，专项 Plugin 测试排除它不代表可以省略最终包验收。

不运行 `seal`、发布覆盖率、发布 Windows Smoke 或 Windows CI，不调整既有发布阈值。最终 verify 只依赖本仓 Host 与 MyPlugTest，不依赖相邻十一插件源码或个人安装目录。

### 9.3 本地真实桌面与外部业务

普通本地人工运行开发产物，使用隔离 `MYAVALONIA_DATA_DIRECTORY` 和测试文档，验证拖出/拖回、整组关闭取消、跨屏、DPI、Esc、失活、重启恢复与找回窗口；不触发 CI 或发布 Smoke，不覆盖日常用户布局。

MyPlugTest 证明通用 Host 路径；原生视频、WebView、后台下载等由相应插件的真实场景补证，先确认实际产物身份和环境。外部产物需要兼容验证时沿用[V10 专项开发入口](../maintenance/plugin-compatibility-verification.md)，不把历史报告自动标为本轮通过。缺环境时记待验收，不虚构成功，也不扩大为全插件源码改造。

## 10. 文档同步与专用材料

遵守 2026-09-16 重组后的职责：roadmap 放活跃计划，quick-start 放使用指南，reference 放详细契约，maintenance 放开发操作，archive 放历史计划和实际阶段记录。不恢复旧目录，不搬动应用内帮助的稳定原文路径。

| 文档 | 创建/更新时机与职责 |
| --- | --- |
| 本计划 | 当前创建于 docs/roadmap；开发完成并结转待办后迁至 docs/archive/plans，重算链接 |
| docs/README.md、docs/roadmap/README.md | 当前加入 V11 待实施入口；实施后按实际状态更新，不提前宣布浮动可用 |
| `docs/reference/dock-layout-snapshot-v3.md` | G1 新建专用严格格式与恢复契约，实施中标明状态；成为 V3 唯一详细权威说明 |
| docs/reference/dock-layout-snapshot-v2.md | V3 落地时改为只读迁移输入说明，保留原字段与旧版行为，不把 V2 内容机械替换成 V3 |
| `docs/quick-start/floating-windows-and-layout.md` | G6–G8 新建专用用户指南：拖动、工具隐藏恢复、文档不重开、屏幕找回、重置、保存失败提示 |
| `docs/maintenance/floating-layout-verification.md` | G7–G8 新建专用开发验证指南：隔离数据、旧格式夹具、故障注入、重启、多屏及排错，明确无发布门禁 |
| `docs/archive/records/host-v11/development-acceptance.md` | 实施 G0 起创建，追加每阶段实际输入、命令、结果、限制与未验收项；当前不创建虚假的验收记录 |
| `docs/archive/records/host-v11/final-development-evidence.json` | 最终验证后按需要记录非嵌入的源码/产物身份、报告摘要和状态，避免嵌入文档与产物哈希循环变化 |
| docs/quick-start/tool-center.md、workbench-search.md | 实施后同步浮动状态、页面激活、命令面板与布局恢复入口 |
| docs/reference/platform-baseline.md、docs/maintenance/verification.md | 同步 Layout V3 和开发测试范围；明确发布 Smoke 旧 V2 断言尚待发布阶段适配及验证 |
| Host 内部架构、设计取舍与 compatibility-contracts.md | 同步窗口职责、关闭所有权、V3、迁移、部分恢复及 SDK 不变量，保留稳定路径 |
| README.md 与受影响 HelpContent | 只更新已实现能力及导航；使用 HelpContentTests 验证嵌入读取 |
| docs/archive/README.md | 出现实际 V11 阶段记录时加入入口；计划归档时再登记历史方案 |

计划中尚未创建的专用文件以代码路径列出，不提前提供失效链接。当前文档不复制历次测试数量和哈希；历史 V9/V10 记录不倒写为 V11 通过。V3 改造不影响的 SDK、Build、模板文档无需为凑清单修改；如实际发现插件使用约束变化，再同步对应独立文档。

## 11. 风险、开发交接与发布边界

| 风险 | 处理与必须留下的证据 |
| --- | --- |
| Dock 实际浮窗关闭绕过 Document 回调 | G0/G3 用锁定版本真实窗口集成路径验证，统一关闭入口后才开放功能 |
| 浮窗 Top/Bottom 操作触发主骨架查找 | 局部查询与全工作区查询分开，增加缺主节点的浮窗夹具 |
| 视图重挂载触发插件自清理或原生资源损坏 | 保留既有 View Lease/回收器；测试临时 Detached 与最终释放，外部原生控件独立验收 |
| 自动保存记录了退出拆除状态 | 最终快照先于拆窗，保存抑制及写入队列关闭有时序测试 |
| 部分恢复后保存丢弃不可用工具 | 保留布局记录与运行时投影分离，验证保存再启动后的缺失项回归 |
| V2 迁移破坏用户回滚数据 | 只读转换、V3 单独文件、失败保留原件，验证字节不变 |
| UI 自动化结果被当作真实桌面结果 | 真机、业务与自动化分项签署；环境缺失保留待办 |
| 布局版本改变导致旧发布 Smoke 失效 | 在发布前待办明确更新 GateChecks 的文件名/schema 断言并自测；V11 开发阶段不运行或放宽该发布流程 |
| 文档更新后沿用旧 Host 验收身份 | 文档定稿后重建并验证，最终身份用非嵌入摘要记录，不能套用 V10 报告 |

没有新增公共包或批量外部插件改造的必要时，实施者按本计划自主完成内部调整。只有出现必须改变 SDK、升级 Dock、修改已约定的文档恢复范围等实质范围变化时，才列清事实和影响供用户决策。

开发交接至少列明：各 G 阶段状态、自动验证结果、真实桌面和原生资源覆盖、布局迁移/回退行为、剩余问题，以及 Windows CI/发布门禁/部署/公开发布均未执行。实际发布时再处理当次 API 政策、V3 发布 Smoke 适配、覆盖率、真实业务、产物备份回退和发布执行，不在本轮提前运行。
