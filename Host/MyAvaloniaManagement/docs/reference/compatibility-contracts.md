# 主项目兼容约束

> 用途：内部重构必须保护的外部可观察行为。状态：当前；核对日期：2026-09-16。事实源：当前 SDK public 类型、Host Loader/Registry/Workspace、文档与布局实现及对应测试。

本文是行为约束，不是阶段验收签字。版本见[集中基线](../../../../docs/reference/platform-baseline.md)，历史结果见[归档](../../../../docs/archive/README.md)。本仓验证只覆盖 Host 与 MyPlugTest；外部业务插件需独立验收。

## 1. public API

当前 public 插件契约只来自 `MyAvaloniaManagement.PluginSdk`、
`MyAvaloniaManagement.PluginSdk.UI` 与 `MyAvaloniaManagement.PluginSdk.Workflow`。Host 窗口、View、ViewModel、加载器、注册表、工厂、消息和
内建贡献实现均为 internal；插件不得编译引用 Host 可执行程序集。Host 生产模块入口已使用最终 UI SDK；
插件只引用 SDK，不引用 Host 实现。`MyAvaloniaManagement.LegacyPluginContracts` 已整体删除；活动项目、
构建探针、Loader 与包均不再包含 `MyAvaloniaManagementCommon.dll`。

当前 Document/Tool 模型只通过 `ManagedDocumentDockable` 与 `ManagedToolDockable` 适配 Dock。普通插件
模型不得创建或继承 Dock；Document 每次创建拥有独立 Scope，Tool 是所属 Provider singleton。View 必须
来自 Workspace Catalog 的精确冻结工厂并在发布前构造，禁止程序集扫描、类型名猜测和反射回退。该内部实现没有改变
Plugin SDK public API 或 manifest；Document 信封保持 V2；当前布局写入 V3，V2 仅首次只读转换。

Core/UI 活动 v3 基线及 Workflow v1 基线分别维护；当前分类、条目数和 verify/seal 区别见
[Plugin SDK API 兼容基线维护指南](../../../../docs/reference/plugin-sdk-api-compatibility.md)。
未登记新增、删除、参数与返回类型变化必须经兼容审阅；seal 不会自动替维护者移动或冻结签名。

[`HostApiBoundaryTests`](../../../MyAvaloniaManagement.Tests/HostApiBoundaryTests.cs) 继续确保 Host 不导出
`MyAvaloniaManagement.*` 类型。测试和真实窗口 Harness 只能通过明确的 `InternalsVisibleTo` 使用
Host 实现，这不构成发布兼容承诺。

当前 Host Provider 不包含插件私有描述符。每个插件从新的空集合建立 Provider，只能通过明确 Host Port
共享能力；宿主或其他插件的普通服务类型不可解析。最终 `IPluginRegistration.Services` 只表示当前插件
私有集合，并在模块返回后封闭。`IPluginRegistrationContext` 已随 Legacy 项目整体删除，不属于活动源码。

### 1.1 窗口交互 Host Port

- `IPluginWindowInteraction` 只位于 UI SDK，只返回本地路径、`null` 或布尔结果；
- 不得暴露 `Window`、`TopLevel`、`IStorageProvider`、剪贴板实例或 Host 实现类型；
- Host 必须把同一受控实例注入每个插件私有 Provider，插件不得自行查找主窗口；
- 调用必须在 Avalonia UI 线程，null 选项/文本抛参数异常；无主窗口时按契约返回空值；
- 原生选择器返回后必须再次检查取消令牌，Document 关闭期间的迟到结果不得提交。

当前窗口端口属于活动 UI SDK。历史 v2 Shipped 仅用于追溯，不作为当前运行时回退接口。

### 1.1.1 全屏租约 Host Port

- V3 活动接口只有 `IDisposable? TryPresent(Control content)`；不得恢复 owner 参数、`TryRestore` 或双接口；
- `null` 只表示已有活动租约或宿主不可用；成功租约排他、引用身份唯一且重复释放无副作用；
- `TryPresent` 与有效租约首次释放必须在 Avalonia UI 线程；错误线程不得消耗租约；
- Host 只暂借 Control，不 Dispose 插件控件；挂载失败必须回滚 Content、覆盖层和活动状态；
- 窗口真正关闭或 ContentHost 销毁可自动失效，取消关闭不得提前释放；旧令牌不能影响后续新租约；
- v2 Shipped 中的 owner API 是历史事实，不参与 V3 编译、包消费或运行时 fallback。

### 1.2 版本所有权

- 产品、Host 程序集身份和 Plugin SDK 版本集中定义在根级 `Directory.Version.props`；
- 当前产品为 `3.0.0`，六包统一 `3.4.1`；Host AssemblyVersion 为 `3.0.0.0`，Core/UI 为 `3.4.1.0`，Workflow 保留 `1.0.0.0`；Host 没有独立 public API 版本线；
- 兼容性按 API、程序集绑定政策及真实消费者验证；破坏性契约变化需要主版本迁移，统一包号不能代替兼容审阅；
- 每个插件只拥有自己的 `PluginVersion`，清单版本必须与入口程序集精确一致；
- manifest、布局、外观、诊断和Document 信封分别拥有整数 schema，不共享全局数字；
- 插件内容 schema 由内容所有者解释，不能使用插件发布版本替代；
- 普通进程内消息不添加无迁移或分派行为的版本占位字段。

### 1.3 SDK 包边界

- 基础包、程序集和根命名空间均为 `MyAvaloniaManagement.PluginSdk`，只依赖 .NET BCL；
- Core 不包含 Host，也不依赖 Avalonia、DI、Dock、Newtonsoft 或任何主题包；
- `MyAvaloniaManagement.PluginSdk.UI` 是同版本真实契约程序集，只允许 Core、Avalonia、
  DI.Abstractions 与宿主明确支持的 Fluent/Semi/Ursa Profile 精确依赖；
- `MyAvaloniaManagement.PluginSdk.Workflow` 只依赖 Core，公开冻结 Schema、实例校验、可赋值、路径和
  双 revision；插件 ZIP 不携带该 DLL，由满足插件 SDK 区间的 Host 默认 ALC 提供；
- UI 不包含 Dock 或 Newtonsoft；插件模型不得继承或创建 Dock 类型；
- Core/UI 的兼容新增可以随 SDK 次版本发布；任何破坏已编译插件的变化必须提升 SDK 主版本；
- Legacy 项目不存在，所有包均不得包含或依赖 `MyAvaloniaManagementCommon.dll`；
- 六包发布记录和 MIT 许可证已存在；发布需要独立流程，详见[NuGet 维护](../../../../docs/maintenance/nuget-release.md)，不由 Host 启动或 Gate 自动上传。

### 1.4 插件私有事件

- V3 SDK 与 Host 不提供事件总线、转发层或兼容接口；需要消息的插件在自身程序集声明最小接口；
- MyPlugTest 与 BiliDownloader 的消息器分别由对应插件 Provider singleton 持有，事件与处理器均不得为 `null`；
- 发布在调用线程同步执行，只匹配精确泛型事件类型，并按订阅顺序派发；
- 处理器异常原样传播并停止后续派发，不包装、吞掉、重试或切换线程；
- 订阅令牌只移除自身且可重复释放；进入发布快照的处理器可能最后执行一次；
- Document 必须持有令牌，并在自身 Scope 释放时退订；关闭竞态仍用
  `IDocumentLifetime.IsClosing` 抑制迟到副作用；
- 不同插件 Provider 和不同 HostRuntime 的消息实例不可见；不允许静态默认实例、全局 Reset 或底层
  messenger 暴露；
- 插件 Provider 释放消息器，释放后发布或订阅抛 `ObjectDisposedException`；普通内存事件不增加版本字段。

### 1.5 插件样式与 UI Profile

Host 在 Light/Dark 下均提供以下 `SolidColorBrush` 语义资源：

```text
AppPanelBrush                 AppSubtlePanelBrush
AppToolSelectedBrush          AppDividerBrush
AppBorderBrush                AppSecondaryTextBrush
AppInfoBrush                  AppWarningBrush
AppWarningPanelBrush          AppWarningBorderBrush
AppErrorBrush                 AppDangerBrush
AppReadMessageBackgroundBrush AppUnreadMessageBackgroundBrush
```

- 主题相关引用必须使用 `DynamicResource`；删除、改名或改变资源类型属于 SDK 破坏性变化；
- 标准 Avalonia 控件自动继承 Host 全局主题，普通插件不需要 UI Profile；
- 插件可以通过本程序集内的 `StyleInclude` 组织局部样式，资源键和 Style Class 使用插件 ID 前缀；
- 插件不得向 `Application.Current.Styles` 注入或替换全局主题；
- `DockTheme*`、Semi、Ursa 内部资源键不属于基础语义契约；直接使用时必须引用同版本 UI Profile；
- Host 按 Fluent、Semi、Ursa Semi、Dock Fluent、Host Styles 的固定所有权顺序组合主题。

### 1.6 诊断兼容与安全边界

- `HostDiagnosticRecord` 的独立 `schemaVersion` 已提升为 2，SDK 兼容字段只写 `sdkRange`；不读取或迁移旧诊断日志；
- `TechnicalDetail` 兼容字段只允许受控生命周期阶段和毫秒耗时，其他诊断为 `null`；
- `UserMessage` 只来自宿主错误码/阶段固定映射；插件、文件和异常不能提供记录文本；
- 持久记录可保留稳定错误码、阶段、异常类型、经校验的 Plugin ID、程序集简单名、稳定 ID、版本区间、
  枚举和耗时；不得保留正文、密码、Cookie、Token、签名 URL、请求响应、绝对路径或异常原文；
- 插件状态、启动失败窗口、剪贴板、默认 Trace/stderr 与 JSONL 必须遵循同一边界；
- `MYAVALONIA_ENABLE_SENSITIVE_DIAGNOSTICS=1` 只允许当前进程向临时 Trace/stderr 输出带警告的原始异常，
  不能进入配置、UI、记录或 JSONL，Release 门禁不得设置它；
- Plugin SDK public API 不增加日志/脱敏接口，`PluginLifecycleState.ErrorMessage` 签名保持不变但失败文本固定。

## 2. 插件发现与激活

### 2.1 加载前清单与版本检查

每个插件独占目录必须在根级提供 `plugin.manifest.json`。宿主先完成全部清单预检和
`pluginId` 全局唯一性检查，之后才允许创建 `PluginLoadContext` 或读取入口程序集元数据。
清单缺失、损坏、schema 未知或版本不兼容时隔离当前目录；重复 `pluginId` 属于全局组合歧义，
在加载任何插件 DLL 前阻断宿主启动。

当前唯一生产清单格式是 manifest v2：

```json
{
  "schemaVersion": 2,
  "pluginId": "myavalonia.plugin.sample",
  "pluginVersion": "3.0.0",
  "entryPoint": {
    "assembly": "SamplePlugin.dll",
    "type": "SamplePlugin.Plugin.SamplePluginModule"
  },
  "sdk": {
    "minInclusive": "3.4.1",
    "maxExclusive": "4.0.0"
  }
}
```

- 字段名称区分大小写；未知、重复或缺失字段均拒绝，不允许注释或尾随逗号；
- 版本只接受 `major.minor.patch` 三段数字；SDK 采用 `minInclusive <= current < maxExclusive`；
- `entryPoint.assembly` 只能是插件根目录中的单个 DLL 文件名；
- `entryPoint.type` 必须是区分大小写的规范完整类型名，不得含空白、程序集限定名、泛型或嵌套符号；
- 入口必须携带同名 `.deps.json`，宿主不扫描目录猜测托管或原生依赖；
- `pluginVersion` 必须与入口 `AssemblyVersion` 精确一致；manifest `pluginId` 是插件身份唯一事实源；
- Core 与 UI 当前版本均为 `3.4.1`；二者不一致属于宿主配置错误，兼容诊断统一为 `PLUGIN_SDK_INCOMPATIBLE`；

reader 不读取 manifest v1，也不存在 v1/v2 双 reader；清单只解决兼容和确定性加载，不提供签名、防篡改、权限沙箱或热卸载。

### 2.1.1 Workflow Action 调用边界

- Provider 通过独立 `IWorkflowActionRegistration` 声明 Descriptor 与 scoped Handler；Consumer 必须显式
  调用 `UseWorkflowActionGateway`；同一插件可以兼任 Provider/Consumer，但 caller-bound 目录隐藏自有
  Action，Host 在资源计数、授权和 Scope 前拒绝自调用，并拒绝 Handler 异步链中的嵌套调用；
- Gateway 绑定 manifest CallerId，只列举当前可用目录并创建 `IWorkflowActionRun`；请求不能提交 CallerId、
  OwnerId、RunId 或授权结果；
- Run 独占取消、并发、OncePerRun 授权缓存和 catalog revision；Dispose 停止接收、取消并等待本 Run 调用；
- 跨 ALC 只允许 SDK/BCL/JSON；Host 每次在已提交 Owner Provider 创建独立 invocation scope；
- 输入和成功输出均通过冻结 Schema/预算；满并发快速拒绝，不建立无界队列；
- Descriptor、输入、输出与 Studio 常量使用 Workflow SDK 的同一验证器；字符串长度按 Rune，integer 为
  Int64，number 必须可表示为 decimal；共享问题仍映射为 Host 既有稳定失败码；
- Catalog 同时计算 ContractRevision 与 PresentationRevision；Run 快照和授权指纹只依赖 ContractRevision；
- 结构化结果和诊断不包含插件异常原文、参数正文、路径、Secret 或进度消息；
- Host 关闭先取消并排空 Handler，未排空时不得停止 Lifecycle 或释放任何相关 Provider。

仓库内 Managed Plugin 的清单不在源码树手写，而由 `ManagedPluginId`、`PluginVersion`、
`ManagedPluginEntryType` 和两个 SDK 区间端点生成。公共构建协议使用独立 `Csc` 探针引用成品程序集，
在生成清单前验证精确入口的可见性、接口、抽象/泛型状态与 public 无参构造；它还强制包含入口 DLL、deps、PDB，排除 Host/SDK/UI 共享闭包
和非 win-x64 原生资产。正式分发物按插件独立生成
`<AssemblyName>-<PluginVersion>-win-x64.zip`；ZIP 内只有 `Controls/<PluginFolder>/`，外置同名
`.manifest.json` 记录 ZIP 与全部文件摘要。目录部署是开发产物，ZIP 是正式分发物，两者使用同一资产集合。

### 2.2 Managed 插件

- Host 只按 `entryPoint.type` 的大小写敏感完整名称取得一个入口类型，不调用 `GetTypes()` 扫描模块；
- 入口必须 public、非抽象、非泛型，实现最终 UI SDK `IPluginModule` 并具有 public 无参构造；
- 同程序集中的第二个模块不构成错误，但未声明模块绝不被构造、配置或用来劫持入口；
- `Configure(IPluginRegistration)` 在 Host Provider 构建后、当前插件 Provider 构建前且每进程只执行一次；
- `registration.PluginId` 由宿主从已验证 manifest 注入，只读且不能覆盖；
- `registration.Services` 只属于当前插件；模块返回后任何写入立即失败，私有多实现、keyed 和开放泛型注册继续允许；
- Document/Tool/Lifecycle 使用所属插件 Provider 激活，允许构造注入；View 使用无参工厂按需创建；
- Lifecycle 的声明取自 Registry，组合阶段验证 singleton 可解析；初始化与关闭由 Host 生命周期协调器执行；
- 注册只发生在组合阶段，不支持运行期追加、删除、启停或热卸载；未登记类型不会被发现。

### 2.3 拒绝与共同规则

- 缺少 deps，或精确入口不存在、不可访问、抽象、泛型、接口错误、缺少 public 无参构造时隔离当前目录；
- 无模块策略程序集不再获得 public 无参构造激活，也不会生成 `myavalonia.legacy.*` 所有者；
- 完整类型预检失败会隔离整个插件目录，不能把同一发布物拆成“部分成功”；
- 模块构造、模块配置和插件 Provider 构建失败只隔离所属插件；Host 与其他成功插件继续组合；
- 通过 `registration.Services` 直接登记普通类型只留在私有 Provider，不会发布到 Registry；插件无法取得 Host 描述符；
- 插件内重复 Document/Tool ID、重复精确模型映射、Document/Tool 共用模型、多生命周期或所有者混入会整体丢弃该候选；
- Descriptor、模型、View 工厂和生命周期类型在专用注册调用中冻结；读取 Registry 元数据不会构造模型或执行插件回调；
- 跨插件 Document/Tool ID 或精确模型冲突排除全部冲突插件；Host 命名空间在插件局部校验时拒绝，
  Host Catalog 与 Plugin Registry 的合并事实若碰撞则立即失败；无冲突插件继续发布；
- 配置、Provider 构建、局部校验或全局冲突均不得留下部分 Registry、Provider 租约或 Document Scope；
- 插件根目录快照在进程内不刷新，更新插件需要重启应用。

基础 SDK 及其 public 签名依赖、受支持 UI Profile 及其依赖均由
`AssemblyLoadContext.Default` 提供。插件目录不得携带这些 DLL 的私有副本；身份或版本不兼容时，
宿主在完整类型预检阶段以 `PLUGIN_SHARED_ASSEMBLY_MISMATCH` 隔离插件。普通业务依赖只由当前
插件的 deps/RID 图在独立 ALC 中解析，不能因为宿主碰巧加载过同名程序集就进入共享集合。

## 3. Document 契约

- Host Welcome 通过 Host Catalog 与同步 Host Activator 创建；插件 Document 通过 Plugin Registry、
  所属插件 Activator 和异步工厂创建。生产代码不存在 Legacy Document 命令参数、ID 映射或 Scope 工厂；
- 唯一磁盘格式是 Document 信封 v2，根必须且只能包含 `schemaVersion`、`pluginId`、`documentTypeId`、
  `title`、`savedAtUtc`、`content`；content 只含 `schemaVersion` 与原生 JSON `payload`；
- 根 `schemaVersion` 只能为 `2`；UTF-8 文件上限为 8 MiB，JSON 最大深度为 8；注释、尾随逗号、
  重复、未知、缺失、大小写错误和类型错误均拒绝；
- 插件公共 `DocumentContent` 克隆 `JsonElement`；可保存模型实现 `IPersistablePluginDocument` 的
  `CaptureSaveSnapshotAsync(ClosingToken)`、`IsDirty` 与 `AcceptChanges(savedRevision)`；插件拥有
  修订含义但不拥有路径或磁盘身份，Host 只原样回传修订；
- 宿主从不可变 Plugin Registry 取得可持久化插件的 `PluginId`、`DocumentTypeId`，并由内部状态存储按 Document 引用保存规范插件注册项与当前主路径；Host Welcome 不进入此路径；标题来自文件名，UTC 时间来自 `TimeProvider`；插件只解释内容版本和 payload；
- 信封中的 Document 类型必须是规范主 ID，不接受历史别名；`pluginId` 必须等于注册项所有者；
- 路径转绝对路径后按 Windows 不区分大小写规则查重；
- 批量打开以单文件为错误边界；
- 同一路径已打开时激活原文档，不创建重复实例；
- 无当前路径时由宿主选择保存目标，已有路径直接覆盖；恢复出的 Document 由宿主内部恢复注册表强制另存，并拒绝覆盖损坏原件或备份；
- 内容捕获不得更新标题、路径或脏状态；主文件写入失败不得确认；捕获后编辑必须使旧修订确认保持
  Dirty，关闭也必须保持打开；插件没有路径策略或通用保存完成回调；
- 主文件和 `<主路径>.recovery.bak` 均通过同目录临时文件原子替换；备份失败不得回滚已成功的主文件；
- 标签关闭和窗口退出必须保护脏 Document；取消确认或保存失败不得提前取消 `ClosingToken`；
- V2 是唯一受支持的 Document 信封；不存在 V1 兼容对象或迁移链，任何非 V2 结构直接拒绝；
- 打开失败不发布 Document、不泄漏临时 Scope，也不创建、迁移或覆盖任何文件；
- Host internal 信封异常、JSON、I/O、权限、路径与插件边界异常统一映射为固定脱敏结果。

所有生产 Document 都拥有独立 DI Scope，并在 Dock 最终确认关闭后通过唯一 Lease 释放。

## 4. Dock 与 Tool 契约

稳定布局 ID：

- `Root`
- `Workspace`
- `WorkspaceColumns`
- `WorkspaceCenterRows` / 当前 `WorkspaceRows` 兼容语义
- `LeftPane` / `LeftTools`
- `TopPane` / `TopTools`
- `Documents`
- `BottomPane` / `BottomTools`
- `RightPane` / `RightTools`

兼容行为：

- 生产与 Harness 只通过规范 `Documents` Locator 或 Workspace Session 取得 Document Dock；`Files` 查询不存在；
- `Plug` 是已删除的临时兼容别名，不得扩散到新消费者；
- Tool 支持 Left、Right、Top、Bottom；
- Top/Bottom 使用工作区全宽稳定停靠点；
- 关闭 Tool 表示隐藏，之后恢复同一实例；
- Pinned 表示仍显示，不等同于关闭隐藏；
- 最后一个 Tool 隐藏后停靠点被移除时，恢复必须重建同一稳定节点；
- Document、Tool 及其合法内容组允许浮动，固定主骨架不能浮动；浮动/回停不重建 View、模型或 Scope；
- 浮窗不支持自动隐藏，回停后恢复；内容全屏期间先退出全屏再迁移；
- 主窗口内部拖放与停靠继续可用；
- 每个 HostRuntime 只有一个 `WorkspaceSession` 和一个主 Root；浮窗 Root 归属该工作区，窗口只作为独立绑定消费者；
- `HostDockFactory` 不拥有 Root、Document 或 Tool 集合；未绑定和重复绑定都必须快速失败；
- 工具中心只消费不含 Dock 类型的 `ToolWorkspaceState` 快照；布局、可用性和收藏是独立状态。
- 当前 Host 中所有 Tool 都允许隐藏，包括旧 SDK 声明 `Prevent` 的插件；该枚举及构造签名保留二进制兼容。
- `myavalonia.host.tool.management` 已退役；新入口为“工具 → 工具中心…”，属于 Host 非模态窗口，不登记为 Tool。

## 5. 布局 V3 契约

- 当前文件 `layout-v3.json`、schema 3；上一有效备份 `.bak`，单数据根独占写入句柄。
- 精确字段、ID、数量、深度、比例与尺寸严格验证；坏输入保留 `.invalid.bak`，未来 schema 只读保护。
- 只记录 Tool 的窗口、分组、显隐与主窗自动隐藏；Document 路径、标题、身份、内容及纯文档浮窗不持久化。
- 合法但不可用或缺失的工具保留原记录，当前仅投影可用项；空浮窗不显示。
- 首次无 V3 历史时严格转换 V2，原字节不变；退役内置工具精确清理；V1 不读取或修改。
- 自动保存防抖且串行；主窗退出先确认和排空全部文档，再冻结最终快照，拆窗不覆盖它。
- 重置先确认，保留文档修改、View 和 Scope；应用失败恢复原运行树，已关闭的展示壳可重建。

详细边界见 [Layout V3](../../../../docs/reference/dock-layout-snapshot-v3.md)；[V2](../../../../docs/reference/dock-layout-snapshot-v2.md) 保留为只读输入和旧行为参考。

## 6. 启动和关闭契约

- `Program.Main` 继续作为唯一生产入口；Avalonia Builder 是 Host internal 组合根能力；
- App 通过 `IHostDesktopShell` 构造注入，生产启动不得读取进程全局服务定位器；
- 根容器继续启用 `ValidateScopes` 与 `ValidateOnBuild`；
- 插件在 Avalonia 消息循环前初始化；
- 关闭先拒绝新操作并排空 Command、Workflow 与 Document 在途工作，再按所有权释放 Workspace 和 Scope；
- 生命周期协调器维护初始化、超时、迟到完成与 Shutdown 状态，停止和释放不能仅按外层等待是否返回判断；
- 仅在实际任务和取消通知安全结束、所需 Shutdown 成功后释放 Provider；否则保留相关对象图并记录脱敏结果；
- 安全路径反向释放插件 Provider，最后释放 Host Provider；启动失败回滚同样遵守已创建对象的所有权。
- 默认宿主数据根为 `%LOCALAPPDATA%\MyAvaloniaManagement\v2\`，旧 `v1` 和预发布目录不读取、迁移或删除；
- `MYAVALONIA_DATA_DIRECTORY` 继续表示完整数据根且不追加 `v2`，避免测试污染用户 LocalAppData；
- `MYAVALONIA_SMOKE_TEST=1` 继续创建真实窗口并通过正常 Closing 路径退出。

## 7. 内部实现不构成契约

以下内容可在保持行为和测试的前提下继续调整：

- Registry、Builder、Navigator、Coordinator、Adapter 的类名和文件组织；
- 内部字典、集合和缓存实现；
- 内部构造函数与 `internal` 记录类型；
- 日志实现细节，但不得改变诊断 schema 2 白名单语义，或记录文档内容、凭据、异常正文和未验证路径数据；
- 测试替身和测试项目内部结构。

## 8. 变更检查表

每次变更重新确认，不预填历史通过标记：

- [ ] public API 与依赖边界没有无意破坏，签名分类符合当前维护规则。
- [ ] manifest、Document、Layout 的严格字段、身份、失败原子性和数据根保持明确。
- [ ] 每个 Document 的取消、Scope/View 释放及 Tool singleton 寿命有对应回归。
- [ ] 在途任务、迟到完成、启动失败与 Runtime 退出没有释放仍在使用的依赖。
- [ ] 菜单、搜索、快捷键与窗口使用同一事实和执行入口，诊断未泄漏原始内容。
- [ ] 验证范围、失败/跳过及未覆盖的外部业务场景记录真实，未用历史结果授予当前发布资格。

## 9. 路径与展示兼容边界

- `C:` 与 `C:\` 均规范化为本地驱动器根 `C:\`，继续使用既有驱动器集合展示；
- `\\server\share` 及尾分隔符形式是 UNC 共享根，作为唯一自定义根展示；其子目录是普通目录；
- 空白、相对、设备、非法或已经消失的路径不得改变当前文件树、标题或模式；
- 路径存在性只经 internal `IHostStorageService.DirectoryExists`，不构成 Plugin SDK public API；
- `CategoryNode.CategoryName` 和 `Documents` 是构造期只读快照，仅 `IsExpanded` 可变；
- 插件部署目录仍为 `Controls`，只把内部符号改名为 `PluginDeploymentConstants.PluginsSubdirectory`；
- manifest、Document envelope、layout schema 与数据根遵循集中基线；插件版本与 SDK 区间由各插件实际产物声明。

## 10. 目录展示兼容边界

- 插件分组菜单保留稳定身份、停靠位置及旧版完整字符串分组；Tool 隐藏政策遵循当前 Host 规则。
- 新树只解释 `/`，`-` 为名称内容；非法空段整体回退，创建身份仍是 DocumentTypeId 与 CreationIntentId。
- IconPath 支持公共 `builtin:` 和经归属校验的 `plugin:` 图标引用，无效值使用默认；旧版平铺目录继续使用统一四宫格。
- 导航文件 `plugin-navigation-v1.json` 独立于布局，`legacy`/`tree` 与可自定义显示名分开保存。
- 功能中心与两个 Tool 模式共用原创建协调器，不改变 SDK、manifest、Document envelope 和插件所有权。
- Host 退出新增文档操作排空屏障；超时遵守 V5 保留策略，迟到完成不自动释放 Provider。
- 外部插件分类取决于实际 Descriptor，本仓不承诺已迁移所有外部元数据。

## 11. 插件看板与验收证据

- 看板沿用原命令 ID 和独立非模态窗口所有权，不写入 Dock 布局，不初始化插件贡献。
- 当前会话状态、磁盘检查快照与验收报告分别表达；证据不能改变 Loader 准入或生命周期可用性。
- 仅显式检查计算完整产物摘要，关闭取消自身任务，代次检查防止迟到结果覆盖新快照。
- 导入报告校验格式与身份后原子保存至 Host 数据根；来源标签不构成签名认证。复制及导出不传播报告任意正文。
- `plugin.build.json` 是可选旁路信息，缺失表示编译依赖未知，不扩大 manifest schema 2。
- 唯一规则源、指纹与矩阵的详细语义见[插件兼容证据契约](../../../../docs/reference/plugin-compatibility.md)。
