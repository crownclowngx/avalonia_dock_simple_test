# V20：Host 历史布局能力退役与现行入口收敛方案

> 用途：删除已经失去业务意义的 Dock Layout V2 能力，保留现行布局行为的验证，并减少旧入口和过时说明带来的理解负担。
> 状态：待实施；本次交付为方案及专用开发验证文档，不代表代码、工具或测试已经完成退役。日期：2026-09-21。
> 调研基线：`master` / `661ac7e4565afc86c2c90ee810259d84378fc61a`，编写前工作树干净。实施前重新确认 HEAD、差异及实际调用关系。
> 配套：[V20 专用开发验证](host-v20-layout-retirement-verification.md)。V19 已完成实施和本机交付，分别见[开发记录](../archive/records/host-v19/development-acceptance.md)与[部署证据](../archive/records/host-v19/local-deployment-20260921.json)。

V20 是 Host 改造序号，不升级产品、程序集、SDK、NuGet 包或现行 Layout schema。部署记录中的实际源码及 EXE 身份以原 JSON 为准，不能把记录提交的 HEAD 当作构建输入。历史测试数量不作为 V20 的测试数量目标。

## 1. 目标与首要规定

本轮以人和 AI 的理解成本为判断依据：理解一个行为需要掌握的概念是否减少，修改规则需要同步的位置是否减少，现行调用路径、状态所有者及失败处理是否可以直接读清。文件数、方法行数和注释数量不作为完成指标。

项目所有者已明确决定：**Dock Layout V2 不读、不写、不恢复、不迁移、不兼容，运行能力整体退役。** 当前代码仍包含 V2 导入，实施完成前现行契约继续如实说明该行为。

必须满足以下规定：

1. **SOLID 是首要规定。** 先解释职责、依赖和资源所有权，再讨论删除或提取；不能为减少代码破坏有效布局、取消、原子提交及释放边界。
2. **设计模式朴素使用。** 优先删除过期分支、复用现有具体协作者和普通方法。不增加通用迁移框架、版本策略注册、事件总线、第二容器或多个共同修改工作区集合的 Manager。
3. 新增或实质修改的核心类型、方法及关键分支使用**详细中文注释**，解释职责、输入输出、状态与资源归属、时序和设计理由；不逐行翻译代码，不粘贴阶段日志。
4. 单元测试、必要的 Plugin/Headless UI 回归、工具自测、边界审查及完整本地开发 `verify` 必须齐全。每项要求落实到真实断言或明确审查证据，不能仅凭测试类存在或总数通过认定覆盖。
5. 同步维护当前契约、内部架构、设计取舍、导航和本轮专用验证。方案、实现、自动验证、人工验收、部署及发布分别记录。
6. **不使用 AIFLOW**，不读取或维护其上下文、任务记录及更新候选。
7. **不使用 Windows CI 与发布门禁。** 开发阶段不执行 `seal`、发布 Windows Smoke、发布覆盖率、发布重复性门禁或 CI；不修改 CI，不部署安装目录，不上传包或创建发布标签。发布阶段再按当次要求执行，既有发布政策和阈值不降低。

本次请求只要求落地文档，不在本次文档交付中修改生产代码、测试实现、Gate 或验证脚本，不自动实施后续独立任务。

## 2. 范围与行为变化

### 2.1 唯一退出的兼容范围

| 对象 | 本轮处理 |
| --- | --- |
| Dock Layout schema 2 | 数据结构、编解码、校验、Store、Dock 映射、恢复和迁移全部退出 |
| `layout-v1.json` / `layout-v2.json` | Host 不探测、读取、迁移或修改；旧文件仅作为不支持边界的测试输入 |
| Layout schema 3、`layout-v3.json` 及备份 | 继续使用现行格式、严格验证、恢复和保存规则，不改名或重置版本 |
| 默认数据根 `%LOCALAPPDATA%/MyAvaloniaManagement/v2/` | 保持；这是数据根版本，与布局 schema 无关 |
| 插件 manifest schema 2、Document Envelope V2 | 保持当前协议和读取行为 |
| SDK API 的 v2 基线、`PluginV2` / `DependencyV2` 测试资产 | 保持；不按 `V2` 文本批量删除 |
| Workspace 所有权、Document Scope、Tool singleton、Avalonia/Dock 补丁 | 保持现行实现和协议 |

有意改变的行为只有：仅有旧 V2 布局而没有可用 V3 时，不再导入旧布局，改为默认布局，之后按 V3 保存。该行为是已确定的产品兼容边界变化，不宣称为完全等价重构。

### 2.2 退役后的读取规则

1. 优先读取有效 V3 主文件，必要时读取有效 V3 备份。
2. 识别未来 schema 时沿用只读保护，不用较旧备份覆盖未来语义。
3. 损坏 V3 的保留、I/O 不确定时的只读保护和写锁规则继续沿用。
4. 无可用 V3 时返回无恢复快照，由原生命周期建立默认布局。
5. 正常保存仍经 V3 串行保存队列和原子提交，不产生 V2 文件。

设计思路：运行时彻底取消对旧格式的认识，不建立旧文件清理器。主动删除旧文件会额外增加探测、删除失败、诊断和重试义务，本轮不引入这些职责。删除只为决定是否导入 V2 而存在的历史探测状态；不要误删用于保留损坏 V3、有效备份或未来格式保护的机制。

## 3. 当前代码依据与删除清单

当前 [DockLayoutLifecycle](../../Host/MyAvaloniaManagement/Business/Layout/DockLayoutLifecycle.cs) 借用 Session 的 `LayoutState` 应用和捕获布局，使用 `DockLayoutV3Store` 读取，通过 `DockLayoutSaveQueue` 保存。旧 Store 不在生产组合根中，但 [V3 Store.Load](../../Host/MyAvaloniaManagement/Business/Layout/DockLayoutV3Store.cs) 仍在没有 V3 历史且可写时调用 V2 reader 和转换器。

| 当前对象 | 拟处理 | 必须先核对的依赖 |
| --- | --- | --- |
| [DockLayoutSnapshotV2.cs](../../Host/MyAvaloniaManagement/Business/Layout/DockLayoutSnapshotV2.cs) | 删除 V2 快照、Pane/Tool 记录及专属验证 | 测试夹具、版本测试、现行有效断言 |
| [DockLayoutSnapshotV2Json.cs](../../Host/MyAvaloniaManagement/Business/Layout/DockLayoutSnapshotV2Json.cs) | 删除 V2 reader/writer | 同文件的 `DockLayoutFormatException` 仍被 V3 使用，先归位 |
| [DockLayoutV2Migration.cs](../../Host/MyAvaloniaManagement/Business/Layout/DockLayoutV2Migration.cs) | 删除转换器 | V3 Store 导入分支、迁移专项测试 |
| [DockLayoutStore.cs](../../Host/MyAvaloniaManagement/Business/Layout/DockLayoutStore.cs) | 删除旧读写、隔离和备份实现 | TestHostContext、UiTestContext 及测试文件构造 |
| [DockLayoutSnapshotMapper.cs](../../Host/MyAvaloniaManagement/Business/Layout/DockLayoutSnapshotMapper.cs) | 删除旧 Dock 双向映射 | 四向布局、自动收起等仍有效的行为测试 |
| [DockLayoutRuntimeValidator.cs](../../Host/MyAvaloniaManagement/Business/Layout/DockLayoutRuntimeValidator.cs) | 删除 V2 运行时校验 | 旧可用性测试、架构图和源码注释 |
| [RetiredToolLayoutMigration.cs](../../Host/MyAvaloniaManagement/Business/Layout/RetiredToolLayoutMigration.cs) | 删除 V2 专用退役工具转换 | `RetiredHostToolIds` 仍由工具偏好校验使用，不随迁移器删除 |
| `DockLayoutV3Store.Load()` 中的 V2 导入 | 删除旧文件探测与导入条件 | 有效主文件/备份、未来格式、只读和坏文件保存行为 |

`DockLayoutFormatException` 的设计职责是报告当前布局格式错误，应迁到现行布局位置并修正只描述 V2 的注释，保持错误码和异常语义。`RetiredHostToolIds` 的现行偏好约束继续保留，按实际调用修正注释，不把它当成 V2 兼容层。

旧接口仅供测试调用时，先迁移有效断言再删除接口；反射工具、项目声明、帮助资源引用和覆盖率文件清单一并检查。若覆盖率清单包含退役文件，只删除实际退役条目，保留存活实现的阈值，不通过豁免绕过验证。

## 4. SOLID 与朴素设计的具体落实

| 原则 | 本轮落实 | 应拒绝的做法 |
| --- | --- | --- |
| SRP 单一职责 | Session 拥有工作区；LayoutState 转换树；Store 拥有文件与写锁；Queue 调度写入 | 把删除旧格式变成新的迁移服务、磁盘清理器或第二布局所有者 |
| OCP 开闭原则 | 删除退出产品支持的分支，继续使用唯一现行 schema 的显式协议 | 为未来假设建立版本处理器注册框架，按插件 ID 增加布局特判 |
| LSP 里氏替换 | 明确记录 V2 支持退出；其余 V3 结果、错误、取消、通知、实例和释放协议保持 | 以等价重构名义改变未知工具保留、原生关闭否决或写入失败语义 |
| ISP 接口隔离 | 删除无生产消费者的旧查询入口；保留当前目录和指定身份查询 | 一类一接口，向 SDK 暴露测试入口，增加宽泛的依赖包 |
| DIP 依赖倒置 | DI 留在组合根，测试使用真实现行路径及已有窄端口 | 将 Provider 传入业务层，依靠反射扫描重新分派布局版本 |

新增类型必须能说明消除了什么重复规则或资源义务。归位共享异常不创造新机制，测试辅助仅组织输入和夹具，不复制旧 Store、Mapper 或校验流程。

## 5. 有效测试先迁移，再删除旧能力

整体风险评为**中**：删除机制本身较简单，但多项有效 Dock 行为仍借 V2 表达。测试迁移是本轮主要风险与审查重点。

| 分类 | 处置 | 说明 |
| --- | --- | --- |
| 纯 V2 格式、Store 和转换测试 | 随能力退出 | 按具体测试方法判断，不能仅凭文件名整类删除 |
| 现行 Dock 行为使用 V2 构造场景 | 改用 V3 / `DockLayoutWorkspaceState` | 保留位置、比例、顺序、自动收起、恢复和引用身份断言 |
| 旧文件不被支持的边界 | 使用少量原始文件输入验证 | 不创建 V2 模型、writer 或迁移模拟器；旧文件与默认结果必须可区分 |

实施前建立逐项表：旧测试方法与参数、原行为断言、删除或迁移理由、新测试方法与参数、验证矩阵编号、实际运行结果。纯兼容测试删除允许总数下降；V19 测试数量不是最低条数，不用无意义测试补齐数字。

重点检查 [DockFourWayLayoutTests](../../Host/MyAvaloniaManagement.PluginTests/DockFourWayLayoutTests.cs)、[DockLayoutAvailabilityTests](../../Host/MyAvaloniaManagement.PluginTests/DockLayoutAvailabilityTests.cs)、[AutoHideRestoreVisualTests](../../Host/MyAvaloniaManagement.UiTests/AutoHideRestoreVisualTests.cs)、[ToolCenterUiTests](../../Host/MyAvaloniaManagement.UiTests/ToolCenterUiTests.cs)、数据根/版本/工具偏好测试及两个测试容器。

旧可用性测试中的“任何未知项都拒绝整个 V2 布局”不能机械迁到 V3。按当前 V3 契约保留其有效目的，例如缺失工具不创建实例、可恢复记录仍保留、可用项仍正确显示；退出的旧恢复政策按理由删除，不修改 V3 去迎合旧预期。

## 6. 工具与 Gate 的开发适配

### 6.1 Writer Lease 验证

[Verify-LayoutV3WriterLease.ps1](../../tools/Verify-LayoutV3WriterLease.ps1) 当前仍要求新实例转换 V2。实施时删除该场景，改用 V3 验证，保留同根唯一写入者、不同数据根隔离、只读会话不自动接管及专属持锁进程退出后新实例可取得写锁。

脚本继续只在隔离临时目录加载当前 Store，不启动桌面、CI 或发布流程。保留就绪信号、有界等待、专属子进程回收及清理前绝对路径检查；不操作用户 Host 进程或真实数据根。

### 6.2 发布检查代码可以适配，发布流程不在本轮执行

[GateChecks](../../tools/MyAvaloniaManagement.Gate/GateChecks.cs) 当前 Windows Smoke 仍断言 `layout-v2.json` / schema 2。V20 将检查及受影响消息改为当前 V3 文件/schema，并补充实际被该调用路径复用的产物验证单元测试。

如果需要测试隔离，最多提取同一 Gate 类中的小型产物检查方法，让发布入口与工具单测调用同一实现；不新增发布执行器、测试模式或通用验证框架。测试直接覆盖临时产物，不启动发布 Windows Smoke。

本轮执行 Gate 工具自测、Writer Lease 本机专项，以及真实 V3 Store/Workspace 的文件读写和 Headless 生命周期回归。`verify` 不执行发布 Windows Smoke，开发通过不能证明发布进程启动路径已经实跑。Roadmap 分别记录“代码适配和开发验证完成”与“发布时真实 Smoke 待执行”，不提前关闭后者。

## 7. 旧查询入口收敛

[DocumentCreationMenuQuery.GetCreationEntriesByCategory](../../Host/MyAvaloniaManagement/Business/Workspace/DocumentCreationMenuQuery.cs) 当前只被 [ServiceAndModelTests](../../Host/MyAvaloniaManagement.Tests/ServiceAndModelTests.cs) 中两个测试使用。

迁移分类测试到 `ReadDirectory().Categories` 的分类与 `EntryCount`；迁移多 Intent 测试到 `Items` 中精确 DocumentTypeId 和 CreationIntentId，并保留声明顺序断言。随后删除旧方法及不再使用的依赖。

设计思路：现行模型已经表达分类树和创建身份，测试应直接观察它。不得在测试中重新 `GroupBy` 并构造旧 Dictionary。现行 `ReadDirectory()` 与 `HasCreationEntry()` 的职责、即时可用性及零业务对象创建约束保持。

## 8. 中文注释与设计思路

| 修改位置 | 中文注释需要解释 |
| --- | --- |
| V3 Store.Load | 主文件/备份的判断顺序、未来格式与 I/O 只读原因、返回无快照后谁建立默认布局 |
| 共享异常归位 | 当前布局格式错误的职责、错误码和安全消息边界，消除只适用 V2 的旧描述 |
| 测试场景迁移 | 正在保护的用户行为、快照输入、对象身份与退出清理的所有者 |
| Writer Lease | 子进程只负责持锁、为什么旧只读实例不自动接管、临时目录及进程如何回收 |
| Gate 产物检查 | 检查文件/schema 的有限职责；单测通过不代表发布进程链已运行 |
| 目录测试 | 为什么使用现行分类模型、精确创建身份及当前顺序，而不重建旧查询 |

设计理由集中在本方案与现行设计文档；代码仅保留理解当前实现所需的约束，不写“P1 完成”“通过多少项”等阶段记录。

## 9. 阶段安排

| 阶段 | 工作与顺序 | 退出条件 |
| --- | --- | --- |
| P0 | 核对 HEAD/工作树，完整开发基线；建立删除清单、共享依赖及逐项测试去向 | 有实际基线结果；行为变化与等价迁移分开标记 |
| P1 | 先迁移现行行为测试和归位共享依赖，再按完整小批次删除 V2 类型、导入分支及纯兼容测试 | 每批可以构建和验证；L/B 矩阵及映射完成 |
| P2 | Writer Lease 去 V2；Gate 产物检查适配 V3并补自测 | W/G 矩阵通过；未执行任何发布路径 |
| P3 | 迁移两个查询测试，删除旧查询入口 | Q 矩阵通过，当前目录和即时查询行为保持 |
| P4 | 当前文档、架构、导航及专用矩阵收口；最终完整本地 verify | D/A 矩阵与完整开发证据对应最终输入 |

共享构建输出串行验证，保留 `-m:1`。不把“先删除所有类型、下一阶段再修编译”作为可交付状态；阶段差异保持可独立审查，不夹带全仓格式化、重命名、版本升级或安装部署。

## 10. 文档同步与交付记录

| 文档范围 | 计划交付时 | 代码实施时 |
| --- | --- | --- |
| 本方案、专用验证 | 标记待实施，写清边界与待补矩阵 | 回填阶段状态、真实测试映射和结果；完成后方案归档、验证转入 maintenance |
| 根 README、总导航、Host 入口、roadmap、验证索引 | 增加 V20 入口，校准已证实的 V19 状态 | 链接实际完成证据，移出完成待办 |
| 版本基线、架构、设计取舍、Layout V3 契约 | 保留当前仍有 V2 导入的事实；修正已经失效的调用图 | 明确只支持 Layout schema 3，移除 V2 导入说明 |
| 当前布局指南、维护说明、兼容约束、帮助资源 | 不提前描述退役已生效 | 逐项核对路径、格式、默认布局、发布检查说明及应用内帮助 |
| `docs/reference/dock-layout-snapshot-v2.md` | 暂留现行引用位置 | 移入归档，修正当前链接和帮助读取；不篡改过去方案与证据 |

历史归档原文及证据保持当时事实，必要路径变动只附带明确迁移说明，不全仓替换 V2。八个帮助章节、四篇理论原文及架构入口保持稳定。

实施开始后建立 `docs/archive/records/host-v20/development-acceptance.md`、`test-matrix.md` 及非嵌入 `development-evidence.json`，文件真实存在后再加入链接。记录 HEAD/工作树身份、实际命令、时间、退出码、TRX、Gate run-id、失败与重跑、工具结果及未执行边界；不在计划阶段创建虚构的成功记录。

## 11. 后续独立任务

| 顺序 | 任务 | 范围及进入条件 |
| --- | --- | --- |
| V20 之后优先 | Workflow `BeginShutdown` 取消锁边界调查 | 当前注释称取消在锁外，而 `_shutdown.Cancel` 位于锁内。先用可控同步点验证回调、关闭/Run 释放交错、取消异常和排空；确认缺陷才最小修复，不直接机械移动 Cancel |
| 随后 | PluginManifestReader 最小整理 | 优先只提取 `TryParseManifest(root)`；复用字段校验，保持首次失败、错误码、异常范围及 JSON 生命周期；另建对应编号的方案和专项 |
| 有实际修改需求时 | Command Palette 按来源分段 | 当前四类来源线性展开，暂不改动刚稳定的目标捕获/复查链 |
| 不列入 | WorkspaceSession 全面拆分、Workflow 全面重构 | 缺乏足以抵消所有权和并发风险的明确收益，不新增 Manager/Provider 体系 |

后续调查不是 V20 已修复缺陷，也不是 V20 完成条件。若发现必须立即处理的正确性问题，独立记录与验证，不混入布局删除差异。

## 12. 完成定义

- Layout V2 运行类型、导入分支及专属测试退出；生产代码不再探测旧布局文件。
- 现行 Dock 行为迁移表完整，有效断言仍覆盖位置、比例、顺序、自动收起、浮窗、取消和实例身份。
- V3 严格格式、未来 schema、坏文件、备份、写入权和最终保存语义保持；旧文件被忽略边界已验证。
- Writer Lease 与 Gate 产物检查使用 V3，开发自测通过；发布 Windows Smoke 仍明确待发布阶段执行。
- 旧查询入口删除，测试直接消费现行目录模型。
- SOLID、朴素设计、详细中文注释及共享资源所有权审查完成。
- 当前文档、专用矩阵、链接和嵌入帮助一致；历史记录未被改写为新的事实。
- 专项与最终完整 `verify` 有对应输入的真实证据，不以测试总数或 V19 结果代替本轮验证。
- 未使用 AIFLOW、Windows CI 或发布门禁，未执行安装部署或公开发布。
