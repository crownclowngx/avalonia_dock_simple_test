# Document 保存 V1 设计

> 状态：已实现
> 更新日期：2026-08-13
> 边界：公共脏状态、关闭确认、最近成功备份与坏文件恢复；不包含历史 Document 格式迁移

## 1. 设计目标

Document 保存必须同时保证四件事：插件只解释自己的业务状态；宿主独占路径选择和磁盘事务；
被取消的关闭不能提前释放 Document Scope；主文件损坏时不能为了恢复而覆盖原件。

本轮没有历史 `.mamdoc` 文件需要兼容。所有插件保存和读取当前格式，不保留旧格式猜测、字段别名
或迁移链。格式不匹配统一抛出脱敏的 `DocumentLoadException`，再由宿主决定是否尝试当前命名规则
下的恢复备份。

## 2. 职责与 SOLID

| 边界 | 单一职责 | 不负责 |
| --- | --- | --- |
| `ISavableDocument` | 生成和加载插件业务快照 | 脏状态、文件事务、关闭 UI |
| `IDocumentSaveState` | 报告未保存变化并接受成功基线 | 序列化和路径策略 |
| `DocumentSaveService` | 路径决策、主文件提交、备份更新 | Dock 关闭和恢复对话框 |
| `DocumentCloseCoordinator` | 关闭决策、异步确认和一次性重入 | JSON 与文件系统细节 |
| `DocumentPersistenceCoordinator` | 打开、批量错误隔离和恢复编排 | 插件业务字段解释 |
| `DocumentWorkspace` | 将 Dock 树适配为 Document 查询与激活 | 保存策略 |

这些类型通过构造注入协作。没有通用工作流、可扩展状态机或事件总线；变化点只用窄接口隔离。

所有 `ISavableDocument` 必须同时实现 `IDocumentSaveState`。`IsDirty` 通常直接映射 Dock 的
`IsModified`，标签视觉状态和关闭保护因此共享同一事实。宿主在创建后立即校验契约，失败时通过
`DOCUMENT_SAVE_STATE_MISSING` 拒绝发布，并回滚可能已经建立的 Document Scope。

## 3. 保存事务

保存顺序不可调整：

1. 插件执行无副作用的 `CreateSaveDocumentMetaData`；
2. 宿主序列化一次，并使用同目录 staging 原子写入主文件；
3. 主文件成功后更新标题与 `FilePath`；
4. 调用 `IDocumentSaveState.AcceptChanges()`；
5. 若存在路径保护，再调用 `IDocumentSavePathPolicy.NotifySaveCompleted()`；
6. 将同一序列化内容原子写入 `<主路径>.recovery.bak`。

主文件是业务提交点。主文件失败时，路径、标题、脏状态和路径保护全部保持原值；备份失败时主文件
已经成功，Document 仍接受新基线，但宿主显示“已保存、备份更新失败”的警告。菜单保存、标签关闭
保存和窗口退出保存共用 `DocumentSaveService` 与 `DocumentOperationGate`，不会并发覆盖同一路径。

## 4. 关闭与退出

Dock 的 `OnDockableClosing` 同步返回，确认窗口和文件选择器是异步操作。脏标签第一次关闭因此返回
`false`，`DocumentCloseCoordinator` 异步询问用户。保存成功或选择放弃后，协调器授予一次性许可并
再次调用 `CloseDockable`；第二次回调消费许可后才进入 Dock 原生关闭流程。

取消、路径选择取消和保存失败都不会触发 `IDocumentLifetime.ClosingToken`。只有
`OnDockableClosed` 才清理恢复注册、取消生命周期并释放 Scope。确认期间的重复关闭复用当前请求。

窗口退出列出全部脏 Document，一次选择“保存全部、放弃全部、取消”。保存全部按 Dock 顺序串行
执行，首个取消或失败即停止；已成功项保持干净，未处理项保持脏。干净窗口不进入异步重入，直接
保存布局并关闭；脏窗口只在最终批准退出时保存布局。

## 5. 坏文件恢复

只有 JSON 或插件抛出的 `DocumentLoadException` 被视为内容损坏。文件缺失、占用、权限错误、插件
未安装和不支持的 Document 类型不会触发恢复。

主文件损坏时，宿主先在新的未发布 Scope 中完整加载 `.recovery.bak`。备份通过宿主信封和插件
内容校验后才询问用户；无效备份直接报告“主文件及恢复备份均已损坏”，不会展示虚假恢复入口。

用户确认后，恢复 Document：

- 清空 `FilePath`，标题追加“（已恢复）”，并置为脏状态；
- 在宿主恢复注册表中记录损坏主路径，重复打开时激活同一标签；
- 保存时强制选择新路径，拒绝损坏原路径和备份路径；
- 新文件成功提交或标签最终关闭时清理恢复注册。

损坏原件在所有分支中都不会被移动、删除或覆盖。

## 6. 失败与测试矩阵

| 场景 | 结果 |
| --- | --- |
| 快照创建失败 | 编程/插件错误向上传播，磁盘和 Document 状态不提交 |
| 主文件写入失败 | 保持脏状态和原路径，关闭被否决 |
| 备份写入失败 | 主文件保存成功、状态变干净、显示备份警告 |
| 关闭确认取消 | 标签和 Scope 保持打开，ClosingToken 不取消 |
| 保存路径取消 | 等同取消关闭，不制造错误保存状态 |
| 主文件损坏、备份有效 | 用户确认后打开强制另存副本 |
| 主文件和备份均损坏 | 不发布标签，临时 Scope 完整回滚 |
| 权限或占用故障 | 不尝试恢复，不修改任何文件 |

自动化覆盖公共契约、写入提交点、备份警告、恢复保护、重复激活、同步关闭否决、异步重入、批量
退出中止和 Scope 恰好释放一次。插件测试分别覆盖 BiliDownloader、银行调节和 MyPlugTest 的脏状态
与安全加载行为。
