# 主项目设计方法论与取舍

## 1. 重构目标

本轮目标不是增加功能，而是在保持外部行为的前提下提高健壮性、可测试性和可理解性。衡量成功的标准不是新增类数量，而是：

- 每个 G 包只改变声明范围内的格式；G7/G8 分别以唯一 V2 替换 Document/Layout V1，不保留双 reader；
- 高风险规则具有唯一实现位置；
- 异常、并发和资源所有权边界明确；
- Dock Factory、Workspace Session、Tool 只读投影和 `MainWindowViewModel` 分别只有明确变化原因；
- 完整回归和覆盖率门禁能够证明行为仍然成立。

## 2. 方法论：契约优先的小步重构

### 2.1 先识别不变量

重构前先把以下内容视为约束，而不是“顺手优化”的对象：

- Common/Plugin SDK 的 public 类型和成员；Host 自有实现不得导出；
- 插件必须具有严格清单、入口 `.deps.json` 和唯一 `IPluginModule`，manifest 是身份唯一事实源；
- Document、Tool、View、Lifecycle 必须显式登记；未登记类型不可见，重复 ID 以结构化诊断阻断启动；
- 运行时兼容 Locator 与布局持久化身份分离；Layout V2 不识别 `Files` 或历史 Tool ID；
- Left/Right/Top/Bottom、隐藏/恢复、Pinned 和禁用浮动；
- 严格 Document 信封 v2、插件原生 JSON `DocumentContent` 与严格 `layout-v2.json`；
- 快照整体无效时隔离并使用默认布局。

只有先固定不变量，内部抽象才不会悄悄变成产品行为变更。

### 2.2 先建立保护，再移动职责

采用的顺序是：

1. 运行现有测试，并从 TRX 动态记录基线；
2. 增加 Plugin SDK 反射指纹和 Host 零自有导出门禁；
3. 找到重复扫描、重复遍历、私有字段反射和分散文件写入等接缝；
4. 先抽取内部协作者；若旧类型只是 internal 万能 Facade，则整体迁移消费者后直接删除，不保留转发层；
5. 每完成一条链路立即运行对应测试；
6. 最后执行 Release 覆盖率门禁和真实窗口冒烟。

这种方式把一次“大重写”拆成可验证的行为保持步骤。出现回归时，定位范围被限制在最近移动的职责内。

### 2.3 按变化原因拆分，而不是按文件大小拆分

类变小只是结果，不是目标。拆分依据是“哪些规则会因不同原因变化”：

- 插件部署规则变化不应迫使 Dock 布局代码变化；
- Tool 恢复规则变化不应影响策略元数据读取；
- 文档 JSON 格式变化不应影响文件选择器；
- 布局版本迁移变化不应修改窗口生命周期；
- 文件提交策略变化应同时惠及文档与布局。

## 3. SOLID 的具体应用

### 3.1 单一职责原则（SRP）

| 原职责聚合 | 拆分后 |
| --- | --- |
| `Program` 注册服务、扫描插件、构建容器、初始化和关闭 | `HostRuntime` 统一组合根和所有权 |
| `ManagementFactory` 同时适配 Dock、拥有 Root/Document/Tool 并协调退出 | `HostDockFactory` + `WorkspaceSession` + ReadModel + 既有 Coordinator |
| `MainWindowViewModel` 选择文件、读写 JSON、查重、修改 Dock | Persistence Coordinator + Workspace Session |
| `DockLayoutLifecycle` 映射、验证、文件事务、窗口编排 | Mapper、严格 JSON Codec、Validator、Store、Atomic Transaction |
| public 生命周期 Manager 同时排序、执行、保存状态和投影 UI | internal Coordinator、单操作 Runner、StateStore、只读 Availability ReadModel |
| 插件 Document 同时自报内容、路径和类型身份 | 内容快照 + 独立脏状态 + 宿主持久化状态存储 |
| 菜单/ViewModel 同时承担命令身份、查询和执行 | 不可变 Catalog + Executor + 打开/保存 Handler；UI 投影后移 |

SRP 在这里指“只有一个变化原因”，不是“每个类只能有一个方法”。Document 打开/恢复由 `DocumentPersistenceCoordinator` 编排，主文件与备份提交由 `DocumentSaveService` 负责，异步关闭确认由 `DocumentCloseCoordinator` 负责；三者共享窄操作门，但不会共享彼此的 UI 或序列化职责。

### 3.2 开闭原则（OCP）

新增 Document/Tool/View/Lifecycle 通过 `IPluginRegistration` 扩展，宿主核心不需要为每个插件类型增加分支。Registry 将变化集中在显式登记、校验与分派，不把扩展判断散落到 Dock 操作中。

Workbench Command G2 将 Host 内建命令和 G1 插件声明合并到同一查询面。新增 Host Command 只需在
`HostWorkbenchCommandCatalog` 显式登记 Handler；插件命令仍来自不可变 Registry。Catalog 不承担运行状态，
因此 G3 可以增加活动 Document Target 路由而不改写 G2 的描述符和所有权事实。

V2 有意破坏历史 v1 SDK，删除重复身份和隐式发现入口；G14 已将收敛后的 Core/UI 表面冻结为 v2 Shipped。没有引入通用模块框架或运行期可变注册表。

### 3.3 里氏替换原则（LSP）

`HostDockFactory` 满足 Dock 基类的 override 行为；Docked/Hidden 先调用基类，Closing 先通过 Session
保护再调用基类，Closed 在 `finally` 释放 Session 所有权。全部浮动 overload 保持同一拒绝语义。

三个可保存 Document 实现遵守同一内容快照语义：创建快照无副作用，恢复前精确验版，
任何成功保存都输出该插件当前内容 schema。宿主因此可以用同一提交流程替换它们，无需插件类型分支。

### 3.4 接口隔离原则（ISP）

文件工作流依赖最小内部 `IHostStorageService`，而不是主窗口、`IStorageFile` 或整个 Avalonia 生命周期。内部协作者优先接收完成职责所需的最小对象。

没有为每个内部类机械创建接口。只有存在真实替代实现、框架边界或测试边界时才使用接口，避免接口数量超过行为复杂度。

`IHostWorkbenchCommandHandler` 有打开与保存两个真实实现；`IWorkbenchCommandShutdownParticipant` 是
Executor 与 HostRuntime 关闭门控之间的窄替换边界。Catalog、Executor、结果和值对象继续使用具体 internal
类型，没有为每个协作者复制接口。

V3 G2 继续保持 Core SDK 的窄契约：`IPluginDocument` 只负责异步初始化，
`IPersistablePluginDocument` 只负责捕获不可变修订快照与指定修订的提交后确认；修订含义和持久字段
归插件，路径、标题和文件事务仍留在宿主内部状态与协调器中。三个插件用少量局部状态实现，没有建立
共享 Revision Tracker，也没有为旧无参确认建立双轨。

V2 G8 同样保持 `IPluginLifecycle` 只有初始化与关闭两个方法。排序、30/10 秒期限、状态和诊断不进入
SDK；菜单、Activator 与布局只依赖 `PluginAvailabilityReadModel`。四插件已在 G9–G12 全部迁移，
G13 已删除 Legacy 回调适配、`Order`、依赖图和 public Manager。

### 3.5 依赖倒置原则（DIP）

主窗口依赖文档用例协调器与唯一 Workspace Session，Tool 管理依赖无 Dock ReadModel；插件模型依赖私有 Provider 中的窄服务。App 依赖内部桌面 Shell，
内建策略依赖窄 `Func<T>` 工厂；静态 `ServiceProvider` 与生产无参构造已删除。模块依赖 SDK
抽象 `IPluginRegistration`，具体 Registration、Builder 和 Registry 均留在 Host 内部。服务解析只允许
出现在 `HostRuntime`、显式贡献激活和 Document Scope 等明确组合边界。

G2 的 Host Handler 依赖现有 `DocumentPersistenceCoordinator` 与 `DocumentOperationState`，Catalog 只依赖
Descriptor/Registry，Executor 只依赖 Catalog、可用性 ReadModel 和诊断端口。任何 Command 类型都不接收
根 Provider、插件 Scope 或 Dock 对象。

## 4. 采用的设计模式

### 4.1 Composition Root：`HostRuntime`

目的：让对象图构建和生命周期所有权只有一个入口。

取舍：没有把 `HostRuntime` 暴露为 public，也没有引入新的宿主上下文契约。它只解决内部启动职责分散问题。

### 4.2 Factory Adapter、一次性绑定与只读投影

`HostDockFactory` 只把 Dock Framework override/Locator 适配为窄 `IWorkspaceDockCallbacks`；
`WorkspaceSession` 是唯一工作区所有者；`ToolWorkspaceReadModel` 发布不含 Dock 类型的不可变状态。

取舍：Factory 与 Session 因互相组合需要一次显式绑定。绑定只能发生一次，未绑定使用立即失败；相比把
`IServiceProvider` 塞入 Factory 或建立通用事件总线，这个小接缝更容易审阅，也让错误在组合时暴露。

### 4.3 Context、Builder 与不可变 `PluginRegistry`

目的：用受控 Registration 表达插件贡献，通过 Builder 分阶段组合，再让元数据、菜单、View 和生命周期
所有权共享同一不可变事实源；模型创建交给独立 Activator。

取舍：模块仍能通过 `context.Services` 注册私有服务，但四类宿主贡献只能走专用方法。每插件先写入
临时 Builder，Provider 成功后才合并；全局 Builder 再以“局部校验 → Provider 可解析性 → 全局冲突过滤
→ 发布”提交。Registration 返回后连同其 `Services` 包装器一起封闭。Registry 不提供写 API、覆盖操作、
Provider 引用或运行期热卸载。

V2 G4 已用所有权替代 v1 G6 的 Policy + Transaction：Host Provider 先建立，每个插件从新的空集合建立
私有 Provider。插件错误不再需要描述符差异算法回滚，只需丢弃当前 Provider 与临时 Builder。仍使用
Microsoft DI，没有引入第三方子容器框架或动态代理。

V2 G5 的全局冲突算法只是按 Document ID、Tool ID 和精确模型类型分组：Host 组保留 Host 并排除插件，
纯插件组排除所有参与者。这里没有优先级规则、覆盖链、规则引擎或回滚事务；冲突 Provider 在发布前释放，
未冲突插件继续工作。G8 的运行状态保存在独立 StateStore，Registry 仍然只含冻结声明。

### 4.4 Factory + Adapter + Scope Lease：Host Dock 边界

目的：让模型激活、Dock 投影、View 创建、布局协调和资源释放分别只有一个修改原因。Activator 返回普通
模型；`IHostDockableFactory` 组合内部 Adapter；ViewLocator 只按冻结注册构造 View；Document Scope Lease
只表达一次释放权。Tool Adapter 不拥有 Provider singleton，Document Adapter 则完整拥有模型、View 与 Scope。

取舍：没有建立通用 Dockable 泛型层次、策略管线或生命周期规则引擎。Document 与 Tool 的所有权不同，
保留两个很小的 sealed Adapter 比强行复用基类更清楚。View 在发布前构造会提前占用少量控件资源，但换来
失败原子性和单实例 DataContext；幂等 View Lease 解决 Adapter 与控件回收器可能重复通知的问题。

### 4.5 Builder：`DockWorkspaceBuilder`

目的：以稳定 ID 构造四向初始布局，避免创建结构与运行时恢复交织。

取舍：Builder 仍调用 Dock Factory 创建集合，这是框架适配所需，不追求完全纯对象构造。

### 4.6 Query Object / Navigator：`DockTreeNavigator`

目的：统一 Dock 树遍历、可见性、Pinned/Hidden 和节点定位规则。

取舍：它是静态内部工具，因为查询无状态且没有替代实现；为形式上的依赖注入把它实例化不会增加价值。

### 4.7 Coordinator：文档、工具和布局协调器

目的：表达跨多个低层对象的用例顺序，例如“恢复工具并激活”“严格验证后应用布局”“保存成功后提交文档状态”以及“正序启动、反向停止生命周期”。

取舍：Coordinator 可以依赖多个具体内部组件，但不应成为新的万能类。判断标准是它是否只拥有一条业务流程及其事务边界。

### 4.8 Session：`WorkspaceSession`

目的：让 Root/Document Dock、已拥有 Document 和已创建 Tool 只有一个所有者，并把创建、发布、失败回滚、
关闭和退出收敛到同一提交点。持久化与布局协调器只向 Session 请求领域操作，不传递 `IRootDock`。

取舍：Session 是有状态的 HostRuntime singleton，但不负责文件格式、插件发现或任意消息。多个窗口共享
同一 Session/Root，并各自解除订阅；这比为每个窗口复制工作区集合更符合真实产品所有权。

### 4.9 Atomic File Transaction

目的：为布局和文档提供相同的“完整写入后一次提交”语义。

取舍：只保证单文件替换，不实现跨文件事务、备份版本链或崩溃恢复日志。当前两个文件格式都以单文件为一致性边界。

### 4.10 Catalog + Executor + Adapter：Workbench Command G2

目的：Catalog 冻结“有什么命令”，Executor 负责“本次调用能否安全执行”，Host Handler 只把稳定身份适配到
既有打开/保存用例。关闭门控只判断入口和排空，不释放 Workspace 或 Provider。

取舍：G2 插件命令已可查询，但在没有活动 Document Context 时稳定返回 `TargetUnavailable`；现有菜单和
`Ctrl+S` 暂不迁移。Executor 不增加单飞、重试、队列、业务超时、授权、Run Manager 或 invocation scope，
避免把用户意图命令扩张成第二套 Workflow Action Runtime。

## 5. 关键设计决策与取舍

| 决策 | 获得的价值 | 接受的代价 |
| --- | --- | --- |
| V2 一次性破坏升级 SDK | 删除重复身份与隐式发现，形成可长期维护的 v2 基线 | 使用 v1 SDK 编译的插件必须重新编译 |
| 插件根目录扫描一次并缓存 | 并发安全、启动确定、减少 I/O | 进程内无法感知替换后的插件 |
| 单个插件/类型失败隔离 | 一个坏插件不阻断其他插件 | 诊断呈现限于插件状态 Tool 与启动错误窗，尚无运行时用户级诊断入口 |
| Managed-only 加载与统一 DI 激活 | 所有权、依赖和错误语义只有一套 | 无模块 Legacy 二进制插件不再加载 |
| 显式贡献 + 不可变 Registry | 未登记类型不可见，所有消费者共享同一组合事实 | 插件作者必须完整列出贡献并随契约变化重编译 |
| 重复 ID 与精确模型碰撞整体隔离冲突插件 | Host 与无冲突插件可继续组合，结果确定 | 冲突插件需要修正后重启，不提供覆盖优先级 |
| Descriptor 在注册调用中冻结 | 元数据读取不构造模型、不执行插件回调 | 运行期元数据变化需要重启并重新组合 |
| 文档操作串行化 | 同路径查重和状态提交确定 | 同窗口文档 I/O 不并行 |
| 保存成功后才更新内存状态 | 失败不会产生“假保存” | 需要暂存标题和路径，流程更显式 |
| 只捕获预期持久化异常 | 用户故障可恢复，编程缺陷不被吞掉 | 调用方仍需承担非预期异常终止风险 |
| 布局整体校验、整体回退 | 不产生半恢复混合状态 | 缺失一个插件会丢弃其余布局恢复结果 |
| Layout 只接受严格 V2 | 格式和行为唯一、失败可预测 | 不提供 V1 迁移或通用版本迁移框架 |
| 模块结构使用独立 Validator | 在插件对象实例化前按目录隔离 | 仍需加载程序集并读取类型元数据 |
| Factory 与 Session 一次性绑定 | Dock 继承面与应用状态彻底分开，无服务定位 | 组合根必须按固定顺序建立两个对象 |
| Tool 状态使用无 Dock ReadModel | ViewModel 只看稳定纯数据，Pinned/Hidden 规则集中 | 状态变化后需要重建小型快照 |
| Command Catalog 与 Executor 分离 | 身份、owner、执行和关闭各有唯一变化原因 | G2 到 G4 之间旧 UI 路径暂时保留 |

## 6. 明确没有采用的方案

### 6.1 不引入通用 Repository 或 Unit of Work

文档和布局都是单文件持久化，不存在需要聚合多个实体仓储的事务。引入这些模式会把简单文件边界伪装成数据库模型。

### 6.2 不为每个类创建接口

内部 Builder、Navigator、Mapper 没有多个实现，也不是外部边界。对它们创建接口只会增加注册和跳转成本。可测试性主要通过输入输出明确和内部可见测试获得。

### 6.3 不使用事件溯源或命令总线替代现有消息器

现有消息主要承担 UI 状态通知，尚没有审计、重放或分布式一致性需求。引入重型消息基础设施超出本轮内部重构目标。

Workbench Command 也不是通用命令总线：它没有任意 Payload、字符串路由、反射发现、跨插件回调或持久化日志，
只处理经过 Registry/Catalog 冻结的用户语义身份。

### 6.4 不实现插件热加载

当前 `PluginLoadContext` 不可回收，Tool、Document、DI 和 XAML 都可能持有程序集类型。真正热卸载需要重新定义对象所有权和 UI 生命周期，不能通过清空程序集缓存安全实现。

### 6.5 不进行缺失插件的部分布局恢复

部分恢复需要定义 Pane 比例重算、活动项替代、未知 Tool 占位和再次安装后的合并规则。这是新功能和新契约，而不是单纯健壮性重构。

## 7. 修改代码时的决策流程

建议每次宿主变更依次回答：

1. 这是外部契约变化、内部职责调整，还是新功能？
2. 哪些现有 public 签名、稳定 ID、JSON 字段或插件规则受影响？
3. 该规则是否已经在另一个类中实现？能否复用唯一事实源？
4. 失败属于用户可恢复故障还是编程错误？异常应该在哪一层转换？
5. 谁创建对象、谁持有对象、谁在什么事件之后释放？
6. 并发调用时身份判断和状态提交是否原子或串行？
7. 最小的契约测试是什么？是否需要 Headless 或真实窗口验证？

如果第 1 题答案是“外部契约变化”或“新功能”，不应伪装成内部重构合入。

## 8. 完成定义

内部重构完成至少需要：

- Plugin SDK API 门禁通过，Host 不重新导出自有实现类型；
- Managed-only 拒绝、Dock 稳定 ID、文档 JSON 和严格布局 V2 回归通过；
- 预期失败不会留下错误内存状态或临时文件；
- Scope、缓存和根容器的释放时机有测试；
- 当前阶段 Release 配置测试与覆盖率门禁通过；Windows 冒烟只在发布阶段按任务书执行；
- 当前架构文档和兼容清单同步更新。

## 9. V4 G6 的路径与展示取舍

路径解析器返回一个带分类的值结果，但不成为文件系统服务；存在性继续放在已有存储端口。这一拆分让
路径语法测试无需磁盘，让 UNC 展示测试无需网络，同时避免增加 Repository、策略族或通用 Facade。
ViewModel 只在解析与存在性都成功后提交完整展示状态，以简单的“先计算、后提交”代替 UI 事务框架。

分类菜单同样选择构造期复制的只读快照。它解决的是外部集合在展示期间变化的问题，不需要 Observable
集合代理、事件总线或专门的缓存 Manager。可变性只留给真实交互状态 `IsExpanded`，使变化原因清晰。
