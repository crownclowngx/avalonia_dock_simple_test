# G6：拆分 Workspace Session 与 Dock Factory

> 状态：已完成（2026-08-22）。本阶段是未发布 V3 的 Host internal 重构，不签名、不上传、不发布。

## 1. 最终职责图

```text
HostRuntime（唯一组合与释放入口）
  ├─ HostDockFactory（Dock Framework Adapter）
  │    └─ IWorkspaceDockCallbacks ──一次性绑定──> WorkspaceSession
  └─ WorkspaceSession（唯一工作区所有者）
       ├─ Root Dock / Document Dock
       ├─ owned Documents / created Tools
       ├─ DockWorkspaceBuilder / ToolDockCoordinator
       ├─ DocumentPersistenceCoordinator / DockLayoutLifecycle（流程协作者）
       └─ ToolWorkspaceReadModel ──> ToolWorkspaceState（无 Dock 类型）
```

`HostDockFactory` 是生产代码中唯一继承 Dock `Factory` 的 `internal sealed` 类型。它只实现 Locator、
禁浮动策略和 Docked/Hidden/Closing/Closed override，不拥有 Root、Document 或 Tool 集合。

`WorkspaceSession` 不继承 Dock 类型。每个 `HostRuntime` 只构造一个 Session 和一棵 Root Dock；多个瞬态
`MainWindowViewModel` 共享这份所有权，但各自订阅和幂等解除定向布局通知。

`ToolWorkspaceReadModel` 只把 Session 与冻结 Registry 投影成不可变 `ToolWorkspaceState`：Tool ID、显示名、
可见性和是否允许隐藏。Pinned Tool 按可见处理，ViewModel 不读取 Root Dock、Dock Tool、Factory 字典或
`IServiceProvider`。

## 2. 对象所有权与提交点

| 对象/事实 | 唯一所有者 | 写入路径 |
| --- | --- | --- |
| Root / Document Dock | `WorkspaceSession` | 幂等 `CreateLayout` 或恢复失败后的整体重建 |
| 已拥有 Document | `WorkspaceSession` | 创建完成后登记，发布失败/恢复失败/最终关闭/退出汇入同一释放入口 |
| 已创建 Tool | `WorkspaceSession` | 每个 Session 只创建一次；显隐和恢复经现有 `ToolDockCoordinator` |
| Dock override / Locator | `HostDockFactory` | Dock Framework 标准 override；通过窄回调通知 Session |
| Tool 管理列表 | `ToolWorkspaceReadModel` | 每次读取生成纯数据快照，不取得 Dock 对象所有权 |
| 持久化与布局文件 | 既有 Coordinator/Store | Session 提供状态和操作，不改变现有磁盘协议 |

已删除 `ManagementFactory` Facade、`DocumentWorkspace` 双重工作区所有权、`ToolManagementData`、
`ToolRegistrySnapshot` 和 `IToolVisibilityStateSink`。生产与 Harness 已删除 `Files` Locator 查询；`Plug`
兼容别名按任务书留到 G9，Host 内建贡献仍暂留 Plugin Registry，按 G7 再处理。

## 3. 回调、关闭与退出时序

Factory 与 Session 由组合根先后构造，再执行一次 `AttachCallbacks`。未绑定使用和重复绑定都会立即失败，
没有服务定位器或延迟的可空正确性依赖。

- Docked / Hidden：Dock 基类先完成框架行为，Session 再归一化 Tool Dock 或发布状态通知。
- Closing：Session 的脏 Document 协调器先决定是否允许关闭；只有允许时才调用 Dock 基类。
- Closed：Dock 基类通知位于 `try`，Session 的最终 Document 释放位于 `finally`。
- Runtime 退出：先停止新建，再按 Document 在前、Tool 逆序在后的顺序释放；单项异常不阻断后续项，
  最终以 `AggregateException` 汇总。
- 浮动：Factory 的全部浮动入口继续稳定拒绝，Root 能力策略也固定 `CanFloat=false`。

Document 创建采用“初始化—登记—发布”单一提交点。重复发布、部分发布、恢复或初始化失败都会撤销候选
Document 的 Dock/持久化/恢复登记与 Scope；最终关闭和 Runtime 退出复用同一幂等释放路径。

## 4. SOLID 与朴素模式取舍

本阶段只使用 Factory Adapter、一次性回调绑定、只读投影和已有 Coordinator，没有引入通用事件总线、
服务定位器、CQRS、MediatR、Repository 或新 NuGet 包。

- **SRP**：Factory 只适配 Dock 协议；Session 只拥有工作区会话；ReadModel 只生成 Tool 状态投影；保存、
  布局、关闭和 Tool Dock 流程继续由原有协作者承担。
- **OCP**：插件新增声明式 Document/Tool 贡献无需修改 Factory 的类型分支。
- **LSP**：Factory override 保持 Dock 基类的调用顺序，所有浮动 overload 保持同一拒绝语义。
- **ISP**：唯一新增接口 `IWorkspaceDockCallbacks` 只服务 Dock 框架接缝，不进入 ViewModel 或 SDK。
- **DIP**：ViewModel 依赖 Session/ReadModel 的窄行为，不依赖 Dock Runtime；Session 的所有正确性依赖均为
  必需构造参数，只有诊断 Sink 合法可选。

Welcome 的两个入口通过组合根注入窄 `Action<string>` 显示 Tool，修复生产对象图可能选中无参构造而丢失
动作的问题；它没有获得 Session、Factory 或容器引用。

## 5. 兼容性与删除面

- Core/UI V3 public API 无变化，实数仍为 Core **127**、UI **46**，两个 Shipped 文件仍为空。
- manifest schema 2、Document envelope schema 2、`layout-v2.json`、默认数据根 `v2` 均未变化。
- 四插件业务、Descriptor、View、内容 schema 和 SDK 引用未变化，也没有新增 NuGet 包。
- `Plug` 与 Host 特殊 Registry 路径分别留给 G9/G7，避免在 G6 越阶段修改。

## 6. 测试、覆盖率与门禁实数

专项入口为：

```powershell
./scripts/Test-WorkspaceSessionDockFactory.ps1 -Configuration Release -NoRestore
```

专项 Release 结果为 **441/441**，零失败、零跳过：

| 分组 | 通过数 |
| --- | ---: |
| Host Unit | 181 |
| Headless UI | 56 |
| Plugin / Dock | 204 |

Host 合并行覆盖率 **83.78%**、分支覆盖率 **70.32%**，均不低于 G0 的 83.24% / 68.98%。G6
重点类型行覆盖率为：

| 类型 | 行覆盖率 |
| --- | ---: |
| `WorkspaceSession` | 92.39% |
| `HostDockFactory` | 97.96% |
| `ToolWorkspaceReadModel` | 100.00% |

专项结构扫描同时证明：旧三类不在生产代码；只有 `HostDockFactory` 继承 Dock Factory；Session 不继承 Dock；
Tool ViewModel/DTO 无 Dock 或服务定位器；MainWindow 构造不依赖 Factory/Root；生产和 Harness 无 `Files`
查询；Session 核心正确性依赖不可空。

其他非发布回归全部通过：G2 **159/159**、G3 **143/143**、G4 **59/59**、G5 **165/165**；
PluginSdk **37/37**、MyPlugTest **11/11**、DaTangAccountingHelpPlug **62/62**、BiliDownloader
**726/726**、MySmallTools **184/184**。Release 全解决方案 `TreatWarningsAsErrors=true` 为零警告、零错误；
v3 API 兼容门禁为 Core 127 / UI 46，SDK 包正负消费、诊断脱敏（104 个生产 C# 文件）均通过。

四插件完成两次确定性 ZIP 构建、包契约检查和本地 Host 加载，摘要均为 `deterministic=true`：

| 插件包 | 文件数 | SHA-256 |
| --- | ---: | --- |
| BiliDownloader 3.0.0 | 14 | `BD9FE55B1B5A748749845CB9D1A2D1A818524C37FA7231F8BD64F42190965791` |
| DaTangAccountingHelpPlug 3.0.0 | 9 | `089B16B4D06C527E097C69C554BFDC93FAAD1959AB5B706D9AC97BBFFB0ED73E` |
| MyPlugTest 3.0.0 | 11 | `299F9E0398E9E0AB5337056BD7EC290128AE2B84CB05AEF0FC2401D5486B32F2` |
| MySmallTools 3.0.0 | 431 | `227D0F8BCB0E8B926CF73D10D592B89F169A93A9AFB0AF810DB220ABCFD7A7D0` |

## 7. 非发布声明

专项摘要 `artifacts/test-results/WorkspaceSessionDockFactory/summary.json` 固定记录：

```text
aiflow=false
windowsCi=false
windowsSmoke=false
releaseAcceptance=false
releaseGate=false
publishable=false
```

本阶段未读取、运行或修改 AIFLOW；未运行 Windows CI、Windows Smoke、ReleaseAcceptance、Host 发布门禁
或发布脚本。Release 仅表示编译配置，本地确定性包和加载验证不构成发布签署。

## 8. 整体回滚边界

若必须回滚，应把 Session、Factory、ReadModel、消费者迁移、专项测试、门禁和文档作为一个变更集整体回到
G5。不得只恢复 `ManagementFactory` Facade，不得保留新旧两个工作区对象，也不得让两个对象同时拥有同一
Document/Tool 集合；否则关闭、失败回滚和退出释放会失去唯一提交点。
