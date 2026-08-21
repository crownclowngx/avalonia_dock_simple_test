# MyAvaloniaManagement Plugin SDK UI

本包是 Managed Plugin V2 的真实 UI 契约程序集，提供模块入口、插件私有 DI 注册、不可变
Document/Tool 描述符、Avalonia View 绑定、窗口交互端口和全屏展示端口。

本包依赖同版本 Core SDK，并把 Avalonia、Fluent、Semi 和 Ursa 限制为 Host 验证的版本。
Dock 与 Newtonsoft 不属于插件 UI 契约，插件不得通过本包取得或携带这些程序集。

当前仓库已完成 V2 G7：Host 生产模块入口和声明式贡献目录使用本程序集，注册时一次绑定 Descriptor、
模型与 View，Document 为 scoped，Tool/Lifecycle 为插件 singleton。模块返回后注册入口及私有服务集合
均被封闭；Registry 只保存不可变事实，模型创建留在 Host internal Activator。Document 通过 Core SDK
的 `DocumentActivationContext`、`DocumentContent`、`IPluginDocument`、
`IPersistablePluginDocument` 与 `IDocumentLifetime` 进入唯一异步创建和持久化链。

G8 已完成生命周期编排，G9–G10 已迁移 MyPlugTest 与 DaTangAccountingHelpPlug。
G10 新增的 `IPluginWindowInteraction` 由 Host 以同一受控实例注入每个插件私有 Provider：
它只返回本地路径或操作结果，不向插件暴露主窗口、`StorageProvider` 或剪贴板实现。
原生选择器返回后会再次检查取消令牌，以丢弃 Document 关闭期间的迟到结果。

MySmallTools 与 BiliDownloader 仍等待 G11–G12；它们的 Legacy 入口不会由 V2 Host 加载。
当前 `2.0.0` SDK 仍是未发布契约，G10 测试 ZIP 不构成公开发布承诺。
