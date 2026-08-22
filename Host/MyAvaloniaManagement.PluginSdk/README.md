# MyAvaloniaManagement Plugin SDK

本包是 Managed Plugin 的平台无关 Core 契约程序集。它只依赖 .NET BCL，并提供稳定身份、
Document 模型、原生 JSON 内容、关闭观察、插件生命周期和同步事件端口。

Core 不引用 Avalonia、Dock、Newtonsoft、Microsoft DI 或 Host 实现。需要声明模块、私有服务以及
Document/Tool 与 Avalonia View 映射的插件，应引用同版本 `MyAvaloniaManagement.PluginSdk.UI`。

当前仓库已完成 V3 G4。Host 使用互斥的 `NewDocumentActivation` 与
`RestoreDocumentActivation` 异步初始化普通 `IPluginDocument`；Creation Intent 只存在于新建分支，
`DocumentContent` 只存在于恢复分支。可保存模型实现 `IPersistablePluginDocument`，通过
`CaptureSaveSnapshotAsync` 返回不可变的 `DocumentSaveSnapshot(DocumentRevision, DocumentContent)`，
并只在 `AcceptChanges(savedRevision)` 收到仍为当前版本的修订时接受保存基线。`IsDirty` 由当前修订与
已接受修订是否相等推导，Host 只投影结果，不解释插件修订值。模型仍只观察 Host 拥有的
`IDocumentLifetime`。G4 没有改变 Core public API；该端口与仍待 G5 删除的 `IHostEventBus` 都只在
插件模块返回并通过所有权校验后由 Host 最终追加，插件不能用普通或 keyed DI 注册影子覆盖。

SDK 版本为未发布的 3.0.0；Core 的 130 条 public 签名位于 v3 Unshipped，UI 为 46 条，两个 v3
Shipped 为空，v2 Shipped 历史文本保持不变。仓库不会自动推送公共包源，也不能把本阶段本地 nupkg
当作发布承诺。
