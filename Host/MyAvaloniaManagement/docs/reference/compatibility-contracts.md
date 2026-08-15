# 主项目兼容约束

## 1. 文档目的

本文列出内部重构不得无意改变的外部可观察行为。它不是完整 API 参考，而是代码评审和回归测试清单。

> Managed Plugin v1 的正式支持边界已在 G1 冻结：Windows x64、可信进程内 Managed Plugin、
> 严格清单、退出后替换插件。G2 已将 Host 自有实现全部收口为 internal；G3 已形成正式基础
> SDK、可选 UI Profile 和宿主语义样式契约；G4 已删除 Legacy 二进制激活、无 deps 回退和
> 历史加载 Facade。

## 2. public API

正式 public 插件契约只来自 `MyAvaloniaManagementCommon`。Host 窗口、View、ViewModel、加载器、
注册表、工厂、消息和内建策略均为 internal；静态 `ServiceProvider` 与生产 ViewModel 无参构造
已经删除。插件不得编译引用 Host 可执行程序集。

[`PublicApiContractTests`](../../../MyAvaloniaManagement.Tests/PublicApiContractTests.cs) 在 G13 前继续
对 Common 导出元数据生成临时 SHA-256；[`HostApiBoundaryTests`](../../../MyAvaloniaManagement.Tests/HostApiBoundaryTests.cs)
使用可读断言确保 Host 不导出 `MyAvaloniaManagement.*` 类型。测试和真实窗口 Harness 只能通过
明确的 `InternalsVisibleTo` 使用 Host 实现，这不构成发布兼容承诺。

### 2.1 版本所有权

- 产品版本、Host API 程序集身份和 Plugin SDK 版本集中定义在根级 `Directory.Version.props`；
- 当前产品与 SDK 版本为 `1.0.0`，Host API 与 SDK `AssemblyVersion` 为 `1.0.0.0`；
- 兼容的 SDK 新增提升次版本但保持同一主版本程序集身份；破坏性契约变化提升主版本；
- 每个插件只拥有自己的 `PluginVersion`，清单版本必须与入口程序集精确一致；
- manifest、布局、外观、诊断和未来 Document 信封分别拥有整数 schema，不共享全局数字；
- 插件内容 schema 由内容所有者解释，不能使用插件发布版本替代；
- 普通进程内消息不添加无迁移或分派行为的版本占位字段。

### 2.2 SDK 包边界

- 正式基础包 ID 为 `MyAvaloniaManagement.PluginSdk`，包内程序集仍为 `MyAvaloniaManagementCommon`；
- 基础包不包含 Host，也不直接依赖 Desktop、字体、Fluent/Semi/Ursa/Dock 主题或 Dock 视觉控件；
- `Dock.Model.Mvvm` 是当前 public 签名的必要依赖，其上游传递的
  `Dock.Controls.Recycling.Model` 是已知例外，不等于基础 SDK 承诺 Dock 视觉控件；
- `MyAvaloniaManagement.PluginSdk.UI` 是同版本 dependency-only package，供直接使用 Semi、Ursa、
  Dock UI 的插件选择；第三方 UI 依赖使用精确 NuGet 版本；
- UI Profile 的兼容新增可以随 SDK 次版本发布；任何会破坏已编译 UI 插件的变化必须提升 SDK 主版本；
- 当前不自动发布公共 NuGet。宿主发布制品应同时提供两个 nupkg；对外分发前必须补充项目许可证。

### 2.3 插件样式与 UI Profile

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
  "pluginVersion": "1.0.0",
  "entryAssembly": "SamplePlugin.dll",
  "compatibility": {
    "hostApi": { "minInclusive": "1.0.0", "maxExclusive": "2.0.0" },
    "commonContract": { "minInclusive": "1.0.0", "maxExclusive": "2.0.0" }
  }
}
```

- 字段名称区分大小写；未知、重复或缺失字段均拒绝，不允许注释或尾随逗号；
- 版本只接受 `major.minor.patch[.revision]`，内部统一为四段比较；
- Host API 与 Common 均采用 `minInclusive <= current < maxExclusive`；
- `entryAssembly` 只能是插件根目录中的单个 DLL 文件名；
- 入口必须携带同名 `.deps.json`，宿主不扫描目录猜测托管或原生依赖；
- `pluginVersion` 必须与入口 `AssemblyVersion` 精确一致；Managed 模块的 `PluginId` 必须与清单一致；
- Host 与 Common 当前 `AssemblyVersion` 均为 `1.0.0.0`。兼容新增提升次版本，破坏性变更提升主版本；
- 清单只解决兼容和确定性加载，不提供签名、防篡改、权限沙箱或热卸载。

### 3.2 Managed 插件

- 程序集恰好包含一个具体 `IPluginModule`；
- 模块使用 public 无参构造发现；
- `ConfigureServices` 在根容器构建前执行；
- Document/Tool 策略使用 `ActivatorUtilities`，允许构造注入；
- 可选 `IPluginLifecycle` 按既有顺序初始化并反向关闭。

### 3.3 拒绝与共同规则

- 缺少 deps、缺少模块、重复模块或模块缺少 public 无参构造时隔离当前目录；
- 无模块策略程序集不再获得 public 无参构造激活，也不会生成 `myavalonia.legacy.*` 所有者；
- 完整类型预检失败会隔离整个插件目录，不能把同一发布物拆成“部分成功”；
- 模块构造、模块身份、服务注册和扩展所有权错误属于全局组合错误，在根容器投入使用前阻断启动；
- 重复 `PluginId`、Document/Tool 主 ID 与别名、所有权错误、空元数据和重复 Creation Intent 形成排序稳定的结构化诊断，并以 `HostCompositionException` 阻断启动；不再有“首次注册胜出”语义；
- 策略元数据在注册时读取一次；
- 插件根目录快照在进程内不刷新，更新插件需要重启应用。

基础 SDK 及其 public 签名依赖、受支持 UI Profile 及其依赖均由
`AssemblyLoadContext.Default` 提供。插件目录不得携带这些 DLL 的私有副本；身份或版本不兼容时，
宿主在完整类型预检阶段以 `PLUGIN_SHARED_ASSEMBLY_MISMATCH` 隔离插件。普通业务依赖只由当前
插件的 deps/RID 图在独立 ALC 中解析，不能因为宿主碰巧加载过同名程序集就进入共享集合。

## 4. Document 契约

- 创建继续通过 `IDocumentCreationStrategy` 和 `DocumentCreationParams`；
- 可选多入口继续通过 `IDocumentCreationIntentProvider`；
- 保存外壳继续使用 Newtonsoft 序列化的 `DocumentSaveData`；
- `ISavableDocument` 必须同时实现 `IDocumentSaveState`，缺失时以 `DOCUMENT_SAVE_STATE_MISSING` 拒绝发布；
- 插件仍负责解释 `Content` 和 `PluginMetadata`；
- 路径转绝对路径后按 Windows 不区分大小写规则查重；
- 批量打开以单文件为错误边界；
- 同一路径已打开时激活原文档，不创建重复实例；
- Save As 继续遵循 `IDocumentSavePathPolicy`；
- 快照创建不得更新标题、路径或脏状态；主文件写入失败不得调用 `AcceptChanges` 或保存完成通知；
- 主文件和 `<主路径>.recovery.bak` 均通过同目录临时文件原子替换；备份失败不得回滚已成功的主文件；
- 标签关闭和窗口退出必须保护脏 Document；取消确认或保存失败不得提前取消 `ClosingToken`；
- 当前 Document 文件不兼容历史内容格式，插件不得猜测迁移旧字段；
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
- 默认宿主数据根为 `%LOCALAPPDATA%\MyAvaloniaManagement\v1\`，旧预发布目录不读取、迁移或删除；
- `MYAVALONIA_DATA_DIRECTORY` 继续表示完整数据根且不追加 `v1`，避免测试污染用户 LocalAppData；
- `MYAVALONIA_SMOKE_TEST=1` 继续创建真实窗口并通过正常 Closing 路径退出。

## 8. 内部实现不构成契约

以下内容可在保持行为和测试的前提下继续调整：

- Registry、Builder、Navigator、Coordinator、Adapter 的类名和文件组织；
- 内部字典、集合和缓存实现；
- 内部构造函数与 `internal` 记录类型；
- 日志实现细节，但不得记录文档内容、密码或未验证路径数据；
- 测试替身和测试项目内部结构。

## 9. 变更检查表

提交宿主变更前确认：

- [ ] Common 临时 API 指纹和 Host 零自有导出门禁通过，或变更已被明确批准；
- [x] Managed-only 专项通过，Host 中不存在 Legacy 策略激活器和加载 Facade；
- [ ] 四个真实插件构建与发布目录均包含有效清单、入口 `.deps.json`，版本和唯一模块身份一致；
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
