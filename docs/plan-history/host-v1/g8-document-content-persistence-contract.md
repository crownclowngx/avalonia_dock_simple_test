# G8：保存契约与内容版本处理重构

> **历史说明：本 V1 阶段已由 Managed Plugin V2 G14 取代；以下日期、数量和结论保持原样。**

> 状态：已完成
>
> 完成日期：2026-08-18
>
> 边界：Managed Plugin v1 封板前候选契约重定基线；Document 信封 v1 磁盘格式不变

## 1. 结论

G8 把“插件业务内容”和“宿主保存状态”彻底分开。插件只创建不可变
`DocumentContentSnapshot` 并恢复其中的业务内容；路径、规范 `PluginId`、规范
`DocumentTypeId`、标题、保存时间、原子事务、恢复备份和恢复副本状态全部由宿主持有。

这是 v1 正式封板前一次有意的破坏式修改。仓库不存在已经发布的旧插件二进制，也不存在需要读取的
历史 Document 内容，因此没有保留 `Obsolete` 转发成员、兼容适配器或虚构的 schema 迁移框架。
SDK 与三个插件版本继续为 `1.0.0`；正式 v1 发布后再删除或改签名必须提升 SDK 主版本。

## 2. 新旧 API 对照

| G7 候选 API | G8 最终 v1 API | 处理原因 |
| --- | --- | --- |
| `DocumentSaveData` | `DocumentContentSnapshot` | 名称明确表示插件只拥有业务内容快照，不是完整磁盘保存数据 |
| `CreateSaveDocumentMetaData(string)` | `CreateContentSnapshot()` | 插件不接收路径，也不生成宿主元数据 |
| `LoadDocumentByMetaData(...)` | `RestoreContent(...)` | 恢复的是业务内容，而不是宿主信封元数据 |
| `ISavableDocument.FilePath` | 删除，移入宿主状态存储 | 插件不能选择、伪造或覆盖主文件路径 |
| `ISavableDocument.SaveDocumentTypeId` | 删除，所有权只来自 Registry | 防止插件运行期自报身份与规范注册项分叉 |

最终公共契约为：

```csharp
public sealed class DocumentContentSnapshot
{
    public DocumentContentSnapshot(int contentSchemaVersion, string payload);
    public int ContentSchemaVersion { get; }
    public string Payload { get; }
}

public interface ISavableDocument
{
    DocumentContentSnapshot CreateContentSnapshot();
    void RestoreContent(DocumentContentSnapshot snapshot);
}
```

快照要求内容版本为正整数、payload 非 `null`，且两个属性均不可变。`IDocumentSaveState` 保持独立且
签名不变；只有需要持久化的 Document 才实现这两个能力。`IDocumentSavePathPolicy` 没有在 G8
顺带删除，仍由 G11 按原任务边界处理。

## 3. 宿主与插件职责

| 事实或行为 | 所有者 | 提交规则 |
| --- | --- | --- |
| 业务 payload、内容 schema | 插件 | 快照创建只读；恢复先验版本再校验正文 |
| `PluginId`、`DocumentTypeId`、注册元数据 | 不可变 Plugin Registry | 创建时写入宿主状态，插件无覆盖入口 |
| 当前主文件路径 | `DocumentPersistenceStateStore` | 仅主文件成功提交或内容成功恢复后写入 |
| 标题、保存时间 | 宿主 | 标题在主文件提交后更新；时间来自 `TimeProvider` |
| 原子主文件、恢复备份 | 宿主保存服务 | 主文件是业务提交点，备份失败只产生警告 |
| 恢复副本强制另存 | 宿主恢复状态 | 清空宿主路径，禁止覆盖损坏原件及其备份 |
| 脏状态与接受基线 | `IDocumentSaveState` | 主文件提交成功后才调用 `AcceptChanges()` |

`DocumentPersistenceStateStore` 是宿主内部普通引用字典。它以 Dock `Document` 实例引用为键，值只含
规范 Registry 注册项和当前主文件绝对路径。重复引用登记被拒绝；关闭、创建失败或恢复失败通过幂等
删除收口。没有把该存储抽象成公共接口，也没有引入仓储、状态机或事件溯源。

加载顺序同样遵守失败原子性：先严格验证七字段信封和 Registry 所有权，再创建未发布 Document，
然后调用 `RestoreContent`。只有完整恢复成功才登记路径并发布到 Dock；失败时释放临时 Scope，且不写、
移、删任何文件。恢复备份创建的副本由宿主清空路径，所以插件无法把保存目标重新指向损坏原件。

## 4. SOLID 与朴素设计取舍

- **SRP / ISP**：`ISavableDocument` 只表达内容能力，`IDocumentSaveState` 只表达脏状态与成功基线，
  宿主状态存储只表达所有权和路径；任何一方都不承担完整保存工作流。
- **DIP**：保存服务依赖启动组合阶段建立的不可变 Registry 事实，而不是依赖插件 ViewModel 自报身份。
- **LSP**：三个保存实现都遵守相同的快照无副作用、精确版本判断、脱敏失败和“保存输出当前版本”语义。
- **OCP**：新增保存插件只需登记策略并实现两个内容方法；宿主不按插件具体类型增加分支。

实现只使用不可变 DTO、引用字典服务、构造注入和现有协调器。当前只有一个真实版本读取分支，因此
没有创建迁移器接口、策略链、抽象工厂或通用版本分派器。未来出现真实旧内容时，应在对应插件中显式
增加旧版本读取分支，并保证再次保存输出当前版本。

## 5. 内容版本矩阵

| 插件 / Document | 当前内容 schema | G8 行为 |
| --- | ---: | --- |
| BiliDownloader 下载 Document | 3 | 只读 3；未知旧版和未来版均稳定拒绝；保存始终输出 3 |
| MyPlugTest Welcome Document | 1 | 只读 1；未知旧版和未来版均稳定拒绝；保存始终输出 1 |
| DaTang 银行余额调节 | 1 | 只读 1；未知旧版和未来版均稳定拒绝；保存始终输出 1 |
| MySmallTools 四个 Document | 不适用 | 不声明保存能力，不虚构内容 schema；纳入完整构建与回归 |

插件程序集/包发布版本不参与内容 schema 判断。空白或损坏 payload、缺失必填字段和未知版本统一形成
稳定、脱敏的 `DocumentLoadException`；异常消息不得包含原始正文。

## 6. 测试与门禁证据

G8 以公共契约、宿主事务、真实插件内容和独立 SDK 包消费四层门禁保护：

- 公共 DTO 构造约束、不可变属性、`ISavableDocument` 精确成员和 Common API SHA256；
- 宿主状态登记/提交/清理、快照无副作用、加载发布点、保存失败原子性、备份警告、恢复强制另存、
  重复路径激活、关闭保存与窗口批量保存；
- 三个真实内容实现的当前版本往返、未知旧版/未来版、空白或损坏正文、缺失字段、脱敏异常和发布版本
  与内容 schema 解耦；MySmallTools 作为非保存插件执行完整回归；
- SDK 正例编译最终快照契约；负例分别证明 `DocumentSaveData`、旧方法、`FilePath` 和
  `SaveDocumentTypeId` 已不存在，不能通过兼容入口绕过宿主状态。

2026-08-18 按任务书顺序执行最终验收：

| 门禁 | 结果 |
| --- | --- |
| `dotnet restore MyAvaloniaManagement.sln --locked-mode` | 通过 |
| 解决方案 Release 构建（`SkipPluginDeploy=true`） | 0 警告、0 错误 |
| Host `DocumentPersistence/DocumentEnvelope/DocumentContent` 专项 | 37/37 通过 |
| PluginTests `DocumentContent` 专项 | 2/2 通过 |
| BiliDownloader 完整测试 | 719/719 通过 |
| DaTangAccountingHelpPlug 完整测试 | 64/64 通过 |
| MySmallTools 完整测试 | 182/182 通过 |
| Plugin SDK 独立包消费 | 新契约正例成功；旧模块、DTO 和保存成员负例按预期失败 |
| Host 综合门禁 | Unit 151 + UI 37 + Plugin 141 = **329/329** 通过 |
| Host 覆盖率 | 行 **80.41%**，分支 **65.71%**，不低于 G7 的 80.3% / 65.47% |
| Windows Smoke | 通过 |

测试数量是 2026-08-18 的时间点证据，不是永久固定门槛。综合报告位于
`artifacts/test-results/MyAvaloniaManagement`。

## 7. 回滚边界

可以整体回滚 G8 源码与同批文档，再重新执行 G7 基线门禁；不能只恢复旧接口或 DTO 而保留新的宿主
状态所有权，否则会形成两套路径和身份事实。回滚不会转换磁盘文件，因为七字段 Document 信封
schema 仍为 1 且字段完全未变。

本任务没有使用 AIFLOW，也没有初始化或修改 `.aiflow`。
