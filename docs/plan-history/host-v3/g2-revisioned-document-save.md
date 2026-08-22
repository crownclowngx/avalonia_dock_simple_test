# V3 G2 修订化 Document 保存

> 状态：已完成
>
> 完成日期：2026-08-22
>
> 所属任务：[MyAvaloniaManagement V3 破坏式架构重构任务书](../../design/host-v3-breaking-refactor-plan.md#g2建立修订化-document-保存已完成)

## 1. 结果

G2 已把无版本保存协议破坏式替换为“不可变内容快照 + 插件修订号 + 指定修订确认”。Host 只把
插件捕获的 `DocumentRevision` 原样交还，不排序、不解释，也不把它写入磁盘。保存期间若模型再次
变化，主文件仍提交捕获时的一致内容，但旧修订确认不会清除当前 Dirty；普通保存报告磁盘成功，
Dock 关闭和窗口关闭则保持 Document 打开并提示再次保存。

本阶段没有改变 Document envelope schema 2 的六字段结构、三个插件各自的内容 schema、默认数据根
`v2`、路径和标题所有权、同目录 staging 原子替换、恢复另存保护或 `RequiresSave` 语义。

## 2. 最终 public API

Core SDK 新增两个最小值类型：

```csharp
public readonly record struct DocumentRevision(long Value);

public sealed class DocumentSaveSnapshot
{
    public DocumentSaveSnapshot(DocumentRevision revision, DocumentContent content);
    public DocumentRevision Revision { get; }
    public DocumentContent Content { get; }
}
```

`IPersistablePluginDocument` 只保留以下新协议：

```csharp
ValueTask<DocumentSaveSnapshot> CaptureSaveSnapshotAsync(CancellationToken cancellationToken);
void AcceptChanges(DocumentRevision savedRevision);
```

`DocumentContent` 和 Snapshot 都在构造边界冻结内容，Snapshot 拒绝空 Content。旧捕获方法和无参确认
没有兼容重载、适配器或条件分支。活动 v3 Core Unshipped 为 101 条，UI Unshipped 为 46 条；两个
v3 Shipped 仍为空，v2 Shipped 历史文本保持 Core 85 / UI 46 不变。

## 3. 保存与关闭时序

保存事务固定为：

1. 使用 Document 的 `ClosingToken` 捕获完整 `DocumentSaveSnapshot`；
2. Host 只把 Snapshot 的 `Content` 放入既有六字段 envelope；
3. 同目录 staging 写入并原子提交主文件；
4. 主文件成功后提交 Host 路径、磁盘标题和恢复状态；
5. 把同一 Snapshot 的 `Revision` 传给插件确认；
6. 确认成功后读取 `IsDirty`，记录是否已经存在较新修改；
7. 更新恢复备份；备份失败只形成提交后警告。

捕获取消、空 Snapshot、捕获异常或主文件失败都不会确认修订，也不会提交 Host 路径。确认异常发生在
主文件提交以后，只能返回“已保存但有警告”，不能伪造回滚。确认成功但仍 Dirty 表示捕获以后发生了
编辑：普通保存仍是成功，关闭流程必须保持标签和窗口打开；再次保存当前修订后才允许关闭。

## 4. 三插件修订策略

三个持久化插件各自拥有 `currentRevision` 与 `acceptedRevision`，没有在 SDK 中引入共享 Tracker：

| 插件 | 进入修订的持久字段 | 明确排除 |
| --- | --- | --- |
| MyPlugTest Welcome | URL、响应文本、历史记录 | 标题与临时 UI 状态 |
| DaTang 银行余额调节 | 既有银行对账保存模型中的字段 | 文件路径/标题之外的 Host 身份、瞬态交互状态 |
| BiliDownloader | 既有保存 DTO 的全部字段 | 标题、下载进度和未进入 DTO 的瞬态 UI |

每次持久内容真正变化都递增当前修订，即使已经 Dirty 也继续递增。`IsDirty` 仅由两个修订是否相等
推导，且只在布尔值变化时发布 `IsDirtyChanged`。捕获采用简单的前后修订检查：编码前后修订一致才
返回；否则响应取消后重试，保证内容和修订来自同一稳定观察区间。`AcceptChanges(savedRevision)`
只在当前修订仍相等时推进接受基线；旧修订与重复确认都是幂等无操作。初始化/恢复期间抑制推进，
完整成功后建立干净基线；关闭后的迟到变化仍由原生命周期门禁拒绝。

## 5. SOLID 与模式取舍

- SRP：插件拥有“什么算持久变化”，Host 拥有路径、标题、envelope 和文件事务；关闭协调器只解释
  保存结果是否允许关闭。
- OCP/LSP：Host 只消费统一 Snapshot，不按 MyPlugTest、DaTang 或 BiliDownloader 类型分支；三个实现
  遵守相同的旧修订幂等语义。
- ISP：没有给非持久化 Document 增加修订能力，也没有拆出只有一个用途的 Tracker 接口。
- DIP：Host 继续依赖 Core SDK 的窄保存契约，插件不依赖 Host 文件实现。

实现只使用值对象、不可变快照、局部锁和现有协调链。没有引入 Repository、状态机、事件溯源、
通用 Revision 框架、策略注册表或额外事务抽象。

## 6. 测试与覆盖率

专项入口：

```powershell
.\scripts\Test-RevisionedDocumentSave.ps1 -Configuration Release -NoRestore
```

专项门禁串行通过 **157/157**：SDK 16、Host 保存/关闭 28、真实插件集成 35、MyPlugTest 3、
BiliDownloader 75。摘要写入 `artifacts/test-results/RevisionedDocumentSave/summary.json`，同时扫描
生产源码，禁止旧捕获方法、无参确认以及新旧双轨回流。

完整 Host 回归为 Unit 173、UI 53、Plugin 202，共 **428/428**；Host 行覆盖率 **83.28%**、分支
覆盖率 **69.02%**。独立全量测试为 PluginSdk **36/36**、MyPlugTest **3/3**、DaTang **62/62**、
BiliDownloader **718/718**、MySmallTools **184/184**。MySmallTools 证明非持久化 Document 未受影响。

## 7. 非发布边界与命令

本阶段执行锁定还原、Release `-warnaserror` 全解决方案构建、Host/SDK/四插件测试、v3 API 兼容、
SDK 本地包消费、四插件本地确定性测试 ZIP、诊断脱敏和文档门禁。`Release` 仅是编译配置，不代表发布。

专项摘要固定记录：

```text
aiflow=false
windowsCi=false
windowsSmoke=false
releaseAcceptance=false
releaseGate=false
publishable=false
```

没有调用 AIFLOW、Windows CI、Windows Smoke、ReleaseAcceptance、任何 Host Release Gate，也没有执行
签名、上传、推送或标签操作。

## 8. 回滚边界

G2 必须整体回到 G1：SDK API、Host 保存/关闭、三个插件、测试、专项门禁和当前文档一起回滚。不得
保留有参/无参确认双协议，不得修改 v2 Shipped 历史文本，也不得迁移、删除、覆盖或降级任何现有
`.mamdoc` 用户数据。Revision 从未进入 envelope，因此回滚不需要数据迁移。
