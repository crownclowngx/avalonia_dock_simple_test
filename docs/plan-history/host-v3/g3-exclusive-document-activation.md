# V3 G3 互斥 Document 激活

> 状态：已完成
>
> 完成日期：2026-08-22
>
> 所属任务：[MyAvaloniaManagement V3 破坏式架构重构任务书](../../design/host-v3-breaking-refactor-plan.md#g3建立互斥-document-激活已完成)

## 1. 结果

G3 已破坏式删除用三个可空事实表达激活状态的 `DocumentActivationContext`。新建与恢复现在分别由
`NewDocumentActivation`、`RestoreDocumentActivation` 表达，两者只共享非 null 标题；Creation Intent
只可能出现在新建分支，恢复内容只可能出现在恢复分支。旧类型、兼容重载、适配器和 fallback 均不存在，
独立消费旧类型会产生 `CS0246`。

本阶段没有改变 manifest schema 2、Document envelope schema 2、三个插件内容 schema、
`layout-v2.json`、默认数据根 `v2`、恢复 Codec、保存修订协议或业务功能。已有 `.mamdoc` 仍由同一个
严格 reader 读取，区别只在于 reader 成功后构造类型安全的恢复输入。

## 2. 最终 public API

Core SDK 的唯一激活入口为：

```csharp
public abstract record DocumentActivation
{
    public string Title { get; }
}

public sealed record NewDocumentActivation : DocumentActivation
{
    public NewDocumentActivation(
        string title,
        CreationIntentId? creationIntentId = null);
    public CreationIntentId? CreationIntentId { get; }
}

public sealed record RestoreDocumentActivation : DocumentActivation
{
    public RestoreDocumentActivation(
        string title,
        DocumentContent restoredContent);
    public DocumentContent RestoredContent { get; }
}

ValueTask IPluginDocument.InitializeAsync(
    DocumentActivation activation,
    CancellationToken cancellationToken);
```

两个具体类型均为 sealed，标题在共同构造边界拒绝 null，恢复构造函数额外拒绝 null 内容。基记录还以
程序集内抽象标记封闭 C# 自动生成的 record 复制构造函数缺口；该标记不参与业务分支，也不扩大 public
API。独立消费负例证明外部程序集无法派生第三种可实例化类型。空标题仍表示插件可采用 Descriptor 或
模型默认标题。活动 v3 Core Unshipped 为 130 条、UI 为 46 条；两个 v3 Shipped 仍为空。v1/v2 API
历史文本保持原样。

## 3. Host 激活与回滚时序

Host 创建菜单只构造 `NewDocumentActivation`；严格主文件和恢复备份读取只构造
`RestoreDocumentActivation`。`ManagementFactory` 在创建插件 Scope 前执行类型分支：New 才按冻结
Descriptor 验证 Intent，Restore 必须对应可持久化注册。错误分支不会构造模型、Adapter 或 View。

合法输入继续复用既有单一链路：激活 scoped 模型、等待 `InitializeAsync`、构造
`ManagedDocumentDockable`、预构建 View，最后才发布到 Dock。初始化取消/异常、Adapter 构造失败、
View 失败和 Dock 发布失败都由当前持有候选对象的一层回滚，ClosingToken、View、模型与 Scope 只释放
一次。G3 没有新增状态机、激活 Manager、Visitor 或策略注册表。

## 4. Host 与四插件支持矩阵

| 所有者 | Document | New | Restore | 说明 |
| --- | --- | --- | --- | --- |
| Host | Welcome | 是 | 否 | 没有内容 Codec |
| MyPlugTest | Welcome | 是 | 是 | schema 1 完整解码后应用 |
| MyPlugTest | Message Receiver | 是 | 否 | 消息集合是 Scope 瞬态状态 |
| MyPlugTest | Batch HTTP GET | 是 | 否 | 批处理结果不进入信封 |
| MyPlugTest | Excel GET Generator | 是 | 否 | 预览与映射未声明 Codec |
| DaTang | Invoice Import | 是 | 否 | 发票会话未声明 Codec |
| DaTang | Bank Reconciliation | 是 | 是 | schema 1 完整验证后应用 |
| MySmallTools | Single Player | 是 | 否 | 文件选择和播放状态不进信封 |
| MySmallTools | Video Library | 是 | 否 | 历史与设置由插件私有存储拥有 |
| MySmallTools | Encryptor | 是 | 否 | 批处理队列不进信封 |
| MySmallTools | Decryptor | 是 | 否 | 候选与执行队列不进信封 |
| BiliDownloader | Download | 是 | 是 | New 解释默认/Quick URL/Personal Source；Restore 解码 schema 3 |

三个可持久化模型继续先把恢复内容解码到独立临时状态，验证成功后一次应用。BiliDownloader 的未知
Intent 只在 New 分支拒绝；Restore 分支没有 Intent，也不会执行任何创建入口逻辑。其余模型收到
Restore 时显式抛出 `NotSupportedException`，不静默降级成空白新建。

## 5. SOLID 与朴素模式取舍

- **SRP**：激活值类型只表达输入；Host 验证入口与 Descriptor；插件解释自身 Intent/Content；Dock
  Factory 只负责初始化和 UI 适配。
- **OCP/LSP**：Host 和插件统一消费 `DocumentActivation`，两个具体类型具有相同标题语义；不支持的
  类型明确失败，不改变替换后的资源所有权和取消语义。
- **ISP**：New 消费者不再看恢复字段，Restore 消费者不再看 Intent；非持久化 Document 不获得 Codec
  或保存接口。
- **DIP**：Host 继续只依赖 Core SDK 的窄契约，插件不知道路径、信封、Dock 或 Host 实现。
- **朴素实现**：只使用密封记录类型、直接模式匹配和现有 try/finally 回滚，没有引入通用处理器、
  状态机、事件或第三方模式库。

新增 public API、Host 分支和插件拒绝理由均使用中文 XML/行内注释，重点解释输入所有权、验证时机和
失败原子性，不对显而易见的赋值逐行注释。

## 6. 测试、覆盖率与 API

专项入口：

```powershell
.\scripts\Test-ExclusiveDocumentActivation.ps1 -Configuration Release -NoRestore
```

专项串行通过 **143/143**：SDK 17、Host 29、Headless UI 17、三插件 Host 集成 40、MyPlugTest 3、
MySmallTools 25、BiliDownloader 12。脚本同时扫描六个生产源码根、核对 Host New/Restore 构造点、
核对 v3 API，并通过两个独立项目分别证明旧类型和外部第三种激活类型编译失败。摘要位于
`artifacts/test-results/ExclusiveDocumentActivation/summary.json`。

完整 Host 为 Unit 174、UI 56、Plugin 202，共 **432/432**；行覆盖率 **83.28%**、分支覆盖率
**69.08%**。独立全量测试为 PluginSdk **37/37**、MyPlugTest **3/3**、DaTang **62/62**、
BiliDownloader **718/718**、MySmallTools **184/184**。Release `-warnaserror` 全解决方案构建为零警告、
零错误；v3 API 130/46、7 个破坏性变异负例、SDK Core/UI 本地包消费与十个依赖反向夹具均通过。
四个 Managed Plugin 均完成两次隔离构建且 ZIP 逐插件确定一致；25 个包契约负例、最终 ZIP Host 加载和
诊断脱敏源码门禁也全部通过。既有修订化保存回归门禁为 **159/159**，Document V2 回归门禁为
**90/90**。

## 7. 非发布边界

专项摘要固定记录：

```text
aiflow=false
windowsCi=false
windowsSmoke=false
releaseAcceptance=false
releaseGate=false
publishable=false
```

本阶段没有读取、初始化或调用 AIFLOW，没有运行 Windows CI/Smoke、ReleaseAcceptance、任何 V1/V2/V3
发布门禁，也没有执行签名、上传、推送、标签或发布操作。`Release` 仅表示编译配置。

## 8. 回滚边界

G3 必须整体回到 G2：Core API、Host 创建/恢复链、Host Welcome、四插件 11 个 Document、测试夹具、
v3 Unshipped、专项脚本和当前文档一起回滚。不得只恢复旧 Context、保留新旧重载或修改 v2 Shipped
历史文本。激活类型从未写入磁盘，因此回滚不迁移、删除、覆盖或降级任何 `.mamdoc`、布局或用户数据。
