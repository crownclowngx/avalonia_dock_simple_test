# MyAvaloniaManagement V9：Avalonia / Dock 升级与定制逻辑整理方案

> 归档说明（2026-09-16）：正文描述当时的基线、方案或验收，不代表当前运行状态。原始命令、版本和结论保留；当前操作见[文档导航](../../README.md)。原路径中的构建产物可能已清理，外部插件材料为可选引用。

> 后续授权：用户已明确要求上传 NuGet 并统一全部自有包版本为 `3.4.1`。此次追加仅覆盖 NuGet 包，实际步骤与结果见 [统一发布记录](../records/host-v9/nuget-unified-3.4.1-release.md)；下文开发阶段约束保留为原始范围，Host 安装程序发布仍另行验收。

> 状态：代码及本地开发自动化已完成；真实交互/外部业务待验收，G8 发布未执行。实测结果见 [V9 专用实施记录](../records/host-v9/development-acceptance.md)。
> 本轮执行约束：用户已授权独立分支及阶段 Git 提交。SOLID 优先、朴素设计、详细中文注释；不使用 AIFLOW、Windows CI、seal 或发布门禁。G8 的发布、部署、产品升版及公开包上传推迟到发布阶段；本轮执行 G0–G7 的开发工作，SDK/模板只生成本地候选。
> 日期：2026-09-16。源码核查基线：`bc3d877fc9b72810a060c2288b1d63cd6fdd0b19`。
> V9 是主项目改造编号，不代表 Avalonia、产品、SDK 或磁盘协议的主版本。
> 实施原则：升级 Host，验证并保留旧插件二进制；保持禁止浮动、四向停靠、Tool 隐藏、Document 关闭与资源所有权语义。

## 1. 目标与完成定义

本方案将 Host 从 Avalonia 12.1.0 / Dock 12.0.0.2 升到 Avalonia 12.1.2 / Dock 12.1.0.6，并处理升级涉及的 Host 定制。

实施进展和实际证据以 [V9 专用开发验收](../records/host-v9/development-acceptance.md) 为准；[使用说明](../../quick-start/host-v9-upgrade.md)记录当前候选包与旧插件边界。下文的原始评估、阶段要求与发布步骤继续保留，不能将未执行的人工/发布步骤视为完成。

完成条件：

1. 框架依赖、锁文件、Host 与 SDK 编译基线一致，Release 构建无新增警告。
2. 每项定制都有明确结论和验证证据，不能只改包版本，也不能整批删除保护代码。
3. 当前安装的 12 个插件使用升级前冻结的 DLL，在新 Host 中通过加载、组合及核心交互回归。
4. Document/Tool 的视图、Scope、焦点及布局行为符合本方案的验收矩阵。
5. 完整本地 `verify` 通过；真实窗口和视频检查单独记录，不能用 Headless 成功替代。
6. 发布包、旧插件哈希、回退材料及实施记录齐全。开发验收、实际部署、公开 SDK 发布分别记录状态。

本次不要求 11 个外部插件仓库批量改版本、重新编译或重新打包；不启用 Dock 浮动窗口；不改变插件 public API、manifest schema 2、Document envelope schema 2、layout schema 2 和数据根 generation 2。

## 2. 已核实事实与前次评估修正

### 2.1 当前 Host 已禁止浮动

以下是当前源码事实，不是本次新增限制：

- `ManagedDocumentDockable` 和 `ManagedToolDockable` 设置 `CanFloat = false`。
- `DockWorkspaceBuilder` 为根布局调用 `HostDockFactory.DisableFloating`。
- `HostDockFactory` 覆盖两种 `FloatDockable` 和两种 `FloatAllDockables`，均不执行浮动。
- `DockFloatingDisabledTests` 验证浮动无效，同时 Drag / Drop 和主窗口内部移动仍有效。

因此，前次评估把“跨浮动窗口拖放”列为当前生产必测路径过于宽泛。V9 应验证“拖出主窗口不生成浮动窗口、主窗口内部拖拽仍正常”。视频全屏通过 Host 自己的全屏端口处理，仍需要回归，不能与 Dock 浮动混为一谈。

### 2.2 已完成的是升级可行性实验

在独立临时源码副本中，仅调整框架及 Headless.XUnit 版本后：

| 项目 | 实际结果 | 不能据此推定的结论 |
| --- | --- | --- |
| Release Host 编译 | 0 警告、0 错误 | 不代表真实窗口交互已通过 |
| Host 单元测试 | 397 / 397 | 不代表外部插件业务流程已执行 |
| 现有插件基础测试 | 217 / 217 | 未执行需要 Gate 注入 ZIP 的 PackageAcceptance |
| Headless UI 测试 | 99 / 99 | 不代表 HWND、视频和真实鼠标捕获已通过 |
| 已安装旧插件二进制探测 | 12 / 12 | 仅覆盖清单、类型解析、入口结构与共享程序集；未构造业务视图 |

实验使用 SDK 3.4.0，尚未验证下文 SDK 3.4.1 / Templates 1.4.2 候选；这些版本必须在 V9 实施中另行验证。

证据见 [前次评估](avalonia-dock-upgrade-assessment-20260916.md) 和 实验摘要（历史产物位置，文件已清理：`artifacts/upgrade-assessment-20260916/summary.json`）。这些实验不等同于 V9 已完成。

## 3. 版本与交付边界

### 3.1 本轮冻结的候选版本

| 项目 | 当前 | V9 候选 | 处理 |
| --- | --- | --- | --- |
| Avalonia 核心 / Desktop / Fluent / Fonts 及同线平台包 | 12.1.0 | 12.1.2 | 同步升级 |
| Avalonia.Headless.XUnit | 12.1.0 | 12.1.2 | 与运行时对齐 |
| Dock 家族 | 12.0.0.2 | 12.1.0.6 | 同步升级全部直接和传递包 |
| Core/UI SDK | 3.4.0 | 3.4.1 | 作为新的公开编译基线，保持 API 兼容 |
| Templates | 1.4.1 | 1.4.2 | 使用 SDK 3.4.1 和 Avalonia.Desktop 12.1.2 |
| Host 产品 | 3.0.0 | 3.0.1 | 正式交付时区分新旧 Host，非 V9.0.0 |
| Semi / Ursa | 12.1.0 / 2.1.0 | 保持 | 实验没有发现必须升级的依赖阻断 |
| WebView / Xaml.Behaviors | 12.1.0 / 12.0.5 | 保持 | 独立包版本线，不机械与 Avalonia 对齐 |
| Workflow SDK / Icons / Plugin.Build | 1.0.0 / 1.0.0 / 1.1.3 | 保持 | 无对应协议或 API 改动 |
| 现有插件版本及 SDK 区间 | 各自原值 / `[3.4.0,4.0.0)` | 保持 | 冻结原始产物用于回归 |

上述包版本以编写日的最新稳定版为基准，实施期间不继续追随新版本漂移。G0 检查候选产品/SDK/模板版本是否已被其他发布占用；若占用，只分配下一个未使用的同线修订号并记录，不覆盖已有包。

`Directory.Version.props` 中产品和 SDK 的 Package / File / Assembly / Informational 相关属性同步维护，Core/UI 继续遵循仓库同版本规则。已编译插件仍请求 3.4.0，Host 提供兼容的 3.4.1，符合当前共享程序集版本规则。

### 3.2 插件可以保持旧版的条件

- Host 继续由默认加载上下文提供共享框架和 SDK DLL，插件包不私带副本。
- 旧 SDK public API、语义资源键及返回类型保持兼容。
- 不把旧插件清单下限改为 3.4.1 来“修复”验收；旧产物是本轮兼容性输入。
- UI SDK 的精确 NuGet 依赖在新包中改为 Avalonia 12.1.2，不能重新发布不同内容的 3.4.0 包。
- 新模板的 SDK 下限改为 3.4.1，上限仍为 4.0.0，使旧 Host 可在入口执行前拒绝新编译基线。
- 旧插件自己的 Standalone / Headless 项目可以继续保持旧版本；这不能作为新 Host 环境的验证结果。

## 4. 定制逻辑逐项结论

| 定制 | V9 决策 | 原因 | 放行依据 |
| --- | --- | --- | --- |
| App.axaml 的 `dock|HostWindow` 原生标题栏等覆盖 | **删除这一组遗留样式** | 当前生产禁止浮动，不再以这些样式维持浮动行为 | 禁浮动、内部拖拽、主窗口标题栏测试通过 |
| `dock|HostWindow dock|DocumentTabStrip` 的 `EnableWindowDrag=False` | **随上项删除** | 同属浮动窗口专用策略 | 任何生产入口均不创建浮动窗口 |
| MainWindow.axaml 的原生标题栏 | **保留** | 这是实际主窗口的产品行为 | 标题栏、最大化、缩放正常 |
| DockTabPointerCaptureGuard | **保留核心捕获，收窄恢复逻辑和注释** | 上游排序助手没有替代这项保护；捕获移交时存在职责重叠 | 真实拖拽/取消/移交测试，不能只测布尔辅助方法 |
| DocumentControlRecycling | **保留，改造脱离父级的处理，验证实例传播** | 上游缓存删除不等于 Host 生命周期释放 | 单一父级、绑定保存、实例身份、精确释放与泄漏检查 |
| HostDockFactory | **保留适配、关闭顺序及禁浮动策略** | 这些是 Host 行为与 SDK 隔离边界 | 回调顺序、关闭拒绝、Tool 隐藏和可观察集合测试 |
| DockDocumentLifetime / ManagedDockableViewLease | **保留所有权与 finally 兜底** | Dock 不拥有插件 View/DI Scope 生命周期 | 正常关闭、取消、异常与退出均只释放一次 |
| DockLayoutSnapshotMapper / Layout V2 | **保留协议，仅按失败用例作局部适配** | Host 自有布局语义，上游 serializer 不能替代 | 旧快照重放、缺失插件、四向比例与隐藏/固定状态 |
| ToolDockCoordinator | **保留，验证新版事件/集合下的重入边界** | 顶部/底部全宽、稳定 ID、隐藏恢复属于产品规则 | 四方向移动、最后 Tool 隐藏和重复归一化无漂移 |

### 4.1 App.axaml：清理浮动专用样式

修改位置：`Host/MyAvaloniaManagement/App.axaml`。

删除 `dock|HostWindow` Style（WindowDecorations、ExtendClientAreaToDecorationsHint、ToolChromeControlsWholeWindow、DocumentChromeControlsWholeWindow 四个 Setter）以及 `dock|HostWindow dock|DocumentTabStrip` Style。同步删除“继续使用独立系统窗口”“标签可浮动”等过时注释。

保留 Fluent/Semi/Ursa/Dock 主题、Dock 预设资源、Host 语义资源、`ControlRecyclingKey` 配置和三类标签的指针保护。不要删除仍被标签选择器使用的 `dock` xmlns，也不要修改 `Views/MainWindow.axaml` 的标题栏设置。

`WindowChromeAndDockDragGuardTests` 目前硬编码要求这些浮动样式存在，必须改写该用例：保留三类标签保护和主窗口标题栏断言，把产品保护重心放到 `DockFloatingDisabledTests`。不得删除整个测试类来绕过失败。

`HostWindowLocator` 暂时保留为 Dock 工厂协议配置；它本身不是浮动开关。不要用“删 Locator”替代能力策略和 Factory 方法的禁止行为。若未来清理它，须单独验证框架 fallback，不并入本轮无依据删除。

### 4.2 DockTabPointerCaptureGuard：保留原因和改造边界

已核对 Dock `v12.0.0.2` 与 `v12.1.0.6` 的 `ItemDragHelper.cs`，下载文件 SHA-256 相同。其 PointerPressed 只设置内部 `_captured` 标记，没有调用 `IPointer.Capture`。因此“升级后上游已经处理早期捕获，可以删除保护”的依据不成立。

[上游 #1137](https://github.com/wieslawsoltes/Dock/pull/1137) 修复的是 DockControl 脱离视觉树时的拖拽转交与重入，不等于替换标签排序捕获保护。

实施任务：

1. 保留左键、按钮排除、外部捕获不抢占、首次移动捕获，以及只释放自身捕获的规则。
2. 将“首次激活浮动窗口”等注释改为实际适用的窗口激活规则；主窗口失活后首次手势仍可能需要激活，不因禁止浮动就直接删除 Activate 调用。
3. 区分“Dock 接管捕获且手势仍有效”与“释放/取消/结束后的残留”。前者停止 Host 捕获，不安排会覆盖正常拖拽的视觉重置；后者才允许延迟恢复。
4. 保留交互序号，防止旧恢复回调影响新一轮拖拽；保留正常完成时不覆盖 RenderTransform 的规则。
5. Detach / Deactivate / Dispose 路径要验证实际行为；不假设所有 Detach 都等同于取消。无证据时不增加全局钩子或复制 Dock 内部状态机。
6. 若新测试证明某条恢复分支从不需要且可完全删除，单独提交该删除及前后对照证据；V9 默认不整体删除 Guard。

### 4.3 DocumentControlRecycling：保留所有权，改进挂载边界

必须先修正当前类注释：“Dock 官方回收器只提供全量清空”已不准确。目标版具体 `ControlRecycling` 类有 `Remove(object)`，但它只删除字典项；`IControlRecycling` 接口不包含单项 Remove。它没有本项目的 PreparedView、关闭令牌、View 租约及 DI Scope 语义。

因此不改为直接使用上游回收器，不通过继承上游类来取得其私有缓存，也不让 Framework Cleanup 取代 `DockDocumentLifetime.Release`。

**A. 修正父级解绑（本轮改造项）**

- 先识别实际视觉父级 ContentPresenter，再检查逻辑父级/其他视觉父级。
- 修改 ContentPresenter / ContentControl 内容时使用 `SetCurrentValue`，保留现有绑定。
- 先确认 presenter.Child、contentControl.Content 或 decorator.Child 确实指向目标控件，避免清空已经承载其他文档的容器。
- ContentPresenter 清空后调用适当的 `UpdateChild()` 并验证旧 View 已脱离；不要只假设 Content=null 会即时移走 Child。
- 不能安全解绑时，不把仍有父级的同一 PreparedView 交给第二个容器；给出可定位的失败，禁止另建一个 View 掩盖所有权错误。
- 保留缓存命中返回同一个 PreparedView、单项删除、DataContext 清理、焦点引用清理和 View 最多 Dispose 一次。
- 不把 `Clear()` 擅自改成 Dispose 所有视图；批量退出仍由 Workspace/Runtime 的既有关闭链负责，避免新建第二个资源所有者。

这些是依据上游实现和当前定制差异确定的加固项，不宣称它们已在当前产品造成故障。先新增可复现边界用例，再改实现。

**B. 验证新版按 Factory 共享回收器（升级关键项）**

目标版 `DockControlFactoryService.InitializeControlRecycling` 用 Factory 作为键记住最先取得的回收器；后续 DockControl 会复用该实例。若首次初始化没有取得 Host 自定义实例，上游可能创建默认回收器并继续传播它。

- 保留 App 注入的 Runtime singleton 和 `ControlRecyclingKey`。
- 验证根 DockControl、分割产生的 DockControl、自动隐藏/固定预览相关控件均取得同一个 Host 回收器实例。
- 测试必须跨 ApplyTemplate / Layout 初始化和分割后的实际控件树，不能只断言资源字典里存在这个键。
- 验证不同 Host Runtime / Factory 之间不共享回收器。
- 若发现初始化顺序问题，在控件首次设置 Layout / 应用模板前明确提供实例；不要用定时器反复覆盖或把实例升为跨 Host 静态单例。
- 上游 `PruneControlRecycling` 对具体 `ControlRecycling` 类型执行缓存清理，当前自定义实现不会走该分支。继续由 Host 自己执行最终释放，不把它误判为升级后重复清理已自动解决。

### 4.4 HostDockFactory 与 DockDocumentLifetime：保留顺序和职责

必须保持：

- `HideToolsOnClose = true`，Tool 关闭仍表示隐藏，不 Dispose 插件 singleton。
- `OnDockableClosing` 先执行 Host 脏文档保护，再执行基类；基类拒绝或抛出时撤销 closing 状态。
- `OnDockableClosed` 在基类通知后通过 finally 回交 Session。
- `DockDocumentLifetime.Release` 即使回收 View 抛出，也在 finally 释放 Adapter/Scope。
- View 租约、关闭令牌和 Scope 释放各自幂等；标签切换、重新停靠、Tool 隐藏不进入最终释放。
- 保留基类 InitLayout；目标版在其中初始化追踪并归一化 VisibleDockables，不复制这些行为到 Host。
- 不缓存 InitLayout 之前的 VisibleDockables 集合引用；上游可能把普通 IList 替换成可观察集合。

现有编译及测试没有证明这些类需要结构性重写。只在新增边界测试失败时局部适配，不新增公共 SDK 方法，也不把 Dock 类型暴露给插件。

### 4.5 布局映射与工具停靠：保持 V2，不迁移为 Dock serializer

- 保留 `ValidateContributions -> 补齐必要 Dock -> Validate -> ApplySnapshot` 的职责顺序，未知/不可用插件不能留下部分修改的布局。
- 保留四向稳定 ID、顶部/底部全宽布局、比例、Tool 顺序、固定/隐藏状态和恢复默认布局。
- `ToolDockCoordinator.OnDockableDocked` 继续在基类处理完成后归一化；保留重入保护和对源集合的快照枚举。
- 新版可观察集合替换后，所有 Insert / Remove / Move 使用当前 Dock 集合或 Factory 操作；不要向已经失效的旧 List 写入。
- 验证上游自动设置 ActiveDockable 后，Host 仍能恢复指定活动 Tool/Document，不覆盖用户状态。
- layout schema 保持 2；保存新布局后，应可由升级前 Host 正确读取并还原本来支持的字段。
- Mapper 中扫描 root.Windows 的防御性读取暂时保留；它不代表支持浮动，也不是本次清理的必要前提。

## 5. 分阶段执行清单

各阶段在 `docs/plan-history/host-v9/` 写入简短记录，包含提交、改动文件、命令、结果和未完成项。以下全部是实施任务，当前不得勾为完成。

### G0 — 冻结源码、旧插件与布局输入

- [ ] 记录实际 HEAD、工作树改动、.NET SDK 和所有候选版本；保留用户已有改动。
- [ ] 使用独立工作区/源码副本执行升级，证据写入 `artifacts/host-v9/`。
- [ ] 将当前完整 Controls 复制到只用于回归的目录，保存每个文件相对路径和 SHA-256，不只保存入口 DLL。
- [ ] 记录插件 ID、版本、入口、SDK 区间和私有依赖；发现缺文件先换用完整发布包，不能重编译旧插件替代。
- [ ] 冻结默认、四向布局、隐藏 Tool、固定 Tool、缺失插件和旧版合法布局样本；用户数据仅使用副本。
- [ ] 保存升级前 Host 完整发布产物与对应布局副本，检查路径无误，供回退使用。

通过条件：源码、旧插件、旧 Host 和布局输入可定位且不可混淆。

### G1 — 只切换框架版本，建立对照

- [ ] 修改 Directory.Version.props 的 Avalonia / Dock 属性，修改 Headless.XUnit，更新受影响锁文件。
- [ ] 先保持全部定制逻辑，构建并执行现有 Host、Plugin、UI 测试，建立“新框架 + 原定制”对照。
- [ ] 检查 Dock 包家族一致，没有误引 `.v11` 包；检查 Avalonia 平台包一致，无精确版本冲突。
- [ ] 保留 Semi、Ursa、WebView、Xaml.Behaviors、SkiaSharp 等本轮未要求升级的包；传递变化必须有解释。

通过条件：编译零新增警告；依赖图合理；失败有归因。不得同时清理定制来掩盖框架升级本身的变化。

### G2 — 删除失效的浮动窗口样式

- [ ] 按 4.1 删除两组 Style 和过时注释，调整对应样式测试。
- [ ] 保留四个 Factory 禁浮动覆盖、根级策略和两个 Adapter 的 CanFloat=false。
- [ ] 验证内部拖动、分割和 Tool 移动正常，所有浮动入口无效，主窗口标题栏不变。

通过条件：没有新浮动窗口；清理不改变实际产品功能。

### G3 — 指针保护适配

- [ ] 按 4.2 增加实际路由事件/捕获移交测试，保留原有布尔与幂等测试。
- [ ] 修正恢复逻辑的手势结束边界与注释；按真实失败结果作最小修改。
- [ ] 执行第 7 节 P01–P05 的真实鼠标矩阵并记录恢复诊断是否异常增长。

通过条件：无残留拖拽视觉、无误排序、无捕获争夺；点击关闭按钮仍可正常关闭。

### G4 — 回收器与关闭生命周期

- [ ] 按 4.3 实施安全解绑，补绑定保留、ContentPresenter 更新、逻辑/视觉父级不一致的用例。
- [ ] 加入同 Factory 多 DockControl 复用 Host 回收器的实例身份测试，以及双 Runtime 隔离测试。
- [ ] 运行四向分割、View 单一父级、精确删除、弱引用释放、关闭取消和异常清理矩阵。
- [ ] 修正文档/中文注释中的“上游只有 Clear”说法。

通过条件：一个 Document 始终只有一个 PreparedView；切换不 Dispose，最终关闭恰好释放一次；其他文档不受影响。

### G5 — Factory、布局与 Tool 行为

- [ ] 对照 4.4 / 4.5 运行已有布局和回调测试，补集合替换、重复初始化、最后一项移除和活动对象恢复用例。
- [ ] 重放 G0 布局；新 Host 保存后用旧 Host 的独立副本验证回退读取。
- [ ] 任何适配限制在 Host internal 层；保持磁盘格式和 SDK 公共签名。

通过条件：四方向停靠、隐藏/固定/恢复不漂移；缺失插件回退不产生半恢复状态。

### G6 — 发布编译基线与完整本地验证

- [ ] Core/UI SDK 候选改为 3.4.1；同步版本属性，保持现有 API Shipped 基线，不为升级删除旧签名。
- [ ] Templates 候选改为 1.4.2，更新嵌入的 SDK / Avalonia.Desktop 精确版本和新模板 SDK 下限；使用隔离 feed 重新生成模板测试锁文件。
- [ ] 测试使用本次候选 nupkg；避免从公共源或全局缓存拿到同名旧内容。包的 Restore 来源和 SHA-256 进入证据。
- [ ] 运行完整本地 Gate verify，覆盖 SDK/API、MyPlugTest 打包与真实 ZIP 验收。
- [ ] 用候选模板在独立目录创建新插件，验证还原、Standalone 构建、测试和打包。
- [ ] 保留旧 SDK/旧清单测试夹具，不做全仓数字替换；Workflow/Icons/Build 不因版本整理被动重发。

通过条件：新的开发基线可消费；旧插件契约仍兼容。NuGet 上传属于实际交付动作，本阶段先准备可审阅的候选制品。

### G7 — 新 Host + 冻结旧插件回归

- [ ] 将 G0 的完整旧 Controls 复制到候选 Host 测试发布目录；测试前后逐文件核对哈希。
- [ ] 将前次临时二进制探测改为可复用验收入口：插件根通过专用参数/环境变量输入，不把个人绝对路径写死到正式测试。
- [ ] 加载/类型检查之后继续执行注册组合、目录发现、贡献 View 构造、打开/关闭等路径；区分加载通过和业务通过。
- [ ] 覆盖所有 12 个插件；外部业务代码不在本仓 Gate 覆盖范围内，单独记录结果。
- [ ] 视频执行真实播放、标签切换、内部停靠、全屏/退出和重复关闭；图像/分形执行真实绘制和缩放。
- [ ] 账号、网络、外部服务不可用时写“未执行/环境受限”；不能记为通过，也不能用重编译插件绕过旧二进制测试。

通过条件：旧产物未改变，实际执行范围清晰；关键功能未完成则保留待验收状态。

### G8 — 形成发布包与回退记录

- [ ] 产品候选标识为 3.0.1，保存 Git 修订、框架/SDK版本、构建日志及文件清单。
- [ ] 生成与现有安装方式一致的 win-x64、自包含、压缩单文件、非裁剪 Host 包。
- [ ] 完成真实窗口矩阵，填写保留/删除/改造结论、未完成项和已知限制；不得直接复用 G1 的成功标记。
- [ ] 交付 Host、SDK/模板候选及验收记录，分别标注是否已部署、是否已公开发布。
- [ ] 实际部署时使用完整 Host 文件清单更新，保留 Controls，验证插件文件哈希；保留旧 Host 的完整回退包。

## 6. 可直接使用的开发命令

下列命令在升级工作区根目录执行。PowerShell 中每个原生命令失败都必须停止，不能靠最后一个成功命令覆盖前面的失败。

### 6.1 G0 基线记录

```powershell
git status --short
git rev-parse HEAD
dotnet --version
```

### 6.2 G1 修改版本后的还原与编译

```powershell
dotnet restore MyAvaloniaManagement.sln --force-evaluate
if ($LASTEXITCODE -ne 0) { throw 'V9 restore failed' }

dotnet restore MyAvaloniaManagement.sln --locked-mode
if ($LASTEXITCODE -ne 0) { throw 'V9 locked restore failed' }

dotnet build MyAvaloniaManagement.sln -c Release --no-restore -warnaserror
if ($LASTEXITCODE -ne 0) { throw 'V9 build failed' }
```

只接受与本次依赖和版本变化相关的 lock diff；正式依赖更新前不要先执行 locked restore 并将必然的旧锁失败误判为不兼容。

### 6.3 定制改造后的定向测试

```powershell
dotnet test Host/MyAvaloniaManagement.PluginTests/MyAvaloniaManagement.PluginTests.csproj -c Release --no-restore --filter 'FullyQualifiedName~DockFloatingDisabledTests|FullyQualifiedName~WindowChromeAndDockDragGuardTests|FullyQualifiedName~DockFourWayLayoutTests|FullyQualifiedName~DockLayoutStoreTests' --logger 'trx;LogFileName=v9-dock-plugin.trx' --results-directory artifacts/host-v9/tests
if ($LASTEXITCODE -ne 0) { throw 'V9 Dock policy/layout tests failed' }

dotnet test Host/MyAvaloniaManagement.UiTests/MyAvaloniaManagement.UiTests.csproj -c Release --no-restore --filter 'FullyQualifiedName~DocumentControlRecyclingTests|FullyQualifiedName~DockSplitVisualRegressionTests|FullyQualifiedName~HostDockAdapterUiTests' --logger 'trx;LogFileName=v9-dock-ui.trx' --results-directory artifacts/host-v9/tests
if ($LASTEXITCODE -ne 0) { throw 'V9 recycling/UI tests failed' }
```

新测试类按实际名称加入筛选或随完整 verify 执行。不要以筛选测试通过代替 G6 的完整验证。

### 6.4 G6 完整本地验证

```powershell
dotnet run --project tools/MyAvaloniaManagement.Gate -- verify
if ($LASTEXITCODE -ne 0) { throw 'V9 full verify failed' }
```

当前 Gate 只接受 `host|all` scope，二者均验证本仓 Host/MyPlugTest；`workflow|workbench` 已退役。`verify` 包含 restore、build、tests、contracts、packages、package-acceptance，不包含 seal 的 coverage/windows-smoke。不得照搬历史 PowerShell 脚本或声称 verify 已覆盖外部插件。

### 6.5 G8 生成独立 Host 发布包

```powershell
dotnet publish Host/MyAvaloniaManagement/MyAvaloniaManagement.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=false -p:SkipPluginDeploy=true -warnaserror -o artifacts/host-v9/publish
if ($LASTEXITCODE -ne 0) { throw 'V9 publish failed' }
```

RID publish 在隔离工作区执行并审查其还原结果。测试时只向上述发布目录复制旧 Controls，不能对安装目录直接运行构建部署目标。

真实窗口测试使用进程级 `MYAVALONIA_DATA_DIRECTORY` 指向独立测试数据根。现有 `MYAVALONIA_SMOKE_TEST=1` 仅执行打开/关闭，不能用于交互或播放验收。业务插件可能拥有独立配置路径，测试 Harness 应注入临时配置/工作目录并使用样例输入，不能假设 Host 数据根已隔离插件的全部读写。

## 7. 验收矩阵

| ID | 场景 | 必须观察的结果 | 验证方式 |
| --- | --- | --- | --- |
| P01 | 文档/Tool/固定标签内部排序，移出标签区再松开 | 顺序正确，无残留偏移、ZIndex 或 :dragging | 路由事件测试 + 真实鼠标 |
| P02 | 按住拖动后切换窗口/取消/捕获被 Dock 接管 | 不抢占新所有者；旧恢复不干扰下一次手势 | 真实鼠标 + 状态断言 |
| P03 | 失活主窗口的首次点击与拖动 | 首次操作可用，无双击才生效 | 真实窗口 |
| P04 | 点击标签关闭按钮、工具隐藏按钮 | 不误触发拖拽；Document 关闭，Tool 隐藏 | Headless + 真实窗口 |
| P05 | 向主窗口外拖出/调用浮动入口 | 不创建浮动窗口，窗口内 Drag/Drop 仍有效 | DockFloatingDisabledTests + 真实鼠标 |
| R01 | 四向分割、标签切换、固定预览 | 同一 Factory 使用同一 Host 回收器，同一 View 单一父级 | 实际控件树实例断言 |
| R02 | 绑定驱动的 ContentPresenter/ContentControl 解绑再挂载 | 原绑定仍生效；旧 Child 已移除；未清空别的文档 | Headless 专项 |
| R03 | 最终关闭活动/非活动 Document | 缓存、焦点强引用、View、Scope 正确释放，其余 Document 不受影响 | 弱引用 + Dispose 计数 |
| R04 | 脏文档取消关闭/基类拒绝关闭 | View/Scope 不释放，closing 状态恢复，命令可继续执行 | 单元 + 集成 |
| R05 | View 清理抛出或退出途中关闭 | 关闭令牌与 Scope 仍清理；无重复 Dispose | 异常注入 |
| L01 | 四方向停靠、最后 Tool 隐藏后再显示 | 顶/底全宽、稳定 ID、活动状态正确，无空白 Pane | 布局测试 + 窗口 |
| L02 | 旧布局恢复，新布局保存后旧 Host 读取 | 既有字段/比例/顺序/固定/隐藏状态正确，schema=2 | 双版本隔离回放 |
| L03 | 插件缺失/不可用/快照损坏 | 默认布局仍可用，无半修改布局 | 既有错误路径测试 |
| B01 | 所有旧插件加载、组合、View 构造与首次打开 | 没有 MissingMethod/TypeLoad/XAML/资源键失败 | 冻结旧包 + 新 Host |
| B02 | 视频真实播放、全屏进出、内部停靠与多标签切换 | 图像/音频连续，无独立错误视频窗；关闭后资源归零 | 真实 Windows/LibVLC |
| B03 | 图像、分形、表单、列表、输入法、快捷键与主题切换 | 显示/交互正确，无命令失效或异常焦点 | 代表性业务流程 |

对 R03/B02 做同进程预热后至少 100 次打开/切换/关闭循环，记录 Handle、私有内存、View 弱引用和原生资源计数。沿用适用验收的固定阈值；若采用本仓历史视频阈值，则 Handle 增量 <=10、私有内存增量 <=64 MiB，并要求 Surface/播放器等资源最终归零。历史 NO-GO 不能自动变成当前失败，也不能忽略；在当前候选上重新采集并解释差异，不因失败放宽阈值。

鼠标矩阵至少在 100% 和 150% DPI、Light/Dark 下执行；多显示器可用时补跨显示器主窗口移动后的重复操作。没有对应设备的项目明确记录未执行。

## 8. 文件变更清单

| 文件/目录 | 预期变更 |
| --- | --- |
| Directory.Version.props | Avalonia/Dock、SDK 候选及最终产品版本 |
| Directory.Packages.props / 受影响 packages.lock.json | Headless 版本与确定依赖图 |
| Host/MyAvaloniaManagement/App.axaml | 删除浮动专用 Style，保留回收器与标签保护 |
| Host/MyAvaloniaManagement/Behaviors/DockTabPointerCaptureGuard.cs | 捕获转交/恢复边界与正确注释 |
| Host/MyAvaloniaManagement/Business/Docking/DocumentControlRecycling.cs | 安全解绑与上游能力说明 |
| HostDockFactory / DockDocumentLifetime / ManagedDockableViewLease | 默认保留；仅修复验证暴露的适配问题 |
| Business/Layout 下 Mapper / Coordinator / Lifecycle | 默认保留 V2；按边界用例局部适配 |
| Host 的 Unit / Plugin / UI Tests | 定制行为、集合通知、回收传播和旧二进制验收 |
| Packaging/MyAvaloniaManagement.Plugin.Templates | 新模板包、依赖、SDK 下限与锁文件 |
| docs/design/本方案及 docs/plan-history/host-v9 | 逐阶段状态、证据、风险与最终清理结果 |

不修改外部插件源码、旧插件清单、旧 SDK API 历史基线或用户业务数据。

## 9. 回退和阻断规则

- 构建/API/旧插件入口不兼容：停止形成发布候选，定位首个破坏点；不要修改旧插件掩盖 Host 兼容失败。
- 定制删除导致回归：仅撤回对应删除，保留框架升级对照；记录为何暂留，避免整批回退失去归因。
- 回收器实例被默认实现抢先占用、出现双父级或关闭后资源泄漏：修复 G4 后再继续。
- 实际部署回退使用升级前完整 Host 文件清单/包，保留 Controls；需要恢复布局时先退出进程，使用 G0 的布局副本，保留新布局供排查。
- 不单独把某一组 Dock/Avalonia DLL 降级混装；不回滚或覆盖插件业务文档。
- 真实交互/视频尚未验收时，状态写“开发验证通过，真实交互待验收”，不能写成“全部兼容”。

## 10. 上游核查依据

以下源码链接固定在目标 tag，避免评审期间随 master 改变：

- [Dock 12.1.0.6 NuGet 与依赖](https://www.nuget.org/packages/Dock.Avalonia/12.1.0.6)：要求 Avalonia >=12.1.1。
- [ItemDragHelper](https://github.com/wieslawsoltes/Dock/blob/v12.1.0.6/src/Dock.Avalonia/Internal/ItemDragHelper.cs)：早期捕获标记与释放路径，和 12.0.0.2 核对无差异。
- [DockControlFactoryService](https://github.com/wieslawsoltes/Dock/blob/v12.1.0.6/src/Dock.Avalonia/Services/DockControlFactoryService.cs)：按 Factory 共享回收器、默认回收器和具体类型清理。
- [ControlRecycling](https://github.com/wieslawsoltes/Dock/blob/v12.1.0.6/src/Dock.Controls.Recycling/ControlRecycling.cs) / [IControlRecycling](https://github.com/wieslawsoltes/Dock/blob/v12.1.0.6/src/Dock.Controls.Recycling.Model/IControlRecycling.cs)：缓存 Remove 与 Host 资源释放的差异。
- [FactoryBase.Init](https://github.com/wieslawsoltes/Dock/blob/v12.1.0.6/src/Dock.Model/FactoryBase.Init.cs)：可观察集合归一化、活动对象和追踪初始化。
- [FactoryBase.Dockable](https://github.com/wieslawsoltes/Dock/blob/v12.1.0.6/src/Dock.Model/FactoryBase.Dockable.cs)：Closing / Remove 或 Hide / Closed 顺序。
- [#1137 拖拽转交修复](https://github.com/wieslawsoltes/Dock/pull/1137) / [#1140 回收实例传播](https://github.com/wieslawsoltes/Dock/pull/1140)：升级涉及的行为变化。

本轮判断：删除已失去用途的浮动样式，保留并收窄指针保护，保留并加固自定义回收器；Factory、生命周期、布局和 Tool 协调继续承担 Host 所有权，仅做有测试依据的适配。
