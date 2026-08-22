# MyAvaloniaManagement Plugin SDK

本包是 Managed Plugin V2 的平台无关 Core 契约程序集。它只依赖 .NET BCL，并提供稳定身份、
Document 模型、原生 JSON 内容、关闭观察、插件生命周期和同步事件端口。

Core 不引用 Avalonia、Dock、Newtonsoft、Microsoft DI 或 Host 实现。需要声明模块、私有服务以及
Document/Tool 与 Avalonia View 映射的插件，应引用同版本 `MyAvaloniaManagement.PluginSdk.UI`。

当前仓库已完成 V2 G7。Host 使用 `DocumentActivationContext` 异步初始化普通 `IPluginDocument`；
可保存模型实现 `IPersistablePluginDocument`，以 `DocumentContent(JsonElement)` 捕获/恢复内容，并在
`IsDirty` 实际变化时发出 `IsDirtyChanged`，让 Host 投影 Dock 修改标记；模型只观察 Host 拥有的
`IDocumentLifetime`。SDK public API 与版本仍为 2.0.0。四个业务插件留待 G9–G12 迁移，
该阶段不得发布到公共源。
