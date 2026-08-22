# MyAvaloniaManagement Plugin SDK

本包是 Managed Plugin 的平台无关 Core 契约程序集。它只依赖 .NET BCL，并提供稳定身份、
Document 模型、原生 JSON 内容、关闭观察、插件生命周期和同步事件端口。

Core 不引用 Avalonia、Dock、Newtonsoft、Microsoft DI 或 Host 实现。需要声明模块、私有服务以及
Document/Tool 与 Avalonia View 映射的插件，应引用同版本 `MyAvaloniaManagement.PluginSdk.UI`。

当前仓库已完成 V3 G1 的未发布版本切换，但尚未实施 G2 及后续 public 契约重构。Host 仍使用
V2 G14 已签署的 `DocumentActivationContext` 异步初始化普通 `IPluginDocument`；
可保存模型实现 `IPersistablePluginDocument`，以 `DocumentContent(JsonElement)` 捕获/恢复内容，并在
`IsDirty` 实际变化时发出 `IsDirtyChanged`，让 Host 投影 Dock 修改标记；模型只观察 Host 拥有的
`IDocumentLifetime`。SDK 版本为未发布的 3.0.0；Core 的 85 条现有 public 签名位于 v3 Unshipped，
v3 Shipped 为空，v2 Shipped 历史文本保持不变。四个业务插件仅完成 G1 版本切换；仓库不会自动
推送公共包源，也不能把本阶段本地 nupkg 当作发布承诺。
