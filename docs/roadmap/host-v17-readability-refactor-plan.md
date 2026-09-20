# V17：Host 面向人和 AI 的可读性重构计划

> 用途：以行为等价为前提，改善 Host 的文件定位、主流程阅读和设计意图表达。
> 状态：计划已编写，重构与开发验证均未开始。日期：2026-09-20。
> 调研基线：`1d9ad9e8e55603165ebf22e75f1dbd914900e013`；实施前重新记录 HEAD 和工作树状态。
> V17 是 Host 改造序号，不代表产品、程序集、SDK、NuGet 或磁盘格式版本升级。
> 测试矩阵、开发命令和结果记录规则统一见 [V17 专用开发验证](../maintenance/host-v17-readability-verification.md)。

## 1. 目标与范围

本轮面向维护 Host 的人和 AI，优先实施风险极低、阅读收益明确的整理。成功标准是读者能从文件名定位职责、从入口看清主要步骤，并能在局部代码中理解关键约束；不以代码总行数、方法数量或文件数量作为成绩。

首批范围固定为三项：

| 阶段 | 对象与调研事实 | 改造方式 | 预期收益 |
| --- | --- | --- | --- |
| V17-P1 | `HostDiagnostics.cs`，833 行，容纳多个已有独立类型 | 按既有类型职责拆文件，保持实现与依赖关系 | 查找错误码、脱敏、等级判断和日志写入时直接定位 |
| V17-P2 | `WorkbenchCommandProjection.cs`，563 行，包含缓存、菜单、快捷键和组合对象 | 分离四个主要实现，相关小契约就近组织 | 文件名与核心类型对应，减少阅读无关展示逻辑 |
| V17-P3 | `AddApplicationServices`，约 232 行 | 将连续且职责完整的注册段提取为同类内私有方法 | 入口表达组装步骤，局部方法表达依赖与生命周期 |

行数包含注释与空行，只是基线事实。三个阶段都应保持小而可独立审查的差异，不开展全面架构重写。

### 1.1 本次文档交付与后续实施

当前任务只创建计划、专用验证说明并同步导航；不修改生产代码、测试代码、项目配置或覆盖率基线，不执行构建、测试及开发 Gate。文档链接与差异检查属于本次交付验证。

后续实施按本计划逐阶段进行，实际测试结果在执行后记录。当前测试矩阵表示要求与已有回归入口，不表示这些测试已经在本轮通过。

### 1.2 后续候选与排除范围

| 项目 | 本轮处理 |
| --- | --- |
| `PluginManifestReader.TryRead` 的文件读取、解析与字段校验分离 | 第二批候选。须独立核对首次失败、错误码、短路顺序、异常范围和 JSON 对象寿命；不列入 V17 完成条件 |
| `WorkspaceSession.PrepareApplicationCloseAsync` 等压缩控制流 | 后续局部表达候选。可先展开分支，但本轮不跨入关闭链修改 |
| `WorkspaceSession` 职责迁移、`WorkflowActionRunManager.InvokeAsync` 编排拆解 | 不实施；涉及状态所有权、并发计数、取消及释放时序，不以文件长为由拆分 |
| 大型错误码 `switch`、明确的字段映射 | 保留集中表达；不为行数限制改成策略体系 |
| 全仓命名、格式化、通用 JSON 校验器、DI 自动扫描或注册框架 | 不实施；避免扩大差异与引入额外抽象 |
| SDK、包版本、持久化格式、Avalonia/Dock 补丁、UI 功能 | 保持当前基线；发现缺陷另记，不混入等价重构 |

**不使用 AIFLOW**，不读取或维护其上下文、任务记录和更新候选。

**不使用 Windows CI 或发布门禁。** 实施阶段仅运行本地专项和既有 `verify`；不执行 `seal`、发布 Windows Smoke、发布覆盖率或重复性门禁，不修改 CI、不部署安装目录、不上传包或创建发布标签。发布阶段再按当次要求执行发布门禁，保留现有政策与阈值。

## 2. 首要规定：SOLID 与朴素设计

SOLID 是方案、实现与审查的首要规定。优先显露现有职责；如果提取使参数、调用跳转或隐含状态明显增加，应缩小提取范围。

| 原则 | 本轮落实方式 | 审查要点 |
| --- | --- | --- |
| SRP：单一职责 | 诊断各策略与会话按现有变化原因归档；菜单、快捷键、命令缓存分别定位；注册方法表达一组明确服务 | 文件拆分不把原来的一个资源所有者拆成多个，也不把无关对象塞进一个通用 Helper |
| OCP：开闭原则 | 保留现有显式贡献、查询与组合边界；让维护者容易找到相应注册段 | 不新增插件 ID 特判，不因“可扩展”引入反射扫描、规则引擎或注册模块框架 |
| LSP：里氏替换 | 现有消费者看到相同实例、结果、异常、通知时机和释放行为 | 保持工厂延迟执行、同实例接口映射、UI 线程和失败回滚语义 |
| ISP：接口隔离 | 复用已有窄接口，普通内部方法保持具体实现 | 不机械增加一类一接口，不扩大 SDK、Dock 回调或命令展示契约 |
| DIP：依赖倒置 | 服务解析留在原组合根，业务继续依赖已有窄协作者 | 不把 `IServiceProvider` 传到新业务类，不引入 Service Locator 或第二套容器 |

采用普通文件组织、私有方法提取、现有构造函数与显式调用即可。保留既有 Adapter、Catalog、Policy 和组合根的实际职责，不新增通用基类、事件总线、泛型管线或生命周期框架。

### 2.1 可读性与表达约定

1. 文件名与主要类型或单一主题对应。紧密相关的小枚举、记录、接口可以同文件，不要求“一类型一文件”。
2. 方法名表达领域动作，如“注册工作流服务”“注册命令展示”；不使用 `SetupPart1`、`ProcessCore` 等没有责任含义的名称。
3. 提取完整步骤，不按任意行数截断。读懂主流程无需逐个打开十几个只转调一行的方法。
4. 主流程中的顺序、异常边界和资源归属保持可见。避免通过回调数组、链式注册或隐藏上下文把它们藏起来。
5. 新私有方法只接收真实需要的现有依赖；不为了少写几个参数创建宽泛的上下文容器或依赖包。
6. 在本轮触及的代码中可展开压缩分支、改善局部变量名称；不要伴随搬移重写条件表达式、LINQ 枚举时机或异常处理。
7. P1/P2 的纯搬移与必要的注释补充在审查时分别核对；不借搬移合并看起来相似但失败政策不同的实现。

### 2.2 详细中文注释与设计思路

新增或实质修改的核心类型、方法和关键分支使用中文注释。纯搬移保留已有有效注释，缺失设计理由时在相应边界补齐，不逐行翻译代码。

- 类型 XML 注释说明用途、输入输出、状态/资源所有者，以及与相邻职责的关系。
- 提取的注册方法说明登记哪组服务、生命周期选择、实例别名关系、何时才会创建实例，以及依赖的调用顺序。
- 涉及工厂、回滚、订阅和释放的注释解释“为什么必须在这里做”，包括成功交付前登记实际对象、按原顺序退订等设计思路。
- 明确区分“注册服务描述符”和“解析并创建服务”。不得把尚未解析的对象写成已经初始化。
- 表达需要保留的约束及原因，不重复历史版本记录，不在代码中粘贴测试数量、命令输出或长篇开发日志。

本轮重点解释四处：诊断先脱敏再进入输出；菜单与快捷键共享同一命令适配器；展示组合对象按原顺序释放；DI 工厂在成功交付前登记实际创建对象。

## 3. 行为等价边界

权威规则继续来自 [Host 兼容约束](../../Host/MyAvaloniaManagement/docs/reference/compatibility-contracts.md)、[内部架构](../../Host/MyAvaloniaManagement/docs/design/architecture.md)、[Workbench Command 契约](../reference/workbench-commands.md)及 [Host 启动契约](../reference/host-startup.md)。本计划不另建对外契约。

| 边界 | 必须保留 |
| --- | --- |
| 类型与接口 | 类型名、命名空间、可见性、构造函数及已有接口签名；不扩大 Host 导出面 |
| 诊断 | 错误码、阶段、受控消息、脱敏字段、分类优先级、记录顺序、镜像规则及持久化失败退化 |
| 展示 | 菜单排序和分隔符、Hide/Disable 区别、Host 快捷键优先与冲突双禁用、Palette 查询和当前目标重查 |
| 通知 | Menu/KeyBinding/Command 的 UI 同步范围、Palette 排队、锁与事件快照、观察者异常隔离、迟到通知处理 |
| DI | 描述符登记顺序、数量、生命周期、工厂与实例注册方式、可选服务的回退规则、同实例别名 |
| 所有权 | 唯一 Session、每 Document Scope、Tool singleton、ShutdownParticipants 登记、正常关闭和失败回滚 |
| 初始化时机 | 注册阶段不提前创建对象；后台插件组合与 UI 线程工作台组装边界不变 |
| 文件和设置 | 日志、manifest、Document、Layout、数据根、插件开关和重启设置格式及路径不变 |

发现实际缺陷时记录触发条件及独立修复建议；不能为让新结构成立而顺便改变这些规则。

## 4. V17-P1：诊断文件按现有职责拆分

源文件：[HostDiagnostics.cs](../../Host/MyAvaloniaManagement/Business/Diagnostics/HostDiagnostics.cs)。

建议在现有 `Business/Diagnostics` 目录内组织为下列文件；名称以现有类型为依据，实施时可微调小契约归组，但不得改变类型身份。

| 目标文件 | 放入的既有职责 |
| --- | --- |
| `HostDiagnosticContracts.cs` | Phase/Severity/Disposition、Draft、Record、`IHostDiagnosticSink` 等紧密相关契约 |
| `HostDiagnosticCodes.cs` | 稳定错误码常量 |
| `HostDiagnosticRedactionPolicy.cs` | 白名单字段、固定用户消息、受控技术详情 |
| `HostDiagnosticFailurePolicy.cs` | 严重程度和启动决策分类 |
| `HostDiagnosticSession.cs` | 会话记录、持久化、保留数量与默认镜像 |
| `HostSensitiveDiagnosticDebugOutput.cs` | 显式敏感开关及其独立输出 |
| `PluginLoadExceptionMapper.cs` | 既有插件加载异常到错误码映射 |

设计思路：这里已经具备策略、契约和会话边界，首批只改善物理组织。`HostDiagnosticSession` 仍独占锁、写入器与记录集合，不把日志存储再拆成新服务，不改造错误码映射表。

搬移时核对每个文件的 using、条件编译、特性及类型身份；原集合声明顺序、静态初始化和方法体保持。原文件搬空后可移除，不保留转发壳或重复类型。

退出条件：D 系列诊断矩阵和关联生命周期回归通过；按类名与文件名可直接定位；没有新增输出渠道、服务或共享状态。

## 5. V17-P2：命令展示文件按职责拆分

源文件：[WorkbenchCommandProjection.cs](../../Host/MyAvaloniaManagement/Business/Presentation/Commands/WorkbenchCommandProjection.cs)。

| 目标文件 | 既有职责与组织 |
| --- | --- |
| `WorkbenchPresentationCommandStore.cs` | 缓存同一 CommandId 的命令适配器，保留缓存与释放语义 |
| `WorkbenchMenuProjection.cs` | 菜单查询与通知；菜单接口和条目记录可就近保留 |
| `WorkbenchKeyBindingProjection.cs` | 快捷键冲突治理、可用性与通知；对应接口/记录可就近保留 |
| `WorkbenchCommandPresentation.cs` | 组合并拥有 Store、Menu、KeyBinding、Palette；保留创建与释放顺序 |

设计思路：已经独立的四个实现各自可检索，小契约与消费者靠近，保持现有协作。`WorkbenchCommandPaletteProjection`、`WorkbenchPresentationCommand` 和 `UiRefreshScheduler` 继续沿用原文件与实现。

保持命名空间、internal 边界、构造函数参数、事件订阅点和锁范围。不得把菜单与快捷键的通知逻辑抽成通用投影基类，不统一两者不同的诊断处理，也不改变 Palette 的排队行为。

同步核对 [覆盖率基线清单](../../Host/MyAvaloniaManagement.Tests/coverage-baseline.json) 中旧文件路径：实施时将旧实现的可执行代码对应到新文件，保留原门槛，不删除受保护逻辑来规避要求。纯契约文件无可执行行时明确说明映射方式。

当前开发 `verify` 不采集覆盖率；路径清单同步不等于完成覆盖率验证，也不意味着本轮执行发布覆盖率门禁。具体检查见专用验证文档。

退出条件：C 系列 Unit/Headless 回归通过；旧文件路径引用已核对；创建、共享实例和释放顺序保持。

## 6. V17-P3：服务注册长方法分组

源文件：[ServiceCollectionExtensions.cs](../../Host/MyAvaloniaManagement/Business/Composition/ServiceCollectionExtensions.cs)，入口为 `AddApplicationServices`。

设计思路：外层保留唯一组合入口与共享输入，私有方法描述连续的登记步骤。优先在同一静态类、同一文件内完成，读者可以顺序阅读；仅当实施后出现明确独立主题时再评估文件拆分，不预先引入 partial 或注册模块对象。

适合优先提取的连续段如下；名称是候选，不是新 API：

| 候选方法/段 | 责任与约束 |
| --- | --- |
| `RegisterLayoutServices` | 布局存储与布局生命周期；保留解析时登记实际实例的方式 |
| 工具中心、插件状态、帮助各自的注册段 | 各自服务与命令处理器；只提取足够完整的组，避免每行一个方法 |
| `RegisterWorkflowActions` | TimeProvider、动作目录、限制、授权、运行管理器和关闭端口；保留原实例别名及登记 |
| 文档、重启及 Host 命令的连续段 | 按现有相邻边界提取，跨段依赖通过 DI 延迟解析；不为了分组把注册移到另一位置 |
| `RegisterWorkbenchCommands` | 目录、上下文、状态、执行器、关闭门及展示；保留工作台对象的解析线程与时机 |
| `RegisterWorkspaceSession` | 工厂与 Session 创建、回调挂接、退出参与者登记及 Factory 别名；保留一个实际工厂实例 |

实施顺序：

1. 先按原顺序标注连续注册段和共享输入，记录现有生命周期与接口到实现的别名关系。
2. 每次只提取一个完整段，在原位置调用；原 lambda 和内部语句顺序尽量原样移动。
3. 保持传入与默认创建的 `PluginRegistryBuilder`、`PluginProviderOwner`、`DocumentScopeRegistry`、`HostShutdownParticipants` 为原实例，不在辅助方法内重新兜底创建。
4. 保持 `AddSingleton(instance)`、`AddSingleton(factory)`、`AddScoped`、`AddTransient` 的原选择；同实例接口映射仍通过既有解析返回，不变成第二份注册实例。
5. 保持 `GetService` 与 `GetRequiredService` 区别及诊断回退表达式；不得把可选服务变成强依赖。
6. `shutdownParticipants.Record` 留在原工厂内部，仍在真实对象创建成功后、返回前调用；不移到注册阶段，不通过提前解析来获得对象。
7. `HostDockFactory` 创建、`WorkspaceSession` 创建、`AttachCallbacks`、`Record(session)` 和返回的顺序不变。
8. 已有 `RegisterHostWorkspace`、`AddDocumentScopeManagement` 和 `AddViewModels` 保持入口及行为；本轮不顺手删除现有参数、重排登记或改变默认值。

入口缩短后仍应呈现真实组装顺序。若一个候选需要大量参数、多个 ref/out 或跨越资源生命周期，保留原段，不为达成行数目标继续抽取。

退出条件：S 系列组合、生命周期和启动回归通过；审查可逐段对应原始描述符顺序；没有提前解析、第二容器或新的资源所有者。

## 7. 阶段安排与开发门禁

| 阶段 | 工作 | 退出条件 | 当前状态 |
| --- | --- | --- | --- |
| P0 | 核对实施基线、运行完整本地 verify、将矩阵映射到真实测试；仅针对行为缺口先补测试 | 记录输入身份与基线结果；受影响既有失败未解决前不宣称等价 | 未开始 |
| P1 | 诊断类型搬移与必要中文设计注释 | D 矩阵及对应回归通过，文档定位同步 | 未开始 |
| P2 | 四个展示实现搬移、契约就近组织、路径清单同步 | C 矩阵通过，覆盖率清单无孤立旧路径或门槛降低 | 未开始 |
| P3 | 逐段提取服务注册方法 | S 矩阵通过，注册/解析/所有权约束均保留 | 未开始 |
| P4 | SOLID 与可读性审查、文档收口、最终完整本地 verify、证据记录 | 必需项无失败、无未解释跳过、无零发现，最终输入有完整开发证据 | 未开始 |

开发验证遵循“已有行为测试优先，存在缺口才补测试”。不为纯文件搬移编写断言文件名、私有方法名或固定行数的单元测试。新增测试必须观察实例身份、寿命、通知、输出或失败边界，并先在原行为上成立。

专项测试按阶段执行；最终完整 `verify` 不能由过滤后的测试代替。Gate 自测只在实际修改 Gate 实现、配置消费或证据逻辑时增加；本计划不要求修改 Gate 生产代码。详细命令与通过标准由专用验证文档维护。

出现行为差异时停止进入下一阶段，缩小或撤销本阶段提取。按阶段回退相关代码与说明，保留失败证据、有效测试和用户无关修改，不执行全仓重置或删除用户数据。

## 8. 文档同步与专用记录

| 文档 | 计划阶段 | 实施阶段 |
| --- | --- | --- |
| 本计划 | 描述范围、SOLID、设计理由及未开始的阶段 | 回填实际文件划分、方法分组和完成状态；候选不自动升级为实施项 |
| [V17 专用开发验证](../maintenance/host-v17-readability-verification.md) | 给出 D/C/S 矩阵、开发命令与证据规则 | 对应到实际测试方法及执行结果 |
| [总导航](../README.md)、[待办](README.md)、[Host 文档入口](../../Host/MyAvaloniaManagement/docs/README.md)、[主仓验证](../maintenance/verification.md) | 添加 V17 计划和验证入口，明确尚未实现 | 更新真实阶段状态与证据链接 |
| [内部架构](../../Host/MyAvaloniaManagement/docs/design/architecture.md)、[设计取舍](../../Host/MyAvaloniaManagement/docs/design/design-methodology-and-tradeoffs.md) | 保持当前实现事实，不提前描述新文件为已存在 | 更新源码定位与注册职责，补详细中文设计思路，保留原所有权说明 |
| 当前契约和使用指南 | 行为不变，无需新增用户流程 | 核对路径引用；确有文字过时时定向修正，不复制第二套契约 |
| 开发记录与非嵌入 JSON | 本次不创建虚构结果 | 首次实施后创建 `docs/archive/records/host-v17/development-acceptance.md` 与 `final-development-evidence.json`，再接入历史导航 |

实施完成前核对新增文档、拆分源码相关引用和覆盖率路径清单。历史验收记录保留当时事实，不批量改写为新路径；新记录说明旧文件到新文件的对应关系。

仓库 Markdown 会嵌入 Host 帮助。实施时先定稿 Markdown，再运行最终 verify，最后将实际结果写入非嵌入 JSON；Markdown 若再次变化，应重新取得对应最终输入的验证。具体次数、TRX 和哈希只记录实际值。

## 9. 完成定义

- 三项首批重构完成，范围未扩展到清单解析、工作区关闭或 Workflow 编排。
- 主要类型可按文件名直接定位，服务入口可按职责顺序阅读；没有为缩短代码引入多余层次。
- SOLID 审查通过，资源所有者、依赖方向、接口边界及行为等价约束均保留。
- 详细中文注释能解释关键顺序、共享实例、延迟创建与释放设计，并与代码一致。
- D/C/S 测试矩阵有真实测试映射和执行证据；最终完整本地 verify 对应最终输入且成功。
- 文档、导航、帮助链接和拆分文件的覆盖率基线映射已同步；未改写历史通过事实。
- 未执行 AIFLOW、Windows CI、发布门禁、部署或发布；开发通过不被表述为发布通过。
