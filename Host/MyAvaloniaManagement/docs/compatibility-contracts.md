# 主项目兼容约束

## 1. 文档目的

本文列出内部重构不得无意改变的外部可观察行为。它不是完整 API 参考，而是代码评审和回归测试清单。

## 2. public API

必须保持：

- Host 和 `MyAvaloniaManagementCommon` 现有 public 类型、命名空间、构造函数、方法、属性、字段和事件；
- `ManagementFactory` 的 public 方法与 Dock override；
- `MainWindowViewModel` 的 public 无参构造、属性、命令和文件拖放入口；
- `AssemblyLoaderHelper`、`PluginModuleCatalog`、`ServiceCollectionExtensions` 等现有 public 辅助入口；
- 静态 `ServiceProvider` 兼容路径。

[`PublicApiContractTests`](../../MyAvaloniaManagement.Tests/PublicApiContractTests.cs) 对 Host/Common 导出元数据生成 SHA-256 指纹。内部类型可以调整；有意 public 变更必须单独评审，并同步更新插件、契约测试和本文。

## 3. 插件发现与激活

### 3.1 Managed 插件

- 程序集包含可实例化的 `IPluginModule`；
- 模块使用 public 无参构造发现；
- `ConfigureServices` 在根容器构建前执行；
- Document/Tool 策略使用 `ActivatorUtilities`，允许构造注入；
- 可选 `IPluginLifecycle` 按既有顺序初始化并反向关闭。

### 3.2 Legacy 插件

- 不属于 Managed 模块程序集；
- Document/Tool 策略必须具有 public 无参构造；
- 不自动获得 Managed DI 激活语义。

### 3.3 共同规则

- 单个 DLL、模块、依赖或类型失败不终止其他插件发现；
- `ReflectionTypeLoadException` 只排除不可加载类型；
- Document/Tool 重复 ID 继续首次注册胜出；
- 策略元数据在注册时读取一次；
- 插件根目录快照在进程内不刷新，更新插件需要重启应用。

## 4. Document 契约

- 创建继续通过 `IDocumentCreationStrategy` 和 `DocumentCreationParams`；
- 可选多入口继续通过 `IDocumentCreationIntentProvider`；
- 保存外壳继续使用 Newtonsoft 序列化的 `DocumentSaveData`；
- 插件仍负责解释 `Content` 和 `PluginMetadata`；
- 路径转绝对路径后按 Windows 不区分大小写规则查重；
- 批量打开以单文件为错误边界；
- 同一路径已打开时激活原文档，不创建重复实例；
- Save As 继续遵循 `IDocumentSavePathPolicy`；
- 写入失败不得更新标题、路径或调用保存完成通知；
- 文档文件通过同目录临时文件原子替换，不改变 JSON 内容格式；
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

完整格式参见 [Dock 布局快照 V1](../../../docs/upgrade/net10/dock-layout-snapshot-v1.md)。

## 7. 启动和关闭契约

- `Program.Main` 与 `BuildAvaloniaApp()` 签名保持不变；
- 根容器继续启用 `ValidateScopes` 与 `ValidateOnBuild`；
- 插件在 Avalonia 消息循环前初始化；
- 只反向关闭成功初始化的生命周期实例；
- 插件关闭后释放根容器和剩余 Document Scope；
- `MYAVALONIA_DATA_DIRECTORY` 继续只覆盖数据目录，避免测试污染用户 LocalAppData；
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
- [ ] 重复策略、局部类型失败和并发扫描行为未变化；
- [ ] Document JSON 与 Save As 行为未变化；
- [ ] 保存失败不会提交内存状态，且无 `.tmp` 遗留；
- [ ] 四向 Dock、Pinned/Hidden、恢复和禁用浮动通过；
- [ ] 布局 V1 迁移、隔离和默认回退通过；
- [ ] Document Scope 与控件缓存关闭后释放；
- [ ] Release 覆盖率门禁通过；
- [ ] 涉及启动、XAML 或窗口生命周期时执行 Windows Smoke；
- [ ] 根级和主项目文档与实现一致。
