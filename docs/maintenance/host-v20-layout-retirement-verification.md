# V20：历史布局退役与现行入口收敛专用开发验证

> 用途：验证 Layout V2 完整退役、现行 Dock 行为测试迁移、验证工具适配、旧查询删除及文档一致性。
> 状态：当前可复用开发验证指南；实际结果见[开发记录](../archive/records/host-v20/development-acceptance.md)与[逐方法映射](../archive/records/host-v20/test-matrix.md)。日期：2026-09-21。
> 设计、删除范围与阶段顺序见 [V20 方案](../archive/plans/host-v20-layout-retirement-plan.md)。本页属于 maintenance；实际结果另存有日期的开发记录。

## 1. 执行边界

SOLID 是首要规定，设计模式朴素使用，详细中文注释与设计理由必须审查。本轮目标是减少历史运行路径，同时保留现行业务行为、资源所有权和失败处理。

**不使用 AIFLOW、Windows CI 或发布门禁。** 不执行 `seal`、发布 Windows Smoke、发布覆盖率和发布重复性检查；不改 CI，不部署安装目录，不上传包或创建发布标签。开发打包与真实插件 ZIP 本地验收由既有 `verify` 负责，不等于公开发布。

可执行范围是本机 Unit、Plugin、Headless UI、Gate 工具自测、仅操作临时目录及专属子进程的 Writer Lease 专项，以及完整开发 `verify`。Gate 的发布产物检查可修改并通过共享检查方法的单测验证，不能以测试名义启动发布流程。

`verify` 的执行范围以[开发门禁说明](verification.md)与 [当前 Gate 顺序计划](../../tools/MyAvaloniaManagement.Gate/GateExecutionPlan.cs) 为准：固定补丁准备、locked restore、Release 零警告构建、契约及已发布 API 比较、SDK/Host/Plugin/Headless/MyPlugTest 测试、MyPlugTest 打包与真实 ZIP 验收。它不执行发布 Windows Smoke，也不自动证明 Gate 新增检查的覆盖。

## 2. 基线与测试迁移纪律

1. P0 记录实施时的 HEAD、工作树差异、SDK 和完整 `verify` 结果。V19 记录只供对照，不用历史通过代替新输入验证。
2. 每个受影响测试按具体方法、参数和断言分类，不仅按文件名处理。纯 V2 格式/转换测试可以删除；现行 Dock 行为必须映射到 V3 用例或已存在的等效断言。
3. 等价迁移先在现行 V3 实现上取得结果，再删除旧生产依赖。新增“不再导入 V2”是有意行为变化，应记录旧/新行为，不登记为意外缺陷。
4. 不在测试中重建 V2 Store、Mapper、writer 或旧分类 Dictionary，不复制生产条件作为预期结果。
5. 并发测试使用就绪信号、TaskCompletionSource 或显式闸门及有界等待；UI 使用真实 Headless Dispatcher。不用任意 Sleep 假设异步顺序。
6. 引用身份、创建/释放次数、通知次数及顺序在上下文最终清理之前断言，避免退出兜底掩盖问题。
7. 零发现、未执行、缺输入、失败、超时和跳过不得登记为通过。必需矩阵无有效断言时保持未完成。
8. 测试总数可以因退役下降；不得为保留 V19 总数增加无意义测试，也不得借减少测试删除现行覆盖。

逐项映射已建立在[测试去向表](../archive/records/host-v20/test-matrix.md)，包括原方法及参数、有效断言、删除理由、新落点与实际运行证据。

## 3. L：布局文件和退役边界

既有落点：[DockLayoutV3StoreTests](../../Host/MyAvaloniaManagement.Tests/DockLayoutV3StoreTests.cs)、[DockLayoutV3Tests](../../Host/MyAvaloniaManagement.Tests/DockLayoutV3Tests.cs)、[DockLayoutV3BoundaryTests](../../Host/MyAvaloniaManagement.Tests/DockLayoutV3BoundaryTests.cs)、[DockLayoutSaveQueueTests](../../Host/MyAvaloniaManagement.Tests/DockLayoutSaveQueueTests.cs)。既有落点不表示所有新增断言已经具备。

| 编号 | 必须验证的行为 | 关键断言 |
| --- | --- | --- |
| L01 | 仅有旧 V2 文件时建立默认布局，后续只生成 V3 | 使用与默认显隐/位置明显不同的原始旧文件；经真实 Store、Workspace/Lifecycle 验证默认状态和 schema 3；旧文件哈希不变，无转换产物 |
| L02 | 损坏或不可读的旧布局不参与运行 | 原始旧文件有效/损坏及可控独占句柄场景；不出现旧布局诊断或隔离副本，当前 V3 保存仍按自身写入权工作；源码审查补充证明未探测旧路径 |
| L03 | 有效 V3 主文件优先，主文件无效时使用有效 V3 备份 | 恢复预设的工具状态，坏 V3 原件及隔离证据遵守原政策，不受旁边旧文件内容影响 |
| L04 | 没有有效 V3 时回退默认布局 | 无文件、双坏、仅剩坏文件历史分别覆盖；不再从 V2 导入，坏 V3 保留规则仍成立 |
| L05 | 未来 schema 保持只读且不降级 | 主文件和备份参数分别覆盖；不以旧备份/默认写入覆盖未来语义，不隔离未来文件 |
| L06 | V3 严格字段、类型、重复/未知字段及大小/深度限制保持 | 现有稳定错误码和结构拒绝行为保持，迁出的异常仍由当前 V3 路径返回 |
| L07 | V3 原子提交和上一有效备份保持 | 更新前后主文件/备份内容正确，失败保留有效内容，不残留事务临时文件 |
| L08 | V3 的 I/O 不确定性仍使本会话只读 | 被占用或无访问权的当前 V3 不被覆盖；不把忽略旧文件扩大为忽略当前格式 I/O 错误 |
| L09 | 保存队列合并、重试、冻结和最终排空保持 | 只保存已提交快照，失败可重试，取消退出能恢复捕获，Store 释放晚于队列结束 |
| L10 | Layout 之外的 v2 身份不变 | 默认数据根、manifest、Document Envelope、SDK 基线及测试插件版本有原断言或差异审查；不能按 V2 文本批删 |

L01/L02 是对已退出输入的忽略测试，允许出现字面量 `layout-v2.json`，不要求测试源码零 V2 文本。不能仅用 `Load() == null` 证明最终采用默认布局，也不能以旧文件未改写单独证明没有读取它。

## 4. B：仍有效的 Dock 行为

迁移重点：[DockFourWayLayoutTests](../../Host/MyAvaloniaManagement.PluginTests/DockFourWayLayoutTests.cs)、[DockLayoutAvailabilityTests](../../Host/MyAvaloniaManagement.PluginTests/DockLayoutAvailabilityTests.cs)、[AutoHideRestoreVisualTests](../../Host/MyAvaloniaManagement.UiTests/AutoHideRestoreVisualTests.cs)、[ToolCenterUiTests](../../Host/MyAvaloniaManagement.UiTests/ToolCenterUiTests.cs)。现行保护还包括 [DockLayoutWorkspaceStateTests](../../Host/MyAvaloniaManagement.Tests/DockLayoutWorkspaceStateTests.cs)、[DockLayoutV3UiTests](../../Host/MyAvaloniaManagement.UiTests/DockLayoutV3UiTests.cs) 与 [DockToolWindowCloseUiTests](../../Host/MyAvaloniaManagement.UiTests/DockToolWindowCloseUiTests.cs)。

| 编号 | 必须验证的行为 | 关键断言 |
| --- | --- | --- |
| B01 | 四向位置、比例、组内顺序和拆分拓扑在 V3 捕获/恢复后成立 | 从真实 Workspace 捕获并应用 V3；比较业务位置及合法拓扑，不要求 V3 复刻旧 V2 固定树形 |
| B02 | 隐藏、自动收起、预览展开与普通停靠的区别保持 | 隐藏不误当固定，固定顺序、恢复尺寸及视图可见状态正确；现行 UI 断言继续执行 |
| B03 | 原组消失、最后工具隐藏及浮窗恢复遵循当前规则 | 同会话操作复用原 Tool/模型/View；新会话只创建自身实例，不把跨重启引用相同当要求 |
| B04 | 缺失/不可用插件不产生无效实例，当前恢复记录保持 | 按 V3 可用项投影处理；不迁入旧 V2 的“任意未知工具拒绝整个快照”政策 |
| B05 | 原生关闭拒绝及布局应用失败保留原对象图 | 取消后窗口、活动项、工具和文档实例保持；重试成功，无重复释放 |
| B06 | 应用布局不重开文档，退出保存/取消/重启保持 | Document Scope 不因布局重建，最终保存失败阻止退出；取消后入口恢复 |

只保护布局格式的旧测试与复用旧格式的行为测试必须分开。迁移后没有某一旧错误码，不等于行为覆盖丢失；应说明该错误码所属政策是否已经退出。

## 5. W/G：Writer Lease 和 Gate 工具

来源：[Writer Lease 脚本](../../tools/Verify-LayoutV3WriterLease.ps1)、[GateChecks](../../tools/MyAvaloniaManagement.Gate/GateChecks.cs)、[Gate 工具测试项目](../../tools/MyAvaloniaManagement.Gate.Tests/MyAvaloniaManagement.Gate.Tests.csproj)。

| 编号 | 必须验证的行为 | 关键断言 |
| --- | --- | --- |
| W01 | 同一数据根只有一个实际跨进程写入者 | 专属子进程取得锁后再建只读 Store；用就绪信号证明先后，不靠启动延时 |
| W02 | 不同数据根互不阻塞，旧只读实例不自动接管 | 第二根可以写；原持锁进程结束后，旧只读会话仍只读 |
| W03 | 原持锁进程实际退出后，新实例可取得写锁并使用 V3 | 用 V3 输入或输出验证读写；不再要求新实例导入 V2；验证输出 schema 3 和程序集身份 |
| W04 | 脚本成功/异常均回收专属资源 | 只终止自己创建的子进程，释放句柄；递归清理前验证绝对路径位于本次临时根，无用户数据写入 |
| G01 | 发布入口引用的产物检查接受有效 V3 | 工具单测调用与发布入口相同的检查实现；临时 `layout-v3.json` / schema 3 通过 |
| G02 | 缺文件、损坏 JSON、错误 schema 和仅有旧文件被拒绝 | 不把缺文件或错误产物当成功，受影响错误消息不再要求 V2；各失败有直接断言 |
| G03 | 开发与发布执行边界保持 | Gate 自测/审查确认 `verify` 不启动 Windows Smoke、覆盖率或 seal；现有发布政策不降低 |
| G04 | 工具结果与发布资格分别记录 | 脚本、自测及本地 V3 文件往返有实际证据；发布 Windows Smoke 明确为本轮未执行 |

G01/G02 由 `LayoutArtifactTests` 直接调用真实发布入口复用的 `AssertWindowsSmokeLayoutArtifact` 验证。新增 15 组用例覆盖有效产物、缺失/仅旧文件、损坏 JSON、错误字段类型/版本及新产物目录出现旧文件；不启动真实发布进程。

本轮不运行发布 Windows Smoke，不添加绕过 seal 的发布命令。Headless 生命周期、真实临时文件和 Store 往返验证开发行为；原生发布进程启动及退出证据留到实际发布阶段。

## 6. Q：目录查询入口

既有落点：[ServiceAndModelTests](../../Host/MyAvaloniaManagement.Tests/ServiceAndModelTests.cs)、[PluginNavigationTests](../../Host/MyAvaloniaManagement.Tests/PluginNavigationTests.cs)、[CognitiveUxV8Tests](../../Host/MyAvaloniaManagement.Tests/CognitiveUxV8Tests.cs)、[CommandPaletteV18Tests](../../Host/MyAvaloniaManagement.Tests/CommandPaletteV18Tests.cs) 与 [PluginNavigationUiTests](../../Host/MyAvaloniaManagement.UiTests/PluginNavigationUiTests.cs)。

| 编号 | 必须验证的行为 | 关键断言 |
| --- | --- | --- |
| Q01 | 原分类测试通过当前目录表达 | 直接检查 Categories、EntryCount 和分类存在性；不在测试内重新 GroupBy 成旧 Dictionary |
| Q02 | 多 Intent 保持精确身份与声明顺序 | 从 Items 定位同一 DocumentTypeId，检查 quick-url/personal-source 等测试输入的 CreationIntentId 和顺序 |
| Q03 | 完整目录与窄查询的现行边界保持 | `ReadDirectory` 构建展示，`HasCreationEntry` 按身份即时判断；不创建业务对象、写布局或增加长期缓存；旧方法无生产/测试调用 |

## 7. A/D：架构、文档与注释

| 编号 | 审查内容 | 必须留下的结果 |
| --- | --- | --- |
| A01 | V2 删除范围与共享职责 | 退役类型/导入分支不存在；异常已归位，偏好仍需的 RetiredHostToolIds 保留；非 Layout v2 未改 |
| A02 | 所有权与依赖方向 | Session、Store、Queue、Scope 和 Provider 所有者保持；未引入新迁移/清理框架、Service Locator 或宽接口 |
| A03 | 中文注释和设计理由 | Store 回退/只读、异常、工具进程、Gate 检查、测试目的等解释与实际代码一致，无过期 V2 所有权说明 |
| D01 | 当前契约与架构一致 | Layout 主链、V20 状态、V19 交付事实准确；仅 V3 参与恢复，旧输入忽略与有意兼容变化准确区分 |
| D02 | 历史原文与帮助入口 | 旧 reference 归档后当前链接可用，历史证据事实保持；八章节、理论原文和架构入口保持 |
| D03 | 专用文档与全部变更链接 | 检查新增/修改 Markdown 的本仓文件及锚点；新方案和验证页可从实际嵌入资源读取并渲染 |

结构审查使用一次性引用检索和差异核对，不为类型名、文件数量或行数编写永久镜像测试。删除类型名仍可出现在方案、历史记录和忽略边界测试中，不能把“全仓零命中”当完成标准。

## 8. 实施阶段的本机命令

以下是可复用命令模板；本次实际时间、输出和退出码见开发记录。各命令串行执行，每条检查退出码；失败时停止进入下一阶段并保留日志。P0 完整开发 `verify` 先准备当前补丁、locked restore 和 Release 输入：

```powershell
dotnet run --project tools/MyAvaloniaManagement.Gate -- verify

$v20RunId = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$v20Results = "artifacts/host-v20/$v20RunId"
```

完成本轮 restore 后，专项可以使用 `--no-restore`；不使用 `--no-build`，避免复用旧 DLL 或旧嵌入 Markdown。共享输出目录保留 `-m:1`，测试按实际阶段选择，不为形式重复已通过且未再受影响的组。

```powershell
# P1：V3 文件、树、保存队列、数据根和当前所有权。
dotnet test Host/MyAvaloniaManagement.Tests -c Release --no-restore -m:1 -warnaserror --filter 'FullyQualifiedName~DockLayout|FullyQualifiedName~HostDataRootPolicyTests|FullyQualifiedName~PluginStatusMigrationTests|FullyQualifiedName~WorkspaceSessionAndDockFactoryTests|FullyQualifiedName~ToolCenterTests' --logger 'trx;LogFileName=layout-unit.trx' --results-directory "$v20Results/layout-unit"

# P1：迁移后的真实插件与布局行为；常规专项排除需要 ZIP 输入的验收项。
dotnet test Host/MyAvaloniaManagement.PluginTests -c Release --no-restore -m:1 -warnaserror --filter '(FullyQualifiedName~DockFourWayLayoutTests|FullyQualifiedName~DockLayoutAvailabilityTests|FullyQualifiedName~VersionPolicyTests|FullyQualifiedName~HostLifecycleOwnershipTests|FullyQualifiedName~DocumentScopeManagerTests)&Category!=PackageAcceptance' --logger 'trx;LogFileName=layout-plugin.trx' --results-directory "$v20Results/layout-plugin"

# P1：真实 Headless 窗口、工具恢复、关闭否决、文档和重启边界。
dotnet test Host/MyAvaloniaManagement.UiTests -c Release --no-restore -m:1 -warnaserror --filter 'FullyQualifiedName~DockLayoutV3UiTests|FullyQualifiedName~AutoHideRestoreVisualTests|FullyQualifiedName~DockToolWindowCloseUiTests|FullyQualifiedName~DockToolSplitUiTests|FullyQualifiedName~ToolCenterUiTests|FullyQualifiedName~DocumentWindowV16UiTests|FullyQualifiedName~HostRestartUiTests' --logger 'trx;LogFileName=layout-ui.trx' --results-directory "$v20Results/layout-ui"

# P2：工具自测；不得与正在运行的 Gate 共用构建输出。
dotnet test tools/MyAvaloniaManagement.Gate.Tests -c Release -m:1 -warnaserror --logger 'trx;LogFileName=gate-unit.trx' --results-directory "$v20Results/gate-unit"

# P2：脚本已适配 V3 后执行；需要 .NET 10 的 PowerShell 7.6 或更新版本。
pwsh -NoProfile -File tools/Verify-LayoutV3WriterLease.ps1 -AssemblyPath Host/MyAvaloniaManagement/bin/Release/net10.0/MyAvaloniaManagement.dll

# P3：现行目录与即时查询。
dotnet test Host/MyAvaloniaManagement.Tests -c Release --no-restore -m:1 -warnaserror --filter 'FullyQualifiedName~ServiceAndModelTests|FullyQualifiedName~PluginNavigationTests|FullyQualifiedName~CognitiveUxV8Tests|FullyQualifiedName~CommandPaletteV18' --logger 'trx;LogFileName=query-unit.trx' --results-directory "$v20Results/query-unit"
dotnet test Host/MyAvaloniaManagement.UiTests -c Release --no-restore -m:1 -warnaserror --filter 'FullyQualifiedName~PluginNavigationUiTests|FullyQualifiedName~CommandPaletteV18UiTests|FullyQualifiedName~CognitiveUxV8UiTests' --logger 'trx;LogFileName=query-ui.trx' --results-directory "$v20Results/query-ui"
```

新增或迁移到其他类的必需用例须同步过滤器，并从 TRX 确认对应方法和参数确实执行。Writer Lease 保存实际输出、退出码与被测程序集哈希，不使用旧脚本通过结果代替适配后验证。

## 9. 文档交付与实施最终验证分别执行

### 9.1 文档与嵌入帮助

运行 HelpContentTests 与 HelpWindowTests；另用实际 HelpContentCatalog/HelpMarkdownRenderer 检查所有变更文档与嵌入原文一致、归档 V2 与 V20 导航可定位、Markdown 可渲染。本仓文件链接和 GitHub 锚点做全仓一次性检查；原有帮助渲染器对中文锚点的差异单列，不能误报为本轮新增断链。

### 9.2 实施后的最终输入

代码、测试、工具及嵌入 Markdown 定稿后，执行完整 `verify`；专项不能替代它。Gate 工具自测和 Writer Lease 仍单独留证。

```powershell
git diff --check
dotnet run --project tools/MyAvaloniaManagement.Gate -- verify
```

校对实际发现/执行数、失败与跳过、包验收输入、Gate 阶段结果及逐项矩阵。SDK/API 比较保持既有流程，不能因为退出 Layout V2 而改动其他 schema 或清空 Unshipped。

若最终验证后修改嵌入 Markdown，需重新取得对应最终输入的验证。完整结果优先写入非嵌入 JSON，避免为了回填输出再次改变已经验证的嵌入资源。

## 10. 证据及完成条件

实施记录保存至方案约定的 `docs/archive/records/host-v20/`；文件存在后再增加链接。记录实际源码及差异身份、时间、完整命令、退出码、TRX、逐项测试迁移、工具输出、Gate run-id、失败与重跑及未执行范围。不预填新测试数量或成功状态。

V20 开发完成要求 L/B/W/G/Q/A/D 每项有真实覆盖或审查依据，必需测试无失败且无跳过，最终完整 `verify` 通过。文件删除和测试数量下降必须能由逐项去向解释，当前行为的有效断言不能丢失。

发布 Windows Smoke、发布覆盖率、Windows CI、seal、部署和公开发布均记录为本轮未执行；开发验证通过不表示已经取得发布资格。后续 Workflow 调查和 Manifest 整理另设计划与验证，不预填到 V20 完成结果中。
