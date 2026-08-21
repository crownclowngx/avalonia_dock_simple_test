# MyAvaloniaManagement Plugin SDK UI

本包是 Managed Plugin V2 的真实 UI 契约程序集，提供模块入口、插件私有 DI 注册、不可变
Document/Tool 描述符、Avalonia View 绑定和全屏展示端口。

本包依赖同版本 Core SDK，并把 Avalonia、Fluent、Semi 和 Ursa 限制为 Host 验证的版本。
Dock 与 Newtonsoft 不属于插件 UI 契约，插件不得通过本包取得或携带这些程序集。

当前仓库已完成 V2 G7：Host 生产模块入口和声明式贡献目录使用本程序集，注册时一次绑定 Descriptor、
模型与 View，Document 为 scoped，Tool/Lifecycle 为插件 singleton。模块返回后注册入口及私有服务集合
均被封闭；Registry 只保存不可变事实，模型创建留在 Host internal Activator。Document 通过 Core SDK
的 `DocumentActivationContext`、`DocumentContent`、`IPluginDocument`、
`IPersistablePluginDocument` 与 `IDocumentLifetime` 进入唯一异步创建和持久化链。

G8 生命周期编排和 G9–G12 四业务插件迁移尚未完成。四业务插件的 Legacy 入口不会由 V2 Host 加载；
本阶段不构成完整 V2 运行时或公开发布承诺。
