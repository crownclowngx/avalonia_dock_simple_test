# Workflow Action G3：外部 Workflow Studio 与 Fake Action 闭环签署记录

> 状态：已完成（2026-08-25；完整本地非发布门禁通过）
>
> 外部仓库：`myavalonia-workflow-studio`
>
> 外部提交：`e651665fb75f241b8b26f5680e2fcac7ff921024`
>
> 外部 Git tree：`9aa344ae9e1dca386e86e742093f54475acf0514`
>
> 模板：`MyAvaloniaManagement.Plugin.Templates 1.1.0`
>
> Core/UI SDK：`3.1.0`；Build：`1.1.2`
>
> 总设计：[工作流执行与可选 AI 规划方案](../../design/ai-workflow-plugin-exploration.md)

## 1. 签署结论与事实源

G3 已在平台仓库之外创建独立 `myavalonia-workflow-studio/WorkflowStudio.slnx`。外部提交
`e651665fb75f241b8b26f5680e2fcac7ff921024` 是本记录签署的源码、测试、脚本和文档基线；外部仓库中的
`docs/plan-history/workflow-action/g3-workflow-studio-fake-action-loop.md` 与 Git 忽略的
`artifacts/test-results/WorkflowStudioG3/summary.json` 是详细实现和机器证据的权威来源。本记录只在平台仓库
登记跨仓库完成事实，不复制外部实现，也不把机器生成物提交到平台仓库。

模板生成后的解决方案根直接包含 `WorkflowStudio.slnx`、`src`、`tests`、`docs` 与 `scripts`，没有额外
`WorkflowStudio` 子目录。三个项目分别为：

| 项目 | 单一职责 |
| --- | --- |
| `WorkflowStudio.Plugin` | 正式 Consumer 插件、非持久化 Document、定义/验证/Secret/风险/Runner 与 UI |
| `WorkflowStudio.Standalone` | Avalonia 开发入口、两个 Fake Action、Fake Gateway 与会话生命周期 |
| `WorkflowStudio.Tests` | Codec、目录、验证器、Runner、Secret、Document、注册与 Fake 契约测试 |

正式插件只从 NuGet.org 精确还原 Core/UI SDK `3.1.0` 与 Build `1.1.2`，没有 Host 源码、本地 feed、Host
artifacts 或跨仓库 `ProjectReference`。正式 ZIP 不包含 Standalone、Fake 或测试程序集。

## 2. 已完成能力与产品边界

插件身份固定为 `myavalonia.plugin.workflow-studio`，通过 `UseWorkflowActionGateway()` 成为纯 Consumer，
并贡献一个非持久化 `Workflow Studio` Document。Studio 私有定义 v1 支持结构化步骤、JSON 常量、前序输出
引用、`${item.*}`、`${secret.*}`、顺序 Sequence 和有限 `ForEach`。确定性验证器在执行前检查目录 revision、
Action、Schema、引用作用域、Secret、风险和预算；Runner 在首个失败、拒绝、不可用、超时或取消后停止。

Standalone 提供两个窄 Fake Action：

| Fake Action | 作用 |
| --- | --- |
| `generate-items` | 生成有界列表，证明前序输出和数组引用 |
| `format-item` | 顺序消费 item 与会话 Secret，证明进度、失败/取消停止和脱敏 |

默认演示执行 `generate-items` 一次，再对三个结果顺序执行 `format-item`，合计 4 次调用。会话 Secret 只在
Document Scope 内存中保存，定义、规范导出、运行摘要、异常、TRX 和门禁摘要均不得包含 Secret 值；关闭
Document 时取消运行并清空 Secret、步骤、JSON 与中间输出。

G3 没有实现 MySmallTools 或 BiliDownloader 的真实 Provider Action。候选 Host 安装正式 ZIP 后能发现并
组合 Studio Document 与 caller-bound Gateway；在没有 G4/G5 Provider 时，Action 目录为空和执行按钮禁用是
预期行为。Fake 闭环只在 Standalone 中运行，不能冒充真实跨插件业务 E2E。

## 3. SOLID 与朴素设计

| 原则 | G3 落地 |
| --- | --- |
| SRP | Catalog、Codec、Validator、Risk、Secret、Resolver、Runner 与 Document 分责 |
| OCP | 新 Action 通过公开 Descriptor 目录进入 Studio，不修改 UI 或调用内核 |
| LSP | Standalone/Test Gateway 完整替代公开 SDK Gateway，Consumer 不增加环境特判 |
| ISP | 每项业务能力使用一个小接口，UI 不取得通用 `IServiceProvider` |
| DIP | 核心依赖 SDK Gateway 和 Studio 私有端口，最外层组合根选择真实 Host 或 Fake |

实现只采用构造注入、不可变目录快照和窄 Gateway 适配，没有引入 Mediator、事件总线、工作流框架、脚本
引擎、Service Locator、抽象工厂或通用管线。公共 SDK、Host public API 和 manifest schema 均未变化。

## 4. 验证与证据

外部专项入口文件名为 `Test-WorkflowStudioG3.ps1`，位于外部仓库的 `scripts` 目录；它接受 Release 配置与
候选 Host 输出目录，依次执行边界扫描、NuGet.org locked restore、Release 零警告构建、全量测试、TRX
检查、覆盖率、Standalone 无窗口闭环、两次确定性 ZIP、manifest/依赖/Secret 扫描和候选 Host 隔离启动。
平台仓库没有同名入口，也不代理执行外部仓库门禁。

最终机器摘要时间为 `2026-08-25T07:16:22.5240694Z`，实测结果如下：

| 证据 | 结果 |
| --- | ---: |
| 单元测试 | **43/43**；失败 0，跳过 0 |
| 行 / 分支覆盖率 | **85.57% / 76.52%** |
| Standalone 无窗口闭环 | **4 次调用 / 1 个 Run 完整释放** |
| 确定性插件构建 | **2 次；ZIP SHA-256 一致** |
| ZIP SHA-256 | `57B0627D8B30887C6D7BF032E9C549F97FC2672D0EAC021F23D48D1190CE663B` |
| ZIP 文件 / manifest | **4 个文件 / schema 2 / SDK [3.1.0, 4.0.0)** |
| 候选 Host 隔离启动 | **退出码 0**；Plugin、入口、容器、Document 与 Gateway 无错误 |

候选 Host 验收复制既有输出到外部仓库的 Git 忽略隔离目录，只替换隔离副本中的 Controls 并设置隔离数据根；
没有修改平台仓库源码或候选 Host 原始输出。该自动关闭路径验证真实加载和组合，不声称执行了 Fake 或真实业务
Action。

## 5. 非发布、后续与回滚

```text
aiflow=false
windowsCi=false
releaseAcceptance=false
releaseGate=false
publishable=false
published=false
uploaded=false
tagCreated=false
```

Release 仅表示本地编译配置。G3 没有接入 Windows CI、Release Acceptance、发布门禁、签名、标签或上传。
后续 G4 负责 MySmallTools 非破坏性加密 Action，G5 负责 BiliDownloader headless Action，G6 才签署真实
“下载 → 顺序 ForEach 加密并保留源文件”的跨插件闭环；这些事实不能由 G3 提前代签。

实现回滚单位是整个外部 `myavalonia-workflow-studio` 仓库，不回滚 Host、SDK、Build 或 Templates。平台仓库
本次只增加事实记录和索引；若 G3 签署失效，应整体撤销本记录及总设计/索引中的 G3 完成状态，不得把 Studio
源码搬入 `MyAvaloniaManagement.sln` 规避包或加载边界。
