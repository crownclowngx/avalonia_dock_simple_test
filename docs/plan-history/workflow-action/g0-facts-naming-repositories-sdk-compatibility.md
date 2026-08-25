# Workflow Action G0：事实、命名、仓库与 SDK 兼容路线冻结记录

> 状态：已重新签署（2026-08-25；G1 实施前补充 Run 与 Consumer 进度出口）
>
> 输入提交：`030a4fca408f72aed75500c105dc51af855d9af7`
>
> 输入 Git tree：`d961e506357fbb6cc7f160f18b65acec0e3b72f5`
>
> 兼容结论：`sdkRoute=3.1-compatible-addition`
>
> 所属任务：[工作流执行与可选 AI 规划方案](../../design/ai-workflow-plugin-exploration.md#G0冻结事实命名仓库与-SDK-兼容路线)

## 1. 结论与生产边界

G0 已重新证明 Workflow Action 可以作为 Plugin SDK `3.1.0` 的兼容新增继续推进，不需要建立 SDK V4。
原候选把调用直接放在 Gateway，无法精确拥有 OncePerRun 授权、运行级并发、Dispose 取消和 Consumer
进度观察器。重新签署后 Gateway 只列举并创建 caller-bound `IWorkflowActionRun`，进度观察器作为
`Run.InvokeAsync` 参数进入 Host 受限代理。当前产品仍为 `3.0.0`，生产 Core/UI SDK 候选为 `3.1.0`；
manifest、Document、Layout 和数据根协议保持不变。模板和外部 Workflow Studio 仍未修改。

生产 v3 public API 事实完全不变：

| API 文件 | 条目 | SHA-256 |
| --- | ---: | --- |
| Core Shipped | 127 | `063BCB5852827612B0501C135D23FECD015069A6F7DDB409547157E4FA00F80F` |
| Core Unshipped | 72 | `7ACB44C89E56CAF25F527A7097736FE8A3DE468C3AD238875357E376219394E0` |
| UI Shipped | 45 | `B11FBE768C3AD04CA65CBF5128BF6FCE8C00058EBB24052D51FE5464A65AD803` |
| UI Unshipped | 6 | `4C3EE5E1A99B2E5610218254DFA9A92AB99AE8883AF599D3D5EECD4491E768B1` |

隔离副本从固定 3.0 Git worktree 生成候选 Core 72 条、UI 6 条签名，并与生产 v3 Unshipped 逐项数量
交叉验证；Analyzer 随后以零警告、零错误重建。专项候选包只作兼容证据，不能作为发布输入。

## 2. 冻结命名与仓库边界

| 用途 | 冻结名称 |
| --- | --- |
| 外部 Git 仓库 | `myavalonia-workflow-studio` |
| 解决方案根/项目前缀 | `WorkflowStudio` |
| 解决方案 | `WorkflowStudio.slnx` |
| 插件项目 | `WorkflowStudio.Plugin` |
| 独立预览项目 | `WorkflowStudio.Standalone` |
| 测试项目 | `WorkflowStudio.Tests` |
| 产品显示名 | `MyAvalonia Workflow Studio` |
| manifest PluginId | `myavalonia.plugin.workflow-studio` |

G0 不创建该仓库。G2 的候选包和模板门禁通过后，外部项目才能由模板生成；它不得进入
`MyAvaloniaManagement.sln`，不得复制到当前仓库 `Plugins/`，也不得用 `ProjectReference`、源码链接或
开发机绝对路径引用 Host/SDK。平台仓库与外部仓库只通过精确版本 NuGet 包和真实插件 ZIP 相交。

ActionId 沿用 128 字符小写点分/kebab-case 规则，并必须属于声明者的
`myavalonia.plugin.<name>.workflow.` 命名空间。`myavalonia.plugin.workflow-studio` 是持久身份，后续不能
因显示名、仓库名或可选 AI Provider 改名。

## 3. 候选公共契约

Core 候选面固定为 `WorkflowActionId`、风险与确认枚举、Descriptor、Context、受限进度、非泛型
`IWorkflowActionHandler`、caller-bound `IWorkflowActionGateway`、显式 `IWorkflowActionRun`、请求、终态和结构化失败。UI 候选面只增加
独立 `IWorkflowActionRegistration` 与 `AddWorkflowAction<THandler>` / `UseWorkflowActionGateway` 扩展方法。

关键所有权规则如下：

- `IPluginRegistration` 不增加成员；候选 Host 的 internal 注册对象同时实现可选扩展接口；
- Gateway 的 `CreateRun()` 绑定可信 CallerId；Run 拥有运行级取消、并发、授权缓存和目录 revision；
- 请求只有 `ActionId` 和克隆后的 `JsonElement Arguments`，没有 CallerId、RunId、OwnerId 或授权结果；
- CallerId 和 InvocationId 由 Host 生成，通过 `WorkflowActionContext` 下发；Consumer 进度观察器先由 Host 包装再交给 Handler；
- Handler public 签名只包含 SDK/BCL/`JsonElement`，插件可以在内部反序列化私有 DTO；
- Descriptor、请求和结果在边界处克隆 JSON，不能依赖调用方 `JsonDocument` 的生命周期；
- 旧 Host 上的扩展方法给出包含 `3.1.0` 的稳定 `NotSupportedException`，而真实插件发现仍优先使用
  manifest 版本检查，在执行入口代码前拒绝新插件。

风险位固定为 `UsesNetwork`、`ReadsLocalFiles`、`WritesLocalFiles`、`DeletesLocalFiles`、`HandlesSecret`、
`LongRunning`。`Never` 只允许 `None`；任一非删除风险至少 `OncePerRun`；删除风险必须
`EveryInvocation`。插件声明最低频率，Host 只能提升。

## 4. Schema Profile 与预算

首版只支持单一字符串 `type`；根必须是 object。对象必须显式声明 `properties` 和
`additionalProperties: false`；数组必须声明 `items` 和 `maxItems`。允许 `required`、标量 `enum`、
字符串/数值边界和数组项数边界；拒绝类型联合、组合 Schema、未知关键字、远程引用及非规范绝对
JSON Pointer。

| 预算 | 冻结值 |
| --- | ---: |
| 每份 Schema | 64 KiB |
| 输入实例 | 256 KiB |
| 输出实例 | 1 MiB |
| 最大深度 | 16 |
| Schema 累计属性 | 128 |
| 数组项 | 1024 |
| 单字符串 | 64 KiB UTF-8 |

G0 的参考验证器只存在于测试资产，目的是把语义转成可执行正反例。G1 应实现 Host internal 验证器并复用
测试语料，不得把参考验证器或预算常量暴露为新的公共“通用 Schema 框架”。

## 5. SOLID 优先与朴素设计

- **SRP**：Core 只表达跨边界数据与 Handler/Gateway；UI 只表达声明语法；Schema、授权、目录和执行属于 Host。
- **OCP**：以独立扩展接口新增能力，不修改 v3 已冻结的 `IPluginRegistration`。
- **LSP**：真实 3.0 MyPlugTest 二进制在候选 3.1 Host 中仍可发现、实例化模块并完成组合。
- **ISP**：Provider 只实现 Handler，Consumer 只取得 Gateway；两方都不依赖对方程序集或 Host 对象图。
- **DIP**：Workflow Studio 将依赖 SDK Gateway 抽象，Host internal 负责所有者 Provider 路由。

只使用值对象、扩展接口和 Gateway 三个普通模式。没有引入工作流框架、策略/工厂体系、反射泛型桥、
服务定位器、第二套容器或测试专用生产入口。候选源码使用详细中文 XML 注释说明所有权与兼容原因。

## 6. 实测兼容证据

从仓库根执行：

```powershell
pwsh -NoProfile -File .\scripts\Test-WorkflowActionG0.ps1 -Configuration Release
```

专项先以固定输入提交建立 detached Git worktree，再从该真实 `3.0.0` 源码生成 MyPlugTest ZIP。
3.0 副本运行新插件反向拒绝测试；随后只在临时副本提升 SDK 到 `3.1.0`、叠加候选源码、根据 RS0016
精确诊断登记临时 Unshipped，并构建两个候选 nupkg、独立 Provider/Consumer 与候选 Host 测试。

| 专项 | 结果 |
| --- | ---: |
| 3.0 Host 在加载伪 DLL 前拒绝要求 3.1 的插件 | 1/1 |
| 候选契约、Schema/预算、确认、JSON 所有权、扩展接口、跨 ALC 与真实旧包组合 | 14/14 |
| 重新签署候选及生产 Core/UI Unshipped | 72 / 6 |
| Host Unit / Headless UI / Plugin | 210 + 65 + 205 = **480/480** |
| Plugin SDK | **37/37** |
| MyPlugTest / DaTangAccountingHelpPlug | 11 + 62 = **73/73** |
| MySmallTools / BiliDownloader | 197 + 729 = **926/926** |

Host 合并覆盖率为行 **85.83%**、分支 **71.70%**，均高于既有 84.39% / 70.58% 保护线。
以上测试合计 **1531/1531**，失败 0、跳过 0。锁定还原和全解决方案 Release `-warnaserror`
构建为零警告、零错误；7 个 API 破坏性负例、1 组兼容新增流程、两个真实 SDK 包正例、十四个包消费
反例及 83 份文档的 475 个本地链接均通过。

候选 Handler 所在 Provider 与发起 Gateway 调用的 Consumer 分别由两个真实 `PluginLoadContext` 加载，
Core SDK 仍来自默认 ALC；Handler 内部使用 private DTO，但 public 调用只传 `JsonElement`、SDK Context
与结构化 SDK 结果，CallerId 由 Host 夹具绑定。候选 3.1 Host 同时发现并组合真实 3.0 MyPlugTest ZIP，
证明程序集版本提升与共享策略没有破坏旧插件。

机器摘要位于 Git 忽略的 `artifacts/test-results/WorkflowActionG0/summary.json`。该文件记录本次测试数、
旧 ZIP SHA-256 和临时候选包 SHA-256；临时候选包本身不保留为生产制品。

本次重新签署的真实 3.0 MyPlugTest ZIP SHA-256 为
`4B1AF24160497EEB0B2A407EAB42922488DA7544D02D83BBF84E18CEF52BFCFA`。本次临时 Core/UI 3.1 nupkg
SHA-256 分别为 `573219C76171BDCA4B4569ABC702958D7AEAAEA00BC4D5F63DACECDCE859D6B8` /
`50C73F72B7F172965835D83871D7DC90BF0885665BE23BF8987C495114967A36`；它们只证明本次候选可构建和消费，
没有被复制到发布目录、上传或承诺为可重复发布制品。

## 7. 非发布声明与回滚

本阶段未读取、初始化或修改 AIFLOW；未运行 Windows CI、Windows Smoke、ReleaseAcceptance、Host Release
Gate、签名、上传、标签或外部发布。Release 只表示本地编译配置。

```text
aiflow=false
windowsCi=false
windowsSmoke=false
releaseAcceptance=false
releaseGate=false
publishable=false
published=false
uploaded=false
tagCreated=false
```

若只回滚 G1，必须连同生产 API/版本、Host 内核、夹具、门禁和文档整体恢复到本记录的测试候选状态；
不得修改 v3 Shipped、保留临时包、预建外部正式项目或用双协议绕过失败。若再次改变 Run/进度边界或
兼容证明失效，必须回到 G0 重新评审。
