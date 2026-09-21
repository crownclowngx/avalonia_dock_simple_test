# V21 测试去向与开发验收矩阵

> 日期：2026-09-21。矩阵要求见[专用开发验证](../../../maintenance/host-v21-gate-and-test-efficiency-verification.md)。具体方法、参数、原/新名称及 TRX 结果见[机器可读明细](test-mapping.json)；最终输入及完整 Gate 状态见[开发证据](development-evidence.json)。

## 1. 迁移、合并与重复轮次

| 原责任与方法 | 本轮去向 | 保留的断言 / 理由 |
| --- | --- | --- |
| Host `PublicApiContractTests.PluginSdk不再公开通用事件总线或旧消息包装器` | SDK `SdkBoundaryTests.Ui程序集不公开旧消息包装器或消息框架签名`；Core 类型缺失归既有 `Core程序集只引用框架程序集且不存在旧公共面` | UI 消息框架签名、三个旧包装类型原样迁移；Core 用 `GetType` 同时拒绝 public/internal 旧总线 |
| `InternalRefactorTests.Host内部直接协作消费者不依赖Sdk事件总线` 中最后两项重复类型缺失 | SDK Core 归上行；HostEventBus 归 `PublicApiContractTests.Host程序集不再包含V2Facade总线或伪插件所有者` | 原方法和三个旧消息、七个消费者构造参数检查继续保留 |
| `SdkBoundaryTests.Legacy项目已删除且活动项目没有旧引用` | 原名保留，换成进入目录前裁剪；新增 `扫描覆盖新增项目与模板但不进入生成目录或嵌套仓库` | 活动新增项目、模板违规仍被发现；artifacts/bin/obj/隐藏和嵌套仓库不污染结果 |
| `ToolCenterUiTests.标题栏关闭最后一个工具后可从工具中心反复显示且复用视图`，两个工具各三轮 | 原方法保留，每项两轮 | 保留首次与重开、原模型/View、可见性及位置；第三轮没有新状态，未删参数或其他交互矩阵。八轮服务寿命与 HelpWindow 二十轮释放测试独立保留 |
| `GateInfrastructureTests.ExecutionGraphDeduplicatesStagesInRegistrationOrder` | `顺序计划每个阶段只执行一次且保留阶段包装器` | 固定计划无动态注册/静默去重；测试唯一阶段及包装器，不保留旧容器实现测试 |
| `GateInfrastructureTests.ExecutionGraphStopsAfterFirstFailure` | `每一开发阶段失败均立即停止`，八个阶段参数 | 每个前置失败均无后继，不执行 coverage/windows-smoke |

SDK 原 91 项 → 93（迁入 1、新扫描边界 1）；Host Unit 原 653 → 652（迁出 1）；Plugin 原 280 → 283（新增夹具 3）；UI 原 289 → 292（新增等待助手 3）。MyPlugTest 11 与真实 ZIP 1 保持。计数变化只解释去向，不作为质量指标。

## 2. 等待与证据改动的行为落点

| 改动 | 真实行为验证 | 矩阵 |
| --- | --- | --- |
| RestartHarness 依赖筛选、复制、子进程摘要与收据 | `RestartHarnessFilesTests` 3 项；`HostRestartProcessTests` 全部 15 参数；`PluginEnablementRestartTests`、`HostRestartLifecycleTests`、`StartupPluginProgressTests` | F01–F08 |
| Dispatcher/渲染/条件等待 | `UiTestWaitTests` 立即满足、排队动作一次、超时/异常直接传播；DocumentWindowTestContext 与原消费用例保持 | U01–U04 |
| 两组恢复浮窗关闭前置就绪 | `DockToolWindowCloseUiTests.P2多组整窗关闭保留比例和位置且取消不隐藏任何成员(false/true)` | U02–U06 |
| 原生否决与再次关闭 | `DockToolWindowCloseUiTests` 24 项全保留，包含五种入口/取消、同组逐项、混合文档、能力、框架取消、持久化恢复及异步重试 | U02–U07 |
| 热点交互及布局 | DocumentWindowV16、ToolCenter、AutoHideRestore、DockToolWindowClose 四组原 88 项全部保留；自动收起四向、最后工具与布局所有权断言保持 | U03–U07 |
| 其他共享排空/渲染调用者 | DockLayoutV3、DockToolSplit、DockAreaFill、DockCrossWindow、CognitiveUxV8、CommandPaletteV18、PluginNavigation、PluginStatus、PluginEnablement；完整 UI 集合执行 | U01–U06 |
| 帮助导航竞态与窗口释放 | `HelpWindowTests.ClosingAndFastNavigationIgnoreStaleRendererCompletion` 使用进入信号；20 轮 GC/所有权检查保留 | U04、U07 |
| 统一附件根 | 各 run/suite/scenario 分目录；进程以 GUID 分例，亮暗截图以文件名区分；完整 UI 专项和最终 Gate 检查实际附件 | U08–U09 |
| 像素断言 | `IconRenderingUiTests` 原断言未改，完整 UI 中执行；其他仅保存 PNG 的用例只算辅助证据 | U08 |

表内仅改共享机制的消费方法全部按原名/原参数保留，机器明细逐项列出；不把未修改的主窗/浮窗、方向、取消或跨窗场景省略成一个代表例。

## 3. Gate 负向与兼容边界

| 检查 | 自动化或审查证据 | 矩阵 |
| --- | --- | --- |
| 唯一开发顺序、各前置失败、发布顺序不变 | GateInfrastructure / LocalGate / AvaloniaLayoutPatch；发布 profile 仅调用模拟阶段 | G01–G03、G10 |
| 必需消费输出清单不滤缺文件 | LocalGate 新增 Dock/Avalonia 两参数；既有身份核对覆盖匹配/旧 DLL/缺文件 | G04 |
| TRX 完整结构、摘要与慢用例 | TestEvidenceReaderTests：带/不带 namespace，计数、唯一执行 ID、运行/用例时间；最终 Gate 消费实际 xUnit v2/v3 TRX | G05、G09 |
| 缺文件、坏 XML、节点缺失/重复、非法或不一致计数与明细 | reader 专项逐项构造负向输入；旧包验收负向夹具改成结构完整，避免因坏结构假通过 | G06 |
| 失败/跳过/异常结果、十类异常辅助计数、非成功运行及进程非零 | reader 与 Gate 政策单测，异常结果不可由 passed 数掩盖 | G07 |
| 真实 ZIP 精确一项、零项/多项/失败/跳过拒绝 | LocalGate 与 reader；最终 Gate 真实打包解压并注入输入 | G08 |
| 失败证据、未执行不补零、旧 summary schema 2 | 序列化单测、真实早期失败 Gate 单测；最终成功套件汇总审计 | G09–G10 |
| 架构与文档 | 中文注释及差异审查、活动扫描受控树、嵌入原文/生产渲染/链接及帮助测试 | A01–A05、D01–D03 |

工具自测独立执行，不属于默认业务五套件；未通过调用真实 seal 来验证发布分支。完整命令、初次失败与修正后的结果分别留存，不以自动重试覆盖失败样本。
