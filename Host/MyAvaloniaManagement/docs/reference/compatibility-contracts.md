# 主项目兼容约束

## 1. 文档目的

本文列出内部重构不得无意改变的外部可观察行为。它不是完整 API 参考，而是代码评审和回归测试清单。

> Managed Plugin v1 的正式支持边界已在 G1 冻结：Windows x64、可信进程内 Managed Plugin、
> 严格清单、退出后替换插件。当前代码中的 Legacy 激活和 Host public 实现面是 G2/G4 完成前的
> 过渡事实，不构成 v1 兼容承诺；在对应任务完成前仍需测试保护，不能在无评审时意外破坏。

## 2. public API

在 G2 执行前，以下当前 public 面仍由指纹测试保护：

- Host 和 `MyAvaloniaManagementCommon` 现有 public 类型、命名空间、构造函数、方法、属性、字段和事件；
- `ManagementFactory` 的 public 方法与 Dock override；
- `MainWindowViewModel` 的 public 无参构造、属性、命令和文件拖放入口；
- `AssemblyLoaderHelper`、`PluginModuleCatalog`、`ServiceCollectionExtensions` 等现有 public 辅助入口；
- 静态 `ServiceProvider` 兼容路径。

[`PublicApiContractTests`](../../../MyAvaloniaManagement.Tests/PublicApiContractTests.cs) 对 Host/Common 导出元数据生成 SHA-256 指纹。内部类型可以调整；有意 public 变更必须单独评审，并同步更新插件、契约测试和本文。

上述 Host 窗口、ViewModel、加载器和静态服务定位器不是 v1 Plugin SDK。G2 已获准将其改为
internal 或删除；正式长期兼容面只由后续收口的 `MyAvaloniaManagementCommon`/Plugin SDK 承担。

### 2.1 版本所有权

- 产品版本、Host API 程序集身份和 Plugin SDK 版本集中定义在根级 `Directory.Version.props`；
- 当前产品与 SDK 版本为 `1.0.0`，Host API 与 SDK `AssemblyVersion` 为 `1.0.0.0`；
- 兼容的 SDK 新增提升次版本但保持同一主版本程序集身份；破坏性契约变化提升主版本；
- 每个插件只拥有自己的 `PluginVersion`，清单版本必须与入口程序集精确一致；
- manifest、布局、外观、诊断和未来 Document 信封分别拥有整数 schema，不共享全局数字；
- 插件内容 schema 由内容所有者解释，不能使用插件发布版本替代；
- 普通进程内消息不添加无迁移或分派行为的版本占位字段。

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
- `pluginVersion` 必须与入口 `AssemblyVersion` 精确一致；Managed 模块的 `PluginId` 必须与清单一致；
- Host 与 Common 当前 `AssemblyVersion` 均为 `1.0.0.0`。兼容新增提升次版本，破坏性变更提升主版本；
- 清单只解决兼容和确定性加载，不提供签名、防篡改、权限沙箱或热卸载。

### 3.2 Managed 插件

- 程序集包含可实例化的 `IPluginModule`；
- 模块使用 public 无参构造发现；
- `ConfigureServices` 在根容器构建前执行；
- Document/Tool 策略使用 `ActivatorUtilities`，允许构造注入；
- 可选 `IPluginLifecycle` 按既有顺序初始化并反向关闭。

### 3.3 Legacy 插件

以下仅描述 G4 删除前的当前过渡行为，不是 v1 支持面：Legacy 插件不属于 Managed 模块程序集，
仍要求有效清单，策略依赖 public 无参构造且不获得 Managed DI 激活语义。v1 不兼容仓库外 Legacy
二进制插件；G4 将删除该激活分支及其 public Facade。

### 3.4 共同规则

- 单个 DLL、模块、依赖或类型失败不终止其他插件发现；
- 完整类型预检失败会隔离整个插件目录，不能把同一发布物拆成“部分成功”；
- 重复 `PluginId`、Document/Tool 主 ID 与别名、所有权错误、空元数据和重复 Creation Intent 形成排序稳定的结构化诊断，并以 `HostCompositionException` 阻断启动；不再有“首次注册胜出”语义；
- 策略元数据在注册时读取一次；
- 插件根目录快照在进程内不刷新，更新插件需要重启应用。

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

- `Program.Main` 与 `BuildAvaloniaApp()` 签名保持不变；
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

- [ ] public API 指纹通过，或变更已被明确批准；
- [ ] Managed 与 Legacy 激活测试通过；
- [ ] 四个真实插件构建与发布目录均包含有效清单，版本和模块身份一致；
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
