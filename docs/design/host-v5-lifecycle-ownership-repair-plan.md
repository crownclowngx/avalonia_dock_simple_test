# MyAvaloniaManagement V5 生命周期所有权与启动回滚修复设计

> 状态：已完成；专项、完整开发门禁及补充 Host 覆盖率检查通过，实际证据见 [V5 专属实施验收记录](../plan-history/host-v5/lifecycle-ownership-repair-acceptance.md)。
> 日期：2026-09-06。
> 本次修订：按风险复评收紧范围，保持现有调用线程行为，区分释放硬约束与故障政策，补齐保留终态及实际创建对象的回滚规则。
> 代码核查基线：`101c783`；实施前重新记录实际提交和工作树状态。
> 范围：仅 Host 内部实现、Host 测试及相关文档。现有外部插件仓库和 SDK 公共契约不变。
> V5 是本轮 Host 设计迭代编号，不代表产品、NuGet、SDK 或数据格式升级到 5.0。
> 本文记录设计依据和目标行为；实施结果以专属记录为准，本轮不执行发布。

## 1. 目标与范围

本轮解决架构审计中的两个问题：

1. 生命周期等待超时后，真实插件任务可能仍在运行，但 Host 继续释放 Plugin Provider 和 Host Provider。
2. Host 启动后半程失败时，回滚没有调用已经成功初始化的插件生命周期的 `ShutdownAsync`。

两者共同目标是：**释放资源之前，Host 必须确认使用这些资源的已知生命周期操作已经结束，并完成必要的关闭协议。**

现有 Core SDK 的 `IPluginLifecycle` 已提供足够的协作边界：

```csharp
Task InitializeAsync(CancellationToken cancellationToken);
Task ShutdownAsync(CancellationToken cancellationToken);
```

契约已经要求 Host 编排调用顺序、超时和容器释放；插件在 Shutdown 返回前停止访问即将释放的资源。
因此，本轮不新增插件接口，不改变 Module 注册方式，不要求插件修改业务逻辑、重新打包或升级 SDK。
既有插件在启动失败时会收到原本遗漏的 Shutdown 调用，这是兑现已有契约的行为修正。

预期收益集中在启动失败和退出超时等异常路径的可靠性，不以正常运行性能提升为目标。
当前已确认代码缺口，但没有生产故障频率数据，不预估故障减少比例。

以下内容不属于 V5：

- Document Scope、View、Plugin Provider 全面迁移到异步释放。
- Shared Assembly 闭包基线、ALC 策略或插件发现机制调整。
- Workflow Action、Workbench Command 业务协议重构。
- 插件热卸载、进程隔离、强制终止线程或新的退出确认界面。
- 生命周期委托统一转移至线程池、同步前缀阻塞治理及通用任务调度框架。
- 保留对象图后的自动恢复、周期重试、后台收尾或自动释放机制。
- SDK、Build、Templates、manifest、Document envelope、layout 和用户数据迁移。

## 2. 已核实的问题

### 2.1 生命周期超时没有成为资源释放屏障

证据：

- [PluginLifecycleOperationRunner.cs](../../Host/MyAvaloniaManagement/Business/Lifecycle/PluginLifecycleOperationRunner.cs)：
  `RunAsync` 使用 `operationTask.WaitAsync(timeout, hostCancellationToken)`。超时后观察迟到异常，
  在后台请求取消，然后直接返回 `TimedOut`；结果没有携带真实任务的终止事实。
- [PluginLifecycleCoordinator.cs](../../Host/MyAvaloniaManagement/Business/Lifecycle/PluginLifecycleCoordinator.cs)：
  `ShutdownAllAsync` 提交超时状态后继续处理下一项，最终返回；只有成功初始化的项进入 `_started`。
- [HostRuntime.cs](../../Host/MyAvaloniaManagement/Business/Composition/HostRuntime.cs)：
  正常退出在 `ShutdownAllAsync` 返回后继续调用 `_pluginProviders.Dispose()` 和 `_provider.Dispose()`；
  Lifecycle 没有类似 Command/Workflow 的释放门控。

当前可能发生：

```text
ShutdownAsync 正在访问插件服务
    → Host 等待超时
    → 请求取消，但插件尚未退出
    → ShutdownAllAsync 返回
    → Plugin Provider / Host Provider 被释放
    → 原 ShutdownAsync 继续访问已释放资源
```

这不是简单的日志缺失。Host 混淆了“停止等待”和“操作结束”，可能产生释放后访问或后台写入失序。
SDK 本身已经返回 Task，不需要插件增加新的配合接口。

同一缺口还覆盖初始化超时、Host 取消等待的分支：初始化任务未结束，也不能因它未进入 `_started`
就忽略它。原任务仍在初始化时，不得对同一实例并发调用 Shutdown。

### 2.2 启动失败缺少生命周期回滚

`HostRuntime.Create` 当前顺序为：

```text
组合插件 Provider
    → 提交 Registry / Command Catalog / Workflow Catalog
    → InitializeAllAsync
    → 解析 WorkspaceSession 和最终 Runtime 依赖
    → 返回 HostRuntime
```

如果 Lifecycle 已成功初始化，但 Workspace 或后续依赖解析失败，外层 catch 只关闭 Document Scope，
随后释放 Plugin Provider 和 Host Provider，没有 Shutdown。

结果是插件可能收到 `InitializeAsync → Provider.Dispose`，遗漏用于停止 timer、watcher、后台工作等资源的回调。
协调器已有成功启动项列表与逆序 Shutdown 能力，可以复用，不必新增第二套插件生命周期协议。

### 2.3 必须纳入方案的相关事实

- [Program.cs](../../Host/MyAvaloniaManagement/Program.cs) 在创建 Runtime 失败后会展示独立错误应用。
  启动失败不等于进程立即退出；保留的资源可能存活到用户关闭错误窗口。
- [PluginProviderOwner.cs](../../Host/MyAvaloniaManagement/Business/Plugins/Registration/PluginProviderOwner.cs)
  当前提供统一逆序释放，不支持逐插件保留；插件还可能持有 Host 注入的窄端口。
- Host 已有 Command 和 Workflow drain 门控。V5 必须保留这些前置条件，不能以生命周期修复为由绕过它们。
- 插件组合注册 Workflow Gateway 时，可通过 `PluginServiceCommitGuard` 间接解析并创建
  `WorkflowActionRunManager`；实际创建时间可能早于组合根记录的 Catalog 或 Lifecycle 阶段。
  不能仅凭阶段尚未完成就认定该关闭参与者不存在。
- 取消回调通过后台任务执行，也可能阻塞、抛异常或继续访问资源。仅等待 Lifecycle Task 不足以覆盖
  Host 自己发起但尚未结束的取消通知。

## 3. 设计约束与建议默认策略

### 3.1 释放依据

以下事实必须分别记录，不能用同一个状态替代：

| 事实 | 含义 | 是否单独足以允许释放 |
| --- | --- | --- |
| 超时 | 超过正常等待期限 | 否 |
| 已请求取消 | 已发出协作取消请求 | 否 |
| 回调 Task 已终止 | 成功、失败或取消进入终态 | 只能证明此 Task 不再执行，不能等价为 Shutdown 成功 |
| 取消通知已结束 | Host 发起的取消回调分发已完成 | 是必要的相关条件 |
| 关闭协议完成 | 该关闭项成功停止资源使用 | 与所有在途操作、其他 drain 条件一起判断 |

超时诊断应保留：即使任务后来退出，也不能把发生过的超时改写成“从未超时”。
运行状态展示与资源释放判定分离，界面上的 `ShutdownTimedOut` 不直接决定 Provider 是否可释放。

### 3.2 建议默认：有限等待，无法确认安全时保留资源

释放硬约束与故障处理政策分别定义：

| 分类 | 条件 | V5 处理 |
| --- | --- | --- |
| 释放硬约束 | 已知 Lifecycle Task 或相关取消通知仍在运行 | 必须保留其依赖，不能释放 |
| 释放硬约束 | 关闭检查异常，无法取得可信的终止事实 | 不得假设已经排空，保留并记录检查失败 |
| 故障处理政策 | Shutdown Task 已结束，但以失败或取消结束 | 本方案采用保守保留；单独记录政策原因，不标成任务仍在运行 |

第三项不是修复在途操作提前释放的逻辑必然要求。Shutdown 可能在停止后台工作后因最后一次 Flush
失败而抛异常，也可能在停止之前失败；现有契约没有提供更细的证明。V5 为避免猜测而选择保留，
代价是可能跳过原本可以正常执行的 Dispose。后续若调整该政策，应独立评估，不改变前两项硬约束。

等待与保留流程：

1. 保持现有初始化正常等待 30 秒、Shutdown 正常等待 10 秒。
2. 超时后请求取消，增加 Host 内部可配置、测试可注入的宽限预算；建议初值 2 秒，实施时验证。
3. 退出或回滚最后进行一次统一在途检查；宽限按明确截止时间计算，不为同一任务反复重置预算。
4. 按上述分类返回明确的释放结论和原因；不能把“任务仍在运行”“回调已经失败”和“检查失败”混成同一事实。
5. V5 采用粗粒度保留：跳过本次全部 Plugin Provider 与 Host Provider 的释放。
   不新增跨对象图分析，也不只保留出问题的单个插件。

本轮保持现有委托调用线程行为，不统一包裹 `Task.Run`，不新增 UI 线程承诺。
有限等待仅针对插件委托已经返回的 Task；正常 Shutdown 等待期限为 10 秒加宽限。
委托返回 Task 前的同步前缀可能阻塞，既有同步 Dispose 也可能阻塞，这些不在本轮有界等待保证内。
整体耗时还受插件数量、既有 Command/Workflow 等待影响，不能宣称整个应用有固定退出时间上限。
同步阻塞治理另行评估，不作为本轮修复完成的前置要求。

本轮不新增强杀进程、无限等待机制或用户确认框；继续沿用现有主程序正常退出或显示启动失败窗口的流程。
保留期间不再开放插件贡献入口，也不把 Runtime 恢复为可用状态。

### 3.3 保留必须有实际所有者

不能仅依靠局部变量仍然存在，也不能假设 GC 会替代关闭协议。
当 Runtime 无法正常释放时，将需要保留的对象交给 Host 内部的进程级保留所有者：

- 保留 Host Provider、PluginProviderOwner、生命周期任务记录及其取消资源的强引用。
- 该所有者只提供保留用途，不提供服务解析能力，不形成全局 Current Provider。
- 启动失败错误窗口存活期间，仍必须维持这些引用；V5 不实现进程内重新启动或自动恢复。
- 不在迟到任务 continuation 中自动 Dispose 容器，避免绕过关闭顺序、线程约束和幂等判定。
- 保留所有者仅持有引用，不提供重试队列、自动 Shutdown、恢复或周期检查能力。
- 正式进入保留状态后，该次关闭流程不再恢复。迟到成功仅作为终态观察，不重新调用插件回调或释放容器。
- 测试使用独立保留容器或可控租约，结束假任务后按协议清理，避免测试间污染。

进程最终结束会回收进程资源，但不能保证未完成写入已经提交。诊断应如实记录“资源保留、关闭未完成”。
保留是故障退出路径的有意取舍，不是正常退出的默认资源管理方式。
关闭 Host 的新调用入口不会终止插件已经开始的后台活动；错误窗口长期存活时，任务仍可能执行写入、
网络请求或持有文件锁。保留的收益是防止提前释放依赖，不能把它解释为后台工作已停止或清理已完成。

## 4. Host 内部修复方案

### 4.1 Runner：持有真实操作及取消通知

将目前只包含 Outcome、Duration、Exception 的结果，扩展为内部可跟踪操作句柄或关联记录，至少包含：

- PluginId、Initialize/Shutdown 阶段与唯一调用身份。
- 插件委托实际返回的 Task；委托同步抛异常则记录对应失败，不改变委托的调度位置。
- 等待结果、真实终态与异常观察。
- Host 发起的取消分发任务及相关 CancellationTokenSource 所有权。
- 供协调器有界等待终止的能力。

具体类型名在实施时确定；不把这些类型放入 SDK，也不做通用任务调度框架。
操作登记不能晚于 Host 放弃等待的时点，超时、Host 取消和异常路径都必须留下可检查的记录。
CancellationTokenSource 只在相关操作和取消通知不再使用它后释放。
取消回调抛异常应进入现有诊断通道，不允许错误处理反过来丢失在途记录。
Host 取消路径与超时取消路径均须纳入验证；不能只跟踪现有后台超时取消任务，就宣称所有关联取消通知均已结束。

### 4.2 Coordinator：成功启动记录与在途记录分开

保留现有确定性启动、逆序 Shutdown 语义，增加明确的关闭汇总结果：

- `CanReleaseProviders` 或等价布尔结论。
- 未结束的插件/阶段、关闭失败项及清理异常。
- 是否仍需保留对象图。

规则：

1. 每个成功初始化项最多执行一次 Shutdown，重复退出/回滚不能重复调用。
2. 从未启动的项不调用 Shutdown。
3. 初始化明确失败的项保持当前“不调用 Shutdown”的语义；本轮不要求插件支持对失败初始化强行关闭。
4. 初始化曾超时但后来成功：仍保持贡献不可用；仅在下述有序关闭步骤能够安全纳入时调用 Shutdown。
5. 初始化仍运行：不并发 Shutdown；在该项有限等待后无法结束则标记需要保留，不延长为无限等待。
6. 某插件 Shutdown 超时或失败，不阻止其他可安全关闭项获得 Shutdown 调用；最终汇总释放资格。
7. Shutdown 在宽限内迟到成功且相关操作全部结束，可以满足释放条件，同时保留原超时诊断。
8. Shutdown 抛异常或取消时，按 3.2 的故障政策保留对象图；记录 Task 已结束，不把失败冒充成功或仍在运行。

迟到初始化按固定顺序和关闭边界处理：

- 为启动尝试保存既有确定性序号，关闭按该序号逆序遍历；不按初始化完成时间追加 `_started` 来决定顺序。
- 遍历到某项时，若初始化已成功则调用一次 Shutdown；若仍在运行，只在该项既定剩余预算内等待。
  等待中成功且相关取消通知已结束，才在当前位置纳入 Shutdown。
- 某项在预算内不能安全关闭时，记录需要保留并继续其他可安全项。已越过的项后来才初始化成功，
  不插回关闭序列，也不在末尾补调，以免破坏顺序或增加新一轮无界等待。
- 最终检查必须同时核对在途任务与待关闭责任：即使迟到任务刚好全部结束，存在未执行必要 Shutdown
  的成功初始化项时，也不能判定可以释放。
- 一旦转交保留所有者，结果即固定；后续迟到成功不恢复贡献，不自动 Shutdown，不自动释放 Provider。
  这类项记录为未完成关闭，由进程退出结束资源存活，不声称已经完成生命周期回滚。

“已成功初始化项必须执行 Shutdown”的保证适用于本次关闭顺序能够安全纳入的项。
超出该边界的项必须显式保留，不能既遗漏 Shutdown 又释放依赖。

失败初始化已经自行清理部分资源、以及成功 Shutdown 确实停止后台工作，仍属于既有插件实现责任。
Host 无法识别插件未通过返回 Task 或既有运行入口登记的私有后台任务。

### 4.3 Runtime：一个释放判定，两种入口

正常退出与启动失败回滚复用同一份生命周期安全判定和 Provider 释放策略。
不在两个 catch/finally 分支分别维护近似但不同的条件。

正常退出目标时序：

```text
关闭新的 Command / Workflow / Contribution / Workspace 入口
    → 按现有所有权关系等待 Command，释放允许释放的 Workspace / Document
    → 等待 Workflow 调用结束
    → 若前置 drain 不成功：保留仍需存活的对象图，禁止释放 Provider
    → 执行生命周期关闭并检查所有已知在途操作
    → 可以释放：Plugin Providers → Host Provider
    → 不能释放：交给保留所有者，记录关闭失败
```

V5 不重排无关 Document/View 关闭协议。已经完成的 UI 清理不回滚；保留 Provider 也不表示恢复 UI。
生命周期依现有契约管理插件级后台资源，不得依赖 Document/Tool 视觉树存活。

任何关闭阶段异常都必须汇总，不能由 `finally` 无条件释放 Provider。
同步入口如继续保留 `HostRuntime.Dispose`，内部等待不得捕获已停止的 Avalonia 消息循环，
并沿用现有同步上下文回归保护；本轮不以全面 `IAsyncDisposable` 改造为前提。

### 4.4 启动回滚：记录已获得的所有权

组合根显式持有启动阶段已获得的资源句柄及进度，而不是在 catch 中重新解析一遍 DI：

| 阶段 | 必须记录的事实 |
| --- | --- |
| Host Provider 已建立 | Host Provider 的释放责任 |
| 插件组合进行中/完成 | PluginProviderOwner 已持有的租约 |
| Registry / Catalog 已提交 | 提交进度；运行管理器是否存在仍以实际创建记录为准 |
| Lifecycle Coordinator 已获得 | 在第一次 Initialize 之前保存引用；局部成功也可回滚 |
| Workspace 已获得 | 保存实际实例，仅清理已获得的所有权 |
| Runtime 完成 | 所有权交给正常退出入口 |

阶段记录不能替代实际对象引用。为避免遗漏插件组合期间间接创建的运行管理器，应由 Host 组合基础设施
持有最小的“已创建关闭参与者”记录，并在相关 DI 工厂成功交付实例时登记明确引用：

- 直接解析和经插件端口间接解析必须落到同一登记路径；同一实例不重复登记。
- 回滚只读取已登记实例；不能为了找到它而调用 `GetRequiredService` 创建新的管理器、Workspace 或 Gate。
- 记录只覆盖本轮所需的既有 Command/Workflow 关闭参与者，不扫描 DI 容器，不做通用启动事务框架。
- 可直接调用已获得参与者，或使用只消费现有引用的内部关闭协作者；不得修改其业务协议。
- 注册工厂未成功交付的局部资源仍由工厂负责失败清理，不能当成登记成功。

具体承载类型在实施时按现有结构确定；关键是对实际创建事实有唯一、可验证的引用来源。

回滚顺序：

1. 使用已经获得的对象关闭新入口；不为了清理创建 Workspace 或重试失败的服务工厂。
2. 对已经存在的 Command/Workflow 运行执行必要的取消与 drain，遵守它们的释放前置条件。
3. 清理可安全释放的 Workspace / Document 所有权。
4. 对能够安全纳入关闭顺序的成功启动项逆序 Shutdown；迟到初始化按 4.2 的边界处理。
5. 只有统一释放判定通过才释放 Plugin Providers，再释放 Host Provider；否则明确转交保留所有者。
6. 保留原始启动异常作为启动失败主因；清理异常单独记录或附加，不能覆盖原始异常。

若阶段在对象构造中途抛异常，未交付给组合根的局部资源仍由该构造/工厂的既有失败清理负责。
不能用“没拿到 Workspace”推断所有 Document Scope 都不存在；保留 Scope Registry 兜底。

## 5. 预计改动位置

| 范围 | 位置 | 拟承担的改动 |
| --- | --- | --- |
| 核心 | `Business/Lifecycle/PluginLifecycleOperationRunner.cs` | 返回 Task、取消通知、宽限与终态跟踪；不改变调用线程 |
| 核心 | `Business/Lifecycle/PluginLifecycleCoordinator.cs` | 在途操作、有序关闭、迟到边界和释放结论 |
| 核心 | `Business/Composition/HostRuntime.cs` | 启动所有权记录、正常退出/失败回滚共用释放判定 |
| 必要配合 | Host 内部保留所有者 | 最小强引用保留，不提供服务定位或后台恢复 |
| 必要配合 | 实际关闭参与者引用、取消资源与诊断出口 | 保证间接创建对象不遗漏、取消通知可追踪、迟到诊断不访问已释放资源 |
| 按证据决定文件改动 | `PluginLifecycleStateStore.cs`、`PluginProviderOwner.cs`、组合注册、`Program.cs` | 仅在现有结构无法满足前述要求时局部调整，不预设所有文件必须改 |
| 验证 | Host Unit / Plugin / UI Tests | 可控假生命周期、回滚失败注入、顺序与释放行为回归 |

`Program.cs` 仅在诊断会话或保留所有权的实际生命周期需要衔接时调整。
特别检查迟到错误上报：不得在诊断会话已关闭后继续直接写入；通过关闭感知的内部出口处理，
或在保留前记录足够信息并停止后续写入。该要求不新增用户可见诊断系统。

## 6. 验收矩阵

测试位于 Host 仓库，使用实现现有 `IPluginLifecycle` 的假插件、可控任务和释放探针。
优先通过 TaskCompletionSource、屏障或注入时钟控制时序，不依赖生产 30/10 秒睡眠。

| 场景 | 必须证明的结果 |
| --- | --- |
| 全部正常初始化和关闭 | 确定性启动、逆序 Shutdown、随后 Plugin/Host Provider 释放 |
| Shutdown 超时后宽限内成功 | 有超时诊断；真实操作与取消通知结束前绝不释放 |
| Shutdown 返回的 Task 始终不结束 | 其他可安全项继续关闭；全部 Provider 保留；对已返回 Task 的等待有界 |
| Shutdown Task 已失败或取消 | 按故障政策保留；诊断明确已终止，与在途保留分别断言 |
| 取消回调阻塞/抛异常 | 跟踪取消任务，禁止提前释放，错误不覆盖原始结果 |
| 初始化超时仍在运行 | 不并发 Shutdown，不恢复可用性，不提前释放依赖 |
| 初始化超时，在其逆序关闭位置等待期间成功 | 按原启动序号关闭一次，不按完成先后排序 |
| 初始化在关闭位置已越过或保留转交后才成功 | 不重入关闭，不恢复可用性，不自动释放；记录未完成关闭 |
| 最终检查前迟到初始化刚好成功但未 Shutdown | 不能仅凭无在途 Task 就释放 Provider |
| Host 取消初始化等待 | 原始任务仍可追踪，已成功项可回滚 |
| 生命周期正常调用及同步抛异常 | 保持现有调度位置和失败处理；不新增同步阻塞可超时返回的承诺 |
| A/B 初始化成功，Workspace 失败 | 按 B/A 顺序 Shutdown，安全后释放 Provider |
| 部分 Lifecycle 成功后启动编排异常 | 仅关闭实际成功项，未启动项不调用 Shutdown |
| Initialize 前发生组合失败 | 不误调用生命周期，已有 Provider 正确清理 |
| 插件组合间接创建 Workflow 管理器后失败 | 实际实例进入回滚；不依赖 Catalog 完成标记，不冷解析新服务 |
| 回滚读取已创建参与者 | 同一实例关闭不重复；未创建服务的工厂调用次数不增加 |
| 回滚的 Shutdown 再次超时或失败 | 使用相同保留策略，保留原始启动异常 |
| 回滚中 UI/Scope 清理抛异常 | 不遗漏其余安全清理；不得无条件释放仍被使用的 Provider |
| Command / Workflow 未排空 | V5 不绕过既有保护，不停止它们仍依赖的生命周期 |
| 重复/并发请求关闭 | Shutdown、释放或保留转交不重复，关闭结果保持一致 |
| 启动错误窗口持续存活 | 保留对象仍可达，Host 新入口关闭；可控既有任务仍能继续，不能将保留标成已停止 |
| 迟到异常发生于诊断会话关闭后 | 不产生对已关闭诊断资源的访问或新的未观察异常 |
| SDK 与既有插件兼容 | API 基线不变，既有插件制品可加载，无外部仓库迁移 |

最重要的测试断言是释放顺序和使用期间未被释放，不能只断言 `TimedOut` 状态或日志出现。
所有可控挂起任务在测试结束时主动解除；不要为验证超时永久占用线程或污染进程保留表。

## 7. 实施阶段与验证

| 阶段 | 工作 | 完成条件 |
| --- | --- | --- |
| G0：基线与复现 | 记录实际版本、工作树、现有 Gate；增加两项缺陷的失败注入复现 | 可以观测提前释放和漏 Shutdown，不修改 SDK |
| G1：生命周期跟踪 | 返回 Task、取消通知、宽限、迟到边界与分原因释放结论 | 生命周期矩阵通过，正常调用线程行为不变 |
| G2：统一退出与回滚 | 实际创建对象记录、释放屏障、最小进程期保留、异常保真 | 两种入口均通过释放探针、间接创建与失败窗口场景 |
| G3：回归与记录 | Host 回归、SDK/API 兼容、已有制品加载和文档更新 | 有实际证据后才将本设计标为完成 |

实施时使用当前 Gate CLI，先进行相关专项测试，再执行 Host 范围门禁：

```powershell
dotnet run --project tools/MyAvaloniaManagement.Gate -- verify --scope host
```

检查实际 Gate 子项是否已覆盖 SDK/API 和既有插件制品加载；缺项补跑对应既有验证，不以修改外部插件
或发布新包作为修复前提。若最终需要解决方案级集成验证，可运行现有 `verify`，按当时实际依赖记录结果。

本轮以专项测试和完整 `verify` 为实施验收，不执行正式 `seal`。实际测试数量、覆盖率和失败处理见
`docs/plan-history/host-v5/lifecycle-ownership-repair-acceptance.md`；不预填发布资格。

## 8. 风险、取舍与回退

- 粗粒度保留会在故障路径占用更多内存/句柄，持续时间可能覆盖整个启动错误窗口。
  已有后台活动可能继续执行；保留防止提前释放，不等价于停止、隔离或完成业务收尾。
- 有界等待仅针对已经返回的 Task；同步前缀阻塞、插件自建线程和同步 Dispose 均不受其约束。
  本轮保持既有线程行为，不把同步阻塞治理扩大为实施前提，也不构成单进程插件沙箱。
- Shutdown Task 已失败后的保留是明确选择的保守政策，可能跳过正常可执行的 Dispose。
  它与“仍有 Task 使用依赖，必须保留”的硬约束分别记录、分别测试。
- 迟到初始化越过关闭位置或发生在保留之后，不自动补做 Shutdown；必须保留依赖并如实记录未完成关闭。
  为此增加后台恢复流程会扩大本轮影响范围，V5 不实施。
- 不要求失败初始化获得新 Shutdown 语义，避免隐含要求所有既有插件支持未完成初始化的关闭。
- 未改变 SDK、数据格式及插件制品，实施后的代码回退可局限于 Host 内部提交。
  回退会重新暴露本文两个缺陷，不应把回退后的状态记为问题已解决。

V5 完成的判据是：正常退出和启动失败回滚都遵守同一份资源释放条件，已知在途生命周期操作不会被
Host 提前释放依赖；能够安全纳入关闭顺序的成功初始化项执行必要 Shutdown，其余项明确保留并记录
未完成关闭；现有插件无需修改公共契约，正常调用线程行为不变。
