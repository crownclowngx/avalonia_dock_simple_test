# V21：Host 开发门禁与测试效能专用验证计划

> 用途：验证 [V21 收敛方案](host-v21-gate-and-test-efficiency-plan.md)中的夹具复制、UI 等待、测试去向和 Gate 可靠性。
> 状态：待实施配套计划，以下矩阵为验收要求，不表示用例已经补齐或 V21 已通过。日期：2026-09-21。
> 当前入口：[主仓验证](../maintenance/verification.md)。调研基线、既有计时口径与首要规定见方案，避免在本页再维护一份数字快照。

## 1. 执行边界与证据规则

1. **SOLID 优先，设计模式朴素使用，新增或实质修改的关键代码使用详细中文注释并解释设计思路。** 验证工具和测试助手也遵守同一要求。
2. **不使用 AIFLOW，不使用 Windows CI 与发布门禁。** 只运行本机专项、Gate 工具自测和完整开发 `verify`。不运行 seal、发布 Windows Smoke、发布覆盖率/重复性门禁，不部署或上传产物，不更改发布阈值与 API 分类政策。
3. RestartHarness 的本地专属进程测试属于开发回归；Headless UI 属于本机自动化，不等于原生桌面或发布 Smoke 验收。
4. 每项矩阵建立“测试全名及参数 → 关键断言 → TRX/审查证据”映射。已有测试类只是起点，不能据此直接登记通过。
5. 测试合并或迁移必须记录旧断言的新落点；失败、缺输入、零发现、跳过、超时和不完整结果均不得记为通过。
6. 不写镜像实现的测试，不用固定耗时判断逻辑成功，不用重复点击或自动重跑掩盖失败。
7. 变更 Gate 时完整运行工具自测；工具自测与 Gate 进程不得并行。主仓构建/专项保持 `-m:1`，不并行写同一输出目录。

## 2. F：进程夹具及真实启动链

既有落点：[HostRestartProcessTests](../../Host/MyAvaloniaManagement.PluginTests/HostRestartProcessTests.cs)、[PluginEnablementRestartTests](../../Host/MyAvaloniaManagement.PluginTests/PluginEnablementRestartTests.cs)、[夹具构建](../../Host/MyAvaloniaManagement.PluginTests/MyAvaloniaManagement.PluginTests.csproj)。

| 编号 | 必须验证的行为 | 关键断言与证据 |
| --- | --- | --- |
| F01 | 复制闭包完整且确实缩小 | 清单、文件数及字节前后对照；必需 apphost、DLL、deps/runtimeconfig、插件清单及当前平台 native 文件存在；不复制证据和其他平台冗余文件 |
| F02 | 两种启动形式保持 | apphost / dotnet 均实际启动，中文/空格/特殊字符参数、工作目录和数据根正确；真实加载补丁与插件身份正确 |
| F03 | 重启及插件开关保持 | 三次不同进程、禁用再启用、贡献变化、初始文档及最终布局正确；旧进程退出前不启动继任者 |
| F04 | 非正常/未授权退出无继任者 | 普通退出、取消、崩溃、强杀、错误退出和延迟退出分别覆盖；使用就绪信号，检查真实 PID 与退出结果 |
| F05 | 首屏与启动异常保持 | 空插件、警告、禁用、失败、慢加载、启动取消和加载中取消分别有结果；首帧响应与后台加载顺序保持 |
| F06 | 目录、注入与进程隔离 | 两个夹具的可写文件互不影响；故障注入不修改源输出；只清理拥有的 PID 和经绝对路径核对的临时根 |
| F07 | 旧文件及必要依赖缺失可观察 | 复用构建输出时退出清单的旧依赖不残留；受控副本缺少必需文件时明确失败，不靠全局缓存/安装目录补齐 |
| F08 | 锁交接与清理完整 | 布局锁随原进程退出释放，继任者按既有协议取得锁；失败证据在清理前归档，不遗留专属存活进程 |

缺依赖负向测试优先在复制清单检查或隔离夹具层完成，只对需要验证进程加载行为的代表场景启动子进程，避免再制造一组高成本重复启动。文件列表和实际加载验证要互补，单纯比较文件数量不算依赖闭包验证。

## 3. U：Headless 交互、布局和像素

既有落点：DocumentWindowV16UiTests、ToolCenterUiTests、AutoHideRestoreVisualTests、DockToolWindowCloseUiTests、DockCrossWindowLayoutUiTests、AvaloniaLayoutOwnershipUiTests、HelpWindowTests、IconRenderingUiTests。

| 编号 | 必须验证的行为 | 关键断言与证据 |
| --- | --- | --- |
| U01 | 等待明确完成或明确超时 | 已满足条件立即完成，尚未满足时等待目标信号；条件不成立时有界失败并说明场景，不无限 RunJobs |
| U02 | 真实输入只发送一次 | 按钮/键盘/关闭动作在等待前后只有一次；慢完成不造成二次命令或重复确认 |
| U03 | 入口接线与决策分工正确 | 菜单、标题按钮、命令、原生关闭各自需要的事件链保留；保存/放弃/取消/失败矩阵有明确层级与新落点 |
| U04 | 取消与竞争不能被等待掩盖 | 关闭否决保留窗口/内容/活动项，重试可行；保存中修改、任务排空与退出竞争仍有显式就绪/释放断言 |
| U05 | 四向自动收起、最后工具与恢复 | 每个有差异的方向/状态均验证尺寸、可见性、位置和重开；Tool 模型/View 复用，Document 正常释放 |
| U06 | 跨窗布局所有权 | 旧布局队列、单一视觉父级、跨窗迁移、回停及关闭不回归；不得改为只检查模型树 |
| U07 | 多轮生命周期回归 | 集中的寿命用例捕获重复订阅、二次释放及窗口残留；在夹具最终清理之前断言，减少轮次有逐项理由 |
| U08 | 像素断言与截图导出独立 | 图标填充/颜色/裁剪断言不受截图开关影响；只有保存 PNG 的用例不登记为像素比较通过 |
| U09 | 证据目录与运行身份一致 | 不同 run/套件/场景不覆盖文件；Gate 和独立 dotnet test 都能定位输出，无导出目录时行为断言仍运行 |

统一助手的关键行为用少量受控条件直接验证；业务交互继续使用现有真实窗口测试证明，不另建一套重复覆盖每个助手调用位置的测试。保持 UI Dispatcher 与现有集合串行约束，不共享可变 Window/Workspace 来节省初始化。

## 4. G：Gate 工具单测与开发链

既有落点：[Gate 工具测试](../../tools/MyAvaloniaManagement.Gate.Tests/MyAvaloniaManagement.Gate.Tests.csproj)、[DLL 身份检查](../../tools/MyAvaloniaManagement.Gate/DockPatchIdentity.cs)、[TRX reader](../../tools/MyAvaloniaManagement.Gate/TestEvidenceReader.cs)。

| 编号 | 必须验证的行为 | 关键断言与证据 |
| --- | --- | --- |
| G01 | 开发阶段完整且顺序正确 | 补丁先于 restore，构建后查 DLL/契约，契约先于重测试，真实包先于验收；没有 coverage/windows-smoke/发布动作 |
| G02 | 任一前置失败立即停止 | 补丁、构建、DLL、契约、测试、打包或包验收失败后不执行依赖阶段；保留失败阶段、已执行结果及未执行状态 |
| G03 | 参数和单仓证据兼容 | 默认 verify 完整；host/all 兼容输入的既有含义保持，非法/退役参数明确拒绝；sources.main 和旧 summary 语义保持 |
| G04 | 必需 DLL 不允许缺失 | 正确/缺失/旧哈希参数输入直接测试，并覆盖调用者生成的必需路径；无该依赖项目按声明排除，不靠文件存在性过滤 |
| G05 | 有效 TRX 可读取 | 现有 v2/v3 测试项目产生的真实结构都能读取；通过数量、执行结果和明细一致，命令与 TRX 结果共同判断 |
| G06 | 损坏/不完整 TRX 拒绝 | 缺文件、坏 XML、缺失/重复关键节点、缺失/非数字/负数计数、零测试、计数不一致和缺结果明细分别覆盖 |
| G07 | 非成功执行不可伪装通过 | failed、notExecuted、error、timeout、aborted、未完成/非成功运行结果及异常退出对应受控输入；按实际 VSTest 字段语义校验 |
| G08 | 包验收边界保持 | 缺真实 ZIP 输入、零项、失败/跳过、多于一项均拒绝；常规 Plugin 专项仍排除 PackageAcceptance |
| G09 | 套件汇总与来源一致 | 一次解析的结果同时驱动判定与报告；计数、路径、TRX 摘要、耗时和慢用例正确；失败/未执行不被填成通过 |
| G10 | 既有证据与发布边界兼容 | 旧 schema 2 必需字段/含义保持；新增证据可定位；共享逻辑的发布边界只用纯函数/模拟阶段自测及差异审查，不启动发布进程 |

计数的具体一致性规则先以本仓实际 VSTest 输出核对，不能将辅助计数要求机械套到不同适配器。小型负向 TRX 可为测试构造，但有效样本必须有真实工具输出依据。G02 使用可控动作记录阶段执行，不为证明失败停止而反复编译整个 Host。

## 5. A/D：架构、单测去向与文档

| 编号 | 审查内容 | 必须留下的结果 |
| --- | --- | --- |
| A01 | SOLID 与依赖方向 | 夹具、等待、编排、解析和判定职责清楚；无万能上下文、第二容器或生产测试入口 |
| A02 | 详细中文注释 | 依赖筛选、隔离、等待、失败、计时口径与设计理由和实现一致 |
| A03 | 测试去向完整 | 旧方法及参数到新断言逐项映射；不同关闭否决、方向或跨窗条件不被误当重复 |
| A04 | 活动源码扫描 | 受控有效项目违规会失败，artifacts/bin/obj/临时上游输入不污染结果；新增活动项目仍纳入检查 |
| A05 | 不变量保持 | SDK/API、数据格式、补丁、包验收、发布阈值和生产业务行为未被提速改动 |
| D01 | 当前说明和状态同步 | 方案、专用文档、开发入口与导航互链；待实施不写成已完成，源码和记录身份分开 |
| D02 | 相对链接及固定入口 | 检查本次新增和修改 Markdown 的本仓目标及锚点；八个帮助章节、理论与架构固定入口保持 |
| D03 | 嵌入帮助一致 | 定稿重建后，新方案/专用文档及修改导航逐份与源码字节相同，可读取并渲染；既有帮助专项通过 |

文档只有编写变更时执行链接、空白、重建与帮助专项即可，不据此声称 V21 代码矩阵通过。实施完成后的完整验收则包含以下专项与最终 verify。

## 6. 本机执行顺序

以下命令在仓库根目录执行。仅在相应阶段实际实施后运行；过滤器使用当前已有类名，测试改名后同步调整并核对发现数。每条命令失败即停止，先保留原结果再修复，不能将多次运行拼接成一次通过。

### 6.1 初始化输入与构建

```powershell
git status --short
git rev-parse HEAD
dotnet --version
$v21Results = Join-Path (Get-Location) ('artifacts/host-v21/' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $v21Results -Force | Out-Null

pwsh -NoProfile -File tools/Build-AvaloniaLayoutPatch.ps1
if ($LASTEXITCODE -ne 0) { throw '布局补丁准备失败' }
pwsh -NoProfile -File tools/Build-DockAreaFillPackage.ps1
if ($LASTEXITCODE -ne 0) { throw 'Dock 补丁准备失败' }
dotnet restore MyAvaloniaManagement.sln --locked-mode --nologo
if ($LASTEXITCODE -ne 0) { throw '锁定依赖还原失败' }
dotnet build MyAvaloniaManagement.sln -c Release --no-restore -m:1 -warnaserror --nologo
if ($LASTEXITCODE -ne 0) { throw 'Release 构建失败' }
```

记录未提交差异及实际输入身份；有改动时不能只记录 HEAD。若夹具引入独立 RID 构建，实施记录追加准确的独立还原/构建命令，不能沿用旧输出执行 `--no-build`。

### 6.2 按阶段运行必要专项

```powershell
# P1：进程夹具、启动与重启交接。输出保留实际计数和耗时。
dotnet test Host/MyAvaloniaManagement.PluginTests -c Release --no-build --no-restore -m:1 --filter '(FullyQualifiedName~HostRestartProcessTests|FullyQualifiedName~PluginEnablementRestartTests|FullyQualifiedName~StartupPluginProgressTests|FullyQualifiedName~HostRestartLifecycleTests)&Category!=PackageAcceptance' --logger 'trx;LogFileName=p1-process.trx' --results-directory "$v21Results/p1-process"
if ($LASTEXITCODE -ne 0) { throw 'P1 进程专项失败' }

# P2：UI 夹具为共享设施，修改后运行完整本仓 UI 集合，避免过滤遗漏调用者。
dotnet test Host/MyAvaloniaManagement.UiTests -c Release --no-build --no-restore -m:1 --logger 'trx;LogFileName=p2-ui.trx' --results-directory "$v21Results/p2-ui"
if ($LASTEXITCODE -ne 0) { throw 'P2 UI 专项失败' }

# P3：工具自测独立构建，确保没有 Gate 进程仍在占用或使用它。
dotnet test tools/MyAvaloniaManagement.Gate.Tests -c Release -m:1 -warnaserror --logger 'trx;LogFileName=p3-gate.trx' --results-directory "$v21Results/p3-gate"
if ($LASTEXITCODE -ne 0) { throw 'P3 Gate 工具自测失败' }

# P4：边界单测调整后运行完整 SDK / Host Unit，避免改名遗漏过滤器。
dotnet test Host/MyAvaloniaManagement.PluginSdk.Tests -c Release --no-build --no-restore -m:1 --logger 'trx;LogFileName=p4-sdk.trx' --results-directory "$v21Results/p4-sdk"
if ($LASTEXITCODE -ne 0) { throw 'P4 SDK 单测失败' }
dotnet test Host/MyAvaloniaManagement.Tests -c Release --no-build --no-restore -m:1 --logger 'trx;LogFileName=p4-unit.trx' --results-directory "$v21Results/p4-unit"
if ($LASTEXITCODE -ne 0) { throw 'P4 Host 单测失败' }
```

各阶段修改后先重新构建对应输入再运行 `--no-build`；上方命令不是允许在 P1–P4 之间修改源码后继续复用旧 DLL。新增夹具清单测试落在其他项目时补跑实际项目，并写入映射。

### 6.3 文档定稿与最终开发验收

```powershell
git diff --check
if ($LASTEXITCODE -ne 0) { throw '差异空白检查失败' }

# Markdown 嵌入 Host，先构建帮助专项以更新实际资源。
dotnet test Host/MyAvaloniaManagement.Tests -c Release --no-restore -m:1 -warnaserror --filter FullyQualifiedName~HelpContentTests --logger 'trx;LogFileName=docs-unit.trx' --results-directory "$v21Results/docs-unit"
if ($LASTEXITCODE -ne 0) { throw '帮助内容专项失败' }
dotnet test Host/MyAvaloniaManagement.UiTests -c Release --no-restore -m:1 -warnaserror --filter FullyQualifiedName~HelpWindowTests --logger 'trx;LogFileName=docs-ui.trx' --results-directory "$v21Results/docs-ui"
if ($LASTEXITCODE -ne 0) { throw '帮助窗口专项失败' }

dotnet run --project tools/MyAvaloniaManagement.Gate -- verify
if ($LASTEXITCODE -ne 0) { throw '最终完整开发 verify 失败' }
```

另核对本次新文档及修改导航的嵌入原文与实际渲染，现有 HelpContentTests 不自动证明所有新增页面已逐份验证。链接检查须包含未跟踪的新文件，不能只检查 git diff 中的已有文件。上方最终完整 verify 是代码实施完成的验收入口，不作为本次仅编写文档的隐含实施动作。

## 7. 性能比较与完成标准

性能样本针对 P1 和实际受影响 UI 热点，实施前后分别固定命令、过滤器、SDK、平台、缓存条件及后台负载。每侧三次有效样本，记录全部值、中位数、计数和失败，不仅报告最快一次。

证据分别包含：夹具文件/字节和准备时间、进程启动/场景/清理时间、UI 热点时间、套件 TRX 时间、命令总耗时及最终 Gate 总耗时。不同输入的历史 run 只作参考，不直接作为性能基准；不加百分比、绝对秒数或覆盖率提速门槛。

完成记录须包含：

- 实际源码身份、命令、环境、run-id、阶段及套件计数、TRX/相关产物路径与摘要。
- F/U/G/A/D 矩阵的逐项结果、测试去向、故障与修复后重跑记录。
- 优化前后复制量及时间，保留未改善项和测量限制；没有证据的收益不宣称完成。
- 文档与嵌入资源一致性、最终完整 verify 结果，以及工具自测独立结果。
- 未执行范围：AIFLOW、Windows CI、发布门禁、部署和公开发布；本轮没有新增人工实机结论时如实说明。

如发现必须改变生产行为才能继续的缺陷，先记录具体触发、风险和独立处理范围，不删除保护该行为的断言，也不降低门禁绕过。未完成的必要矩阵明确保留；只有全部必要开发要求具备实际证据后，才将 V21 标为已实施并归档。
