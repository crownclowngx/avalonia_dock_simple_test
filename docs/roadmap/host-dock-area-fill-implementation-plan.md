# Dock 区域默认居中停靠实施计划

> 用途：供后续开发直接执行的主项目实施方案。状态：交互方案已确认，实现与验证未开始；核对日期：2026-09-16。本次交付仅生成方案文档，不实施代码、依赖、部署或发布变更。文中新增属性、类型、脚本及测试名称均为拟新增项，不代表当前能力。

## 1. 目标与完成定义

解决浮动窗口回停时必须精确命中中央小按钮、按钮被浮窗遮挡后难以操作的问题。

实现以下行为：鼠标进入合法停靠分组的内容区域，默认预览居中合并；命中方向按钮才预览分屏；松开后执行预览所表示的操作。保持原页面、View、Scope 和工具实例，复用现有 Dock 移动、分割、关闭与布局保存协议。

完成必须同时满足：

- [ ] 普通内容区域可居中回停，中央按钮被正在拖动的浮窗遮挡时仍能完成操作。
- [ ] 局部方向按钮和窗口外侧全局方向按钮继续有效，不被区域回退抢占。
- [ ] 原生浮窗拖动、Document 标签拖动、Tool 标签或标题拖动使用一致的区域规则；标签栏排序保留原语义。
- [ ] 预览与最终布局对应，拒绝操作、离开目标和结束拖动时没有残留提示。
- [ ] 自动化、完整 Gate verify 和真实桌面验收分别记录实际结果。
- [ ] 补丁依赖可从干净检出重建和还原，不依赖个人 NuGet 缓存中的手工替换 DLL。

## 2. 已确认的行为契约

### 2.1 区域与操作

| 鼠标位置或状态 | 预览与松开结果 |
| --- | --- |
| 可接收的空文档分组内容区 | 居中填满该分组 |
| 可接收的已有 Document / Tool 分组内容区 | 合并到该分组，遵守原有内容类型及停靠权限 |
| 局部上、下、左、右按钮 | 执行该按钮已有的分割行为 |
| 局部中央按钮 | 执行原有 Fill 行为 |
| 窗口外侧全局方向按钮 | 执行原有全局停靠行为 |
| 已有标签栏或标签插入位置 | 使用现有标签插入、合并、排序规则 |
| 菜单、窗口边框、分隔条、不可接收区域或窗口外 | 不执行区域默认 Fill；保留原有浮动或取消结果 |
| 可见方向目标被明确命中，但权限校验拒绝 | 不执行停靠，不回退成 Fill |

“填满”以鼠标所在的目标分组为范围，不合并其他分组，不清空已有分屏。以鼠标位置决定目标，不使用浮窗矩形的重叠面积决定目标。拖动过程只更新预览，不提前修改布局或触发布局保存。

### 2.2 保留现有项目语义

- Tool → Tool、浮窗内部的方向分割保持局部布局。
- Tool → 主窗口 DocumentDock（含文档子组）的上、下分割保留现有全宽兼容策略；不能把所有方向预览统一写成当前分组的一半。对应实现见 [HostDockFactory](../../Host/MyAvaloniaManagement/Business/Docking/HostDockFactory.cs) 与 [ToolDockCoordinator](../../Host/MyAvaloniaManagement/Business/Layout/ToolDockCoordinator.cs)。
- 中央回退遵守 Dock 原有的 Document / Tool 类型和分组约束，不主动放宽 `CanDockAsDocument` 等能力。
- 主窗口固定布局骨架不能被当作可移动业务项；内容全屏期间的迁移限制保持有效。
- 运行时 Document 实例与编辑状态必须保留；Layout V3 仍只恢复工具，不跨启动重开 Document，不保存文档路径或内容。
- 浮窗回停后的外壳回收交给现有框架与 Host 适配协议，不将回停实现成关闭再新建页面。

权威边界见 [浮窗使用指南](../quick-start/floating-windows-and-layout.md)、[Layout V3](../reference/dock-layout-snapshot-v3.md)、[Host 兼容约束](../../Host/MyAvaloniaManagement/docs/reference/compatibility-contracts.md)。

### 2.3 首版范围

覆盖本应用主窗口和浮窗之间的合法停靠，包括目标被正在拖动的浮窗遮挡的情况。其他应用遮挡、最小化窗口穿透、自动展开隐藏边栏、边缘自动分屏、停留延时、吸附动画和新增修饰键不属于首版需求。

自动隐藏入口及原生内容控件沿用现有行为；若真实 WebView / 视频区域不能命中，作为单独复现记录，不以推测标记通过。

## 3. 基线与技术路线

### 3.1 实施前重新核对的事实

| 项目 | 本次核对值 / 入口 |
| --- | --- |
| .NET / Avalonia / Dock | `10.0.302` / `12.1.2` / `12.1.0.6`；见 [集中基线](../reference/platform-baseline.md) |
| Dock 包源码提交 | `cc08602d02fde1b85067cec064da29f34785e505`；来自当前 Dock.Avalonia 包的 nuspec |
| 依赖版本与锁定 | [Directory.Version.props](../../Directory.Version.props)、[Directory.Packages.props](../../Directory.Packages.props)、受影响项目的 `packages.lock.json` |
| 主窗口 DockControl | [MainView.axaml](../../Host/MyAvaloniaManagement/Views/MainView.axaml) |
| 浮窗及其 DockControl | [HostFloatingWindow.cs](../../Host/MyAvaloniaManagement/Views/HostFloatingWindow.cs) |
| 全局主题接入 | [App.axaml](../../Host/MyAvaloniaManagement/App.axaml) |
| 移动、分割和关闭适配 | [HostDockFactory.cs](../../Host/MyAvaloniaManagement/Business/Docking/HostDockFactory.cs) |

以实际开始实施时的工作树和已还原包为准。若版本发生变化，先重新核对源码入口、现有能力及补丁适用性，不能直接套用行号。

### 3.2 选择小范围 Dock 源码补丁

当前 `DockTargetBase` 通过选择按钮判断操作，未命中时局部目标默认返回 `Window`。框架的整区 indicator-only 模式会隐藏选择按钮，不能直接满足“区域 Fill 与方向按钮并存”。内部 `AdornerHelper<DockTarget>` 直接创建目标实例，单纯继承一个 Host 控件不足以替换整条调用链。

采用固定上游提交上的补丁，优先将修改限制在 `Dock.Avalonia`，由宿主样式开启。拟新增 `FillOnAreaDrop` 属性，默认 `false`；只在局部 `DockTarget` 上启用，`GlobalDockTarget` 保持原行为。属性具体声明位置以基线代码审阅为准，但不扩散到 Plugin SDK 或布局 schema。

不要仅把 `DefaultDockOperation` 改成 Fill：这不足以完成目标权限、区域边界、明确拒绝、预览和提交的一致性。也不要扩大中央 selector 的矩形覆盖其他按钮。

源码核对入口：

- [DockTargetBase](https://github.com/wieslawsoltes/Dock/blob/cc08602d02fde1b85067cec064da29f34785e505/src/Dock.Avalonia/Controls/DockTargetBase.cs)：selector 命中、默认操作、指示器绘制。
- [DockManagerState](https://github.com/wieslawsoltes/Dock/blob/cc08602d02fde1b85067cec064da29f34785e505/src/Dock.Avalonia/Internal/DockManagerState.cs)：局部和全局目标的创建与共享处理。
- [HostWindowState](https://github.com/wieslawsoltes/Dock/blob/cc08602d02fde1b85067cec064da29f34785e505/src/Dock.Avalonia/Internal/HostWindowState.cs)：浮窗拖动和回停。
- [DockControlState](https://github.com/wieslawsoltes/Dock/blob/cc08602d02fde1b85067cec064da29f34785e505/src/Dock.Avalonia/Internal/DockControlState.cs)：标签及 DockControl 拖放。

## 4. 核心设计

### 4.1 共用判定结果

在已有拖放层内形成一次判定结果，包含：目标 DockControl、目标分组、操作、局部/全局范围、是否允许提交，以及是否明确命中了按钮。可采用一个小型内部结果类型，不创建第二套拖放管理器。

明确区分“没有命中按钮”和“命中按钮但被拒绝”。后者不能走区域 Fill 回退。`DockOperation.None`、`Window` 在现有入口中处理不同，拒绝状态应通过明确分支结束，不能假设返回任一枚举值就会安全取消。

判定顺序：

```text
读取最新拖动坐标，定位合法目标窗口、分组和内容边界
  ├─ 命中全局按钮 → 校验并使用其操作；拒绝则结束
  ├─ 命中局部按钮 → 校验并使用其操作；拒绝则结束
  ├─ 位于标签栏 → 沿用原有标签插入 / 排序语义
  ├─ 区域策略开启且位于合法内容区域
  │    ├─ Fill 可见且通过校验 → Fill
  │    └─ 否则 → 不停靠
  └─ 其他 → 原有浮动 / 取消结果
```

局部 / 全局按钮重叠时保留明确的全局优先级。隐藏按钮不制造不可见的命中阻挡区；可见但不可用的按钮被命中时明确拒绝。

### 4.2 区域边界与坐标

- 由当前合法 drop control 的模型归属解析目标 DocumentDock / ToolDock，不能把任意内容控件的 DataContext 当作目标模型。
- 以目标实际内容区和可见裁剪范围为边界；标签栏单独处理，菜单、分隔条及原生窗口非客户区不参与区域 Fill。
- 复用当前浮窗拖动的坐标转换方式，不把窗口位置误当成鼠标位置；跨窗口转换使用屏幕坐标与目标 `PointToClient`，避免重复乘除 DPI。
- 查找目标时跳过被拖动的源窗口；对于同窗口标签拖动，保留现有自身 / 同组无效操作检查，避免把整个源窗口一律排除。
- 沿用可见窗口的层级顺序，不新增扫描所有矩形并穿透其他前景窗口的逻辑。
- 没有具体合法分组时不对整个 RootDock 无条件 Fill；空文档分组应作为专门覆盖场景。

### 4.3 校验、预览与提交

1. 复用 Dock 当前的拖放能力、DockGroup、源 / 目标类型和自身拖放校验；Host 全屏限制也必须在预览可提交性中得到体现。
2. Fill 选中时高亮目标分组，方向选中时展示对应布局结果；全局选中时抑制局部 Fill 高亮，只显示一个最终操作。
3. 核对 Tool → 主文档区上下全宽策略的预览；若原预览不能反映最终范围，使用已有目标分类事实修正预览，不在预览层复制另一份布局重排策略。
4. 两种拖动入口使用同一判定规则。松开时按最新坐标与最新布局重新校验；目标已脱离、关闭或能力已变化时不使用缓存目标强行提交。
5. 仅在松开并通过校验后调用现有 Dock 执行入口。悬停阶段不搬移模型、不关闭浮窗、不写布局。
6. 离开、取消、捕获丢失、关闭目标窗口和拖动结束均清除临时预览与目标引用。复用的 adorner 不能携带上次拖动状态，清理时不要覆盖样式配置的属性值。

### 4.4 遮挡与提示层

先验证现有目标查找已经能识别被源浮窗遮挡的目标，再验证高亮及方向按钮是否可见。若提示层仍在源浮窗下面，评估现有 `DockSettings.UseFloatingDockAdorner`，在创建拖放状态前初始化，并验证：

- 提示层在拖动时可见、位置和尺寸正确，且不获取焦点或打断拖动。
- 提示窗口自身不成为业务停靠目标，不阻断实际目标识别。
- 局部、全局提示层之间优先级正确，不遗留透明窗口。
- 100%、150%、200% 缩放以及跨屏不同缩放下坐标一致。

是否开启该选项由真实桌面结果决定；单独开启该选项不代表已经实现区域停靠。

## 5. 补丁包与项目接入

### 5.1 可复现产物

拟在主仓新增以下维护材料，具体名称可在实施时调整，但交付职责必须齐全：

| 拟新增位置 | 内容 |
| --- | --- |
| `patches/dock-area-fill/README.md` | 上游仓库、固定提交、补丁说明、构建命令、许可证、适用版本与升级检查 |
| `patches/dock-area-fill/area-fill.patch` | 框架修改和框架侧测试的可审阅差异 |
| `patches/dock-area-fill/baseline.json` | 上游提交、补丁包版本、依赖基线及产物校验信息 |
| `tools/Build-DockAreaFillPackage.ps1` | 在仓库内隔离工作目录获取固定源码、检查并应用补丁、测试、打包的可重复入口 |
| `artifacts/dock-area-fill/feed/` | 生成的本地 NuGet 源，不作为源码目录 |

构建脚本必须对源码身份、补丁无法应用、测试失败和重复版本内容不一致明确报错。保留上游 MIT 许可；不修改用户全局 NuGet 配置或清空全局缓存。

补丁使用独立且满足现有依赖下界的包版本。注意 `12.1.0.6-area.1` 排在正式版 `12.1.0.6` 之前，可能触发依赖降级；不能直接采用这个后缀替换稳定包。最终版本在确认依赖图后登记，并核对包版本、AssemblyVersion、文件版本各自用途。

### 5.2 主仓集成

- 在集中版本管理中单独表达 `Dock.Avalonia` 补丁版本，其他 Dock 包以当前基线为起点，不把一个补丁版本无差别赋给所有 Dock 包。
- 检查主题和其他包的依赖约束、程序集身份以及 XAML 编译；如必须扩大补丁包范围，在基线说明中列出实际原因和依赖闭包。
- 提供仓库级包源及必要映射，确保官方包仍能还原、本地补丁版本能唯一确定。首次还原前必须有明确的补丁包准备步骤。
- 把补丁准备接入正式开发验证流程；如果 Gate 独立验收工作目录不继承本地 feed，使用其支持的源配置或补齐明确的准备环节。验证 `Gate verify` 不依赖执行者碰巧已有的缓存。
- 更新受影响的 `packages.lock.json`，重新做 locked restore；不得通过关闭锁文件或放宽依赖告警通过检查。
- 在 App 样式为局部 DockTarget 开启 `FillOnAreaDrop`，覆盖主窗口和浮窗。关闭属性后可恢复原按钮命中行为。
- 新增能力只属于 Host 的 Dock 实现。Plugin SDK、插件模板、manifest 和 Layout V3 不为本交互增加配置字段。

实际产物变化会影响 Host 指纹和兼容证据，应按 [插件兼容验证](../maintenance/plugin-compatibility-verification.md) 重新生成适用结果，不复用旧 DLL 哈希的报告。补丁包本地消费不等同于公共 NuGet 发布。

## 6. 按阶段执行

### P0：冻结基线和复现

- [ ] 阅读本计划及链接的当前契约，记录 `git status`、源码 revision 和实际依赖版本。
- [ ] 记录浮窗遮挡中央按钮的失败步骤，分别确认拖标题栏和拖标签的现状。
- [ ] 核对固定上游提交、补丁入口及包还原流程；建立上述补丁维护目录与构建入口。
- [ ] 将“区域空白处不能 Fill”转成框架侧失败测试，同时保留方向按钮对照用例。

退出条件：复现明确，测试失败原因对应真实问题，能够生成默认策略关闭的基线包。

### P1：实现判定和预览

- [ ] 新增默认关闭的区域策略及共用判定结果，区分未命中、有效命中和明确拒绝。
- [ ] 实现合法内容区域 Fill、方向优先和标签栏语义保留。
- [ ] 连接两条拖放入口，补齐最新坐标提交、失效目标拒绝和预览清理。
- [ ] 验证局部、全局及 Tool 全宽分割的预览与最终行为；补丁包内运行对应测试。

退出条件：框架侧规则用例通过，开启 / 关闭策略可对照验证。

### P2：接入 Host 与回归

- [ ] 完成补丁包版本、仓库源和锁文件接入，从干净还原验证依赖可重建。
- [ ] 宿主样式启用策略，验证主窗口、纯文档浮窗、工具浮窗及混合分组。
- [ ] 验证 Host 原模型 / View / Scope、活动项、工具 Pin 状态、空窗清理及布局保存协议。
- [ ] 在真实桌面复现遮挡，必要时接入浮动提示层并补窗口层级验证。

退出条件：专项自动化通过；桌面发现的问题有明确修复或未完成记录。

### P3：交付验证与证据

- [ ] 定稿受影响的使用指南、Host 设计说明、专项验证指南和本计划状态。
- [ ] 执行完整本地主仓 Gate verify，记录真实结果。
- [ ] 完成下方桌面矩阵；无法执行的行填写未执行及原因，不代填通过。
- [ ] 在非嵌入 JSON 中记录最终源码、补丁、包 / DLL 身份、命令和验证结果；在待办中链接证据。

文档会嵌入 Host DLL。最终构建前定稿 Markdown；最终运行 ID、TRX 和哈希写入非嵌入证据文件，例如 `docs/archive/records/dock-area-fill/development-evidence.json`。如果之后修改嵌入文档，重新生成受影响产物及身份，不沿用旧哈希。

## 7. 自动化验证与运行命令

### 7.1 必须验证的结果

| 层级 | 用例与断言 |
| --- | --- |
| Dock 补丁测试 | 区域内 Fill、边界外不 Fill、局部 / 全局方向优先、可见目标拒绝不回退、隐藏按钮、空分组、策略关闭恢复原行为 |
| Dock 拖放入口测试 | 两种拖动入口最终选择一致；最新松开坐标、目标脱离 / 关闭、能力变化、取消与捕获丢失；每次提交最多执行一次 |
| Host Headless | 真实 Dock 执行入口下的单项 / 整组 / 跨窗移动，原模型与 View 引用、Scope 寿命、活动项、工具状态、分隔条归属与空窗回收 |
| 布局与关闭回归 | V3 工具保存恢复、临时脱离状态不落盘、Document 不持久化、关闭取消、全屏限制、现有 P1 / P2 回归 |
| 依赖验证 | 无个人缓存的包准备与 locked restore、实际程序集闭包、完整 Gate verify、受影响兼容证据 |

拟新增 Host 测试可命名为 `DockAreaFillUiTests`，但必须测试可观察结果。直接调用 DockManager 移动成功不能证明鼠标命中和遮挡正确；后两项必须有入口测试和真实桌面结果。

### 7.2 命令顺序

在主仓根目录执行。第一条脚本是 P0 阶段必须交付的新入口，当前尚不存在；其职责包含固定源码上的补丁测试与本地打包。

```powershell
pwsh -NoProfile -File tools/Build-DockAreaFillPackage.ps1
dotnet restore MyAvaloniaManagement.sln --locked-mode
dotnet build MyAvaloniaManagement.sln -c Release --no-restore -m:1 -warnaserror
```

依赖首次接入时先审阅并更新锁文件，再执行上述 locked restore；它不能代替生成锁文件的步骤。

开发期间按变更运行专项验证：

```powershell
dotnet test Host/MyAvaloniaManagement.UiTests -c Release --no-build -m:1 --filter 'FullyQualifiedName~DockAreaFill|FullyQualifiedName~DockToolSplit|FullyQualifiedName~DockToolWindowClose|FullyQualifiedName~DockPointerCapture|FullyQualifiedName~DockLayoutV3'
dotnet test Host/MyAvaloniaManagement.Tests -c Release --no-build -m:1 --filter 'FullyQualifiedName~Dock|FullyQualifiedName~WorkspaceSessionAndDockFactory'
dotnet test Host/MyAvaloniaManagement.PluginTests -c Release --no-build -m:1 --filter 'Category!=PackageAcceptance&FullyQualifiedName~Dock'
```

检查新增测试实际被发现且执行，零测试不算通过。修改代码后先重建相应项目，再使用 `--no-build`。最终完整开发验证入口为：

```powershell
dotnet run --project tools/MyAvaloniaManagement.Gate -- verify
```

若修改了 Gate 的包准备或源传递流程，再运行 `dotnet test tools/MyAvaloniaManagement.Gate.Tests -c Release -m:1`。不为文档生成任务执行上述构建或测试；实施阶段再按依赖顺序执行。正式 seal、安装目录部署和公共包发布遵守各自交付流程，不能用开发验证结果替代。

## 8. 真实桌面验收矩阵

使用已完成 Release 构建的产物和隔离数据目录。以下命令保留并恢复当前终端原有的数据目录设置：

```powershell
$areaFillPreviousDataDirectory = $env:MYAVALONIA_DATA_DIRECTORY
try {
    $env:MYAVALONIA_DATA_DIRECTORY = Join-Path $PWD 'artifacts/dock-area-fill/manual-data'
    dotnet run --project Host/MyAvaloniaManagement -c Release --no-build
}
finally {
    if ($null -eq $areaFillPreviousDataDirectory) {
        Remove-Item Env:MYAVALONIA_DATA_DIRECTORY -ErrorAction SilentlyContinue
    }
    else {
        $env:MYAVALONIA_DATA_DIRECTORY = $areaFillPreviousDataDirectory
    }
}
```

| 编号 | 操作 | 通过标准 | 状态 |
| --- | --- | --- | --- |
| M01 | Document 浮窗拖到空主文档区，避开所有按钮松开 | 整区预览，Document 回停，原实例保留 | 待执行 |
| M02 | 目标已有多个标签，拖到内容区松开 | 合并到该组，已有内容和标签顺序符合框架约定 | 待执行 |
| M03 | 目标已左右 / 上下分组，在各组内容区分别投放 | 仅命中的组接收，其他组保持原布局 | 待执行 |
| M04 | 分别命中四个局部方向按钮及四个全局方向按钮 | 对应方向有效，区域 Fill 不抢占，预览匹配最终范围 | 待执行 |
| M05 | Tool → Tool、浮窗内部、主文档区分别做上下分割 | 前两者局部；主文档区保留全宽；分隔条可继续拖动 | 待执行 |
| M06 | 用被拖动的浮窗盖住目标中央按钮，在合法区域松开 | 无需露出或命中中央按钮即可回停，提示清晰 | 待执行 |
| M07 | 源浮窗同时遮住鼠标下方的目标内容，连续拖动 | 能识别其背后的合法目标，不把源浮窗或提示层当成目标 | 待执行 |
| M08 | 在内容区与方向按钮间往返，再离开目标松开 | 预览及时切换和消失，区域外保持浮动 | 待执行 |
| M09 | 同组排序、跨组标签投放、整组浮窗回停 | 标签行为正常，组内内容无丢失和重复 | 待执行 |
| M10 | 制造不允许 Fill / 分割的目标，及目标失效场景 | 无误导预览，不发生拒绝分割后偷偷合并 | 待执行 |
| M11 | 全屏限制、关闭取消、源 / 目标窗口关闭及连续多次拖动 | 没有空壳、残留提示、失效引用或提前释放 | 待执行 |
| M12 | 100% / 150% / 200% 与不同缩放双屏、负坐标屏幕 | 指针、按钮和预览对齐，跨屏命中正确 | 待执行 |
| M13 | 回停后隐藏 / 显示工具，保存并重启 | 工具分组与比例恢复；Document 按既有契约不自动重开 | 待执行 |
| M14 | 关闭区域策略后重复核心场景 | 恢复原精确按钮行为，原方向停靠仍可用 | 待执行 |

逐项填写通过、失败或未执行及原因，记录 OS、屏幕缩放、操作步骤、源码与产物身份。外部 WebView / 视频 / 下载业务另记实际覆盖范围。Headless 通过不能替代 M06、M07、M12 等原生桌面观察。

## 9. 回退、升级与最终交付

行为回退：关闭宿主启用的 `FillOnAreaDrop`，重启后恢复现有精确按钮行为；若启用了浮动提示层，记录其独立配置并验证回退效果。

依赖回退：恢复补丁接入前的官方 Dock.Avalonia 版本、源配置和锁文件，重新构建并核对产物；同时撤销依赖补丁属性的样式引用。由于没有改变 Layout V3 schema，不通过删除或重置用户布局完成回退。

后续升级 Dock 时重新核对四个源码入口，尝试应用补丁并执行规则、Host 和桌面回归；上游提供等效能力后移除补丁，不能无验证地沿用旧差异。

最终交付清单：

- [ ] 固定上游来源、补丁、许可证和可重复构建脚本。
- [ ] 明确的补丁包版本、仓库源、锁文件与干净还原记录。
- [ ] Host 策略接入、框架与 Host 测试、更新后的当前使用说明。
- [ ] 完整 Gate 结果、桌面矩阵与未完成事项，状态分别登记。
- [ ] 补丁及 Host 实际产物身份、兼容证据适用范围和回退步骤。

本计划当前只有文档交付完成。实现、自动化、桌面、部署、公开发布的状态不得互相代替；集中状态见 [待办与验收](README.md)。
