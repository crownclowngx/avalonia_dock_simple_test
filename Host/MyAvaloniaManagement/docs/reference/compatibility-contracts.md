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

> 当前分支已完成 V2 G2：最终 Core/UI SDK 已建立；下述 manifest、Document、layout 与当前 Host/四插件
> 运行形状仍是 G3–G12 前的未发布 Legacy 阶段桥。历史 v1 签署事实保持可追溯。

## 2. public API

最终 V2 public 插件契约只来自 `MyAvaloniaManagement.PluginSdk` 与
`MyAvaloniaManagement.PluginSdk.UI`。Host 窗口、View、ViewModel、加载器、注册表、工厂、消息和
内建策略均为 internal；插件不得编译引用 Host 可执行程序集。当前 Host 与四插件暂时引用
`MyAvaloniaManagement.LegacyPluginContracts`，该项目不可打包且不得增加新的生产消费者。

历史 v1 正式签名随 Core 的 `ApiCompatibility/v1` 保存；Core/UI 分别拥有 v2 基线，Shipped 均为空，
G2 表面全部登记为 Unshipped，并由 `scripts/Test-PluginSdkCompatibility.ps1 -Baseline v2` 验证。未登记
新增、删除、可见性收窄、参数或返回类型变化都会给出成员级 RS 诊断。完整维护流程见
[Plugin SDK API 兼容基线维护指南](../../../../docs/reference/plugin-sdk-api-compatibility.md)。

[`HostApiBoundaryTests`](../../../MyAvaloniaManagement.Tests/HostApiBoundaryTests.cs) 继续确保 Host 不导出
`MyAvaloniaManagement.*` 类型。测试和真实窗口 Harness 只能通过明确的 `InternalsVisibleTo` 使用
Host 实现，这不构成发布兼容承诺。

### 2.1 版本所有权

- 产品、Host 程序集身份和 Plugin SDK 版本集中定义在根级 `Directory.Version.props`；
- 当前产品与 SDK 版本为 `2.0.0`，Host 与 SDK `AssemblyVersion` 为 `2.0.0.0`；V2 不再维护独立 Host API 版本线；
- 兼容的 SDK 新增提升次版本但保持同一主版本程序集身份；破坏性契约变化提升主版本；
- 每个插件只拥有自己的 `PluginVersion`，清单版本必须与入口程序集精确一致；
- manifest、布局、外观、诊断和未来 Document 信封分别拥有整数 schema，不共享全局数字；
- 插件内容 schema 由内容所有者解释，不能使用插件发布版本替代；
- 普通进程内消息不添加无迁移或分派行为的版本占位字段。

### 2.2 SDK 包边界

- 基础包、程序集和根命名空间均为 `MyAvaloniaManagement.PluginSdk`，只依赖 .NET BCL；
- Core 不包含 Host，也不依赖 Avalonia、DI、Dock、Newtonsoft 或任何主题包；
- `MyAvaloniaManagement.PluginSdk.UI` 是同版本真实契约程序集，只允许 Core、Avalonia、
  DI.Abstractions 与宿主明确支持的 Fluent/Semi/Ursa Profile 精确依赖；
- UI 不包含 Dock 或 Newtonsoft；插件模型不得继承或创建 Dock 类型；
- Core/UI 的兼容新增可以随 SDK 次版本发布；任何破坏已编译插件的变化必须提升 SDK 主版本；
- Legacy 项目 `IsPackable=false`，两个 nupkg 均不得包含或依赖 `MyAvaloniaManagementCommon.dll`；
- 当前不自动发布公共 NuGet。宿主发布制品应同时提供两个 nupkg；对外分发前必须补充项目许可证。

### 2.3 事件总线

- 公共契约只有 `IHostEventBus.Publish<TEvent>` 和返回 `IDisposable` 的
  `Subscribe<TEvent>`，事件与处理器均不得为 `null`；
- 发布在调用线程同步执行，只匹配精确泛型事件类型，并按订阅顺序派发；
- 处理器异常原样传播并停止后续派发，不包装、吞掉、重试或切换线程；
- 订阅令牌只移除自身且可重复释放；进入发布快照的处理器可能最后执行一次；
- Document 必须持有令牌，并在自身 Scope 释放时退订；关闭竞态仍用
  `IDocumentLifetime.IsClosing` 抑制迟到副作用；
- 每个 HostRuntime 根容器独占总线实例；不允许静态默认实例、全局 Reset 或底层 messenger 暴露；
- 总线释放后发布或订阅抛 `ObjectDisposedException`；普通内存事件不增加版本字段。

### 2.4 插件样式与 UI Profile

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

### 2.5 诊断兼容与安全边界

- `HostDiagnosticRecord` 的 `schemaVersion` 继续为 1，现有 JSON 属性名称和类型保持不变；
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

V1 清单格式：

```json
{
  "schemaVersion": 1,
  "pluginId": "myavalonia.plugin.sample",
  "pluginVersion": "2.0.0",
  "entryAssembly": "SamplePlugin.dll",
  "compatibility": {
    "hostApi": { "minInclusive": "2.0.0", "maxExclusive": "3.0.0" },
    "commonContract": { "minInclusive": "2.0.0", "maxExclusive": "3.0.0" }
  }
}
```

- 字段名称区分大小写；未知、重复或缺失字段均拒绝，不允许注释或尾随逗号；
- 版本只接受 `major.minor.patch[.revision]`，内部统一为四段比较；
- Host API 与 Common 均采用 `minInclusive <= current < maxExclusive`；
- `entryAssembly` 只能是插件根目录中的单个 DLL 文件名；
- 入口必须携带同名 `.deps.json`，宿主不扫描目录猜测托管或原生依赖；
- `pluginVersion` 必须与入口 `AssemblyVersion` 精确一致；manifest `pluginId` 是插件身份唯一事实源；
- Host 与 Common 当前 `AssemblyVersion` 均为 `2.0.0.0`；G3 前双区间只允许投影同一个 SDK 事实；
- 清单只解决兼容和确定性加载，不提供签名、防篡改、权限沙箱或热卸载。

仓库内 Managed Plugin 的清单不在源码树手写，而由 `ManagedPluginId`、`PluginVersion`、入口程序集名
和四个显式兼容端点生成。公共构建协议还强制包含入口 DLL、deps、PDB，排除 Host/SDK/UI 共享闭包
和非 win-x64 原生资产。正式分发物按插件独立生成
`<AssemblyName>-<PluginVersion>-win-x64.zip`；ZIP 内只有 `Controls/<PluginFolder>/`，外置同名
`.manifest.json` 记录 ZIP 与全部文件摘要。目录部署是开发产物，ZIP 是正式分发物，两者使用同一资产集合。

### 3.2 Managed 插件

- 程序集恰好包含一个具体 `IPluginModule`；
- 模块使用 public 无参构造发现；
- `Configure(IPluginRegistrationContext)` 在根容器构建前且每个进程只执行一次；
- `context.PluginId` 由宿主从已验证 manifest 注入，只读且不能覆盖；
- `context.Services` 只允许追加插件私有业务服务；插件不得删除、替换、重排既有描述符或追加宿主保护 ServiceType；私有多实现、keyed 和开放泛型注册继续允许；
- Document/Tool/Lifecycle 使用根容器激活，允许构造注入；View 使用无参工厂按需创建；
- Lifecycle 的身份取自 Registry，可选依赖引用其他插件 manifest ID，并按计划初始化、反向关闭；
- 注册只发生在组合阶段，不支持运行期追加、删除、启停或热卸载；未登记类型不会被发现。

### 3.3 拒绝与共同规则

- 缺少 deps、缺少模块、重复模块或模块缺少 public 无参构造时隔离当前目录；
- 无模块策略程序集不再获得 public 无参构造激活，也不会生成 `myavalonia.legacy.*` 所有者；
- 完整类型预检失败会隔离整个插件目录，不能把同一发布物拆成“部分成功”；
- 模块构造、模块配置、服务注册、贡献激活和扩展所有权错误属于全局组合错误，在根容器投入使用前阻断启动；
- 通过 `context.Services` 直接登记三类贡献接口会以 `CONTRIBUTION_REGISTRATION_BYPASS` 拒绝；删除、替换、重排或覆盖宿主服务会以 `PLUGIN_HOST_SERVICE_MUTATION` 在容器构建前拒绝；
- 重复 Document/Tool 主 ID 与别名、重复贡献类型、重复 ViewModel 映射、所有权错误、空元数据和重复 Creation Intent 形成结构化诊断，并以 `HostCompositionException` 阻断启动；不再有“首次注册胜出”语义；
- 策略元数据在注册时读取一次；
- Builder 失败时整个容器和组合结果丢弃，不发布部分 Registry，不运行生命周期或 UI；
- 插件根目录快照在进程内不刷新，更新插件需要重启应用。

基础 SDK 及其 public 签名依赖、受支持 UI Profile 及其依赖均由
`AssemblyLoadContext.Default` 提供。插件目录不得携带这些 DLL 的私有副本；身份或版本不兼容时，
宿主在完整类型预检阶段以 `PLUGIN_SHARED_ASSEMBLY_MISMATCH` 隔离插件。普通业务依赖只由当前
插件的 deps/RID 图在独立 ALC 中解析，不能因为宿主碰巧加载过同名程序集就进入共享集合。

## 4. Document 契约

- 创建继续通过 `IDocumentCreationStrategy` 和 `DocumentCreationParams`；
- 可选多入口继续通过 `IDocumentCreationIntentProvider`；
- 唯一磁盘格式是宿主严格读写的 Document 信封 v1，必须且只能包含七个 camelCase 字段：`schemaVersion`、`pluginId`、`documentTypeId`、`contentSchemaVersion`、`title`、`savedAtUtc`、`payload`；
- `schemaVersion` 只能为 `1`；UTF-8 文件上限为 8 MiB，JSON 最大深度为 8；注释、尾随逗号、重复、未知、缺失、大小写错误和错误类型字段均拒绝；
- 插件公共 `DocumentContentSnapshot` 是不可变内容 DTO，只包含正整数 `ContentSchemaVersion` 和非 null `Payload`；
- `ISavableDocument` 只包含 `CreateContentSnapshot()` 和 `RestoreContent(snapshot)`；插件不拥有路径或 Document 类型成员；
- `ISavableDocument` 必须同时实现 `IDocumentSaveState`，缺失时以 `DOCUMENT_SAVE_STATE_MISSING` 拒绝发布；
- 宿主从不可变 Registry 拥有 `PluginId`、`DocumentTypeId`，并由内部状态存储按 Document 引用保存规范注册项与当前主路径；标题来自文件名，UTC 时间来自 `TimeProvider`；插件只解释内容版本和 payload；
- 信封中的 Document 类型必须是规范主 ID，不接受历史别名；`pluginId` 必须等于注册项所有者；
- 路径转绝对路径后按 Windows 不区分大小写规则查重；
- 批量打开以单文件为错误边界；
- 同一路径已打开时激活原文档，不创建重复实例；
- 无当前路径时由宿主选择保存目标，已有路径直接覆盖；恢复出的 Document 由宿主内部恢复注册表强制另存，并拒绝覆盖损坏原件或备份；
- 快照创建不得更新标题、路径或脏状态；主文件写入失败不得调用 `AcceptChanges`；插件没有路径策略或保存完成回调；
- 主文件和 `<主路径>.recovery.bak` 均通过同目录临时文件原子替换；备份失败不得回滚已成功的主文件；
- 标签关闭和窗口退出必须保护脏 Document；取消确认或保存失败不得提前取消 `ClosingToken`；
- v1 是第一个且唯一受支持的 Document 信封；不存在旧信封兼容对象，任何非 v1 结构直接作为无效输入拒绝；
- 打开失败不发布 Document、不泄漏临时 Scope，也不创建、迁移或覆盖任何文件；
- `DocumentLoadException`、JSON、I/O、权限和路径故障属于预期持久化失败；编程错误不应被宽泛捕获。

拥有独立 DI Scope 的 Document 在 Dock 确认关闭后释放；未采用 `IDocumentScopeFactory` 的历史 Document 维持原有所有权行为。

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

- 历史调用仍可通过 `Files` Locator 找到 DocumentDock；持久化 ID 固定为 `Documents`；
- Tool 支持 Left、Right、Top、Bottom；
- Top/Bottom 使用工作区全宽稳定停靠点；
- 关闭 Tool 表示隐藏，之后恢复同一实例；
- Pinned 表示仍显示，不等同于关闭隐藏；
- 最后一个 Tool 隐藏后停靠点被移除时，恢复必须重建同一稳定节点；
- 禁止 Document、Tool 或整个 Dock 浮动为独立窗口；
- 主窗口内部拖放与停靠继续可用；
- `GetToolManagementData()` 在根布局建立前继续返回 `null`；内部只读快照不属于 public 契约。

## 6. 布局 V1 契约

- 文件名保持 `layout-v1.json`；
- `schemaVersion` 保持 `1`；
- JSON 字段、稳定 ID、Tool 顺序、Pane 比例、可见/Pinned/活动状态保持兼容；
- 保留现有两向到四向迁移；
- 历史浮动 Tool 读取后归一化回主窗口；
- 快照引用缺失插件、缺失 Pane、未知 Tool 或非法稳定 ID 时，隔离整个文件并回退默认布局；
- 隔离文件继续使用带 UTC 时间戳的 `.invalid.bak` 命名；
- 保存继续使用同目录原子替换；
- 不自动部分恢复、不引入 V2。

完整格式参见 [Dock 布局快照 V1](../../../../docs/reference/dock-layout-snapshot-v1.md)。

## 7. 启动和关闭契约

- `Program.Main` 继续作为唯一生产入口；Avalonia Builder 是 Host internal 组合根能力；
- App 通过 `IHostDesktopShell` 构造注入，生产启动不得读取进程全局服务定位器；
- 根容器继续启用 `ValidateScopes` 与 `ValidateOnBuild`；
- 插件在 Avalonia 消息循环前初始化；
- 只反向关闭成功初始化的生命周期实例；
- 插件关闭后释放根容器和剩余 Document Scope；
- 默认宿主数据根为 `%LOCALAPPDATA%\MyAvaloniaManagement\v2\`，旧 `v1` 和预发布目录不读取、迁移或删除；
- `MYAVALONIA_DATA_DIRECTORY` 继续表示完整数据根且不追加 `v2`，避免测试污染用户 LocalAppData；
- `MYAVALONIA_SMOKE_TEST=1` 继续创建真实窗口并通过正常 Closing 路径退出。

## 8. 内部实现不构成契约

以下内容可在保持行为和测试的前提下继续调整：

- Registry、Builder、Navigator、Coordinator、Adapter 的类名和文件组织；
- 内部字典、集合和缓存实现；
- 内部构造函数与 `internal` 记录类型；
- 日志实现细节，但不得改变 schema 1 白名单语义，或记录文档内容、凭据、异常正文和未验证路径数据；
- 测试替身和测试项目内部结构。

## 9. 变更检查表

提交宿主变更前确认：

- [x] Plugin SDK v1 文本基线、成员级变异门禁和 Host 零自有导出门禁已建立；
- [x] Managed-only 专项通过，Host 中不存在 Legacy 策略激活器和加载 Facade；
- [x] 四个真实插件的最终独立 ZIP 均包含有效清单、入口 `.deps.json`、PDB 和私有资产，版本与唯一模块身份一致；
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
- [ ] 布局 V1 迁移、隔离和默认回退通过；
- [ ] Document Scope 与控件缓存关闭后释放；
- [ ] Release 覆盖率门禁通过；
- [ ] 涉及启动、XAML 或窗口生命周期时执行 Windows Smoke；
- [ ] 根级和主项目文档与实现一致。
