# V12 Host 内部职责重构开发记录

> 日期：2026-09-17。起始源码：`538d173b7a0d90dc7f1b25dcbc901b5ac597f288`。分支：`codex/host-v12-internal-refactor`。
> 计划见 [V12 方案](../../../roadmap/host-v12-internal-refactor-plan.md)，测试矩阵与重跑入口见[专用开发验证](../../../maintenance/host-v12-refactor-verification.md)。
> 状态：P0–P3 实现及专项完成；Markdown 定稿后执行完整本地 verify，结果与源码、产物身份仅回填 [final-development-evidence.json](final-development-evidence.json)。

## 1. 实际范围与收益

| 阶段 | 实际变化 | 保留的边界 | 收益 |
| --- | --- | --- | --- |
| P1 | Builder 提取 PluginContributionSnapshot、PluginContributionValidator、PluginConflictAnalyzer | Add/Import/Seal/Build 时机、诊断顺序、整个 Owner 排除、Registry 构造成功后的唯一 Provider 提交 | 修改校验不必同时理解服务容器提交；纯数据测试直接表达规则 |
| P2 | Menu、KeyBinding、Palette、Command 使用各自 UiRefreshScheduler | UI 同步／始终排队两种时机、Normal 优先级、原订阅与异常政策、在途观察者快照 | 排队合并和释放行为集中验证，消费者保留业务语义 |
| P3 | GetOpenPages 与 ToolWorkspaceReadModel 每次同步捕获 WorkspaceLayoutQuerySnapshot | 原对象引用、首个分组与浮窗、Hidden 优先级、Session 唯一所有权及执行时重查 | 去掉逐页面／工具重复扫描；关系查询与业务许可分开表达 |

本轮不改 SDK/API、包版本与依赖、稳定身份、manifest、Document V2、Layout V3、主树查找语义或磁盘格式；没有新建通用框架、单实现接口、服务定位器、事件总线或长期查询缓存。新增核心类型、关键方法及时序分支均使用中文设计注释。

## 2. 关键取舍与 SOLID 审查

| 原则 | 已审查的实现事实 |
| --- | --- |
| SRP | Validator 校验局部声明，Analyzer 生成全局冲突事实；Scheduler 只管刷新时机；查询快照只管关系。Builder 与 Session 保留写入和提交职责 |
| OCP | 规则围绕既有贡献类别扩展，关系围绕引用与拓扑计算；没有插件 ID 特判或额外策略注册机制 |
| LSP | 原入口的异常、诊断、事件时机、释放和执行前重查由行为锁定及现有集成测试保护；不把同步通知改成统一异步 |
| ISP | 新协作者为内部具体类／静态类，方法仅覆盖当前调用需要；不扩大 SDK、Dock 回调或展示接口 |
| DIP | 校验接收声明事实，不接收 IServiceProvider；刷新不依赖 Catalog/Workspace；工作区查询不创建模型或视图，真实替换边界沿用现有窄端口 |

P1 集合防御性复制并加只读包装，Descriptor 与工厂引用沿用注册时的冻结事实，工厂不执行。全局分析使用惰性枚举，让诊断端口抛错仍立即停止；Provider 提交继续发生在所有 Registry 索引构造之后。已有“多 Owner 且多 Lifecycle”的畸形内部输入会由 SingleOrDefault 抛 InvalidOperationException，本轮保留并用测试说明，不顺手改变失败政策。

P2 在原方案 RequestRefresh/Dispose 外增加 TryBeginRefresh，复用消费者的锁。清除 pending、检查释放、取得观察者快照必须保留在原临界区；独立调度锁会改变竞态。消费者在锁外派发，回调异常仍由原消费者处理。已经取得的事件快照即使首个观察者释放对象，后续观察者仍完成本轮通知。

P3 先沿原 Navigator 捕获工作区节点，再建文档与工具关系索引。窗口按原窗口声明顺序分别扫描可见树，每个窗口只扫描一次；不能用 DFS 首次到达代替 FindWindow 的首个声明匹配。引用重复、同 Id、嵌套浮窗和窗口回边均保留原结果。快照不跨 await、不存入 Session、不写盘、不拥有任何实例的释放责任。

## 3. 基线、行为锁定与专项结果

基线完整 verify：`artifacts/gate/20260917-053338-538d173b7a0d/summary.json`，通过。SDK 91、Host Unit 496、Plugin 255、Headless UI 205、MyPlugTest Unit 11、真实 ZIP 验收 1、Avalonia 补丁测试 7；均无失败或跳过。行为锁定测试先在原生产实现运行成功，再开始提取。

下表是不同阶段的实际运行，不可相加当成独立测试总数。专项使用 Release、`--no-restore -m:1 -warnaserror`；测试方法及矩阵对应关系见专项指南第 9 节和 JSON。

| 阶段 | 通过 | 失败／跳过 | 本地 TRX |
| --- | ---: | --- | --- |
| P0 原入口行为锁定 Unit | 18 | 0／0 | artifacts/host-v12/p0-unit/behavior-unit.trx |
| P0 原入口行为锁定 UI | 1 | 0／0 | artifacts/host-v12/p0-ui/behavior-ui.trx |
| P1 Registry 单元 | 90 | 0／0 | artifacts/host-v12/p1-unit/p1-unit.trx |
| P1 Provider／Scope／生命周期集成 | 43 | 0／0 | artifacts/host-v12/p1-plugin/p1-plugin.trx |
| P2 命令单元 | 34 | 0／0 | artifacts/host-v12/p2-unit/p2-unit.trx |
| P2 调度及真实消费者 Headless | 36 | 0／0 | artifacts/host-v12/p2-ui-final/p2-ui.trx |
| P3 查询及工作区单元 | 58 | 0／0 | artifacts/host-v12/p3-unit-complete/p3-unit.trx |
| P3 布局、跨窗、工具与视图 Headless | 92 | 0／0 | artifacts/host-v12/p3-ui/p3-ui.trx |

新增 21 个 Unit 案例与 15 个 Headless 案例。Registry 诊断的 StableId 断言在最终审查时补强；完整 verify 会重新执行最终测试源码，而非复用阶段 DLL。未修改 Gate 工具、阈值、API 基线、依赖锁或测试分类。

### 开发过程中修正的测试问题

- P1 首次编译缺少 CommandPlacementId 的 UI SDK using；P3 首次编译缺少本测试工程的 Xunit.Abstractions using。补引用后通过，未改依赖。
- P2 首次 Headless 30 通过、2 失败：同步等待 Task.Run 未保证独立后台线程，使同步模式立即发布。改为独立 Thread、明确 CheckAccess 为 false 后 32 项通过，再补四消费者在途释放成为最终 36 项。首次 TRX 被随后同路径重跑覆盖，JSON 保留工具输出中的失败摘要，不把后续 TRX 冒充首次证据。
- P3 补强夹具首次 56 通过、2 失败：读取 Store 独占的 layout-v3.lock，以及只清理外层根而遗漏内层根的初始 Hidden 关系。改为比较锁文件元数据、其余文件字节，并先清除全部初始 Hidden 关系。失败 TRX 保留于 `artifacts/host-v12/p3-unit-final/p3-unit.trx`，修正后 58 项通过；生产查询未因此改变。

## 4. 查询采样与适用范围

`WorkspaceLayoutQuerySnapshotTests.有限大布局与原查询一致并记录关系查询采样` 固定构造 270 页面、90 工具、8 浮窗、396 节点。预热 3 轮，交替先后顺序采样 7 轮。旧入口每页查两次成员、每工具查一次浮窗；新入口包含快照创建，再执行相同次数索引读取。每轮断言结果一致，时间不作测试阈值。

P3 最终专项本机样本（毫秒）：

| 方法 | 7 次样本 | 中位数 |
| --- | --- | ---: |
| 原关系遍历 | 13.976、14.984、17.357、13.496、13.078、13.359、12.854 | 13.496 |
| 建立快照及索引查询 | 0.195、0.167、0.167、0.152、0.162、0.143、0.158 | 0.162 |

这是固定拓扑下的关系查询采样，不是整体 GetOpenPages、工具中心、启动或 UI 渲染基准。新实现有每次建索引的分配成本，未建立小布局收益或整体帧率承诺；最终 verify 的采样值可以因机器负载不同而变化。

## 5. 最终证据与未执行边界

先定稿方案、专项、架构、设计取舍和全部索引，再运行完整 `dotnet run --project tools/MyAvaloniaManagement.Gate -- verify`。之后只回填不嵌入 Host 的 JSON，记录 run-id、源码文件清单和 SHA256、HEAD、未提交状态、TRX 数量、矩阵实际执行、构建产物与报告哈希。JSON 自身从最终源码清单排除以避免自引用；Gate 自带指纹保留运行前的证据文件状态，并明确与最终内容清单的区别。

| 场景 | 本轮状态 | 说明 |
| --- | --- | --- |
| 菜单、快捷键、面板、同名页面、浮窗、工具显隐、关闭和退出 | 自动化专项已执行 | 使用既有生产组合、Avalonia Headless 和实际 Dispatcher；全量回归由最终 verify 确认 |
| 原生鼠标、焦点、多屏 DPI、显示器与窗口管理器 | 未执行 | 本轮未操作真实桌面；Headless 结果不作为原生桌面证据 |
| 外部账号、下载、数据库、视频／WebView 业务 | 未执行 | 本轮范围是 Host 内部等价重构，未开展外部业务联调 |
| Windows CI、seal、发布 Smoke、发布覆盖率和发布重复性 | 未执行 | 按用户要求留到实际发布时使用 |
| AIFLOW、包上传、安装目录部署和公开发布 | 未执行 | 本轮只交付工作区代码、测试、文档及本地开发证据；构建输出目录的示例插件准备由原 verify 执行 |

真实桌面及历史外部场景继续在[待办](../../../roadmap/README.md)独立记录。本轮没有创建提交，也没有替换现有安装产物。
