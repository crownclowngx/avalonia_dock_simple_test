# Managed Plugin V3 G0：V2 绿色基线冻结记录

> 状态：已完成
>
> 验证日期：2026-08-22
>
> 分支：`dev-重构-2026年8月18日`
>
> 输入提交：`e3cb86bec591e3e524402ccefe500229a355dbdb`
>
> 输入 Git tree：`609b6057579ef2d905046328b6bcd294b74ffa69`
>
> 所属任务：[MyAvaloniaManagement V3 破坏式架构重构任务书](../../design/host-v3-breaking-refactor-plan.md#G0冻结-V2-绿色基线已完成)

## 1. 结论与边界

G0 只冻结 V2 G14 之后的真实代码、测试、覆盖率、API、包和数据事实。生产 Host、Plugin SDK 与四个
业务插件均未修改；产品与 SDK 仍为 `2.0.0`，manifest、Document envelope、layout schema 仍为 2，
布局文件仍为 `layout-v2.json`，默认数据根仍为 `v2`。

本轮增加一个保存竞争的 Host 单元测试、测试替身的一次性写入暂停点、文档门禁和本记录。最终验证使用
一次性本地干净克隆，不在用户工作区创建提交或标签。机器证据位于 Git 忽略的
`artifacts/baseline/host-v3/g0/`。

非发布边界固定为：`aiflow=false`、`windowsCi=false`、`windowsSmoke=false`、
`releaseAcceptance=false`、`releaseGate=false`、`publishable=false`。本轮没有读取或初始化 AIFLOW，
没有运行 Windows CI/Smoke、`ContinuousIntegrationBuild=true`、任何 V1/V2 发布门禁、发布验收、
真实网络/媒体、签名、上传或标签。

## 2. 保存竞争复现与设计思路

G0 使用真实 `DocumentSaveService` 和既有完整测试对象图，只在内存存储替身增加一个“一次写入后失效”的
同步点。同步点先通知测试“内容已经捕获并序列化”，再等待测试放行主文件提交；它不进入生产端口，也不
建立新的存储接口、调度器或并发框架。

确定性时序如下：

1. Document 内容设为“捕获时的内容”并标记 Dirty；
2. Host 捕获内容并进入第一次主文件写入，测试替身暂停提交；
3. 测试把内存内容改为“捕获后的新内容”，Document 仍为 Dirty；
4. 放行保存，主文件得到旧快照；
5. V2 Host 调用无参 `AcceptChanges()`，插件无法区分捕获前后的修改，错误地把当前模型标记为干净。

测试同时断言磁盘旧内容、内存新内容、`IsDirty=false` 和一次接受回调，避免只验证某个中间事件而没有
证明数据竞争。等待使用十秒上限，并在 `finally` 中无条件放行写入，失败时不会把测试进程永久挂起。
G2 必须引入修订化确认并把最终断言反转为“捕获后发生的新修改继续 Dirty”。

## 3. SOLID 优先与朴素设计

- **SRP**：生产保存服务继续只负责捕获、写入和提交后回调；测试暂停点只负责制造一个确定时间窗口。
- **OCP**：没有为了未来场景建立可配置故障脚本；既有存储替身只增加一次性能力，其他测试行为不变。
- **LSP**：测试替身仍遵守 `IHostStorageService` 的成功、失败和异步完成语义，暂停只改变完成时间。
- **ISP**：生产接口没有测试开关，暂停编排不会被 Host 或插件消费者看见。
- **DIP**：特征测试通过现有存储端口驱动真实保存服务，不复制 `DocumentSaveService` 算法。

没有引入 Mock 框架、状态机、策略模式、事件总线、通用并发夹具或 G0 聚合脚本。中文注释重点解释暂停点
为何位于捕获与提交之间、为什么必须在失败路径放行，以及当前断言为什么是缺陷证据而不是期望语义。

## 4. 实测测试、覆盖率与 API

最终数字从一次性干净克隆的 TRX、Cobertura 和 JSON 动态读取，不使用 V2 G14 历史数字。

| 门禁 | 实际结果 |
| --- | --- |
| 锁定还原与 Release `-warnaserror` | 通过；0 警告、0 错误 |
| Host Unit/UI/Plugin | 170 + 53 + 202 = **425/425**；失败 0、跳过 0；TRX 总时长 2.50 + 20.23 + 14.13 秒 |
| Host 覆盖率 | 行 **83.24%**、分支 **68.98%**；总体和全部重点文件门槛通过 |
| Plugin SDK 单元测试 | **34/34**；失败 0、跳过 0；TRX 总时长 0.736 秒 |
| BiliDownloader | **718/718**；失败 0、跳过 0；TRX 总时长 8.230 秒 |
| DaTangAccountingHelpPlug | **62/62**；失败 0、跳过 0；TRX 总时长 1.401 秒 |
| MySmallTools | **184/184**；失败 0、跳过 0；TRX 总时长 42.926 秒 |
| Core/UI v2 API | Shipped 85/46、Unshipped 0/0；7 个破坏性负例和 1 组兼容新增流程通过 |
| SDK 包消费、诊断与文档 | SDK 两个正例、十个反例通过；诊断检查 102 个生产 C#；最终文档 55 份、312 个链接、119 个脚本路径、41 个项目路径通过 |

重点文件行覆盖率为：`DocumentSaveService` **97.44%**、`DocumentPersistenceCoordinator` **94.51%**、
`DocumentCloseCoordinator` **97.62%**、`ManagedDocumentDockable` **96.25%**、
`MainWindowViewModel` **90.12%**，均未降低现有门槛。

API 文本没有变化：Core Shipped/Unshipped SHA-256 分别为
`3341B0D8FBE28339E7040FDAD4416A889EB53DEF3FCEA7086C5AA07A44793A99` / 
`3C3B422DDDB1EFEB0FC5B228608FD8682452B2A9507D4031894BCE3C4DEDADBD`；UI Shipped/Unshipped 分别为
`102BF050540106B56A1EF3683A6F4490E614BF9D8E317A337AE7DDA9A2902BCE` / 同一空基线摘要。

## 5. 解决方案、依赖与四插件包

| 事实 | 实测结果 |
| --- | --- |
| 解决方案项目与包图 | 18 个项目、18 个目标框架、79 条直接包关系、662 条传递包关系；项目清单 SHA-256 `AC378AE94FF5E17509834FE8F2123B9B56388F36ED6F64E799D108F6263D661F` |
| 包图 SHA-256 | `B32101E56C1F7DD8F09C01684348E3E9FF20FF8A5E5E5E25EFBF55BEE28CAA14` |
| 共享程序集与 RID 白名单 | 25 个构建协议负例通过；每插件两轮构建；包内容/RID 检查和最终 Host 加载 4/4 通过 |
| 源码诊断脱敏扫描 | 检查 102 个生产 C# 文件，通过；默认路径无异常正文、自由技术详情和完整路径输出 |

| 插件 | 版本 / SDK 区间 | ZIP 文件数 | ZIP SHA-256 | 两轮确定性 |
| --- | --- | ---: | --- | --- |
| BiliDownloader | `2.0.0` / `[2.0.0, 3.0.0)` | 14 | `BAADAD8AFBF9E086C7E28FDD7A9037B8BA52CE43D951C0A8A4390CBF46DF758A` | 是 |
| DaTangAccountingHelpPlug | `2.0.0` / `[2.0.0, 3.0.0)` | 9 | `910059BD427F2AFBAE6F1F953D3F6C2E65D0D72B9FCAAB89784E6E9DDF3289FD` | 是 |
| MyPlugTest | `2.0.0` / `[2.0.0, 3.0.0)` | 11 | `69B1F1CFB0B65E7BB880C7133C42B45D9B3DFFD342880CA7AB4EEF7A8E4B1789` | 是 |
| MySmallTools | `2.0.0` / `[2.0.0, 3.0.0)` | 431 | `00428A0D1A73BD592C8ADCF2F65CE40C18A0971539828E4FEF62D64FE197A080` | 是 |

机器证据共 211 个文件、80,851,270 字节，汇总入口为
`artifacts/baseline/host-v3/g0/summary.json`。该目录由 Git 忽略，数字和摘要只表示本次时间点事实。

## 6. 验证命令

在一次性本地干净克隆中执行以下非发布命令；没有调用任何 Release Gate 或 Windows Smoke：

```powershell
dotnet restore .\MyAvaloniaManagement.sln --locked-mode --nologo
dotnet build .\MyAvaloniaManagement.sln -c Release --no-restore --nologo -warnaserror -p:SkipPluginDeploy=true
.\scripts\Invoke-MyAvaloniaManagementTests.ps1 -Configuration Release -NoRestore
dotnet test .\Host\MyAvaloniaManagement.PluginSdk.Tests\MyAvaloniaManagement.PluginSdk.Tests.csproj -c Release --no-restore -p:SkipPluginDeploy=true
dotnet test .\Plugins\BiliDownloader\BiliDownloader.Tests\BiliDownloader.Tests.csproj -c Release --no-restore -p:SkipPluginDeploy=true
dotnet test .\Plugins\DaTangAccountingHelpPlug\DaTangAccountingHelpPlug.Tests\DaTangAccountingHelpPlug.Tests.csproj -c Release --no-restore -p:SkipPluginDeploy=true
dotnet test .\Plugins\MySmallTools\MySmallTools.Tests\MySmallTools.Tests.csproj -c Release --no-restore -p:SkipPluginDeploy=true
.\scripts\Test-PluginSdkPackage.ps1 -Configuration Release
.\scripts\Test-PluginSdkCompatibility.ps1 -Baseline v2 -Configuration Release
.\scripts\Test-ManagedPluginPackages.ps1 -Configuration Release
.\scripts\Test-HostDiagnosticRedaction.ps1
.\scripts\Test-DocumentationCore.ps1
.\scripts\Test-Documentation.ps1
```

## 7. Git 事实、回滚与进入 G1 的条件

| Git 事实 | 值 |
| --- | --- |
| 输入提交 | `e3cb86bec591e3e524402ccefe500229a355dbdb` |
| 输入 tree | `609b6057579ef2d905046328b6bcd294b74ffa69` |
| 一次性验证提交 | `c5d65a61772350a01d5bb63515e07e3068ba75c8` |
| 一次性验证 tree | `0fcf87f1c7f1f231f217fc75dd9ffc343937a579` |
| 用户工作区最终差异 | 2 个 Host 测试文件、1 个文档门禁脚本、4 份 V3/测试文档；无生产源码、版本、锁文件或 API 文本变化 |

G0 回滚只删除保存竞争特征测试、测试替身暂停点、V3 G0 文档与对应文档门禁增量。不得修改 V2 G14
封板记录、v2 API 文本、版本、锁文件、schema、布局文件名或数据根。

只有全部非发布门禁为零失败、零跳过，覆盖率不低于既有下限，四插件确定性包与真实加载通过，且最终
差异不包含生产代码时，才允许进入 G1。G1 才负责建立未发布 V3 版本线；G0 不预先修改任何 V3 API。
