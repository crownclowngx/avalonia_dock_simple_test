# MyAvaloniaManagement Plugin SDK UI

本包是 Managed Plugin 的 UI 契约程序集，提供模块入口、插件私有 DI 注册、不可变
Document/Tool 描述符、Avalonia View 绑定、窗口交互端口和全屏展示端口。

本包依赖同版本 Core SDK，并把 Avalonia、Fluent、Semi 和 Ursa 限制为 Host 验证的版本。
Dock 与 Newtonsoft 不属于插件 UI 契约，插件不得通过本包取得或携带这些程序集。

当前仓库已完成 V3 G5；UI public 形状仍沿用 V2 G14 已签署语义，Core 激活协议已破坏式更新：Host 生产模块入口
和声明式贡献目录使用本程序集，注册时一次绑定 Descriptor、
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

G8 已完成生命周期编排，G9–G11 已迁移 MyPlugTest、DaTangAccountingHelpPlug 与 MySmallTools。
G10 新增的 `IPluginWindowInteraction` 由 Host 以同一受控实例注入每个插件私有 Provider：
它只返回本地路径或操作结果，不向插件暴露主窗口、`StorageProvider` 或剪贴板实现。
原生选择器返回后会再次检查取消令牌，以丢弃 Document 关闭期间的迟到结果。

G11 由同一 UI SDK 的 `IWindowContentFullscreenHost` 承载 MySmallTools 全屏交互，没有重复 Legacy
接口或 Host 门面。BiliDownloader 已在 G12 完成迁移，G13 已删除 Legacy 项目和过渡入口。
当前未发布 `3.0.0` UI SDK 的 46 条现有 public 签名位于 v3 Unshipped，v3 Shipped 为空；G4 没有改变
public C# 形状。v2 Shipped
继续保存 V2 G14 历史承诺。本阶段不运行 Windows Smoke、ReleaseAcceptance 或任何发布门禁。
