# V5 生命周期所有权与启动回滚实施验收记录

> 状态：已完成；最终代码的完整开发门禁与补充 Host 覆盖率检查通过。
> 日期：2026-09-06。
> 输入基线：`101c783`，工作树已有本轮 V5 设计及两处导航修改；本轮不自动提交。
> 设计依据：[V5 生命周期与启动回滚修复设计](../../design/host-v5-lifecycle-ownership-repair-plan.md)。
> 本记录不使用 G9 编号，不伪造其他阶段记录。
> 边界：`published=false`、`uploaded=false`、`tagCreated=false`、`aiflow=false`；不执行正式 seal。

## 1. 问题与结果

原 Runner 超时后只观察迟到异常并请求取消，Runtime 随后释放 Provider，缺少真实任务退出的释放屏障。
启动失败回滚也没有停止已经初始化的 Lifecycle。V5 使正常退出与启动回滚共用释放流程，并显式跟踪
返回 Task、取消通知和必要 Shutdown 责任；无法证明安全时保留全部 Plugin/Host Provider。

已复现的修复前失败：初始化超时后在关闭前成功，Shutdown 计数预期 1，实际 0。
证据：`artifacts/v5-lifecycle/before/v5-before.trx`。首次并行专项构建发生 SDK DLL 写入冲突，
改用 `-m:1` 后获得上述真实失败证据；不把构建失败当作缺陷复现。

实现不改变 SDK 公共 API、manifest、用户数据、插件注册和生命周期委托调用线程。
外部插件仅作为完整 Gate 的既有验证输入，不修改源码。

## 2. SOLID 与朴素实现

| 原则 | 本轮落实 |
| --- | --- |
| SRP | Operation/Runner 负责执行及取消资源；Coordinator 负责顺序和结论；RuntimeShutdown 负责释放；Retention 只保留引用。 |
| OCP | 复用现有 Command/Workflow 关闭端口，新增生命周期释放结果，不改写业务执行协议。 |
| LSP | 插件继续实现既有 IPluginLifecycle；失败初始化不新增强制 Shutdown 要求，正常调用线程保持原样。 |
| ISP | 关闭流程仅消费已创建参与者与释放端口，不访问完整 IServiceProvider；没有通用反射发现或事务框架。 |
| DIP | 实际 DI 工厂交付时记录引用，正常退出/回滚依赖同一份已创建对象事实；测试替换窄端口与时间。 |

关键所有权、线程与异常分支使用中文说明，重点解释“为什么必须等待/保留”和“为什么不能在 catch 冷解析”，
不以增加设计模式或抽象层数量作为成果。

## 3. 验证证据

专项命令：

```powershell
dotnet test Host/MyAvaloniaManagement.PluginTests -m:1 --no-restore --filter "FullyQualifiedName~HostLifecycleOwnershipTests|FullyQualifiedName~PluginLifecycle" --logger "trx;LogFileName=v5-ownership.trx" --results-directory artifacts/v5-lifecycle/focused -v minimal
```

最终专项结果：40/40 通过，0 失败，0 跳过。其中新增所有权测试 25 项，原有生命周期/兼容专项
加迟到初始化回归共 15 项。
证据：`artifacts/v5-lifecycle/focused/v5-ownership.trx`。新增测试使用手动时钟推进超时，
实际断言容器是否已释放、是否漏掉 Shutdown 和关闭入口是否重新创建服务，不仅检查状态名。

完整开发门禁命令：

```powershell
dotnet run --project tools/MyAvaloniaManagement.Gate -- verify
```

最终完整 Gate：`artifacts/gate/20260906-014731-101c7835bb18/summary.json`，
`profile=verify`、`scope=all`、`passed=true`。北京时间 09:47:30 至 09:49:34，
restore、Release 零警告 build、tests、contracts、packages、cross-repository、resource-harness 全部通过。

| 测试组 | 通过数 |
| --- | ---: |
| SDK | 81 |
| Host Unit | 292 |
| Host Plugin | 213 |
| Host Headless UI | 55 |
| MyPlugTest | 11 |
| WorkflowStudio | 73 |
| ClassicGame | 526 |
| VideoSecurityPlayer | 205 |
| DaTang 业务 | 71 |
| DaTang Host / Host UI / Standalone UI | 14 / 6 / 3 |
| Workbench 包 / UI、Workflow Action 组合专项 | 1 / 1 / 1 |

Host 三层合计 **560/560**；整轮 TRX 合计 **1553 次测试执行通过**，0 失败、0 跳过。
合计含专项重复执行，不能解释为 1553 个互不重复的用例。WorkflowStudio 独立预览自检也通过。
真实打包和组合包含 MyPlugTest、DaTang、VideoSecurityPlayer、WorkflowStudio、ClassicGame 五个既有插件。

资源 Harness 证据：同一 Gate 目录的 `pass-1/harness/report.json`。
`Success=true`、`Cycles=1`；LiveLeases、LivePlayers、LiveMediaInputs、LiveEncryptedStreams、
ActiveSurfaceRestores、CachedPlaintextChunks、LiveNativeDispatchers、LiveResourceReapers 均为 0；
已关闭 Document、View、加密流的存活弱引用均为 0，Failures 为空。
原生媒体 stderr 有非致命诊断，验收以实际进程退出码和上述资源断言为准。

### 3.1 补充 Host 覆盖率

核对 Gate 实现后确认：当前 `verify` 不采集覆盖率，因此本轮独立补采，未把 Gate 的空覆盖率字段当作通过。
对 Host Unit/Plugin/UI 三组执行 Release `--collect:"XPlat Code Coverage"`，并按现有 Gate 的 Host
覆盖来源补采 DaTang HostTests/HostUiTests（`FullyQualifiedName!~DaTangPackageTests`，13/6 项）。
五组均通过，共 579 次测试执行。外部工程只用于验证，没有修改源码。

原始 TRX、Cobertura 与日志在 `artifacts/v5-lifecycle/coverage-final/` 的五个测试子目录。
每组只选 GUID 目录中的原始 Cobertura，避免重复导入 TRX 附件副本；使用 ReportGenerator
`-assemblyfilters:+MyAvaloniaManagement` 合并，程序集范围与既有 `Host/MyAvaloniaManagement.Tests/coverage.runsettings`
的 Host-only 定义一致，没有排除任何 Host 类型或降低现有阈值。

| 检查 | 实测 | 现有阈值 | 结论 |
| --- | ---: | ---: | --- |
| Host 行覆盖率 | 88.02% | 84.39% | 通过 |
| Host 分支覆盖率 | 73.45% | 70.58% | 通过 |
| Lifecycle Operation 行 / 分支 | 98.04% / 91.67% | 无新增独立阈值 | 已采集 |
| Lifecycle Runner 行 / 分支 | 100% / 100% | 无新增独立阈值 | 已采集 |
| Lifecycle Coordinator 行 / 分支 | 93.78% / 80.77% | 无新增独立阈值 | 已采集 |
| RuntimeShutdown 行 / 分支 | 92.59% / 94.44% | 无新增独立阈值 | 已采集 |
| ShutdownParticipants 行 / 分支 | 100% / 100% | 无新增独立阈值 | 已采集 |

证据：`coverage-final/host/Cobertura.xml`、`coverage-final/host/Summary.txt`、
`coverage-final/host-threshold-result.json`。阈值直接读取当前 `gate.config.json`，配置文件没有修改。

### 3.2 失败处理与复验边界

- `20260906-013919-101c7835bb18`：完整 Gate 首次在 build 阶段发现旧测试清理调用放错位置，
  已修正，专项重新通过；该失败证据保留。
- `20260906-014038-101c7835bb18`：完整 verify 通过。随后补齐调用方取消 Shutdown 的剩余宽限分支，
  新增对应专项，因此以最后的 `20260906-014731-101c7835bb18` 作为最终代码门禁证据。
- 覆盖率第一次合并包含外部业务程序集，总体为 75.30% / 60.85%，不能用于判断 Host 阈值。
  保留原报告与失败检查输出，随后按既有 Host-only 范围重新合并，得到上述正确的 Host 指标；未修改测试或阈值来绕过检查。

最终证据保留主仓和外部输入指纹，`workspaceSnapshotVerified=true`；部分外部输入原本有未提交修改，
故 `externalInputsClean=false`。本轮未覆盖这些修改，也未将开发验证描述成发布封板。
`releaseEligible=false`、`publishable=false`。完整 verify 不包含正式 seal 专有的 Host Windows Smoke，
本记录不宣称完成正式隔离封板。最终证据生成后只回填文档和检查链接，生产代码与测试不再修改。

## 4. 已知限制与回退

- 超时只控制委托已返回的 Task；同步前缀阻塞与同步 Dispose 不在保证内。
- 保留防止提前释放，不代表停止插件已有后台活动；启动错误窗口长期存在时资源也可能长期存在。
- Shutdown 已失败/取消后的保留是保守政策，可能跳过原本可以正常执行的 Dispose。
- 迟到初始化越过关闭位置后不补调；交接保留后不恢复、不后台重试、不自动释放。
- 只需回退 Host 实现与对应文档即可回退本轮行为，无 SDK 或数据迁移；回退会重新暴露原所有权缺口。
