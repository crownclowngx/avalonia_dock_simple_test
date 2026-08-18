# G7：Document 信封 v1

> 状态：已完成
> 完成日期：2026-08-18
> 边界：唯一 Document 磁盘信封、插件内容快照、严格读取、资源限制与失败原子性

## 1. 结论

G7 建立了项目第一个也是唯一受支持的 Document 信封。仓库不存在需要兼容的旧 Document
信封，因此实现中没有旧字段探测、格式猜测、迁移器、兼容分支或旧格式测试夹具。任何不符合
v1 的输入都作为无效文件拒绝；宿主不会判断它来自哪个历史版本，也不会创建、迁移或覆盖文件。

插件公共 DTO 已收口为不可变内容快照：

```csharp
public sealed class DocumentSaveData
{
    public DocumentSaveData(int contentSchemaVersion, string payload);

    public int ContentSchemaVersion { get; }
    public string Payload { get; }
}
```

`ContentSchemaVersion` 必须为正整数，`Payload` 不得为 `null`。payload 的业务结构和有效性始终
由插件负责；宿主不会解析其中的业务 JSON。

## 2. 唯一磁盘格式

v1 必须且只能包含以下七个 camelCase 字段：

```json
{
  "schemaVersion": 1,
  "pluginId": "myavalonia.plugin.sample",
  "documentTypeId": "myavalonia.plugin.sample.document.report",
  "contentSchemaVersion": 1,
  "title": "示例文档",
  "savedAtUtc": "2026-08-15T00:00:00+00:00",
  "payload": "{\"example\":true}"
}
```

| 字段 | 所有者 | 约束 |
| --- | --- | --- |
| `schemaVersion` | Host | 只能为整数 `1` |
| `pluginId` | Host Registry | 必须是规范稳定 ID，并等于 Document 注册项所有者 |
| `documentTypeId` | Host Registry | 必须是规范主 ID；历史别名不允许落盘或打开 |
| `contentSchemaVersion` | 插件 | 正整数；插件决定当前支持哪些值 |
| `title` | Host | 保存时取目标文件名，打开时由宿主恢复 |
| `savedAtUtc` | Host | 由 `TimeProvider.GetUtcNow()` 提供，偏移必须为零 |
| `payload` | 插件 | 非 null 字符串；宿主只透传，不解释业务内容 |

严格 reader 拒绝重复、未知、缺失、大小写错误和错误类型字段，也拒绝注释、尾随逗号、非 UTC
时间及非规范稳定 ID。最大 JSON 深度为 8；文件按 UTF-8 编码后的最大长度为 8 MiB。读取前先在
存储边界检查文件长度，读取后再按 UTF-8 字节数复核；保存序列化完成后执行同一限制。

## 3. 数据流与提交边界

保存时，插件只创建 `DocumentSaveData`。宿主从不可变 `PluginRegistry` 取得 `PluginId` 和
`DocumentTypeId`，从目标文件名取得标题，从注入的 `TimeProvider` 取得 UTC 时间，再由
`DocumentEnvelopeSerializer` 组装并严格输出信封。主文件原子写入成功后才提交路径、标题和
脏状态；恢复备份仍沿用既有事务语义。

打开顺序固定为：

1. 检查文件长度并严格解析 v1；
2. 校验规范 `DocumentTypeId`，拒绝历史别名；
3. 从 Registry 精确获取注册项；
4. 校验 `pluginId` 等于注册项 `OwnerId`；
5. 使用宿主标题创建尚未发布的 Document；
6. 校验 `ISavableDocument` 与 `IDocumentSaveState` 契约；
7. 只把内容 DTO 交给插件；
8. 全部成功后才发布到 Dock。

任一步失败都会回滚尚未发布的 Document Scope，不向 Dock 发布半成品，不执行写入，也不在
错误消息中泄漏原始 payload。主文件损坏后的备份恢复仍只产生强制另存副本，绝不覆盖损坏主文件。

## 4. SOLID 与朴素设计取舍

- **SRP**：内容 DTO、内部信封、严格序列化、保存事务、打开编排和 Registry 所有权各自只有一个变化原因。
- **OCP**：新插件通过 Registry 数据自然参与保存和打开，不增加插件名称或类型分支。
- **LSP**：所有可保存 Document 接受同一个内容 DTO，并以稳定 `DocumentLoadException` 拒绝无效业务内容。
- **ISP**：插件只依赖内容版本和 payload，不接触标题、宿主身份或保存时间。
- **DIP**：时间依赖 `TimeProvider`，身份依赖不可变 Registry；没有静态全局状态。

实现只使用不可变 DTO、内部 Envelope、一个严格序列化器以及现有 Registry/Coordinator。没有为了
未来可能性引入迁移框架、策略链、抽象工厂或只有单一实现的额外接口。G8 仍单独负责后续保存接口
命名与“创建快照”语义重构，本次保持 `ISavableDocument` 的同步方法签名。

## 5. 插件适配与错误语义

- BiliDownloader 只接受内容 schema `3`；已删除 `PluginMetadata` 解码和默认版本猜测。
- 银行余额调节只接受内容 schema `1`。
- MyPlugTest 只接受内容 schema `1`。

未知或未来内容版本、空白/损坏 payload、缺失业务必填字段均由插件抛出稳定且脱敏的
`DocumentLoadException`。异常不包含原始 payload、账户数据或下载参数。

## 6. 自动化证据

2026-08-18 在 Windows x64、Release 配置执行：

| 门禁 | 结果 |
| --- | --- |
| 锁定还原 | 通过 |
| 解决方案构建 | 0 警告、0 错误 |
| `DocumentEnvelopeV1` 专项 | 24/24 通过 |
| Host Unit / UI / Plugin | 147 / 37 / 138，共 322/322 通过 |
| BiliDownloader | 719/719 通过 |
| DaTangAccountingHelpPlug | 64/64 通过 |
| Plugin SDK 包消费 | 新内容 DTO 编译成功；已删除旧信封成员编译失败 |
| Host 覆盖率 | 行 80.3%，分支 65.47% |
| Windows Smoke | 通过 |

SDK public API 指纹已同步更新。专项测试覆盖精确字段、Unicode 与转义、确定性往返、宿主字段
所有权、8 MiB 和深度 8 边界、严格 JSON、UTC、稳定 ID、未注册类型、历史别名、所有权冲突、
失败不发布/不泄漏 Scope/不写入，以及既有主文件、备份和恢复另存事务回归。没有任何“旧信封
可以读取或迁移”的测试。

## 7. 后续边界

G7 完成的是磁盘协议和所有权边界，不提前合并 G8。G8 可以在不改变 v1 磁盘格式的前提下重命名
保存接口、强化快照创建的无副作用语义，并继续扩充插件内容版本测试。
