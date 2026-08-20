# G11：删除低价值 public 面和占位代码

> 后续状态：本文记录的 SHA256 是 G11 收口后的临时门禁，已由 G13 的 243 条 v1 Shipped 签名和
> `Test-PluginSdkCompatibility.ps1` 替代；本文原始验收数据继续作为时间点证据保留。

> 状态：已完成
> 完成日期：2026-08-20
> 适用范围：Plugin SDK、Host Document 创建与保存、MySmallTools 播放器交互、DaTang 发票导入、Host/BiliDownloader 单向转换器及对应门禁
> 前置任务：G3；后续任务：G13 正式 API 基线

## 1. 结论

G11 在 Managed Plugin v1 正式冻结前完成了一次破坏式收口。SDK 不再公开没有稳定语义的自由参数、
没有生产实现的保存路径策略和只有单个界面消费者的通用反射 Behavior；DaTang 发票 Document 也不能
再绕过 View Registry、Document Scope 与关闭令牌自行创建 ViewModel。单向转换器仍实现上游接口要求的
`ConvertBack`，但以 `NotSupportedException` 准确表达“不支持反向转换”。

本次没有增加兼容 Facade、通用参数包、Mediator 或新的基础设施。`IPluginLifecycleDependencies`、
`IDocumentCreationIntentProvider`、`IDocumentLifetime.ClosingToken`、`IWindowContentFullscreenHost`、
历史 ID、布局迁移和插件业务数据迁移均保留。SDK 仍是预封板 `1.0.0`，磁盘 schema、manifest schema
和插件内容 schema 均未改变；由于 public API 已破坏式收口，所有仓库插件已经随本次变更重新构建。

## 2. 删除项、替代机制与重新引入条件

| 删除或收口项 | 删除前的真实消费者 | 当前替代机制 | 允许重新引入的条件 |
|---|---|---|---|
| `DocumentCreationParams.AdditionalData` | 无调用方 | 插件业务输入进入插件自有的强类型 ViewModel 或服务 | 至少存在一个经审阅的跨 Host/插件稳定用例，并以明确类型设计新契约；不得恢复 `object` 参数包 |
| `DocumentCreationParams.InitializationData` | 仅 Welcome 策略读取，但 Host 从未提供输入来源 | Welcome 使用宿主固定正文；入口差异使用 `CreationIntentId`；业务数据使用插件私有强类型模型 | 出现不能由创建意图或强类型插件模型表达的真实跨边界需求，并按新 SDK 版本审阅；不得恢复自由字符串占位 |
| `IDocumentSavePathPolicy` | Host 做类型判断并配有测试替身；无插件生产实现 | 无路径时由 Host 选择目标，已有路径直接覆盖；损坏文件恢复由 Host 内部 `DocumentRecoveryRegistry` 强制另存并保护原件和备份 | 出现真实、稳定且不能由 Host 状态/恢复注册表拥有的路径规则，并先明确单一所有者和版本兼容语义 |
| `HandledEventsAwareBehavior` | 仅 MySmallTools 播放进度条一个 XAML 消费者 | `PlaybackTransportView` 对命名 Slider 使用 `AddHandler(..., handledEventsToo: true)`，只把手势转发给已有开始/结束拖动命令 | 至少出现两个语义一致、经过 UI 契约审阅的消费者；插件仍应直接声明所需 UI 包，不能借基础 SDK 传递依赖 |
| Common 空 `Chain` 项 | 无代码；G3 已删除 | G11 项目结构门禁禁止其恢复 | 有实际源文件和清晰职责时按普通目录加入，不允许空占位 |
| `InvoiceInfoImportViewModel()` 与 XAML 自建 DataContext | DaTang XAML 运行时直接构造路径 | View Registry 显式建 View，Document Scope 注入业务接口、文件对话框和非空 `IDocumentLifetime` | 不恢复生产无参构造；若设计器确有需要，应使用不进入运行期组合根的专用设计时数据 |
| 转换器中的 `NotImplementedException` | Host 文件图标与 BiliDownloader 单向转换器 | 保留上游 `IValueConverter.ConvertBack` 签名，并抛含中文原因的 `NotSupportedException` | 只有产品真正支持可验证的双向转换时才实现反向逻辑 |

`DocumentCreationParams` 现在只公开 `DocumentTypeId`、可选 `Title` 和可选 `CreationIntentId`。
`PluginRegistry` 规范化历史 ID 时只复制这三个正式字段，标题和创建意图不会丢失。

## 3. SOLID 与朴素设计取舍

- 单一职责（SRP）：保存路径和恢复保护统一由 Host 状态与恢复注册表拥有；插件只提供内容快照，不参与路径事务。播放器 View 只适配 UI 手势，播放状态仍由现有命令和 ViewModel 管理。
- 接口隔离（ISP）：删除无生产实现的保存策略和无语义创建字段，插件不再依赖自己永远不会实现或读取的成员。
- 依赖倒置（DIP）：DaTang ViewModel 必须显式依赖业务接口、对话框与 `IDocumentLifetime`；DataContext 只能由注册表和 Document Scope 注入，不使用隐藏的运行时自建路径。
- 里氏替换（LSP）：单向转换器保留接口成员，以 `NotSupportedException` 明确声明能力边界；不会用“尚未实现”误导调用方。
- 开闭原则（OCP）：历史 ID 规范化、创建意图和插件自有强类型模型继续承担扩展点；没有为假想需求保留自由字典或兼容转发层。

播放器没有新建插件级通用 Behavior。局部代码直接订阅 `PointerPressed`、`PointerReleased`，使用
`handledEventsToo: true` 保留 Slider 已处理事件场景，并在执行现有命令前调用 `CanExecute`。这一实现
可从 View 一眼看到事件来源和目标命令，也避免反射属性名、运行期类型错误与基础 SDK 的 UI 依赖扩张。

## 4. 对现有插件的影响与迁移

| 插件 | 实际影响 | 已完成迁移 |
|---|---|---|
| MySmallTools | 不能再从基础 SDK 使用 `HandledEventsAwareBehavior` | 播放进度 Slider 改为 View 内定向事件适配；既有拖动命令、暂停计时和最终 Seek 语义不变 |
| DaTangAccountingHelpPlug | 发票导入 ViewModel 不能无参构造，测试或宿主组合根必须提供 Document Scope 生命周期 | 删除 XAML DataContext 自建；构造函数显式注入三个依赖；异步令牌链接命令令牌与 `ClosingToken` |
| BiliDownloader | 调用单向转换器的 `ConvertBack` 时异常类型从 `NotImplementedException` 变为 `NotSupportedException` | 所有单向转换器和测试统一更新；正向转换无变化 |
| MyPlugTest | 没有被删除 API 的源码调用 | 仅随 SDK 重新还原、构建和包验证 |

仓库外插件若使用了被删除成员，必须删除对应赋值或实现：创建参数只传三项正式属性；保存路径交给 Host；
UI Behavior 迁到插件自己的 View 或由插件直接依赖相应 UI 包；需要创建 DaTang ViewModel 的测试组合根
必须注册 `AddDocumentScopeManagement()` 或显式提供有效的 `IDocumentLifetime`。本次不提供 `Obsolete`
转发层，因此旧二进制或旧源码不能被当作仍受支持；应针对本次 SDK 重新编译。

## 5. 依赖和兼容门禁

- 基础 SDK 项目和 nuspec 不再直接或传递引入 `Xaml.Behaviors`；Host 文件树仍有真实用途，因此由 Host 保留自己的直接依赖。
- public API 临时 SHA256 更新为 `AD87DDEDA904C266CED5236CBDCB22BB03FD0016FF3D4CDA92B666014A078C5C`，并辅以可读反射断言，不只依赖哈希失败定位。
- 反射门禁确认四个删除项不存在、`DocumentCreationParams` 只有三个正式属性，同时确认生命周期依赖、创建意图、关闭令牌和全屏宿主仍存在。
- SDK 包门禁先编译最小正向消费者，再分别编译创建占位字段、保存路径策略和通用 Behavior 反例；反例必须失败，且不提供兼容层。
- 精确源码搜索确认生产代码中没有四个删除符号、Host/BiliDownloader 转换器没有 `NotImplementedException`，项目 XML 没有 `Chain` 项。

## 6. 测试证据

2026-08-20 使用本次生成的 TRX、Cobertura 和脚本输出记录结果，没有沿用 G10 的固定数量：

| 门禁 | 结果 |
|---|---|
| `dotnet restore MyAvaloniaManagement.sln --locked-mode -p:SkipPluginDeploy=true --nologo` | 通过 |
| Release 解决方案构建 | 0 警告、0 错误 |
| Host 综合门禁（含 Windows Smoke） | Unit 168、UI 38、Plugin 146，共 352/352；Windows Smoke 通过 |
| Host 覆盖率 | 行 80.62%，分支 65.91% |
| BiliDownloader | 720/720 |
| DaTangAccountingHelpPlug | 64/64 |
| MySmallTools | 183/183 |
| `scripts/Test-PluginSdkPackage.ps1` | 包内容、依赖图、正向消费者及 G5/G8/G9/G11 反向编译全部通过 |

新增或加强的验证覆盖：Welcome 不再接受初始化正文；历史 ID 规范化保留标题和创建意图；DaTang
ViewModel 无无参构造、Scope 实例隔离和关闭令牌抑制迟到提交；播放器 View 可 Headless 加载、命名
Slider 存在且事件适配触发既有拖动命令；Host/BiliDownloader 每个单向转换器的 `ConvertBack` 均抛
`NotSupportedException`；恢复副本仍强制新路径并保护损坏原件和备份。

## 7. 失败复跑记录

门禁过程中出现三处有效反馈，均按生产组合规则修正，没有恢复 nullable 生命周期或兼容旁路：

1. 最初把两个已删除类型放在同一个反向编译夹具时，编译器只稳定报告其中一个符号。门禁改为保存策略和 Behavior 两个独立夹具，使每个删除事实都有独立诊断证据。
2. Host Managed-only 插件测试的旧手写组合根未注册 `IDocumentLifetime`，DaTang 完整构造因此主动失败。测试改为使用生产 `AddDocumentScopeManagement()` 注册。
3. DaTang 自身 Scope 测试也有同类手写注册，按相同方式改为生产注册后完整复跑 64/64。

这些失败证明旧测试组合根确实曾绕过生产 Document Scope。修正测试装配比给 ViewModel 恢复无参构造
或可空生命周期更符合依赖倒置，也让测试和生产保持同一生命周期边界。

## 8. 回滚边界

若单个插件交互回归，可只回滚该插件的 View 适配或构造根修正；Host 的恢复注册表、磁盘格式和内容
schema 不受影响。若要恢复任一 SDK public 成员，必须把它视为新的 API 设计变更，提供真实消费者、
明确所有权、版本策略、正反向包消费测试和迁移说明，不能仅为了让旧源码重新编译而恢复占位成员。
