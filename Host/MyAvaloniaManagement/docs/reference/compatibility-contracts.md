# 主项目兼容约束

## 1. 文档目的

本文列出内部重构不得无意改变的外部可观察行为。它不是完整 API 参考，而是代码评审和回归测试清单。

> Managed Plugin v1 的正式支持边界已在 G1 冻结：Windows x64、可信进程内 Managed Plugin、
> 严格清单、退出后替换插件。G2 已将 Host 自有实现全部收口为 internal；G3 已形成正式基础
> SDK、可选 UI Profile 和宿主语义样式契约；G4 已删除 Legacy 二进制激活、无 deps 回退和
> 历史加载 Facade；G5 已用显式贡献和不可变 Plugin Registry 替换策略/View 隐式发现；G9 已用
> SDK 自有、每 HostRuntime 隔离的 `IHostEventBus` 收口进程内事件；G13 已用可读文本和成员级
> 变异门禁冻结正式 Plugin SDK v1 public API；G15 已固定 schema 1 诊断的白名单语义和默认脱敏边界；
> G16 已用 `managed-plugin-v1.0.0` 定位最终文档、SDK API 和四插件兼容基线。

> 当前分支已完成 V2 G14 封板：正式 Core/UI SDK、严格 manifest v2、精确入口加载、构建协议、每插件
> 独立 Provider、Host 声明式贡献目录、internal Dock Adapter、Document V2、Layout V2 与 internal
> 生命周期已建立；MyPlugTest、DaTang、MySmallTools 与 BiliDownloader 已全部迁移。Legacy 项目和
> 过渡构建面已经删除；Core/UI v2 API 已进入 Shipped，两轮隔离门禁与 Windows V2 Smoke 已建立，
> 历史 v1 签署事实保持可追溯。

> 当前源码已完成未发布 V3 G6：产品、Core/UI SDK 和四插件版本为 `3.0.0`，SDK 区间为
> `[3.0.0, 4.0.0)`。活动签名位于 v3 Unshipped，Document 保存采用修订快照与指定修订确认，
> 激活采用互斥的 `NewDocumentActivation` / `RestoreDocumentActivation`，插件注册采用 Host 最终提交
> 与 ID 归属校验；SDK/Host 通用事件总线已删除，消息实例归对应插件 Provider 所有；Dock Factory、
> 唯一 Workspace Session 与无 Dock Tool ReadModel 已分离；
> manifest、Document envelope、layout 和默认数据根继续使用 schema/generation 2。

## 2. public API

当前 V3 G6 public 插件契约只来自 `MyAvaloniaManagement.PluginSdk` 与
`MyAvaloniaManagement.PluginSdk.UI`。Host 窗口、View、ViewModel、加载器、注册表、工厂、消息和
内建贡献实现均为 internal；插件不得编译引用 Host 可执行程序集。Host 生产模块入口已使用最终 UI SDK；
四个业务插件只引用最终 SDK。`MyAvaloniaManagement.LegacyPluginContracts` 已整体删除；活动项目、
构建探针、Loader 与包均不再包含 `MyAvaloniaManagementCommon.dll`。

G8 生产组合中，只有 `ManagedDocumentDockable` 与 `ManagedToolDockable` 可以继承 Dock 类型。普通插件
模型不得创建或继承 Dock；Document 每次创建拥有独立 Scope，Tool 是所属 Provider singleton。View 必须
来自 Registry 冻结工厂并在发布前精确构造，禁止程序集扫描、类型名猜测和反射回退。该内部实现没有改变
Plugin SDK public API 或 manifest；Document 与 Layout 磁盘契约均已切换为唯一 V2。

历史 v1 正式签名随 Core 的 `ApiCompatibility/v1` 保存；Core/UI 的 v2 基线由 G14 冻结为
Shipped 85/46 条且 Unshipped 均为空。活动 v3 Shipped 为空、Unshipped 为 127/46，并由
`scripts/Test-PluginSdkCompatibility.ps1 -Baseline v3` 验证。未登记
新增、删除、可见性收窄、参数或返回类型变化都会给出成员级 RS 诊断。完整维护流程见
[Plugin SDK API 兼容基线维护指南](../../../../docs/reference/plugin-sdk-api-compatibility.md)。

[`HostApiBoundaryTests`](../../../MyAvaloniaManagement.Tests/HostApiBoundaryTests.cs) 继续确保 Host 不导出
`MyAvaloniaManagement.*` 类型。测试和真实窗口 Harness 只能通过明确的 `InternalsVisibleTo` 使用
Host 实现，这不构成发布兼容承诺。

当前 Host Provider 不包含插件私有描述符。每个插件从新的空集合建立 Provider，只能通过明确 Host Port
共享能力；宿主或其他插件的普通服务类型不可解析。最终 `IPluginRegistration.Services` 只表示当前插件
私有集合，并在模块返回后封闭。`IPluginRegistrationContext` 已随 Legacy 项目整体删除，不属于活动源码。

### 2.1 窗口交互 Host Port

- `IPluginWindowInteraction` 只位于 UI SDK，只返回本地路径、`null` 或布尔结果；
- 不得暴露 `Window`、`TopLevel`、`IStorageProvider`、剪贴板实例或 Host 实现类型；
- Host 必须把同一受控实例注入每个插件私有 Provider，插件不得自行查找主窗口；
- 调用必须在 Avalonia UI 线程，null 选项/文本抛参数异常；无主窗口时按契约返回空值；
- 原生选择器返回后必须再次检查取消令牌，Document 关闭期间的迟到结果不得提交。

SDK 与插件的 V2 正式基线为 `2.0.0`；当前未发布版本线为 `3.0.0`。本窗口端口已包含在
G14 冻结的 v2 Shipped，并在 G1 原样进入 v3 Unshipped。

### 2.2 版本所有权

- 产品、Host 程序集身份和 Plugin SDK 版本集中定义在根级 `Directory.Version.props`；
- 当前未发布产品与 SDK 版本为 `3.0.0`，Host 与 SDK `AssemblyVersion` 为 `3.0.0.0`；V3 不重新引入独立 Host API 版本线；
- 兼容的 SDK 新增提升次版本但保持同一主版本程序集身份；破坏性契约变化提升主版本；
- 每个插件只拥有自己的 `PluginVersion`，清单版本必须与入口程序集精确一致；
- manifest、布局、外观、诊断和未来 Document 信封分别拥有整数 schema，不共享全局数字；
- 插件内容 schema 由内容所有者解释，不能使用插件发布版本替代；
- 普通进程内消息不添加无迁移或分派行为的版本占位字段。

### 2.3 SDK 包边界

- 基础包、程序集和根命名空间均为 `MyAvaloniaManagement.PluginSdk`，只依赖 .NET BCL；
- Core 不包含 Host，也不依赖 Avalonia、DI、Dock、Newtonsoft 或任何主题包；
- `MyAvaloniaManagement.PluginSdk.UI` 是同版本真实契约程序集，只允许 Core、Avalonia、
  DI.Abstractions 与宿主明确支持的 Fluent/Semi/Ursa Profile 精确依赖；
- UI 不包含 Dock 或 Newtonsoft；插件模型不得继承或创建 Dock 类型；
- Core/UI 的兼容新增可以随 SDK 次版本发布；任何破坏已编译插件的变化必须提升 SDK 主版本；
- Legacy 项目不存在，两个 nupkg 均不得包含或依赖 `MyAvaloniaManagementCommon.dll`；
- 当前不自动发布公共 NuGet。宿主发布制品应同时提供两个 nupkg；对外分发前必须补充项目许可证。

### 2.4 插件私有事件

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

### 2.5 插件样式与 UI Profile

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

### 2.6 诊断兼容与安全边界

- `HostDiagnosticRecord` 的独立 `schemaVersion` 已提升为 2，SDK 兼容字段只写 `sdkRange`；不读取或迁移旧诊断日志；
- `TechnicalDetail` 兼容字段只允许受控生命周期阶段和毫秒耗时，其他诊断为 `null`；
- `UserMessage` 只来自宿主错误码/阶段固定映射；插件、文件和异常不能提供记录文本；
- 持久记录可保留稳定错误码、阶段、异常类型、经校验的 Plugin ID、程序集简单名、稳定 ID、版本区间、
  枚举和耗时；不得保留正文、密码、Cookie、Token、签名 URL、请求响应、绝对路径或异常原文；
- 插件状态、启动失败窗口、剪贴板、默认 Trace/stderr 与 JSONL 必须遵循同一边界；
- `MYAVALONIA_ENABLE_SENSITIVE_DIAGNOSTICS=1` 只允许当前进程向临时 Trace/stderr 输出带警告的原始异常，
  不能进入配置、UI、记录或 JSONL，Release 门禁不得设置它；
- Plugin SDK public API 不增加日志/脱敏接口，`PluginLifecycleState.ErrorMessage` 签名保持不变但失败文本固定。

## 3. 插件发现与激活

### 3.1 加载前清单与版本检查

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
    "minInclusive": "3.0.0",
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
- Core 与 UI 当前 SDK 版本均为 `3.0.0`；二者不一致属于宿主配置错误，兼容诊断统一为 `PLUGIN_SDK_INCOMPATIBLE`；
- reader 不读取 manifest v1，也不存在 v1/v2 双 reader；
- 清单只解决兼容和确定性加载，不提供签名、防篡改、权限沙箱或热卸载。

仓库内 Managed Plugin 的清单不在源码树手写，而由 `ManagedPluginId`、`PluginVersion`、
`ManagedPluginEntryType` 和两个 SDK 区间端点生成。公共构建协议使用独立 `Csc` 探针引用成品程序集，
在生成清单前验证精确入口的可见性、接口、抽象/泛型状态与 public 无参构造；它还强制包含入口 DLL、deps、PDB，排除 Host/SDK/UI 共享闭包
和非 win-x64 原生资产。正式分发物按插件独立生成
`<AssemblyName>-<PluginVersion>-win-x64.zip`；ZIP 内只有 `Controls/<PluginFolder>/`，外置同名
`.manifest.json` 记录 ZIP 与全部文件摘要。目录部署是开发产物，ZIP 是正式分发物，两者使用同一资产集合。

### 3.2 Managed 插件

- Host 只按 `entryPoint.type` 的大小写敏感完整名称取得一个入口类型，不调用 `GetTypes()` 扫描模块；
- 入口必须 public、非抽象、非泛型，实现最终 UI SDK `IPluginModule` 并具有 public 无参构造；
- 同程序集中的第二个模块不构成错误，但未声明模块绝不被构造、配置或用来劫持入口；
- `Configure(IPluginRegistration)` 在 Host Provider 构建后、当前插件 Provider 构建前且每进程只执行一次；
- `registration.PluginId` 由宿主从已验证 manifest 注入，只读且不能覆盖；
- `registration.Services` 只属于当前插件；模块返回后任何写入立即失败，私有多实现、keyed 和开放泛型注册继续允许；
- Document/Tool/Lifecycle 使用所属插件 Provider 激活，允许构造注入；View 使用无参工厂按需创建；
- Lifecycle 的身份和实现类型取自 Registry；G5 只验证 singleton 可解析，不执行初始化、关闭、依赖图或状态机；
- 注册只发生在组合阶段，不支持运行期追加、删除、启停或热卸载；未登记类型不会被发现。

### 3.3 拒绝与共同规则

- 缺少 deps，或精确入口不存在、不可访问、抽象、泛型、接口错误、缺少 public 无参构造时隔离当前目录；
- 无模块策略程序集不再获得 public 无参构造激活，也不会生成 `myavalonia.legacy.*` 所有者；
- 完整类型预检失败会隔离整个插件目录，不能把同一发布物拆成“部分成功”；
- 模块构造、模块配置和插件 Provider 构建失败只隔离所属插件；Host 与其他成功插件继续组合；
- 通过 `registration.Services` 直接登记普通类型只留在私有 Provider，不会发布到 Registry；插件无法取得 Host 描述符；
- 插件内重复 Document/Tool ID、重复精确模型映射、Document/Tool 共用模型、多生命周期或所有者混入会整体丢弃该候选；
- Descriptor、模型、View 工厂和生命周期类型在专用注册调用中冻结；读取 Registry 元数据不会构造模型或执行插件回调；
- 跨插件 Document/Tool ID 或精确模型冲突排除全部冲突插件；与 Host 冲突时保留 Host；无冲突插件继续发布；
- 配置、Provider 构建、局部校验或全局冲突均不得留下部分 Registry、Provider 租约或 Document Scope；
- 插件根目录快照在进程内不刷新，更新插件需要重启应用。

基础 SDK 及其 public 签名依赖、受支持 UI Profile 及其依赖均由
`AssemblyLoadContext.Default` 提供。插件目录不得携带这些 DLL 的私有副本；身份或版本不兼容时，
宿主在完整类型预检阶段以 `PLUGIN_SHARED_ASSEMBLY_MISMATCH` 隔离插件。普通业务依赖只由当前
插件的 deps/RID 图在独立 ALC 中解析，不能因为宿主碰巧加载过同名程序集就进入共享集合。

## 4. Document 契约

- Host Welcome、MyPlugTest 与 DaTang 只通过 Registry、internal Activator 和异步工厂创建；
  它们的生产代码不存在 Legacy Document 命令参数、ID 映射或 Scope 工厂；
- BiliDownloader 尚未迁移，V2 Host 不加载其 `IDocumentCreationStrategy` 或 Creation Intent Provider；
- 唯一磁盘格式是 Document 信封 v2，根必须且只能包含 `schemaVersion`、`pluginId`、`documentTypeId`、
  `title`、`savedAtUtc`、`content`；content 只含 `schemaVersion` 与原生 JSON `payload`；
- 根 `schemaVersion` 只能为 `2`；UTF-8 文件上限为 8 MiB，JSON 最大深度为 8；注释、尾随逗号、
  重复、未知、缺失、大小写错误和类型错误均拒绝；
- 插件公共 `DocumentContent` 克隆 `JsonElement`；可保存模型实现 `IPersistablePluginDocument` 的
  `CaptureSaveSnapshotAsync(ClosingToken)`、`IsDirty` 与 `AcceptChanges(savedRevision)`；插件拥有
  修订含义但不拥有路径或磁盘身份，Host 只原样回传修订；
- 宿主从不可变 Registry 拥有 `PluginId`、`DocumentTypeId`，并由内部状态存储按 Document 引用保存规范注册项与当前主路径；标题来自文件名，UTC 时间来自 `TimeProvider`；插件只解释内容版本和 payload；
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

## 5. Dock 与 Tool 契约

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
- `Plug` 仍是 G9 删除的临时兼容别名，不得扩散到新消费者；
- Tool 支持 Left、Right、Top、Bottom；
- Top/Bottom 使用工作区全宽稳定停靠点；
- 关闭 Tool 表示隐藏，之后恢复同一实例；
- Pinned 表示仍显示，不等同于关闭隐藏；
- 最后一个 Tool 隐藏后停靠点被移除时，恢复必须重建同一稳定节点；
- 禁止 Document、Tool 或整个 Dock 浮动为独立窗口；
- 主窗口内部拖放与停靠继续可用；
- 每个 HostRuntime 只有一个 `WorkspaceSession` 和一棵 Root；多个窗口只作为独立绑定消费者；
- `HostDockFactory` 不拥有 Root、Document 或 Tool 集合；未绑定和重复绑定都必须快速失败；
- Tool 管理只消费不含 Dock 类型的 `ToolWorkspaceState` 快照；布局前、Hidden、Pinned 与 Prevent 均有稳定投影。

## 6. 布局 V2 契约

- 文件名固定为 `layout-v2.json`，`schemaVersion` 固定为 `2`；
- 根、Pane、Tool 精确字段集合严格拒绝未知、重复、缺失、大小写错误和错误类型；
- Tool 顺序、Pane 比例、可见/Pinned/活动状态与四向 Dock 行为保持；
- 不存在两向迁移、浮动字段、历史 ID 归一化或 V1 fallback；
- 快照引用缺失插件、缺失 Pane、未知 Tool 或非法稳定 ID 时，隔离整个文件并回退默认布局；
- 隔离文件继续使用带 UTC 时间戳的 `.invalid.bak` 命名；
- 保存继续使用同目录原子替换；
- 不自动部分恢复；生命周期不可用与插件缺失同样隔离整份快照；
- `layout-v1.json` 原样保留且不会读取、迁移、覆盖或隔离。

完整格式参见 [Dock 布局快照 V2](../../../../docs/reference/dock-layout-snapshot-v2.md)。

## 7. 启动和关闭契约

- `Program.Main` 继续作为唯一生产入口；Avalonia Builder 是 Host internal 组合根能力；
- App 通过 `IHostDesktopShell` 构造注入，生产启动不得读取进程全局服务定位器；
- 根容器继续启用 `ValidateScopes` 与 `ValidateOnBuild`；
- 插件在 Avalonia 消息循环前初始化；
- 只反向关闭成功初始化的生命周期实例；
- 禁止新建后先释放 Adapter/View 和全部 Document Scope，再反向停止成功启动的生命周期；
- 生命周期停止后反向释放插件 Provider，最后释放 Host Provider；
- 默认宿主数据根为 `%LOCALAPPDATA%\MyAvaloniaManagement\v2\`，旧 `v1` 和预发布目录不读取、迁移或删除；
- `MYAVALONIA_DATA_DIRECTORY` 继续表示完整数据根且不追加 `v2`，避免测试污染用户 LocalAppData；
- `MYAVALONIA_SMOKE_TEST=1` 继续创建真实窗口并通过正常 Closing 路径退出。

## 8. 内部实现不构成契约

以下内容可在保持行为和测试的前提下继续调整：

- Registry、Builder、Navigator、Coordinator、Adapter 的类名和文件组织；
- 内部字典、集合和缓存实现；
- 内部构造函数与 `internal` 记录类型；
- 日志实现细节，但不得改变诊断 schema 2 白名单语义，或记录文档内容、凭据、异常正文和未验证路径数据；
- 测试替身和测试项目内部结构。

## 9. 变更检查表

提交宿主变更前确认：

- [x] Plugin SDK v1 文本基线、成员级变异门禁和 Host 零自有导出门禁已建立；
- [x] Managed-only 专项通过，Host 中不存在 Legacy 策略激活器和加载 Facade；
- [x] 四个真实插件的最终独立 ZIP 均包含有效 manifest v2、入口 `.deps.json`、PDB 和私有资产，版本与精确入口身份一致；
- [x] 四个插件及宿主使用显式 Context，生产代码不存在策略/View 隐式扫描与命名回退；
- [x] manifest 是唯一身份来源，SDK 不再包含模块或生命周期 `PluginId`；
- [x] 所有生产消费者使用同一个只读 `PluginRegistry`，Registry 在生命周期和 UI 前发布；
- [x] 诊断内存/UI/JSONL/默认镜像使用同一白名单，G15 专项源码门禁已接入 Release 入口；
- [ ] 清单缺失/损坏/不兼容在程序集加载前隔离，重复身份在任何 DLL 加载前阻断；
- [ ] 重复 ID 与碰撞诊断按预期阻断启动，局部类型失败和并发扫描行为未变化；
- [ ] 当前 Document JSON、安全加载与 Save As 行为符合新契约，不存在历史格式兼容分支；
- [ ] 保存失败不会提交内存状态，备份失败只产生警告，且无 `.tmp` 遗留；
- [ ] 脏标签与窗口退出的保存、放弃、取消路径均通过；
- [ ] 损坏主文件只从有效 `.recovery.bak` 创建强制另存副本，原件保持不变；
- [ ] 四向 Dock、Pinned/Hidden、恢复和禁用浮动通过；
- [x] Factory / Session 所有权分离、无 Dock Tool 投影、`Files` 删除和多窗口共享通过 G6 专项门禁；
- [ ] 布局 V1 迁移、隔离和默认回退通过；
- [ ] Document Scope 与控件缓存关闭后释放；
- [ ] Release 覆盖率门禁通过；
- [ ] 涉及启动、XAML 或窗口生命周期时执行 Windows Smoke；
- [ ] 根级和主项目文档与实现一致。
