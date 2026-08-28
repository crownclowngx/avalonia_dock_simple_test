# MyAvaloniaManagement Plugin SDK

本包是 Managed Plugin 的平台无关 Core 契约程序集。它只依赖 .NET BCL，并提供稳定身份、
Document 模型、原生 JSON 内容、关闭观察、插件生命周期，以及 Workbench Command 的平台无关身份与
Document Target 候选契约。

Core 不引用 Avalonia、Dock、Newtonsoft、Microsoft DI 或 Host 实现。需要声明模块、私有服务以及
Document/Tool 与 Avalonia View 映射的插件，应引用同版本 `MyAvaloniaManagement.PluginSdk.UI`。

当前仓库已完成 V3 G14 封板。Host 使用互斥的 `NewDocumentActivation` 与
`RestoreDocumentActivation` 异步初始化普通 `IPluginDocument`；Creation Intent 只存在于新建分支，
`DocumentContent` 只存在于恢复分支。可保存模型实现 `IPersistablePluginDocument`，通过
`CaptureSaveSnapshotAsync` 返回不可变的 `DocumentSaveSnapshot(DocumentRevision, DocumentContent)`，
并只在 `AcceptChanges(savedRevision)` 收到仍为当前版本的修订时接受保存基线。`IsDirty` 由当前修订与
已接受修订是否相等推导，Host 只投影结果，不解释插件修订值。模型仍只观察 Host 拥有的
`IDocumentLifetime`。该端口只在插件模块返回并通过所有权校验后由 Host 最终追加，插件不能用普通或
keyed DI 注册影子覆盖。G5 已删除 SDK 通用事件总线；需要消息通信的插件应在自身程序集声明最小接口，
并由自身 Provider 持有实现与生命周期。四插件已证明多 Document 独立 Scope、插件级 Tool/消息器、
Revision 保存竞争、严格内容读取、关闭令牌与 Lifecycle/readiness 可沿最终 Registry、Workspace 和
Dock Adapter 链工作；G13 已证明旧 public 入口和运行闭包零残留，没有新增 SDK public API。

当前 SDK 候选版本为 3.3.0；既有 Core 127/UI 45 条仍位于 v3 Shipped，Workflow Action 与
Workbench Command 兼容新增位于 Core/UI v3 Unshipped 91/66，v2 Shipped 历史文本保持不变。
Workflow Action 的 Core 契约提供 JSON 边界的 Handler、
caller-bound Gateway、显式 Run、结构化请求/终态与受限进度；不包含 Host、工作流定义或 AI 类型。

Workbench Command G1 新增 `CommandId`、单命令状态事件和窄
`IWorkbenchDocumentCommandTarget`。Target 由当前 Document 模型实例可选实现，只接收稳定身份和取消令牌，
不取得 Context、Provider、Control 或 Dock。G2 已在 Host internal 建立 Host/Plugin 合并 Catalog、打开/保存
Handler、统一 Executor、脱敏诊断和 10 秒关闭门控；SDK public API 与 G1 完全相同。活动 Document Context、
G3 已完成活动 Document Context 与插件 Target 路由，G4/G5 已把 Host 打开、保存及声明式菜单/快捷键统一到
同一个 Executor。G6 冻结 3.3.0 候选包并用独立模板、真实插件 ZIP、双 ALC 及新旧 Host 负例验证外部消费；
Command Palette 仍未进入生产。

Provider 的私有 DTO 不穿越公共边界。Consumer 通过 Gateway 创建绑定可信 CallerId 的 Run，不能提交
CallerId、OwnerId、RunId 或授权结果；Run 的 Dispose 会取消并等待本 Run 的在途调用。

Workflow Action G2 已把该能力传播到模板，并以真实 nupkg、三个 lock file、外部 Provider/Consumer
和 Host 实调通过门禁。SDK 与模板本次同步提升，Build 协议未变化并继续精确使用 `1.1.2`：

```xml
<PackageReference Include="MyAvaloniaManagement.PluginSdk" Version="[3.3.0]" />
<PackageReference Include="MyAvaloniaManagement.PluginSdk.UI" Version="[3.3.0]" />
<PackageReference Include="MyAvaloniaManagement.Plugin.Build" Version="[1.1.2]" PrivateAssets="all" />
```
