# Document envelope v2 与 V3 G2 修订保存设计

> 状态：当前实现
>
> 更新日期：2026-08-22
>
> 边界：V2 G14 的唯一创建/打开/恢复/Scope 链，加上 V3 G2 的修订化保存与关闭保护

## 1. 设计结论

Document 只有一条生产路径。新建、Creation Intent 和磁盘恢复仍构造 V2 G14 建立的
`DocumentActivationContext`；插件实现普通 `IPluginDocument`，可保存模型额外实现
`IPersistablePluginDocument`。Host 独占 Registry 身份、路径、磁盘标题、恢复标记、Dock 发布和
Scope 释放，插件只解释 `DocumentContent` 中自己的 schema 与原生 JSON payload。

V2 不读取、迁移或写回 Document V1。历史 V1 设计与验收记录仍保留为历史事实，但不是当前运行时
契约。MyPlugTest、DaTangAccountingHelpPlug、MySmallTools 与 BiliDownloader 已在 G9–G12 全部迁移，
G13 已删除 V1 生产面，G14 已完成正式测试与文档签署。

DaTang 银行余额调节是第二个真实持久化 Document：其 content schema 固定为 1，独立 Codec
严格拒绝错误 schema、根类型、未知/重复/缺失字段、错误类型和无效配置。恢复必须先完整
解码和业务验证，再一次提交到模型；损坏内容不得改变标题、路径、选项或原脏状态。
V3 G2 的 `CaptureSaveSnapshotAsync` 不是保存提交点；它返回同一稳定观察区间内的
`DocumentRevision` 与不可变 `DocumentContent`。只有 Host 原子主文件成功后才调用
`AcceptChanges(savedRevision)`。插件仅在该修订仍是当前修订时接受基线；旧修订和重复确认都是幂等
无操作。插件在 `IsDirty` 布尔值实际变化时发出 `IsDirtyChanged`，Adapter 将最终状态投影到 Dock。

## 2. SOLID 与朴素模式

| 协作者 | 单一职责 | 明确不负责 |
| --- | --- | --- |
| `DocumentEnvelopeSerializer` | 唯一 V2 线格式、严格字段与资源约束 | 文件选择、Dock、插件 payload 解释 |
| `DocumentPersistenceCoordinator` | 新建、打开、恢复和活动项保存用例编排 | JSON 细节、Scope 释放算法 |
| `DocumentSaveService` | 捕获修订快照、原子主文件提交、指定修订确认和提交后警告 | 修订排序/解释、关闭决策、活动标签选择 |
| `DocumentPersistenceStateStore` | 保存 Registry、规范路径、Host 标题和 `RequiresSave` | 插件展示状态和文件 I/O |
| `HostDockAdapterFactory` | 异步初始化并组合 Adapter/View | Registry 发现、磁盘格式 |
| `ManagedDocumentDockable` | 投影 Dock/View 并拥有唯一 Scope Lease | 保存事务、用户确认 |
| `DocumentCloseCoordinator` | 关闭选择和一次性重入许可 | JSON、路径、Scope 创建 |
| `DocumentScopeManager` | 创建独立 Scope，提供窄 Lease，固定释放顺序 | Dock 发布、插件业务 |

实现只使用 Factory、Adapter、Coordinator 与 Scope Lease。接口只放在真实变化边界，没有仓储框架、
通用状态机、策略注册双轨、动态代理、反射恢复、事件溯源或 V1/V2 reader 链。这一取舍优先满足
单一职责、依赖倒置和所有权可审阅性，同时避免为一次线性事务引入抽象炫技。

## 3. 唯一所有权链

创建顺序固定如下：

1. `ManagementFactory` 使用冻结 Registry 核对 `DocumentTypeId` 和 Creation Intent；
2. `PluginContributionActivator` 在所属 Provider 的 `DocumentScopeManager` 中创建独立 Scope；
3. Scope Manager 返回只含模型、`ClosingToken` 和幂等释放入口的 `PluginDocumentScopeLease`；
4. `HostDockAdapterFactory` 以该令牌等待 `InitializeAsync`；
5. 初始化成功后才构造 `ManagedDocumentDockable`，预构建并绑定唯一 View；
6. 创建方登记 Host 持久化状态，最后把完整 Adapter 原子发布到 Document Dock。

初始化、Presentation、Adapter、View、状态登记或发布任一步失败，待发布引用仍由调用者持有，并汇入
同一个释放入口：断开 View 和 `DataContext`，发出 `ClosingToken`，释放模型与 scoped 依赖。失败不会
留下半个标签、路径索引或恢复登记。Host 内建 Welcome 也走相同工厂，但布局只接受同步完成的结果，
不会阻塞等待任意插件异步代码。

## 4. Document envelope v2

根对象必须且只能按确定顺序写出六个字段：

```json
{
  "schemaVersion": 2,
  "pluginId": "myavalonia.plugin.example",
  "documentTypeId": "myavalonia.plugin.example.document.sample",
  "title": "示例",
  "savedAtUtc": "2026-08-21T08:00:00+00:00",
  "content": {
    "schemaVersion": 1,
    "payload": { "value": 42 }
  }
}
```

`payload` 通过 `JsonElement.WriteTo` 写为嵌套 JSON，可为对象、数组、字符串、数字、布尔值或 null，
绝不二次编码为 JSON 字符串。读取后由 `DocumentContent` 克隆，因此不依赖解析器内部
`JsonDocument` 的生命周期。

根对象和 `content` 都拒绝未知、重复、缺失、大小写错误和类型错误字段；同时拒绝注释、尾逗号、
非规范 ID、非 UTC 时间、空白标题、schemaVersion 1、空文件、超过 8 MiB 的 UTF-8 信封和超过深度 8
的 JSON。文件系统长度在读取文本前预检，解析入口按实际 UTF-8 字节再次检查。格式异常仅使用 Host
internal `DocumentEnvelopeException`，用户只看到固定脱敏提示。

## 5. 打开、恢复与并发

打开顺序不可调整：规范路径查重、长度预检、严格解析、Registry 所有者与持久化能力核对、异步初始化
未发布 Adapter、提交路径状态、原子发布。新建、打开和保存共享同一个串行门；并发打开同一路径时，
后一个请求只激活已发布标签。批量打开逐文件隔离失败，不因一个坏文件跳过后续合法文件。

主文件内容损坏时只尝试 `<主路径>.recovery.bak`，备份本身也必须是严格 V2，并在询问用户前完成插件
初始化和 View 预构建。用户拒绝恢复时立即释放暂存 Scope。接受后，Host 清空主路径、设置
`RequiresSave` 并记录损坏原件与备份路径；即使插件报告 `IsDirty == false`，关闭仍需确认且保存必须
另选路径，不能覆盖损坏原件或恢复备份。任何分支都不移动、删除或修改输入文件。

## 6. 保存提交点与关闭

保存只接受 Registry 声明为 `IsPersistable` 的 Adapter。服务使用该 Scope 的 `ClosingToken` 调用
`CaptureSaveSnapshotAsync`，只序列化 Snapshot 的 `Content`，并通过同目录 staging 原子提交主文件。
只有主文件成功后才提交 Host 路径、磁盘标题和恢复状态，再把同一 Snapshot 的 `Revision` 原样传给
`AcceptChanges(savedRevision)`，最后更新恢复备份。Revision 不进入 envelope。已提交的 Host 标题成为
Tab 的权威标题；恢复副本的 `RequiresSave` 与插件 `IsDirty` 共同决定 Dock `IsModified` 和主题的 `*`。

捕获取消、空 Snapshot、捕获异常或主文件失败时，不确认修订，也不改变路径、标题、恢复状态或脏状态。
主文件已成功而确认或备份更新失败时，结果是“已保存但有警告”，磁盘事实不能被回报为保存失败。
确认成功后若插件仍 Dirty，说明捕获后已有较新修改：普通保存仍成功，但关闭流程必须保持 Document
打开并提示再次保存；再次捕获和确认当前修订后才能关闭。插件自定义异常与对话框异常只进入内部脱敏诊断。

关闭取消、保存取消或保存失败不触发 `ClosingToken`。最终批准关闭后，Dock 完成移除，再由 Adapter
依次断开事件/View、发出令牌、释放模型和依赖；所有步骤幂等。同步 Dock 回调通过“首次否决、异步
确认、一次性重入许可”适配异步 UI，重复请求不会弹出第二个确认。Runtime 退出先释放全部 Adapter、
View 和 Document Scope，再由 Host internal Coordinator 反向停止生命周期、逆序释放插件 Provider，
最后释放 Host Provider。

## 7. 测试与阶段边界

envelope v2 历史专项入口仍为 `scripts/Test-DocumentV2.ps1`。当前修订保存专项入口是
`scripts/Test-RevisionedDocumentSave.ps1`，串行运行 SDK、Host 保存/关闭和三个持久化插件测试，并扫描
旧 API 回流。其机器摘要固定声明 `aiflow=false`、`windowsCi=false`、`windowsSmoke=false`、
`releaseAcceptance=false`、`releaseGate=false`、`publishable=false`。G2 不调用 V2/V3 发布门禁。
覆盖率门禁继续要求
Serializer ≥95%，Persistence Coordinator、Save Service、Close Coordinator、State Store ≥90%，
同时保留 Adapter、Scope Manager ≥90% 和既有整体阈值。

G2 的完整回滚单位是 SDK、Host、三个插件、测试、门禁与文档整体，回到 G1 的无修订保存协议；不得
保留有参/无参确认双轨。回滚不得恢复 V1 双栈，也不得读取、迁移、覆盖、删除或降级写回任何用户
`.mamdoc` 文件。
