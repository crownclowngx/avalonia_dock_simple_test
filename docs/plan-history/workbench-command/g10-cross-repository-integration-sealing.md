# Workbench Command G10：跨仓库本地集成封板

> 状态：已完成（2026-08-29；单轮完整隔离本地非发布门禁）。
>
> Host 输入提交：`e4f5235589574ce478fdf25b64a24532253b2670`
>
> WorkflowStudio 输入提交：`a817ab226dd5b0ceee65eb5be8cbafc468cea2f6`
>
> ClassicGame 输入提交：`6033030008f11c9cf9491c383532d21fc9fe594e`
>
> 总设计：[Workbench Command 引入任务书](../../design/workbench-command-introduction-plan.md#g10跨仓库集成回归文档同步与封板)

## 1. 封板结论与边界

G10 将 Host、Core/UI/Workflow SDK、Templates、四个仓内插件、WorkflowStudio、ClassicGame、菜单、
快捷键与 Command Palette 签署为同一 Workbench Command v1 本地开发基线。G10 没有修改生产行为，
没有增加 Toolbar、ContextMenu、参数化命令、设置或新的 public API。

三仓输入工作树原有大量用户保留的行尾变化。本轮没有执行 reset、clean、checkout 或全仓格式化；机器摘要
同时记录各仓 HEAD、工作树文件数和按相对路径/长度/内容计算的 SHA-256，因此未提交内容也是明确输入，
不是被忽略的噪声。权威证据位于 `artifacts/test-results/WorkbenchCommandG10/source.json` 和
`summary.json`，工作树指纹不回写本文，避免文档参与自身哈希形成循环事实。

本轮不是发布阶段。Release 只表示本地编译配置，不运行或新增 Windows CI、Windows Smoke、
Release Acceptance、Host Release Gate，不上传、签名、打 tag 或发布，最终 `publishable=false`。
SDK 3.3.0 与 Templates 1.3.0 在 G6 已发布是历史事实，不是 G10 执行的新动作。

## 2. SOLID 与朴素设计

```text
三仓当前工作树
      │  git clone --no-hardlinks + 当前文件覆盖
      ▼
   单轮独立副本
      │
      └── 严格校验完整 JSON 事实 ──► G10 summary
```

- **SRP**：外部 G7/G8 和 Host G6–G9 叶子门禁继续拥有业务断言；G10 只负责隔离、编排、摘要验证和签署。
- **OCP**：G10 通过显式阶段列表组合既有门禁，没有修改 Catalog、Context、Executor、Target 或 Projection。
- **LSP**：聚合层只消费叶子脚本的退出码、JSON、TRX 与实体包，不替换其测试语义。
- **ISP**：Core 模块只提供路径、复制、指纹、非发布标记和稳定比较，不暴露通用发布或仓库管理接口。
- **DIP**：跨仓测试只依赖实体 ZIP、公共 SDK 和生产 Loader，不引用两个外部仓库的源码项目。

采用的模式只有显式编排、不可变证据投影和窄 Adapter/Target。没有事件总线、Mediator、服务定位、反射命令
发现或通用工作流引擎。反射只在测试中驱动五子棋落子，不进入生产路由。

## 3. 单轮完整门禁与新增回归

主入口：

```powershell
pwsh -NoProfile -File .\scripts\Test-WorkbenchCommandG10.ps1 -Configuration Release
```

本轮从三个无硬链接独立克隆开始，再覆盖当前工作树文件；NuGet、TEMP、DOTNET CLI 与结果目录均位于源仓之外。
固定顺序为 Core 单元测试、WorkflowStudio G10、ClassicGame G10、Host V4 G7 开发门禁、G6 SDK/模板、
Host G7、Host G8、G9 Palette、G10 组合实体包测试和文档门禁。首个失败立即停止且不会写最终成功摘要。
TEMP 使用同盘短 Junction 名称避开点号项目的传统 MAX_PATH，并只指向本轮物理工作根中的独立 TEMP。
按最终确认的验收口径，第一轮完整门禁没有问题即可封板，不再执行第二轮逐字段复跑；最终摘要明确写入
`singleRoundVerified=true`、`repeatabilityVerified=false`，不得把单轮结果描述为重复性或发布资格证明。

G6 在 G10 中显式使用 `-UsePublishedSdkBaseline`。它从 NuGet.org 两次独立下载 Core/UI `3.3.0`，验证
Repository 签名、固定公开 SHA-256、两次下载一致性和模板中冻结的发布前内容哈希；随后仍执行当前源码
nupkg 正反消费、Templates `1.3.0`、普通/点号生成项目、Standalone、确定性插件 ZIP、真实 Host 双 ALC、
旧模板兼容与旧 Host 负例。之所以不在物理副本里伪造历史候选，是因为保留的工作树行尾会参与 Portable
PDB 校验和，而已发布的同版本包不可覆盖。该模式不刷新三份 lock file，也不执行上传或签名动作。

干净副本还证明 Template 包项目不能依赖开发机残留的 `obj/project.assets.json`；G6 现在先显式还原这个
不属于主解决方案和模板示例解决方案的打包项目，再以 `--no-restore` 打包。该修复只完善门禁自足性，
不修改 Template 内容或生产行为。

新增 Core 单元测试覆盖目录越界、同根/同级、链接文件、跟踪及未跟踪文件复制、遗漏、内容漂移、摘要字段、
非发布标记、时间/绝对路径归一化、差异 JSON 路径和失败时不签署。

新增真实包组合测试把 WorkflowStudio 与 ClassicGame 同时放入一个 Controls 根，经生产 Loader 建立两个独立
ALC 和共享 Core/UI SDK，验证 2 个插件、14 个 Document、25 条 Command、25 条菜单和 5 条无冲突快捷键。
Headless MainWindow 在 Studio、两个五子棋实例和无活动 Document 之间切换，验证菜单、快捷键、Palette、
Enabled/Hidden、Undo 当前实例执行、重复关闭重建、迟到生命周期通知和窗口关闭后订阅归零。定向实测为
PluginTests **1/1**、Headless UI **1/1**。

完整测试数、覆盖率、四仓内插件与 SDK/模板制品哈希由本轮 `summary.json` 保存。门禁固定 Host 不低于
G9 的 **584** 项、**87.32% / 72.58%**；WorkflowStudio 不低于 54 项及既有 85% / 75% 门槛；
ClassicGame 不低于 526 项及既有 70.87% / 57.82% 门槛。任何缺失摘要、跳过测试、覆盖率不足、API/版本/
Schema 漂移或发布类标记为 true 都立即失败；Core 仍保留双证据差异定位测试，但正式入口不运行第二轮。

## 4. API、格式与制品冻结

G10 保持 API 分层，不把 Command 条目迁入 Shipped：

| API 文件 | 条目 | SHA-256 |
| --- | ---: | --- |
| Core v3 Shipped | 127 | `063BCB5852827612B0501C135D23FECD015069A6F7DDB409547157E4FA00F80F` |
| Core v3 Unshipped | 91 | `6805C1C131B7420CE1C7A601A06694B1910FA225D6063B38594D6FAF4D1E05EF` |
| UI v3 Shipped | 45 | `B11FBE768C3AD04CA65CBF5128BF6FCE8C00058EBB24052D51FE5464A65AD803` |
| UI v3 Unshipped | 66 | `AACE9EF4878E209FABDB1D49DF7657C7DD38A2D54753C1BD5E560CF0272E1FD8` |
| Workflow v1 Shipped | 68 | `7A3F931E36AEE1F6E135DF8B2CFB16C06CBA947BD585527B1500FD2998F36585` |
| Workflow v1 Unshipped | 0 | `0570CF88EF7BA0638A95F61E904C349C0C00BD34F76241B5EA968CE31482606A` |

Host 产品 3.0.0、Core/UI 3.3.0、Workflow SDK 1.0.0、Templates 1.3.0、Build 1.1.2、
WorkflowStudio 1.2.0 和 ClassicGame 1.1.0 保持不变。manifest、Document envelope、layout schema 均为 2，
布局文件仍为 `layout-v2.json`，数据根仍为 `v2`。两个外部实体 ZIP 的最终 SHA-256 写入本次执行生成的
`artifacts/test-results/WorkbenchCommandG10/summary.json`；Markdown 不预填依赖构建绝对根的旧哈希，
也不在门禁完成后回写参与源码指纹的本文，从而避免“修改证据文档后仍沿用旧签署”的循环事实。

## 5. 非发布声明与回滚

```text
aiflow=false
windowsCi=false
windowsSmoke=false
releaseAcceptance=false
releaseGate=false
publishable=false
published=false
uploaded=false
signed=false
tagCreated=false
```

回滚时整体移除 G10 Core/入口、组合测试、三仓包装脚本和三份 G10 记录，并恢复索引与任务书状态；G1–G9
生产实现、外部插件原 UI 和 G6 已发布包不随 G10 回滚。若门禁发现生产缺陷，必须回到对应 G1–G9 职责做
最小修复后重跑一轮完整门禁，不能降低阈值、删除测试、忽略哈希或覆盖已发布同版本包。
