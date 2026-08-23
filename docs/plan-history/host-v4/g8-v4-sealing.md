# Host V4 G8 封板记录

> 状态：已完成；Host V4 G0–G8 已封板并建立本地发布资格。
> 日期：2026-08-23。
> 正式入口：`scripts/Invoke-HostV4ReleaseGate.ps1`。
> 发布边界：`releaseEligible=true`、`publishable=true`、`published=false`、
> `uploaded=false`、`tagCreated=false`、`aiflow=false`。

## 1. 目标、输入与结论

G8 只签署 G0–G7 已经形成的 Host internal 收口，不修改生产 C#、Plugin SDK public API、manifest、
Document envelope、layout 或数据根。G7 的实现输入提交为
`ee16ef3865269a762abc747b7e6800b292f5756e`；G8 门禁能力候选提交为
`a2a0d9395b41b9eb8d0583f0aac0d7fd98f3f63d`，tree 为
`88f2470306149621e744f8f1dc58b172b8016cb4`。两者之间已有仓库文档提交保持原样，G8 没有读取、
初始化或调用 AIFLOW。

Host 产品、程序集、Core/UI Plugin SDK 与四插件版本继续为 `3.0.0`；SDK API 继续使用 v3 Shipped
Core 127 / UI 45，Unshipped 0 / 0。manifest、Document envelope 和 layout 继续为 schema 2，
布局文件仍为 `layout-v2.json`，默认数据根仍为 `v2`。V4 是 Host internal 收口代号，不是 SDK 4.0.0。

本记录保存候选提交上取得的审计事实。文档提交会改变 revision/tree，所以最终封板提交的精确
revision/tree、两轮实体 ZIP/manifest 长度与 SHA-256 以文档提交后生成的
`artifacts/release-gate/v4/<UTC>-<revision>/summary.json` 为权威来源，不为抄写自引用哈希追加第三个提交。

## 2. 门禁设计与 SOLID 取舍

正式入口没有引入工作流框架、反射发现、DI 容器、策略工厂或 Pester。实现只由显式阶段列表、
组合式编排和稳定证据投影组成：

| 原则 | G8 的朴素落实 |
| --- | --- |
| SRP | Core 模块只负责路径安全、阶段状态、稳定比较和实体证据复核；正式入口只负责环境、克隆和编排；G7 与各插件脚本继续拥有业务断言。 |
| OCP | 新阶段通过显式列表追加，不改写阶段执行器；没有自动发现或隐藏约定。 |
| LSP | G8 只消费既有 G7、文档、插件与 Smoke 的退出码和摘要，不改变叶子脚本契约。 |
| ISP | 正式入口无参数，阶段只接收自身所需的仓库根、证据根和隔离环境，不暴露通用发布接口。 |
| DIP | 比较与复核依赖稳定 JSON/实体文件，而不依赖具体测试框架内部对象；外层编排依赖叶子命令的公开退出边界。 |

`HostV4ReleaseGate.Core.psm1` 对越界、同根、通配符和未经验证的删除目标全部拒绝；受控清理只作用于
已证明位于指定父目录内的本轮目录。阶段按固定顺序串行执行，首个失败立即停止，并把原始错误与失败阶段
写入状态文件。两轮比较只忽略时间、耗时和绝对证据路径；revision/tree、测试数、覆盖率、API、文档、
插件、哈希、Harness、数据格式、Smoke 和发布标记必须逐路径一致。

每轮从同一提交创建 `git clone --no-hardlinks` 独立克隆，隔离 NuGet、TEMP、Host 数据根和构建环境，
依次运行：

1. `scripts/Test-HostV4ReleaseGateCore.ps1`；
2. `scripts/Test-DocumentationCore.ps1`；
3. `scripts/Test-HostV4DevelopmentGate.ps1 -Stage G7 -Configuration Release`；
4. `scripts/Invoke-MyAvaloniaManagementWindowsSmoke.ps1 -Configuration Release -NoRestore`。

G8 不复制 G7 的业务规则。G7 继续拥有锁定还原、Release `-warnaserror`、Host 三层测试与覆盖率、
SDK API/包、诊断、四插件专项、确定性 ZIP、真实 Host Loader、MySmallTools 资源 Harness 和文档门禁。
G8 复制 transcript、阶段状态、TRX、Cobertura、各级摘要、四插件 ZIP/manifest 与资源报告，再读取实体
内容并重新计算长度和 SHA-256；缺少任一必需证据或聚合值与实体不一致都会失败。

## 3. 候选提交实测证据

候选提交先独立运行 G7 完整开发门禁和 Windows 真实窗口 Smoke，结果如下：

| 范围 | 通过/总数 | 行覆盖率 | 分支覆盖率 |
| --- | ---: | ---: | ---: |
| Host Unit | 210/210 | — | — |
| Host Headless UI | 63/63 | — | — |
| Host Plugin/Dock | 205/205 | — | — |
| Host 合计 | **478/478** | **85.06%** | **71.41%** |
| MyPlugTest 专项 | **527/527** | 关键事件总线 98.15%，内容 Codec 100% | — |
| DaTangAccountingHelpPlug 专项 | **578/578** | 70.09% | 49.31% |
| MySmallTools 专项 | **712/712** | 73.20% | 49.28% |
| BiliDownloader 专项 | **1246/1246** | 83.87% | 67.86% |

SDK Core/UI Shipped 为 **127/45**、Unshipped 为 **0/0**；真实 nupkg 正向消费者与十四个反向消费夹具
通过。MySmallTools Windows x64 真实媒体 Harness 执行 **20** 轮，Document/View/加密流弱引用为 0，
最终 `LiveLeases`、`LivePlayers`、`LiveMediaInputs`、`LiveEncryptedStreams`、Surface 恢复、明文缓存、
原生调度器和资源回收器全部为 0。

Windows Smoke 在隔离数据目录启动真实 Host，退出码 0，并确认 `layout-v2.json` 已保存、layout schema 2、
旧布局不存在。候选机器为 Windows x64，PowerShell `7.6.4`，`global.json` 对应 .NET SDK `10.0.302`。

正式两轮封板的首次重复性检查曾正确拒绝 BiliDownloader 行覆盖率 `83.83%` / `83.87%`
漂移。定位后发现，登录失效分支与工作区能力探测失败收尾原先只会被未显式等待的后台任务偶然
覆盖。修复严格退回 G7 的测试边界：增加离线、可控依赖的显式用例，并等待可观测状态真正收口。连续四次
采样均为 `83.85%`，行命中漂移为 0；完整专项再次通过 **1246** 项、插件 `83.87% / 67.86%`。
该修复只修改单元测试，没有修改生产 C#、覆盖率阈值或 G8 比较规则。

重新进入 G8 时，第二轮的独立 NuGet 缓存经历了可恢复的下载 EOF/超时，SDK 正反消费业务断言通过后，
Windows 扫描器仍短暂占用 Avalonia BuildServices DLL，导致旧的 10 秒清理窗口失败。修复仅将已经
`Assert-ChildPath` 验证的本轮系统临时树改为最长两分钟有界重试；不扩大删除范围，不吞掉最终原始错误。
独立 SDK 包矩阵再次通过，两个正向消费者和十四个反向夹具全部成功，临时树最终不存在。

随后两轮实体比较再次正确拒绝 MySmallTools 行覆盖率 `73.15%` / `73.10%` 漂移。按归一化源码路径
逐行复核后，差异只来自 `CapturedUiScheduler` 的跨同步上下文投递两行，以及加密批处理总体进度向
文本和兼容属性投影的三行；它们此前依赖调度时机偶然命中。修复仍严格留在 G7 测试边界：分别加入
可控 `SynchronizationContext` 与真实 ViewModel 依赖图的显式单元测试。MySmallTools 测试程序集连续三轮
均为 **195/195**，三轮 9,397 个有效源码行的命中差异为 0；完整专项为 **711/711**、
`73.16% / 49.15%`，20 轮资源仍全部归零。该修复没有修改生产 C#、覆盖率阈值或 G8 忽略字段。

下一次完整双轮又拒绝了 `73.16%` / `73.19%`：逐行差异只剩 `SecureVideoPlayer` 在“没有当前媒体”
时降级为空快照的两行，以及 Document 已关闭后拒绝尾部事件的一行。它们原先由播放器异步事件的收尾
时机偶然覆盖。新增同步、可控生命周期的显式测试，分别断言空媒体允许发布一次、关闭后同一调用不得
再次发布；没有给生产类型增加测试 API。修复后测试程序集连续三轮均为 **196/196**，三轮原始采集的
10,800 个源码行命中映射完全一致；完整专项为 **712/712**、`73.20% / 49.28%`，20 轮资源仍归零。
该修复同样只修改单元测试，没有修改生产 C#、阈值或比较忽略规则。

## 4. 四插件实体包投影

以下是候选 G7 `package-first` 实体文件的重新计算结果，不是照抄插件聚合摘要：

| 插件 | ZIP 字节 / SHA-256 | manifest 字节 / SHA-256 | 文件数 |
| --- | --- | --- | ---: |
| MyPlugTest | 2,390,840 / `D309D2362EC940B9401E18BCD87AF3C676E8F7144D885DD44C32A82E1AE91389` | 2,648 / `71A8BEC17AE075C6D2B4DA5D5FB40DC18A8384A64CDDCBB80F31D503CD039E52` | 11 |
| DaTangAccountingHelpPlug | 2,398,229 / `9D8572C2556EE55224966F8EE9CD47ED49DBC77973EE9EE1B27BDA4EC1A9D5AC` | 2,364 / `29630A871E926F49F6E3F7ABC8D5CEDDDE501DD02942DC19EF8040A2B62C8AFF` | 9 |
| MySmallTools | 48,982,617 / `7C45924A0BA5E84DB025075C81811166D3E177540BD1A5F7976681E94DF25FC2` | 96,172 / `383777F1AAAC5878D6133EE85E8B3E2E6780017C9D10F0AAA3A2E56E69D33C07` | 431 |
| BiliDownloader | 2,495,101 / `C33AFB75E9E8986575D40F415C6AE5990D383FAEA4EA86E99DE1183C8F26E076` | 3,280 / `97FA0CC081850EF08FBCDBCDE27F238A713DF1D0FC4E010AF7F3A20D8F637A56` | 14 |

四份实体 manifest 均为 schema 2、插件版本 `3.0.0`、SDK 区间 `[3.0.0,4.0.0)`；每个插件均完成
两次确定性构建和真实 Host Loader。最终两轮封板目录会分别保存两轮实体包，顶层摘要只在两轮复核均
通过后建立。

## 5. 发布状态、回滚与审计边界

G8 的 `publishable=true` 只表示当前干净提交通过本地两轮门禁，绝不表示已经对外发布。正式摘要固定：

```text
releaseEligible=true
publishable=true
published=false
uploaded=false
tagCreated=false
aiflow=false
```

本阶段不修改 CI，不签名，不 push，不创建 tag，不上传 NuGet/ZIP，不调用外部发布或历史
ReleaseAcceptance，也不读取、初始化或调用 AIFLOW。若最终两轮发现生产回归，必须退回 G7 的既有职责
做最小修复并完整重跑；不得降低覆盖率、删除测试、放宽严格 reader 或忽略实体哈希。回滚以 V4 封板提交
为源码单位，证据目录可整体删除后重建；用户的 `v2` 数据根、Document、布局和外观设置不得删除或降级。
