# V12：Host 内部职责重构方案

> 用途：规定 Registry 校验、UI 刷新调度和工作区查询快照三项重构的范围、设计与验收条件。
> 状态：P0–P3 已实施并通过专项验证；最终本地 verify 以[开发证据](../archive/records/host-v12/final-development-evidence.json)为准。日期：2026-09-17；起始源码基线：`538d173`。
> V12 只表示 Host 改造序号，不改变产品、程序集、SDK、NuGet 包或磁盘 schema 版本。
> 用户已授权按本方案实施。实际取舍、测试与桌面验证边界见[专用开发记录](../archive/records/host-v12/development-acceptance.md)。

## 1. 目标与范围

在不改变任何对外约束的前提下，减少理解和修改 Host 所需的上下文，让业务规则、执行顺序及资源所有权可以直接从代码读出。收益以变更影响范围缩小、行为更易验证和重复规则减少判断，不以类、接口或文件数量判断。

| 阶段 | 对应探索项 | 目标 | 主要收益 |
| --- | --- | --- | --- |
| V12-P1 | 1．插件注册校验 | 分离声明收集、局部校验与全局冲突分析，保留唯一提交入口 | 修改校验规则时无需同时理解 Provider 提交；便于数据驱动测试 |
| V12-P2 | 2．命令展示刷新 | 提取有限的刷新调度协作者，显式保留不同刷新时机 | 集中验证 Dispatcher、合并和迟到回调；展示类聚焦自身语义 |
| V12-P3 | 3．工作区查询 | 单次查询捕获布局关系，避免逐项重复遍历 | 集中表达挂接、窗口归属及显隐查询，降低重复遍历成本 |

本轮不扩展到 Workflow 调用编排、诊断持久化、设置文件事务或 DI 全面整理；也不升级 Avalonia、Dock、SDK 或源码补丁。WorkspaceSession 继续独占业务实例与提交顺序。

不使用 AIFLOW，不读取或维护 `.aiflow` 流程上下文。不使用 Windows CI、`seal`、发布 Windows Smoke、发布覆盖率或发布重复性门禁；不上传包、不创建发布标签、不部署安装目录。开发验证只走本地专项测试及已有 `verify`，详见 [V12 专用开发验证](../maintenance/host-v12-refactor-verification.md)。

## 2. 首要规定：SOLID 与朴素设计

SOLID 是方案和代码审查的首要规定。每个新边界都必须解释“为什么独立变化、谁调用、谁拥有状态、如何验证”。若拆分导致更多隐含状态、调用跳转或释放歧义，应缩小拆分。

| 原则 | 本轮具体要求 | 审查依据 |
| --- | --- | --- |
| SRP | 校验只比较冻结事实；调度只管通知时机；快照只管查询关系；Builder 和 Session 保留提交职责 | 纯规则不得解析 Provider、调用插件工厂、修改 Dock 或写盘 |
| OCP | 新增规则主要落在对应校验职责；通过现有类型、身份和拓扑处理扩展 | 不增加插件 ID 特判；不提前建设可插拔规则引擎或查询插件系统 |
| LSP | 提取实现后，现有入口的返回值、异常、线程、取消、回调时机与释放语义保持 | 用既有消费者行为及新补充的边界测试证明替换成立 |
| ISP | 接口只用于真实替换或跨模块边界，纯函数和稳定内部协作者优先使用具体类型 | 不为每个新类机械增加单实现接口，不扩大 SDK 或 Dock 回调接口 |
| DIP | 校验消费声明数据，展示消费查询事实；DI 解析与资源提交留在原组合边界 | 不引入 Service Locator，不向新协作者传入可任意解析的 IServiceProvider |

设计只使用普通具体类、只读数据、显式方法调用和现有事件。保留已有 Builder、Adapter 与查询模型；不新建通用中介者、事件总线、泛型策略管线、继承式投影框架或事务框架。

### 2.1 面向人和 AI 的表达

1. 类型与方法名描述领域动作和结果，例如“校验单插件声明”“分析全局冲突”“请求刷新”“捕获布局关系”。下文类名是实施候选，不是新增兼容契约。
2. 主流程按“检查 → 计算 → 提交 → 通知”直接排列；明确标出实际副作用，不把顺序藏进链式回调或反射发现。
3. 参数表达必要输入，返回值表达结果；有两种行为时使用含义明确的枚举，避免多个布尔开关组合。
4. 文件按职责命名，关键类型便于检索；不以任意行数限制切碎流程，也不只用 partial 掩盖职责混杂。
5. 每份可变状态只有一个权威所有者。快照、展示数据和测试替身不能成为第二套业务状态。

### 2.2 中文注释与设计思路

新增及实质修改的核心类型、方法和时序分支使用详细中文注释：

- 类型的 XML 注释说明用途、输入输出、所有者及不承担的职责。
- 方法的 XML 注释说明前置条件、线程约束、失败行为和副作用；关键参数明确身份或引用比较语义。
- 普通行注释解释“为什么必须这样做”：冻结时机、诊断顺序、重入、锁外调用、迟到通知、主树与浮窗区别。
- 复杂规则先写短小的中文设计思路，再给实现；避免把代码逐句翻译成注释。
- 注释与对应测试一起更新，不保留已不成立的阶段说明，不记录历史测试数量。

P1 注释重点是失败原子性和冲突排除规则；P2 是两种通知时机及释放竞态；P3 是快照寿命、遍历顺序和原实例身份。

## 3. 保持不变的外部约束

完整事实源仍为 [Host 兼容约束](../../Host/MyAvaloniaManagement/docs/reference/compatibility-contracts.md)、[Workbench Command 契约](../reference/workbench-commands.md)及 [Layout V3](../reference/dock-layout-snapshot-v3.md)。本方案不另建一套兼容定义。

| 边界 | 本轮不可改变的内容 |
| --- | --- |
| API 与依赖 | SDK public 签名、程序集身份和包依赖；Host 自有类型继续 internal；插件不引用 Host |
| 注册与装载 | 显式贡献、精确工厂、manifest 所有权、每插件私有 Provider、声明封闭、失败隔离 |
| 稳定身份与格式 | Plugin/Document/Tool/Command/Placement/Page 身份；manifest V2、Document V2、Layout V3 及 V2 只读迁移；数据根和设置文件 |
| 资源寿命 | Document 独立 Scope；Tool singleton；创建失败不发布；关闭取消不提前释放；正常退出及失败回滚顺序 |
| UI 与交互 | 菜单分组、快捷键冲突政策、搜索排序、同名页面定位、焦点、工具显隐和命令执行时重新检查目标 |
| 诊断 | 错误码、阶段、顺序和既有失败传播边界；schema 2 脱敏规则；不新增原始异常或插件文本输出 |
| 测试与发布政策 | 保留现有有效断言和开发 Gate；不移动 API 基线、不放宽阈值、不修改发布流程来获取绿灯 |

若发现现有行为有缺陷，应单独记录问题与影响，不能借等价重构顺手改变兼容行为。

## 4. V12-P1：插件声明校验与提交分离

### 4.1 改造前事实

[PluginRegistryBuilder](../../Host/MyAvaloniaManagement/Business/Plugins/Registration/PluginRegistryBuilder.cs) 同时包含 Add/Import、ValidateSingleOwner、Command/Placement 关系校验、全局冲突诊断和 Registry 构造。

现有顺序具有实际含义：插件候选先局部校验；成功 Provider 的声明才导入；全局冲突排除整个 Owner；只读 Registry 构造完成后，才通过 PluginProviderOwner 提交排除和 Scope 所有权。Import 再次校验与 Build 全局防线不能因“重复”而删除。

### 4.2 已落地职责

| 组件 | 职责 | 不拥有的对象 |
| --- | --- | --- |
| PluginRegistryBuilder（保留） | 收集声明、控制可写状态、导入、按原顺序报告诊断、构造 Registry、最终提交 | 不新增运行期查询或热修改能力 |
| PluginContributionSnapshot（内部只读数据） | 给校验提供独立的声明集合快照，沿用注册时已冻结的 Descriptor 和工厂引用 | 不创建模型、View、Scope 或 Provider |
| PluginContributionValidator（内部静态类） | 校验单插件归属、重复、角色冲突及 Command/Placement 关系，返回有序诊断 | 不报告全局日志、不释放资源 |
| PluginConflictAnalyzer（内部静态类） | 从多个候选的冻结事实生成有序冲突项，指出相关 Owner 和诊断事实 | 不决定 Provider 释放、不产生持久副作用 |

快照只在现有校验／构建边界按需创建，集合需要防御性复制，不通过只读接口伪装共享可变 List；不重复读取插件元数据，不新增 SDK Descriptor 克隆协议。校验结果沿用现有诊断类型；仅当全局冲突需要携带 Owner 和诊断事实时使用最小 internal 记录类型。

### 4.3 主流程与顺序

1. Add 入口保持当前参数验证和描述符验证时机；不能把原来立即拒绝的错误推迟到 Seal。
2. ValidateSingleOwner 消费声明快照，按当前顺序汇总诊断，并由原入口抛出原类型异常。
3. Import 保持其校验、导入顺序及所有集合完整性；Builder 的封闭状态和重复 Build 行为不变。
4. Build 保持原可写检查及封闭时点；逐项消费有序冲突事实，按原顺序记录 rejectedOwners 和诊断。
5. 过滤全部贡献类别，保留无冲突 Owner；构造不可变 Registry 和索引成功后再调用 CommitRegistryResult。
6. 诊断端口抛错或构造失败继续遵循原异常边界；不吞掉错误、不提前提交、不改变后续诊断是否会发生。

局部重复、跨 Owner 冲突和图标引用重复存在不同判定，分别保留。不能把它们改成“第一个胜出”，也不能将同 Owner 快捷键重复与跨插件快捷键展示冲突合并为一种拒绝规则。

### 4.4 开发验收

- 先在原入口补行为锁定测试，再提取校验；正确的等价测试应在旧实现上通过。
- 校验纯逻辑用数据夹具覆盖；集成测试继续验证 Provider、Scope 与 Registry 的提交边界。
- 加入新贡献类别时有清晰的归属校验与全局分析入口，不要求修改无关运行期对象。
- 所有诊断、冲突集合、贡献过滤和失败原子性用例满足专项矩阵；P1 通过后再接入 P2。

## 5. V12-P2：命令展示刷新调度

### 5.1 改造前事实

[菜单与快捷键投影](../../Host/MyAvaloniaManagement/Business/Presentation/Commands/WorkbenchCommandProjection.cs)、[Palette](../../Host/MyAvaloniaManagement/Business/Presentation/Commands/WorkbenchCommandPaletteProjection.cs) 和 [ICommand Adapter](../../Host/MyAvaloniaManagement/Business/Presentation/Commands/WorkbenchPresentationCommand.cs) 分别维护排队标记、Dispatcher 和释放后抑制逻辑。

相似实现具有两种明确语义：

| 消费者 | 当前通知时机 | 必须保留的差异 |
| --- | --- | --- |
| Menu、KeyBinding、WorkbenchPresentationCommand | UI 线程立即发布；后台线程排入 Dispatcher | UI 同步通知不能整体改成异步；Command 的定向过滤仍在 Adapter |
| Palette | 包括 UI 线程在内始终排队 | 同一次事务的连续失效合并为一次刷新 |

菜单与 Palette 报告固定观察者失败诊断，快捷键投影隔离观察者异常，Command 使用自身执行失败诊断。调度器不能顺手统一这些处理。

### 5.2 已落地设计

新增一个 internal sealed 的 UiRefreshScheduler，由每个投影或 Adapter 分别持有。它只维护 pending/disposed 调度状态，提供 RequestRefresh、TryBeginRefresh 和 Dispose；使用明确的 ImmediateOnUiThread / AlwaysPost 模式枚举，DispatcherPriority 沿用 Normal。

调用关系保持简短：业务事件 → 消费者过滤 → RequestRefresh → 消费者发布入口在共享锁内 TryBeginRefresh 并取得事件快照 → 锁外通知。

实施时补充 TryBeginRefresh 并复用消费者原有同步锁。原因是清除 pending、检查释放、取得观察者快照原本处于同一个锁内；独立调度锁会引入新的竞态窗口。没有增加额外业务状态或通用调度接口。

消费者继续拥有事件订阅、事件委托、查询规则、业务释放状态及异常映射。调度器不读取 Catalog、Workspace 或 Provider，不共享全局单例，不拥有任何插件对象，不缓存查询结果。

### 5.3 并发、重入与释放

1. 排队标记的检查与更新保持同步，pending 的事实只由调度器维护；消费者不保留第二份刷新标记。
2. 不在内部锁中执行 Dispatcher 外部回调、业务通知或诊断；原消费者的事件快照规则继续保留。
3. 消费者进入发布入口后，在共享锁内调用 TryBeginRefresh 清除 pending、检查释放并捕获事件快照，再在锁外通知；允许通知期间的新失效触发后续刷新，不新增递归抑制或节流。
4. Dispose 幂等，使尚未执行的调度回调失效，并按原顺序解除订阅；不引入等待 UI 队列排空的同步阻塞。
5. 已经取得观察者快照的在途通知按现有语义处理，不承诺“释放可以撤销已经开始的委托调用”。
6. Query、CanExecute 和 Execute 的释放后行为及执行时状态重查保持原样。调度提取不改变异常传播政策。

先接入菜单和快捷键，再接入 Palette，最后接入 Command Adapter；每个消费者单独比较通知时机和诊断，不批量替换后只检查能否编译。

### 5.4 开发验收

通过实际 Avalonia Headless Dispatcher 验证 UI／后台线程、排队合并、重入、释放竞态和异常观察者。测试使用明确的队列推进或 TaskCompletionSource，不用任意 Sleep 猜测完成。

纯规则测试继续验证菜单、快捷键冲突、Palette 排序与定向过滤。测试观察调用线程、次数、先后关系和业务结果，不断言私有字段名称。

## 6. V12-P3：单次工作区查询快照

### 6.1 改造前事实

[WorkspaceSession.Pages](../../Host/MyAvaloniaManagement/Business/Workspace/WorkspaceSession.Pages.cs) 的 GetOpenPages 在筛选和可激活判断中重复寻找 DocumentDock。[ToolWorkspaceReadModel](../../Host/MyAvaloniaManagement/Business/Workspace/ToolWorkspaceReadModel.cs) 已捕获节点集合，但仍逐工具查找所属浮窗。

[DockTreeNavigator](../../Host/MyAvaloniaManagement/Business/Layout/DockTreeNavigator.cs) 明确区分主树 Enumerate 与含浮窗的 EnumerateWorkspace。这个区分保护固定骨架查询，不能借统一遍历改变其含义。

### 6.2 已落地设计

新增 WorkspaceLayoutQuerySnapshot 具体协作者，仅按查询实际需要保存以下关系：

- DocumentDock 成员关系；节点序列只用于本次构建索引，不另存一份长期挂接目录。
- 浮窗归属；主窗口中的对象仍以无浮窗表示。
- 根上的 Hidden / Pinned 集合及可见、活动节点集合。

集合和索引优先使用对象引用身份，不能按标题、Dock Id 或业务类型合并两个不同实例。按原遍历顺序确定首个匹配，保留重复引用下既有查询结果；不顺便修复或重排布局。

快照内部可在一次结构捕获后遍历所捕获的集合构造必要索引；目标是去掉“每一个页面／工具再扫描整树”。不为“一次遍历”口号把规则压进难读的大循环。

### 6.3 寿命与消费

1. 快照在现有工作区查询线程内同步生成和消费，中间不 await、不切到后台读取 Avalonia／Dock 可变对象。
2. 每次 GetOpenPages 或 Capture 使用本次快照；不强制合并两个独立入口，也不把快照存入 Session、服务单例或磁盘。
3. 快照包含关系事实，标题、Dirty、插件可用性及关闭状态仍从现有权威来源读取；返回展示记录不持有快照。
4. 页面筛选保留 _ownedDocuments 与 _publishedPages 的共同条件、稳定发布序号及 CanActivate 检查。
5. 工具判断保持 Hidden 优先于 Pinned、随后判断挂接和浮窗的既有顺序；未创建、不可用、缺失工具的说明保持。
6. TryActivatePage、工具显隐和命令执行继续在执行时重新检查真实状态；不能信任上次查询的 CanActivate 或 CanOpen。
7. Dispose、创建发布、ActiveDocumentChanged、PagesChanged 和 LayoutChanged 的发出位置均保持；本阶段不改变 WorkspaceSession 所有权。

### 6.4 开发验收

以显式构造的主树、文档分组、浮窗、隐藏和固定工具夹具验证查询结果，覆盖同名不同实例、相同 Dock Id 的不同窗口、关闭中页面、退出和不可用插件。

在测试中保留一个独立的简单遍历参照，与新查询做结果对照；参照不能复用新快照实现而成为自证。查询不得创建或释放模型／View／Scope，不得写布局或触发业务通知。

性能收益只在实施后以固定布局规模、预热和多次采样记录；机器耗时不作为易波动的硬门槛。未测量前只声明减少重复遍历的设计目标，不填写提速比例。

## 7. 实施顺序与开发门禁

| 阶段 | 工作 | 退出条件 |
| --- | --- | --- |
| P0：基线与行为锁定 | 确认源码身份；运行完整本地 verify；补三项关键兼容行为的测试 | 基线结果可追溯；已有失败单独记录，不能降低断言后继续声称等价 |
| P1：Registry | 提取快照、局部校验、全局分析；保持入口提交顺序 | P1 单元与插件集成矩阵通过，中文注释和对应架构说明同步 |
| P2：刷新 | 逐消费者接入调度器，保留两种时机与各自异常边界 | P2 单元与 Headless 矩阵通过，释放／重入／线程行为有证据 |
| P3：查询 | 构造短命关系快照，接入页面和工具查询 | P3 对照、实例寿命、布局／UI 回归通过，性能结论如实记录 |
| P4：收口 | 完成 SOLID 审查、文档定稿、全量本地 verify 与证据整理 | 专项必需项无失败、无未解释跳过、无零测试；完整开发验证成功 |

各阶段保持可独立审查和回退的差异，不创建双生产路径或兼容开关。若后续实施涉及 Git 提交，阶段边界可按上述组织；本轮工作分支为 `codex/host-v12-internal-refactor`，重构已提交为 `85a3bfe`；开发 JSON 保留验证时的 HEAD 与未提交内容清单，后续[本机部署与手工影响范围](../maintenance/host-v12-local-deployment.md)单独记录。

门禁命令、逐项测试矩阵、TRX 证据要求与文档校验统一维护在 [V12 专用开发验证](../maintenance/host-v12-refactor-verification.md)，不以本表代替实际执行。

### 7.1 失败处理与回退

出现契约差异、通知顺序变化或实例寿命回归时，停止进入下一阶段，先定位并修复当前阶段；无法在原约束下成立的抽取应撤销。回退单位是该阶段的代码、对应测试调整和内部架构说明，不清理用户文件、不重写布局，也不回退 SDK 或数据格式。保留原有有效行为测试、失败证据及无关用户修改；不以全仓重置代替阶段回退。

## 8. 文档同步与证据

| 文档 | 本轮已同步内容 | 结果边界 |
| --- | --- | --- |
| 本方案 | 保留设计约束，更新已落地协作者、共享锁取舍和阶段状态 | 具体结果见专用记录 |
| V12 专用开发验证 | 实际命令、新增测试类及 R/U/Q 与真实测试方法映射 | 不以矩阵文字代替 TRX |
| 总导航、待办、Host 文档入口、归档 | 实现和专项状态、专用记录与证据链接 | 原生桌面与外部业务仍独立记录 |
| Host 内部架构与设计取舍 | 当前 Validator/Analyzer、Scheduler、QuerySnapshot 与所有权 | 描述当前代码，不扩大外部契约 |
| 兼容约束、Command、Layout 文档 | 原权威规则保持有效 | 未修改对外契约或格式 |
| V12 开发记录与最终证据 | 已创建 [Markdown 记录](../archive/records/host-v12/development-acceptance.md)及[非嵌入 JSON](../archive/records/host-v12/final-development-evidence.json) | 最终 verify 仅在实际运行后回填 |

仓库 Markdown 会嵌入 Host。实施阶段应先定稿 Markdown，再运行最终 verify；运行 ID、TRX 摘要、源码身份和产物哈希写入非嵌入 JSON。验证后若又改 Markdown，原 DLL 哈希不再代表最终文档产物，应按影响重新验证。

## 9. 完成定义

以下为验收条件，不是执行状态勾选表；阶段结果见开发记录，完整 verify、源码清单、TRX 和产物身份以最终 JSON 为准。

- P1/P2/P3 按职责落地，没有超出范围的生产改动。
- SOLID 五项审查通过；新增类型各自有明确消费者和所有者，无无用接口或通用框架。
- 中文注释充分说明设计思路、线程、失败、顺序和资源所有权。
- 新增边界测试与现有单元、插件集成、Headless 回归通过；完整本地 verify 通过。
- SDK/API、格式、身份、诊断、UI 行为和资源寿命均保持原约束。
- 架构、设计说明、专项验证、索引和开发记录同步，最终证据绑定实际源码与产物。
- 未执行的原生桌面或外部业务场景明确记录，不用 Headless、历史结果或本方案代替。
- 全程未使用 AIFLOW、Windows CI 或发布门禁，未进行发布／部署。
