# V23 开发测试与矩阵落点

> 日期：2026-09-24。对照[专用回归矩阵](../../../maintenance/host-v23-local-zip-installation-verification.md)。本文区分自动化断言、代码审查和未执行场景；实际测试参数、数量及最终结果见[开发证据](development-evidence.json)，要求本身不等于通过记录。

## 测试命名与有效边界

本轮新增测试由原 Gate 测试组自动收集，不使用 `Category=PackageAcceptance`。实际方法和参数可从 JSON 的专项/最终 TRX 获取；这里使用下列真实类名缩写，均位于对应测试项目根目录：

| 缩写 | 项目与完整类名 | 主要断言 |
| --- | --- | --- |
| Package | PluginTests / `MyAvaloniaManagement.PluginTests.PluginPackageInstallationTests` | 实际 ZIP 与清单，不产生 InstallationProbe 程序集/ALC |
| Boundary | PluginTests / `MyAvaloniaManagement.PluginTests.PluginInstallationBoundaryTests` | 真实流限额、元数据、提交竞争、锁文件和 Junction |
| Transaction | PluginTests / `MyAvaloniaManagement.PluginTests.PluginInstallationTransactionTests` | 真实目录移动、逐提交点故障、完整旧目录与登记恢复 |
| Process | PluginTests / `MyAvaloniaManagement.PluginTests.PluginInstallationProcessTests` | 真实 Program、加载、生命周期、Headless Shell 与重启助手 |
| Planner | Tests / `MyAvaloniaManagement.Tests.PluginInstallPlannerTests` | 无 IO 的版本和身份规则 |
| Window | UiTests / `MyAvaloniaManagement.UiTests.PluginInstallationWindowTests` | 窄安装端口替身与真实看板绑定；不替代文件/进程验证 |
| Help | Tests / `MyAvaloniaManagement.Tests.HelpContentTests` | V23 当前/归档 Markdown 实际嵌入、读取与渲染 |

## P / V：包与版本

| 矩阵 | 真实测试落点与断言 | 限制 |
| --- | --- | --- |
| P01、P02、P13、P14 | Package `旧协议两种版本有无配套清单均可检查且不执行插件`；Boundary `私有图标例外和可选构建信息保留` | 是协议夹具，非外部正式 ZIP；未以相同 SDK 字节声称验证所有业务依赖 |
| P03、P04、P05 | Package `错误配套清单不得静默降为仅ZIP`；Boundary `配套文件集合和每个摘要均严格核对`、`层级清单大小及坏格式无法绕过预检` | 覆盖字段/集合/格式，实际权限拒绝单列人工项 |
| P06、P15 | Package `改名ZIP可显式选择原配套清单且预览不依赖源文件`；Boundary `预览到提交之间的变化必须重新审阅`；Transaction `应用前任一已确认输入变化都保留原目录` | 原 ZIP 改名后显式配对；源被替换不改变快照，暂存被改拒绝 |
| P07、P08、P11、P12 | Package `异常路径和多插件包拒绝且没有活动目录写入`、`结构兼容及资产边界均在执行前拒绝`；Boundary 坏格式与私有图标用例；原 `PluginManifestCompatibilityTests` | 仅 ZIP 缺发行 RID 时保持基础级别，不写平台完全验证 |
| P09 | Package symlink 参数；Boundary `检查后管理父目录换成Junction会被再次拒绝` | 在再次进入用例前改变父路径；不是恶意进程并发替换路径的穷尽验证 |
| P10 | Package `资源预算按真实流和条目执行`；Boundary `配额等于真实字节通过而一字节不足拒绝`及 deep/manifest-size 参数 | 使用缩小预算，实际流累计；没有创建数 GiB 压缩炸弹 |
| P16 | Boundary `冻结安装准入可撤销且取消检查不产生待办`、坏 ZIP、取消时真实文件占用；Transaction IO 故障 | 磁盘满由提交失败语义覆盖，未进行物理满盘/ACL 试验 |
| V01、V02、V03、V04 | Planner 全部参数；Window Install/Upgrade/Reinstall/Downgrade/Restore 参数；服务提交再次校验 | 同版同摘要不产生待办，改名目录仍选择既有身份 |
| V05 | Planner 重复 ID/撞目录；Boundary 提交前原目录变更；Inventory 严格盘点与登记核对代码审查 | 不凭目录名接管未知身份 |
| V06、V07、V08 | Transaction `整目录更新保留旧版并区分禁用与运行确认`；Process 禁用、初始化失败、首次接管 | 开关、布局、业务数据未由安装层写入；原开关/布局回归继续执行 |

## T：文件事务故障位置

Transaction `每个应用和确认提交边界中断均可恢复旧版(step, after)` 通过 `IPluginInstallFileCommit` 包裹真实文件系统，执行到指定步骤抛出一次 IOException；每例最终断言旧目录完整指纹、旧登记和日志阶段。用相同恢复意图再执行一次，确认幂等。

| step | 真实变更 | 故障参数 |
| --- | --- | --- |
| 1 | 写 Applying | before / after |
| 2 | 旧目录 → 本次 backup | before / after |
| 3 | payload → 活动目录 | before / after |
| 4 | 写 AwaitingStartup 与进程身份 | before / after |
| 5 | 写 installed-v1.json | before / after |
| 6 | 写最终 Committed | before；成功后的返回结果见正常确认用例 |

| 矩阵 | 测试落点与事实 |
| --- | --- |
| T01、T02、T04 | Boundary 预览/提交变化；Transaction `取消先落盘再清理且两个服务不能覆盖同一个待办`、应用前输入变化；提交后旧目录仍在 |
| T03 | Boundary `取消落盘后清理被占用也不能复活待办`；Transaction 阶段校验；重启不执行 Cancelled |
| T05 | Transaction 整目录更新断言 old-only 只在备份；文件移动仅针对目标，兄弟目录范围由路径构造审查 |
| T06、T07、T15 | 上述 11 个故障参数；Boundary `写待办后返回失败的服务收尾仍保留事务载荷`；Process 直接退出后的下一进程恢复 |
| T08 | Transaction `新装未确认恢复为未安装并保留失败载荷`；Process fresh=true 的初始化失败/直接退出参数 |
| T09、T10 | Process `新进程真实更新并根据启用状态确认`及看板重启；实际 2.0.0 生命周期证据，禁用无构造证据 |
| T11 | Transaction `已加载失败只记录恢复意图不在本进程替换文件`；Process `初始化失败在下一真实进程恢复旧版或撤销新装`、终止试启动所有者 |
| T12、T13 | Transaction `已确认版本可以预览并恢复到上一版本`、`损坏备份阻断恢复并保留现有载荷`；无备份受控失败分支审查 |
| T14 | Transaction `损坏操作日志保留原件且不重置为空`六参数；Boundary `损坏登记不能误作新安装`三参数 |
| T16 | 未提交暂存清理、已提交不被 Stop 删除；当前明确保留所有历史备份，未实现自动裁剪，因此没有“自动清理通过”结论 |

## L / R：租约与真实进程

| 矩阵 | 测试落点与事实 |
| --- | --- |
| L01、L02、L07、L08 | Process `运行租约持续到真实进程退出且独立数据根不能绕过`；Boundary `现有只读锁文件仍可协作且不同根相互独立`、真实文件占用和 Junction；真正只读安装 ACL / 旧 Host 并发未人工执行 |
| L03 | Transaction 双服务待办竞争；Boundary 准入冻结撤销；审查 runtime → operations 固定顺序，服务不等待 runtime |
| L04、L06 | Process `试启动期间另一实例不能加载候选且终止所有者后可恢复`，核对 PID/开始时间、第二进程无构造、终止后旧版运行 |
| L05 | Session 独占释放后取得共享、重读日志的代码审查；上述另一实例试启动隔离验证共享分支。未用时序碰运气构造恰好落在两次锁操作之间的抢占 |
| R01、R05 | Process `看板确认经过真实助手重启后运行新版本`的 apphost/dotnet 参数，检查唯一助手、两次 Host、2.0.0 实际初始化与 Committed |
| R02、R03 | 原 `HostRestartProcessTests` cancel/crash/kill/bad-exit、单元重启协调器及原 UI 关闭回归；Boundary 安装准入可撤销，Staged 留存由独立事务用例断言 |
| R04、R06 | Process 更新/禁用、初始化失败与直接退出参数，父测试手动启动下一 Host；运行版本由真实程序集探针记录，无同进程重载 |

## U / A / D / G：交互与门禁

| 矩阵 | 测试或审查落点 |
| --- | --- |
| U01、U02、U04 | Window `空看板也能预览且替换动作必须明确确认`五参数；Process 生产看板路径。实际原生文件选择取消仅有适配分支审查，未做人工对话框验收 |
| U03 | Window `检查忙碌时禁止重复操作并可显式取消`、`窗口释放不取消已接受提交且迟到结果不再更新窗口`；服务状态归 Host 拥有 |
| U05 | Window `受控失败显示中文而底层异常不泄露路径`；文件/兼容失败通过服务边界抛出受控中文原因 |
| U06 | 原 `PluginStatusWindowTests`、开关、兼容报告、重启、未保存文档及全量 Headless 回归 |
| A01、A02、A03 | [安装契约](../../../reference/plugin-installation.md)职责表、生产依赖与中文注释审查；原 SDK 边界/API 检查，未修改 public API/manifest/Build 规则 |
| D01、D02 | 方案/专项归位、导航/模板/帮助同步；Help 五个 V23 文档的实际资源读取渲染；全量链接/锚点及 JSON 脚本结果入 JSON |
| G01、G02、G03 | 原 verify 固定测试组自动包含新增类；Gate 记录源码身份、命令、TRX 计数与摘要，完整结果保存到本轮 JSON；没有改动 Gate，不额外运行工具自测或发布门禁 |

原生文件选择、大包体验、真实 ACL/满盘、外部正式包业务流程和公开发布仍未执行。自动化已覆盖实现的主要提交/恢复行为；这些人工与外部观察项保留，不拿 Headless 成功替代。
