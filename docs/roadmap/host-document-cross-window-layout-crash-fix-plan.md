# Document 拖入已有浮窗崩溃修复方案

> 用途：主项目后续实施与验收依据。日期：2026-09-17。状态：方案已编写，生产修复尚未实施。故障证据见[专项诊断](../archive/records/dock-area-fill/cross-window-layout-crash-20260917.md)。本轮只更新文档；后续实施遵守 SOLID、朴素设计、详细中文注释和完整本地开发验证，不使用 AIFLOW、Windows CI、seal 或发布门禁。

## 1. 修复目标与约束

Document 从主窗口拖入已经打开的浮窗后，两个窗口都能继续布局、切换标签和接受后续操作。区域默认 Fill、明确方向按钮分屏及标签排序保持既有行为。

修复必须保留同一个 Document、PreparedView 和作用域。不能通过关闭并重建页面、清空编辑状态、禁用浮窗、关闭控件复用或忽略未处理异常规避问题。暂时离开视觉树不等于最终关闭，不能调用 `ReleasePreparedView` 或释放业务模型。

实施和完成状态分别记录：源码修复、自动化通过、真实桌面验收、安装部署。本文不是已经完成修复或再次部署的记录。

## 2. 已知事实与待确认边界

| 项目 | 当前事实 |
| --- | --- |
| 安装版源码 | `85749180218a94cade4e7157c61bb6997f25b770`，见[部署记录](../archive/records/dock-area-fill/local-deployment-20260917.json) |
| Avalonia / Dock | Avalonia `12.1.2`；Dock 呈现补丁 `12.1.0.7-area.5`，其余 Dock 基座 `12.1.0.6` |
| Avalonia 固定源码 | 当前包 nuspec 指向 `d3c867a9e2de379249b03dbeb3495bd7f076a81a` |
| 实际异常 | 三次 Windows `.NET Runtime` 事件均为 `Attempt to call InvalidateArrange on wrong LayoutManager.` |
| 独立复现 | 两个 Window 和一个带 TextBlock 的 Border，无 Dock、Host 和业务插件，也能复现相同布局调用栈 |
| 时序对照 | 直接挂接、只在摘除前刷新旧窗口：失败；完整摘除后刷新旧窗口再挂接、先完成新窗口布局：通过 |
| 尚未确认 | 安装版异常对应的具体控件实例，以及生产模板的完整延迟创建和布局回调时序 |

当前已确认的是布局根归属冲突机制。不能仅凭异常栈把责任归为百度网盘插件，也不能把最小复现中的两个通过对照当作宿主修复已完成。

Avalonia 会为每个布局根维护待测量和待安排队列。旧队列中的控件跨窗口后，在新布局根尚未完成测量时被旧队列再次安排，可能进入 `_toArrangeAfterMeasure`；重新入队时发现布局根已变化而抛错。参见固定源码的 [LayoutManager](https://github.com/AvaloniaUI/Avalonia/blob/d3c867a9e2de379249b03dbeb3495bd7f076a81a/src/Avalonia.Base/Layout/LayoutManager.cs)。

## 3. 技术路线与选择条件

**主路线 A：在宿主正文复用边界完成最小的跨窗口交接修复。** 先用真实 Host 回归固定失败，再验证“摘除旧正文 → 在脱离状态处理旧布局 → 返回原 View 给新正文”的顺序。

这一选择是当前应用的兼容性修复：独立的原始 Avalonia 复现仍可能失败，不能声称框架本身已经修好。验收对象是所有实际经过 Host 的相关迁移入口。

选择 A 的原因：

- 当前所有托管正文通过 `DocumentControlRecycling` 取得同一个 PreparedView，已有集中交接入口，无须在每个插件或拖动按钮上增加补丁。
- [Plugin SDK UI 项目](../../Host/MyAvaloniaManagement.PluginSdk.UI/MyAvaloniaManagement.PluginSdk.UI.csproj) 对 Avalonia 使用 `[12.1.2]` 精确依赖。直接把 Host 改为自定义 Avalonia 预发布版本会产生依赖约束问题，不能通过关闭 NuGet 警告解决。
- 固定版本的公开 `ILayoutManager` 没有可直接读取的“正在执行布局”状态，因此无条件增加 `UpdateLayout` 不是可靠证明；主路线必须通过下面的重入边界检查。

**路线 A 的准入条件：** 跨窗正文迁移能在旧布局队列不再执行本次交接的安全边界完成；确定性回归必须覆盖模板延迟创建、布局回调和连续迁移。若 `UpdateLayout` 因旧管理器仍在执行而直接返回，不能把返回当作队列已处理完。

**切换条件：** 若为了满足这些条件必须依赖私有字段反射、全局异常拦截、固定延时、反复刷新直至不报错，或另建一套异步页面生命周期，停止扩展 A，进入第 6 节的框架修复路线 B。保留失败证据并更新路线决策，不将不可靠候选合入生产。

## 4. 主路线 A 的代码设计

### 4.1 职责与修改范围

| 文件 / 类型 | 计划职责或变化 |
| --- | --- |
| [DocumentControlRecycling.cs](../../Host/MyAvaloniaManagement/Business/Docking/DocumentControlRecycling.cs) | 唯一正文缓存及转交入口；区分同一正文、同窗转交、跨窗转交、最终释放 |
| 拟新增 `Business/Docking/DockViewTransfer.cs` | 必要时提取一个内部帮助类，封装父级摘除与跨窗布局交接；不缓存模型、不拥有 Scope |
| [HostDockFactory.cs](../../Host/MyAvaloniaManagement/Business/Docking/HostDockFactory.cs) | 保留现有移动与布局事务；仅在 P0 证明需要模型迁移之前的交接边界时接入帮助类 |
| 拟新增 `DockCrossWindowLayoutUiTests.cs` | 正式主窗到已有浮窗、队列顺序、重入及资源生命周期回归 |
| [DockAreaFillUiTests.cs](../../Host/MyAvaloniaManagement.UiTests/DockAreaFillUiTests.cs) | 补真实标签输入的主窗到浮窗方向，保留原反向用例 |
| [HostDockAdapterUiTests.cs](../../Host/MyAvaloniaManagement.UiTests/HostDockAdapterUiTests.cs) | 同一 Presenter、同窗复用、最终关闭和晚到模板回调回归 |
| 文档与非嵌入证据 JSON | 更新实际行为、测试覆盖、源码和产物身份；不覆盖旧部署事实 |

先在现有类的私有方法内验证最小改动，职责明显独立时再提取帮助类。不得为了套设计模式引入通用迁移框架、Service Locator、第二套 Document 集合或新的全局状态。

### 4.2 必须区分的分支

1. `IsViewReleased == true`：沿用最终释放协议，移除缓存并返回空，不恢复视图。
2. `existing` 就是缓存 View：直接返回，不摘除、不触发布局交接。
3. View 已无旧父级：直接按现有模板协议返回；不为已经脱离的对象猜测旧窗口。
4. 新旧父级属于同一 TopLevel：只做现有安全摘除，不增加跨窗口刷新。
5. 新旧 TopLevel 不同：在摘除前捕获旧根和目标根，按下述交接顺序处理。
6. 目标 Presenter 或目标 TopLevel 尚未形成：不能按“同窗”跳过检查，也不能把同步 `Build` 随意改成返回空后等待。P0 必须确定延迟模板的真实入口；若该边界无法可靠取得目标，按第 3 节切换条件处理。

### 4.3 跨窗口交接顺序

以下是设计步骤，不是可以直接粘贴的完整实现：

```text
确认 UI 线程、View 尚未最终释放、目标仍有效
捕获旧视觉根和目标视觉根
从实际 ContentPresenter.Child 及残余逻辑父级完整摘除
确认 View 当前没有旧父级，仍为原实例且未释放
在 View 保持脱离状态时处理旧窗口的布局任务
再次核对 View、目标和所有权，防止布局回调改变状态
将同一个 View 返回给目标正文模板
让新窗口完成正常测量和安排
```

`ContentPresenter` 仍优先用现有 `SetCurrentValue` 与 `UpdateChild` 协议，保留模板绑定。不能只把 Content 置空、实际 Child 尚未摘除就开始转交。Panel、Decorator、ContentControl 等已有支持分支都要确认完成实际视觉摘除，避免只处理一种父级。

旧窗口已关闭或视觉根已不存在时，沿用有效生命周期状态结束旧侧处理；不能对销毁对象强行安排布局。同一交接重入时必须避免重复摘除、再次提交或把 View 还给过期 Presenter。临时标记要在 `finally` 复位，不能变成长期保活旧窗口的缓存。

### 4.4 重入和同步协议的限制

`IControlRecycling.Build` 是同步模板构建协议。增加一次 Dispatcher.Post、固定毫秒延时或递归 `UpdateLayout`，无法自动保证新旧窗口的队列顺序。

正式代码不能把 `UpdateLayout` 的一次正常返回当作“旧队列肯定清空”。P0/P1 需要在真实模板测量回调中记录根和调用顺序，并增加确定性的重入回归。如果必须将迁移推迟，需先证明一个现有的、可取消且不会提前改变模型所有权的提交边界；本方案不默认引入异步转交状态机。

中文注释需解释：为何摘除前刷新无效、为何只处理跨窗、为何不能重建 View、为何最终关闭与暂时转交分开，以及重入边界的实际保证来源。

## 5. 自动化回归矩阵

测试使用生产主题和真实 Dock/Host 模板。正文至少包含 Border、TextBlock、可编辑状态及多层子控件，不能继续只用空 UserControl。复杂布局另使用 ScrollViewer、ItemsControl 等确定性本地控件，不依赖网络登录。

| 编号 | 必测场景 | 关键断言 |
| --- | --- | --- |
| T01 | 主窗非最后一个 Document → 已有浮窗，旧窗口先布局 | 无异常；主窗存活；文档归属正确；目标正文完成布局 |
| T02 | 主窗最后一个 Document → 已有浮窗 | 主窗空区合法；目标保留原标签；不关闭主窗 |
| T03 | T01 改为目标窗口先布局 | 与 T01 最终状态一致，不依赖调度顺序 |
| T04 | 浮窗 → 主窗、浮窗 A → 浮窗 B | 同时覆盖源窗保留其他文档及源空窗回收 |
| T05 | 待 Arrange、待 Measure、子控件待布局 | 旧任务不会导致跨根异常，最终 Bounds/有效布局状态正确 |
| T06 | 模板延迟创建、目标正文首次显示 | 不返回永久空白，不产生双父级或重复 View |
| T07 | 在 Measure/Arrange 或模板回调内发生转交 | 不依赖递归刷新；不重复提交；实际重入保障可解释 |
| T08 | 同一 Presenter、同窗标签切换与排序 | 不额外跨窗刷新，不清空自己，不改变顺序协议 |
| T09 | 跨窗整组移动及活动项切换 | 每个 View 唯一；成员顺序、ActiveDockable 和焦点符合原协议 |
| T10 | 连续往返 20 次，交替先刷新源窗与目标窗 | 每轮都验证；无残留旧根、旧 Presenter 或重复成员 |
| T11 | 目标失效、拖动取消、离开正文、全屏限制 | 不提交错误迁移；提示清理；原布局可继续操作 |
| T12 | 最终关闭与晚到模板回调 | Scope 和 View 仅释放一次，不能重新挂回已释放正文 |
| T13 | 区域 Fill、中央按钮、四向按钮、标签栏入口 | 迁移安全处理不抢占原有命中和分屏规则 |
| T14 | Tool 跨窗、上下全宽分屏、Pin 和 V3 重启 | 原有工具行为与布局持久化继续通过 |
| T15 | 真实标签鼠标事件：主窗 → 已有浮窗 | 从 MouseDown/Move/Up 完整经过拖放提交，不只直接调用 DockManager |

T01/T02/T05/T15 必须至少有用例在修复前以同一 `wrong LayoutManager` 异常失败，修复后在不额外清空队列的同一夹具中通过。不能通过在投放前统一 Flush、改用新 View、关掉源主窗、删除子控件或放宽断言取得绿色结果。

每次移动均核对：同一个 Document、PreparedView 和 Scope；未发送最终关闭；模型只存在于一个合法组；旧窗仍能操作或按已有规则回收；目标内容具有有效布局，而不仅是“没有抛错”。

原始纯 Avalonia 诊断工程保留为框架基线，不混入默认绿色门禁。路线 A 的正式回归必须经过宿主交接实现；路线 B 才要求无宿主的直接跨窗复现也转为通过。

## 6. 备选路线 B：固定 Avalonia 布局队列补丁

当 A 不能可靠覆盖延迟模板和重入时，正确修复点是 Avalonia 布局队列消费，而不是继续在宿主堆叠刷新调用。

### 6.1 框架修改原则

基于固定提交 `d3c867a9e2de379249b03dbeb3495bd7f076a81a`：

- 在内部消费待测量、待安排任务时，跳过已经脱离或 `GetLayoutRoot()` 不再属于 `_owner` 的旧任务。
- Measure/Arrange 可以触发用户回调，回调后再次检查归属，再决定重新入队；递归处理祖先时也不能越过当前布局根。
- `_toArrangeAfterMeasure` 重新入队前必须再次校验，保证列表处理期间发生迁移也安全。
- 保留公开 `InvalidateMeasure` / `InvalidateArrange` 的错误根检查；合法跳过内部过期任务，不等于允许调用方把任务交给错误管理器。
- 通过测试确认新根能自行接收并完成有效任务，避免“旧根跳过后，新根也不布局”的静默空白。

补丁代码只负责任务归属，不理解 Dock、Document、插件或业务作用域。无需新增公开 API。

### 6.2 包与兼容边界先决条件

`Avalonia.Base.dll` 位于 `Avalonia` 包内，不能假设存在可单独替换版本的 `Avalonia.Base` NuGet 包。当前包同时提供 net8.0/net10.0 和多个程序集，必须核对完整资产清单。

进入 B 后，先建立明确的 Host 专用交付设计及依赖验证，满足以下条件后才接入生产：

1. 固定上游、SDK、补丁、测试、程序集身份及完整包资产；记录实际包来源和 SHA-256。
2. 明确解决 SDK UI 对 `[12.1.2]` 的精确依赖。不能直接提高 Host 包版本后忽略 NU1608，不能同版本覆盖官方包或用户缓存，也不能静默改写已发布的 SDK UI 契约。
3. 若采用 Host 专用运行时资产补丁，必须使用独立身份、仓库内可重建来源和明确的构建资产选择，证明 build/test/publish/single-file 四条路径采用同一补丁；不得靠发布结束后手工替换 DLL。
4. 若必须升级整个 Avalonia/UI Profile 依赖闭包，将其明确列为路线扩大并补齐兼容计划；不能用本次小修复的完成状态掩盖框架升级。
5. 强名称、公钥、AssemblyVersion、引用程序集和符号身份都要实测。使用公开签名时明确记录，不能宣称具有上游私钥签名。
6. 在无个人缓存的独立还原环境和不同源码目录分别构建，比较完整包及 DLL 摘要，并验证 Host、UI 测试与发布产物实际采用的文件。

拟新增 `patches/avalonia-cross-window-layout/` 保存固定输入、补丁、许可和维护说明，拟新增构建脚本负责测试后生成本地依赖。默认 verify 必须在 restore 前准备该依赖，并在 build 后核验实际 DLL；脚本失败、测试为零、跳过或摘要不符都阻断后续阶段。

这些是 B 的必要交付工作，当前尚未实现或验证。若依赖身份无法在既有约束内收口，保留红灯和范围说明，不生成声称可部署的产物。

## 7. 实施顺序与各阶段产出

### P0：固化宿主失败

- [ ] 核对工作区、安装版与依赖版本；保留原部署记录和 Windows 异常。
- [ ] 将诊断样例迁为正式 Host 回归，补 T01/T02/T05/T15 的关键组合。
- [ ] 用测试夹具记录旧根、目标根、实际父级与模板调用时序，不记录用户文档内容。
- [ ] 保存失败 TRX，确认是相同布局异常，而非编译错误、插件缺失或错误测试坐标。

产出：可稳定重现的 Host 红灯、源/目标根变化记录，以及是否满足 A 的安全交接边界的结论。

### P1：实现最小修复并锁定路线

- [ ] 按第 4 节在复用边界实现 A；优先私有方法，不改变 SDK、Dock 区域命中或布局 schema。
- [ ] 运行旧窗先布局、新窗先布局、重入和延迟模板的关键回归。
- [ ] 若满足切换条件，停止扩大 A，依第 6 节完成 B 的技术与交付设计，更新本节的实际选择和理由。
- [ ] 不保留互相掩盖的两套修复；每个生产改动都要能对应到失败原因或回归断言。

产出：关键红灯转绿、中文设计注释、实际修改清单；尚未宣称全面验收通过。

### P2：补齐回归与本地开发验证

- [ ] 完成 T01–T15，测试用例命名及注释使用中文；对照条件使用固定输入和确定性时序。
- [ ] 回归现有正文回收、浮窗关闭、指针捕获、工具分屏与 Layout V3。
- [ ] 先定稿当前 Markdown，再执行完整本地 verify。
- [ ] 如修改构建门禁或依赖脚本，增加失败传播、缺失 DLL、摘要不一致及错误缓存回归。
- [ ] 保存新一次的通过、失败、跳过数量及源码/产物身份，不沿用此前 1039 项结果作为修复证明。

计划中的执行命令如下；新增测试文件及类型落地后才能执行，不是当前通过记录：

```powershell
$ErrorActionPreference = 'Stop'

pwsh -NoProfile -File tools/Build-DockAreaFillPackage.ps1
if ($LASTEXITCODE -ne 0) { throw 'Dock 补丁准备失败' }

dotnet restore MyAvaloniaManagement.sln --locked-mode
if ($LASTEXITCODE -ne 0) { throw '还原失败' }

dotnet test Host/MyAvaloniaManagement.UiTests -c Release -m:1 --filter 'FullyQualifiedName~DockCrossWindowLayout|FullyQualifiedName~DockAreaFill|FullyQualifiedName~HostDockAdapter|FullyQualifiedName~DockToolSplit|FullyQualifiedName~DockToolWindowClose|FullyQualifiedName~DockPointerCapture|FullyQualifiedName~DockLayoutV3' --logger 'trx;LogFileName=cross-window-layout.trx' --results-directory artifacts/dock-cross-window-layout/host-ui
if ($LASTEXITCODE -ne 0) { throw '宿主专项回归失败' }

dotnet test tools/MyAvaloniaManagement.Gate.Tests -c Release -m:1 --logger 'trx;LogFileName=gate-tools.trx' --results-directory artifacts/dock-cross-window-layout/gate-tools
if ($LASTEXITCODE -ne 0) { throw '门禁工具自测失败' }

dotnet run --project tools/MyAvaloniaManagement.Gate -- verify
if ($LASTEXITCODE -ne 0) { throw '完整本地 verify 失败' }
```

路线 B 接入后须同步上述补丁准备步骤与默认门禁图，不能拿未准备新依赖的旧命令冒充完整验证。核对 TRX 中确实运行新增用例、总数大于零、无失败及非预期跳过；ExitCode 0 本身不够。

### P3：真实桌面验收与证据定稿

| 编号 | 实际操作 | 通过条件 |
| --- | --- | --- |
| M01 | 主窗保留一个页面，把另一个普通 Document 拖入已有浮窗正文 | 页面出现，主窗和浮窗均可继续操作，无同类事件 |
| M02 | 主窗最后一个 Document 拖入已有浮窗 | 主窗存活，空区正常，目标标签不丢失 |
| M03 | 按原截图操作，将百度网盘 Document 拖入欢迎页浮窗 | 无崩溃、无永久空白、原页面状态保留；不为验收执行上传或下载 |
| M04 | 反向拖回、浮窗之间移动、连续往返 20 次 | 每次均可切换和继续拖动，关闭次数及窗口回收正确 |
| M05 | 中央按钮与四向按钮，拖入标签栏，Esc/离开取消 | 原操作语义正确，预览清理，无卡死 |
| M06 | 多屏负坐标及 100%/150%/200% DPI | 实际落点和显示正确，无布局归属异常 |
| M07 | WebView/视频等原生内容与焦点切换 | 页面及业务实例保持；真实环境未执行时明确保留待验收 |

没有原生输入能力时如实标为未执行。Headless 通过、单 EXE 启动通过不能替代这些项目；M01–M03 未完成时只能报告“自动化修复完成，原故障桌面验收待补”。

## 8. 文档、验收记录与完成定义

实施时同步本方案、[区域回停专项](../maintenance/dock-area-fill-verification.md)、[原实施计划](host-dock-area-fill-implementation-plan.md)、[待办列表](README.md)及相关 Host 设计说明。历史诊断和部署证据保留当时结论。

拟增加专用维护指南 `docs/maintenance/dock-cross-window-layout-verification.md`、实际修复记录及非嵌入 `cross-window-layout-fix-evidence.json`。证据至少包括：选择的技术路线、失败/修复后 TRX、源码提交和工作树指纹、测试清单、框架/Dock/Host DLL 摘要、Gate runId、真实桌面逐项结果及未执行原因。

由于 Markdown 会嵌入 Host，先定稿文档再生成最终构建和验证证据；验证后只补非嵌入 JSON。源码或嵌入文档再次变化，必须更新对应构建与证据。

完成条件：

- [ ] 保留修复前同一异常的确定性 Host 红灯。
- [ ] 选择的方案对两种窗口布局顺序均通过，并覆盖重入和延迟模板。
- [ ] T01–T15 对应的自动化全部实际执行，无失败、无跳过；生命周期和布局状态断言有效。
- [ ] 现有本地 verify 通过；若有新依赖，构建来源、实际 DLL 和兼容约束均已验证。
- [ ] M01–M03 原故障场景实际通过；其余桌面限制单列，不以测试总数掩盖。
- [ ] 当前文档和专用证据完整，能够重建并说明产物与源码的对应关系。

## 9. 后续部署和回退

本次文档任务不编译、不部署、不自动提交 Git。后续收到实施或部署指令时按相应阶段执行，不把此前一次部署授权写成永久自动发布任务。

后续本地交付沿用 Release、win-x64、单文件、自包含、原生库入包和关闭裁剪。先在隔离数据目录验证实际 EXE 及其 Host 身份，再备份和原子替换安装目录主程序，逐项核对 HelpWeb 与 Controls；保持用户数据和插件内容。

回退分清两件事：恢复上一产物可以撤销修复候选的新增回归，但该旧产物仍具有本次已知崩溃，不能称为故障已解决。源码回退恢复对应提交及其准确依赖；安装回退使用哈希核对过的完整旧交付。若走 B，不得把补丁与官方运行时 DLL 混装，也不得用旧兼容报告证明新 Host 身份。
