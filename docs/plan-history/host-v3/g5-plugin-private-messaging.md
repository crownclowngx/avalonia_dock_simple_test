# G5：把事件通信收回插件内部

> 状态：已完成（2026-08-22）。本阶段是未发布 V3 的内部破坏式重构，不签名、不上传、不发布。

## 1. 结果

V3 Core SDK 已删除 `IHostEventBus`，Host 已删除 `HostEventBus`、根容器注册、插件 Provider 注入和对应
保留端口。Host 没有提供转发层、兼容层或新的全局总线。

MyPlugTest 与 BiliDownloader 分别拥有自己的同步消息器：

| 插件 | 公开的插件内契约 | 实现与所有者 |
| --- | --- | --- |
| MyPlugTest | `IMyPlugTestEventBus` | internal sealed `MyPlugTestEventBus`；MyPlugTest Provider singleton |
| BiliDownloader | `IBiliDownloaderEventBus` | internal sealed `BiliDownloaderEventBus`；BiliDownloader Provider singleton |

两个接口都只包含 `Publish<TEvent>` 和 `Subscribe<TEvent>`。接口公开在各自插件程序集内，是为了让公开贡献
模型能够构造注入，并让独立插件测试提供替身；它们不是 SDK 能力，也不能被 Host 或其他插件解析。

## 2. 消息拓扑与所有权

MyPlugTest 的请求结果发布和多个接收 Document 均改用 MyPlugTest 私有消息器。每个接收 Document Scope
保存自己的订阅令牌，并在 Scope 关闭时释放；关闭一个接收者不会影响同插件的其他接收者。

BiliDownloader 的登录广播、下载提交、进度、状态和删除消息均改用 BiliDownloader 私有消息器。现有 DTO、
`DocumentId` 定向过滤、提交兼容路径以及下载、认证、SQLite、FFmpeg 等业务行为没有改变。Document 与
Coordinator 继续各自释放所拥有的订阅令牌。

消息实例的最远生命周期是对应插件 Provider。不同插件、不同 Provider 以及并行 HostRuntime 之间没有共享
实例；Provider 释放后，消息器拒绝新的发布和订阅。

## 3. 朴素设计与 SOLID 取舍

本阶段只使用 Observer、DI singleton 和 `IDisposable` 订阅令牌，没有引入 MediatR、CQRS、静态
Messenger、公共基类、共享消息项目或新 NuGet 包。

- **SRP**：Host 只保留窗口交互和 Document 生命周期等真实 Host 端口；插件消息器只负责本插件的同步通知。
- **OCP**：插件可以新增自己的事件 DTO 和订阅者，而无需修改 SDK 或 Host 组合根。
- **LSP**：两个最小接口的测试替身遵守相同的发布、订阅和释放约定。
- **ISP**：贡献模型只依赖本插件的两个消息操作，不获得全局服务定位或跨插件能力。
- **DIP**：公开贡献模型依赖插件内接口；具体消息器由插件 Provider 构造并拥有。

两个实现刻意保持原有同步语义：仅精确类型匹配；按订阅顺序在发布线程调用；发布前取得订阅快照；
用户处理器在锁外执行；处理器异常原样传播并停止后续处理；令牌可以幂等释放；订阅、发布、释放和总线
释放均为线程安全。快照使重入、自释放和“处理器中新增订阅”具有确定行为，也避免持锁执行用户代码。

## 4. 删除面与 API

- 删除 Core SDK `IHostEventBus` 及两个方法，v3 Unshipped 实数更新为 Core **127**、UI **46**；两个
  v3 Shipped 文件仍为空。
- 删除 Host `HostEventBus`、DI 注册、插件提交、保留端口、行为测试和关键文件覆盖率条目。
- Host 测试和 UI 测试从对应插件 Provider 解析插件消息器，不再从 Host 根解析消息器。
- v1/v2 API 文本和历史阶段记录未改写；它们只用于历史审计。
- 使用 V3 SDK 编译 `IHostEventBus` 的负例稳定失败，证明没有隐式兼容面。

## 5. 测试与门禁实数

专项命令 `scripts/Test-PluginPrivateMessaging.ps1` 通过 **165/165**：

| 分组 | 通过数 |
| --- | ---: |
| MyPlugTest 消息器通用语义 | 8 |
| BiliDownloader 消息器通用语义 | 8 |
| Host 契约删除面 | 10 |
| 插件/Provider/Runtime 隔离 | 34 |
| MyPlugTest Headless UI | 11 |
| BiliDownloader 消息行为 | 94 |

两个消息器重点实现文件的行覆盖率均为 **97.72%**，高于 90% 门槛。Host 全量测试通过
**429/429**（Unit 169、UI 56、Plugin 204），Host 行覆盖率 **83.28%**、分支覆盖率 **69.19%**，
均不低于 G0 的 83.24% / 68.98%。

独立项目测试通过：PluginSdk **37/37**、MyPlugTest **11/11**、DaTangAccountingHelpPlug **62/62**、
BiliDownloader **726/726**、MySmallTools **184/184**。G2/G3/G4 回归分别通过 **159/159**、
**143/143**、**59/59**。

v3 API 兼容门禁通过 Core 127 / UI 46，7 个破坏性变异负例与 1 个 additive review 通过；SDK 包消费
门禁通过。诊断脱敏门禁检查 **103** 个生产 C# 文件并通过。

四插件均完成两次确定性 ZIP 构建、包契约检查和本地 Host 加载，四个摘要均为 `deterministic=true`：

| 插件包 | 文件数 | SHA-256 |
| --- | ---: | --- |
| BiliDownloader 3.0.0 | 14 | `9877A30F304FBD32A460E5D3AA92060CA37234F0C474F4BB15C3CC799BC6A0AA` |
| DaTangAccountingHelpPlug 3.0.0 | 9 | `108B777A75736A93D3DD2643DA1EB0613F60CF7B6DF28B8788C9B84228BC3B33` |
| MyPlugTest 3.0.0 | 11 | `291EB248A9E50A2D8A27014E4F847183019C744CE17031041523BE07AD4DDA13` |
| MySmallTools 3.0.0 | 431 | `928911229F7EE0B7F4A8FB47AB515A5E48BAE8ED90FB7FBAC7BCC109F22BF0BD` |

## 6. 非发布声明

专项摘要固定记录：

```text
aiflow=false
windowsCi=false
windowsSmoke=false
releaseAcceptance=false
releaseGate=false
publishable=false
```

本阶段没有运行 Windows CI、Windows Smoke、ReleaseAcceptance、Host 发布门禁或发布脚本；本地包验证
不构成发布签署。

## 7. 回滚边界

若必须回滚，应整体回到 G4：SDK 接口、Host 实现与注入、两个插件消费者、测试、API 基线和文档必须作为
同一个变更集恢复。不能只恢复 SDK 接口、只恢复 Host 注入，或让任一插件重新依赖旧总线，否则会重新制造
名称与所有权不一致的半兼容状态。
