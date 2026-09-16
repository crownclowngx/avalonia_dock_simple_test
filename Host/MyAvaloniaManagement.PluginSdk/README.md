# MyAvaloniaManagement Plugin SDK

> 用途：包消费者了解 Core 契约。当前包版本 3.4.1；核对日期：2026-09-16。事实源为本程序集 public 类型、ApiCompatibility/v3 与集中版本属性。

Core 是平台无关契约，只依赖 .NET BCL，不引用 Avalonia、Dock、Microsoft DI、Newtonsoft 或 Host 实现。模块、服务注册、View 与窗口交互使用同版本 UI SDK。

## Document 与生命周期

Host 用互斥的 `NewDocumentActivation` / `RestoreDocumentActivation` 初始化 `IPluginDocument`；Creation Intent 只属于新建，DocumentContent 只属于恢复。每个 Document 独立 Scope，模型只观察 Host 拥有的 `IDocumentLifetime`。

可保存模型实现 `IPersistablePluginDocument`，捕获 `DocumentSaveSnapshot(DocumentRevision, DocumentContent)`；只有主文件提交成功后，Host 才以同一修订调用 `AcceptChanges(savedRevision)`。插件只在确认仍是当前修订时接受基线，旧确认不能清除新修改。

SDK 不提供通用事件总线；插件内部消息器由自己的 Provider 持有，不能把插件私有类型或服务解析器跨边界传递。

## 用户命令与跨插件调用

`CommandId` 和 `IWorkbenchDocumentCommandTarget` 表达活动 Document 的工作台操作；Target 只接收稳定身份和取消令牌，不取得 Provider、Control 或 Dock。Host 将菜单、快捷键和命令面板统一到同一执行路径。

Workflow Action 契约提供 JSON 边界的 Handler、caller-bound Gateway、显式 Run、结构化终态和受限进度。Consumer 不能伪造 CallerId 或授权结果，Run 异步释放会取消并等待在途调用。Provider/Consumer 可以由同一插件兼任，但 Host 禁止自调用及 Handler 嵌套调用。

Core/UI v3 Shipped 保持 127/45，增量文本分别为 91/79；已公开版本中的部分新增仍位于 Unshipped，不能据文件名推断“未发布”。Workflow 共享 Schema 位于独立包，不包含 AI 或工作流 Runner。

```xml
<PackageReference Include="MyAvaloniaManagement.PluginSdk" Version="[3.4.1]" />
<PackageReference Include="MyAvaloniaManagement.PluginSdk.UI" Version="[3.4.1]" />
<PackageReference Include="MyAvaloniaManagement.Plugin.Build" Version="[3.4.1]" PrivateAssets="all" />
```

插件发布物不携带共享 SDK DLL；由 Host 提供契约程序集。新模板最低 SDK 为 3.4.1，既有旧二进制保留真实 manifest 下限并独立验证。
