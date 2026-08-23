# MyAvaloniaManagement Plugin SDK UI

本包是 Managed Plugin 的 UI 契约程序集，提供模块入口、插件私有 DI 注册、不可变
Document/Tool 描述符、Avalonia View 绑定、窗口交互端口和全屏展示端口。

本包依赖同版本 Core SDK，并把 Avalonia、Fluent、Semi 和 Ursa 限制为 Host 验证的版本。
Dock 与 Newtonsoft 不属于插件 UI 契约，插件不得通过本包取得或携带这些程序集。

当前仓库已完成 V3 G14 封板。Host 生产模块入口和声明式贡献目录使用本程序集，注册时一次绑定 Descriptor、
模型与 View，Document 为 scoped，Tool/Lifecycle 为插件 singleton。模块返回后注册入口及私有服务集合
均被封闭；Registry 只保存不可变事实，模型创建留在 Host internal Activator。G4 进一步规定模块进入时
`Services` 为空，插件只登记私有服务和贡献声明；Seal 校验 ID 归属与保留类型后，Host 才最终追加
`IPluginWindowInteraction`、`IDocumentLifetime`、Scope 基础设施和固定生命周期贡献根。插件内部消息器由
插件自己登记并归其 Provider 所有，Host 不再提交或转发通用事件总线。
普通及 keyed 影子注册均在 Provider 构建前隔离当前插件；私有开放泛型、keyed 与多实现仍保持原生语义。
Document 通过 Core SDK
的 `DocumentActivation`、`NewDocumentActivation`、`RestoreDocumentActivation`、
`DocumentContent`、`IPluginDocument`、
`IPersistablePluginDocument` 与 `IDocumentLifetime` 进入唯一异步创建和持久化链。

V2 已完成生命周期编排及四插件迁移；V3 G2–G7 又完成修订保存、互斥激活、注册所有权、插件私有消息、
Workspace/Dock 和 Host/插件目录边界。`IPluginWindowInteraction` 由 Host 以同一受控实例注入每个插件私有 Provider：
它只返回本地路径或操作结果，不向插件暴露主窗口、`StorageProvider` 或剪贴板实现。
原生选择器返回后会再次检查取消令牌，以丢弃 Document 关闭期间的迟到结果。

V3 G8 由同一 UI SDK 的 `IWindowContentFullscreenHost` 承载 MySmallTools 全屏交互，唯一 public 方法为
`IDisposable? TryPresent(Control content)`。成功租约排他且幂等，Host 自动失效后再次释放为无操作；
插件不取得 Window、Dock、owner 或 `TryRestore`。`3.0.0` UI SDK 的 45 条 public 签名已进入
v3 Shipped，v3 Unshipped 为空。V3 G9–G12 只用既有声明和 Host internal Workspace/Dock Adapter
验证四插件，G13 又以真实 nupkg 负例证明旧 owner API 不可消费；G14 未增加公共类型、接口或成员，只完成
API 分类和两轮隔离签署。v2 Shipped 继续保存 V2 G14 历史承诺；G14 未运行历史 ReleaseAcceptance，
也没有上传或打标签。
