# G12：BiliDownloader Host V2 迁移

> 完成日期：2026-08-21。
>
> 性质：开发期迁移与非发布验证，不是发布验收。摘要固定为 `aiflow=false`、
> `windowsCi=false`、`windowsSmoke=false`、`releaseAcceptance=false`、
> `releaseGate=false`、`publishable=false`。

## 1. 结果

BiliDownloader 已从 Legacy Contracts、Dock Strategy、Dock 根模型和字符串快照完整迁移到最终
`MyAvaloniaManagement.PluginSdk` 与 `MyAvaloniaManagement.PluginSdk.UI`。生产入口启用
`ManagedPluginUseV2EntryContract`，真实 V2 Loader、Preflight、Registry 和插件私有 Provider 最终发布
1 个可持久化 Document、1 个右侧可隐藏 Tool 与 1 个 Lifecycle。

下载、认证、SQLite、FFmpeg、限速、内容来源、任务快照和现有产品错误分类均未改变。本阶段只替换
Host 接入、生命周期所有权、JSON 基础设施和 UI 交互边界；没有增加兼容层，也没有删除留给 G13 的
Legacy 项目。

## 2. SOLID 责任划分与设计思路

本次以所有权和变化原因划分职责，使用的机制只有构造注入、插件私有 Provider、Document Scope、
窄 Host Port、不可变快照、简单状态与幂等释放：

- Host 独占 Dock、Document Scope、信封、标题持久化、保存事务、关闭令牌的创建与最终释放。
- `BiliDownloaderViewModel` 是普通 `ObservableObject`，只负责一个下载方案的界面状态和内容 schema；
  它实现很小的 `IPersistablePluginDocument`，不继承 Dock，也不自行创建 Scope。
- `BiliDownloaderPluginLifecycle` 只编排插件级本地状态、登录验证、Coordinator 和限速初始化/关闭；
  Host 的超时、隔离、启动顺序和生命周期状态机没有复制到插件。
- `BiliDownloaderPluginReadiness` 是线程安全的插件内 singleton，只发布不可变快照和变更通知；Tool
  只读该投影，不反向控制 Lifecycle。
- `BiliSchedulerToolViewModel` 是插件 Provider singleton。它只组合任务、设置与历史投影；未 Ready
  时不会触碰设置、SQLite 或 FFmpeg，隐藏和恢复继续复用同一模型及 Coordinator。
- 目录、FFmpeg 路径等选择继续通过插件内部 `IUserPromptService` 窄端口完成，子 ViewModel 不再从
  Avalonia 全局窗口查找状态，也没有为单个插件扩展 SDK public API。
- JSON 解析统一到 `System.Text.Json`；远端容错读取集中在小型 `JsonNodeReader`，Document 严格解码
  集中在 `DocumentSaveCodec`，避免各业务服务重复实现相互矛盾的规则。

这组边界分别满足 SRP、ISP 与 DIP；内容 Provider、冲突/媒体选择策略保持 OCP；Document 多 Scope、
仓储和运行时替身继续满足 LSP。没有引入服务定位器、通用工厂、公共基类或额外状态框架。

## 3. 贡献与生命周期矩阵

| 类型 | 稳定 ID / 类型 | 生命周期 | 说明 |
| --- | --- | --- | --- |
| Plugin | `myavalonia.plugin.bili-downloader` | Provider | manifest 唯一身份 |
| Document | `myavalonia.plugin.bili-downloader.document.download` | scoped | 可持久化、允许多实例 |
| Tool | `myavalonia.plugin.bili-downloader.tool.scheduler` | singleton | Right / Hide，隐藏恢复复用 |
| Lifecycle | `BiliDownloaderPluginLifecycle` | singleton | 初始化与有序关闭插件后台资源 |
| Creation Intent | `quick-url` | 声明 | 快速 URL / ID 工作台 |
| Creation Intent | `personal-source` | 声明 | 个人及订阅来源工作台 |

旧 GUID、Tool 别名、Document/Tool Strategy 和工厂注册已经删除。生产程序集引用闭包不包含
Legacy Contracts、Dock、Host 或 Newtonsoft；最终 ZIP 同样不携带这些程序集，也不携带由 Host
共享的 Core/UI SDK。

## 4. Document schema 3 与保存提交点

插件内容继续使用 schema 3，payload 为原生 `JsonElement`，既有 PascalCase 字段和数值枚举保持不变。
读取规则如下：

1. Host 先验证唯一六字段 Document V2 信封；插件不读取 V1/V2 Host 信封。
2. `DocumentSaveCodec` 完整解析 schema 3，拒绝损坏 JSON、错误根类型、非法枚举、敏感/临时内容和
   违反既有安全上限的数据。
3. 未知字段允许读取但不进入领域对象，因此再次保存时自然丢弃；缺失可选字段使用既有默认值。
4. Mapper 先得到完整、已验证的候选状态，随后一次性应用到刚创建的 Document；失败 Scope 不发布，
   已可见模型也不会被半恢复状态污染。
5. `InitializeAsync(DocumentActivationContext, token)` 在 View 发布前统一处理新建、两个创建意图和恢复；
   视觉树不再触发初始化，关闭令牌贯穿恢复、解析、预检和子 ViewModel。
6. `CaptureContentAsync` 只捕获快照，不清除 `IsDirty`；只有 Host 成功写入文件后调用
   `AcceptChanges` 才提交干净状态。

## 5. readiness 与关闭时序

readiness 状态固定为：

```text
NotStarted → Initializing → Ready → Stopping → Stopped
                         ↘ Faulted
```

初始化失败或取消时，Lifecycle 先尝试停止已启动的登录后台验证和 Coordinator，再把 readiness 置为
只含固定脱敏消息的 `Faulted`，最后向 Host 原样抛出初始异常。这样 Host 保留真实诊断类型，UI 不接触
异常正文。

关闭时先发布 `Stopping`，同时请求登录验证与 Coordinator 停止，再用 `Task.WhenAll` 等待两者。
Coordinator 会拒绝新命令、取消处理循环、协作等待活动任务退出并将中断事实落库，最后 Flush 进度。
其中一项失败不会阻止另一项开始；失败仍交给 Host 生命周期诊断。全部成功后进入 `Stopped`，重复关闭
安全。Tool 使用具名处理器解除 readiness、Coordinator 和设置事件，已排入 UI 队列的迟到回调还会在
执行前检查 disposed 门闩。

## 6. 失败矩阵

| 失败点 | 可见结果 | 清理与提交语义 |
| --- | --- | --- |
| manifest / 入口 / Provider 组合 | 仅隔离 BiliDownloader | 不发布任何贡献，其他插件继续 |
| 本地状态、SQLite、FFmpeg、登录或 Coordinator 初始化 | readiness=`Faulted` | 清理已启动资源，原异常交还 Host |
| Host 初始化取消 | readiness=`Faulted` | 使用独立补偿令牌清理，不吞 Host 取消 |
| 非法创建意图 | Document 创建失败 | 候选 Scope 释放，View 不发布 |
| 损坏、敏感或非 schema 3 内容 | Document 恢复失败 | 不应用部分状态，不覆盖原文件 |
| Tool 在插件未 Ready 时恢复 | 显示脱敏不可用原因 | 不访问设置、SQLite、FFmpeg 或 Coordinator 查询 |
| 登录验证或 Coordinator 关闭失败 | Host 获得失败结果 | 另一关闭分支仍已启动并被等待 |
| Tool 释放后的迟到事件 | 无 UI 状态写回 | 具名退订 + disposed 门闩双重抑制 |

## 7. 专项门禁与实际证据

执行命令：

```powershell
.\scripts\Test-BiliDownloaderV2.ps1 -Configuration Release -NoRestore
```

结果位于 `artifacts/test-results/BiliDownloaderV2/`：

| 项目 | 本次结果 |
| --- | ---: |
| Host Plugin / Loader / 边界定向测试 | 57/57 |
| Headless UI | 21/21 |
| 最终 SDK 定向测试 | 14/14 |
| BiliDownloader 完整单元测试 | 718/718 |
| 最终 ZIP 真实加载、预检、Registry 与 Provider 组合 | 2/2 |
| 专项合计 | **812/812** |

BiliDownloader 覆盖率门禁为总体行 **83.77%** / 分支 **67.62%**；A 组
**89.09% / 76.82%**，B 组 **85.17% / 69.36%**，C 组 **76.73% / 56.65%**。
现有总量和分组阈值均未降低，模块、readiness、Document codec、Document 根模型和 Tool 根模型已加入
关键文件检查。

两次隔离构建产生相同的 14 文件测试 ZIP；文件路径、长度、逐文件 SHA 和归档 SHA 全部一致。
归档 SHA-256 为
`4F73359B0B1AD8E559391EC254BF892794EFF1FED79973D3E2B8F60C12B331D8`。解压后的 ZIP 已通过真实
Loader、Preflight、Registry 和私有 Provider 组合，并精确发布 1 Document、1 Tool、1 Lifecycle。
`summary.json` 固定记录全部发布相关开关为 `false` 且 `publishable=false`。

专项通过后又执行了完整非发布回归：locked restore 成功；全解决方案 Release `-warnaserror` 为
0 警告、0 错误；Host Unit 172、UI 52、Plugin 210，共 **434/434**，行覆盖率 **83.15%**、
分支覆盖率 **68.74%**；DaTang **62/62**、MySmallTools **184/184**。Core/UI API v2 基线、
7 个破坏性负例、SDK nupkg 内容/依赖与 10 个反向消费夹具全部通过。四插件包矩阵完成每插件两次
确定性构建、26 个契约负例和最终 ZIP Host 加载；BiliDownloader 在矩阵中的 14 文件归档摘要与专项
摘要一致。文档核心与完整门禁通过，完整门禁检查 50 份文档、288 个本地链接、98 个脚本路径和
39 个项目路径；差异检查在 Windows CRLF 仓库口径下无错误。

## 8. 明确未执行的流程

本阶段没有使用 AIFLOW，没有运行 Windows CI、Windows Smoke、ReleaseAcceptance、真实 Bilibili/账号、
真实 FFmpeg 媒体门禁、发布脚本、发布总门禁、签名、上传或标签流程。历史 P0/P1 产品发布记录继续作为
历史事实保留，不属于本次 G12 证据；尤其没有调用 `Release-BiliDownloaderP0.ps1`。
