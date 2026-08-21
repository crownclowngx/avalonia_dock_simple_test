# G8：建立布局与生命周期 V2

> 状态：已完成。基线提交为 `3422847`（G7）。
>
> 本阶段明确未使用 AIFLOW，未运行 Windows CI、Windows Smoke、ReleaseAcceptance、发布包或发布门禁。

## 目标与范围

G8 用唯一 `layout-v2.json` 替换生产布局 V1，并把生命周期顺序、超时、状态、诊断和贡献可用性全部收回 Host internal。BiliDownloader 与 MySmallTools Playback Harness 只解除旧 Manager 依赖；G9–G12 的完整插件模型迁移不在本阶段提前实施。

Core SDK 的 `IPluginLifecycle.InitializeAsync/ShutdownAsync` 不变。Legacy `IPluginLifecycle` 仅删除 `Order`，用于尚待迁移的业务插件源码桥；Manager、Runner、PlanBuilder、Options、Registration、状态/阶段 DTO 与 `IPluginLifecycleDependencies` 已从 Legacy public 面删除。

## SOLID 设计

| 组件 | 唯一职责 | 明确不负责 |
| --- | --- | --- |
| `PluginRegistry` | 保存冻结的插件、Document、Tool、View 和 Lifecycle 声明事实 | 运行状态、超时、实例生命周期 |
| `PluginLifecycleCoordinator` | 按 PluginId 编排启动和反向停止，提交一次操作结果 | 保存状态、解析 Provider、菜单或 Dock |
| `PluginLifecycleOperationRunner` | 执行一个带期限和协作取消的回调，观察迟到异常 | 排序、决定可用性、写 UI |
| `PluginLifecycleStateStore` | 保存状态和当前可用所有者集合 | 执行插件代码、解释布局 |
| `PluginAvailabilityReadModel` | 向菜单、Activator、布局和状态 Tool 提供只读查询 | 提供任何写入口 |
| `PluginProviderOwner` | 解析实际 singleton 并适配 SDK/Legacy 两种启动关闭回调 | 决定顺序或状态 |
| `DockLayoutStore` | 路径、原子文件事务和坏文件隔离 | JSON 字段规则、Dock 应用 |
| `DockLayoutSnapshotV2Json` | 严格读取和固定写出唯一 V2 JSON | 文件路径、隔离、运行时 Registry |
| `DockLayoutSnapshotValidator` | 校验纯快照值 | 创建 Tool 或修改 Dock |
| `DockLayoutLifecycle` | 编排 Prepare、预检查、Apply、Save | 兼容迁移或历史 ID 猜测 |

这里使用的模式只有窄端口、只读投影、协调器和不可变快照。没有状态机框架、Mediator、规则引擎、通用仓储或 public Facade。

## 生命周期顺序

启动：

1. manifest、插件 Provider 与不可变 Registry 完成两阶段提交；
2. Host 按规范 PluginId ordinal 正序枚举 Lifecycle 声明；
3. 没有 Lifecycle 的已接受插件和 Host 内建贡献立即可用；
4. 单项进入 `Initializing`，通过 Runner 执行 `InitializeAsync`；
5. 30 秒内成功才进入 `Ready` 并开放菜单、Document、Tool 和布局恢复；
6. 失败或超时只隔离当前插件，后续插件继续；宿主取消停止后续调度并向调用方传播；
7. 全部初始化完成后才解析 `ManagementFactory` 或创建任何插件 Tool/View。

退出：

1. 状态存储和 `ManagementFactory` 禁止新激活；
2. 释放全部 Adapter/View；
3. 反向关闭全部 Document Scope；
4. 清除已经停止工作的 UI `SynchronizationContext`；
5. 只对成功启动项按实际成功顺序反向执行 `ShutdownAsync`，单项期限 10 秒；
6. 单项失败/超时不阻断更早启动项清理；
7. 反向释放插件 Provider；
8. 最后释放 Host Provider。

初始化、关闭和并发重复调用通过同一个异步门保持幂等。超时先提交不可逆结果，再异步请求协作取消并观察迟到 fault；迟到成功或异常都不能把 `TimedOut` 改成 `Ready/Stopped`。

## 状态与失败矩阵

| 场景 | 状态 | 可用性 | 诊断 | 后续动作 |
| --- | --- | --- | --- | --- |
| 无 Lifecycle | 状态 Tool 显示“无需后台生命周期” | 立即可用 | 无 | 正常创建 |
| 初始化成功 | `Ready` | 可用 | 无 | 加入反向停止列表 |
| 初始化异常/空 Task/同步抛错 | `InitializationFailed` | 隔离 | `LIFECYCLE_INITIALIZE_FAILED` | 继续其他插件 |
| 初始化 30 秒超时 | `InitializationTimedOut` | 隔离 | `LIFECYCLE_INITIALIZE_TIMEOUT` | 请求取消，继续其他插件 |
| Host 取消 | `HostCancelled` | 隔离 | `LIFECYCLE_HOST_CANCELLED` | 停止调度并传播取消 |
| 正在关闭 | `Stopping` | 已禁止新建 | 无 | 执行停止回调 |
| 关闭成功 | `Stopped` | 已停止 | 无 | 释放 Provider |
| 关闭异常 | `ShutdownFailed` | 已停止 | `LIFECYCLE_SHUTDOWN_FAILED` | 继续反向清理 |
| 关闭 10 秒超时 | `ShutdownTimedOut` | 已停止 | `LIFECYCLE_SHUTDOWN_TIMEOUT` | 请求取消并继续 |

取消回调本身异常使用 `LIFECYCLE_CANCELLATION_FAILED`。内存、状态 Tool、JSONL 与默认 stderr 只保留 PluginId、阶段、耗时、稳定码和异常类型；异常正文不进入产品诊断。

## Layout V2

当前线格式与完整恢复规则见 [Dock 布局快照 V2](../../reference/dock-layout-snapshot-v2.md)。根、Pane 和 Tool 字段集合均精确固定；`activeToolId` 仅允许字符串或 `null`。注释、尾逗号、未知/重复/缺失/大小写错误字段、错误类型和 schema 1 均整体拒绝。

V2 删除浮动字段、两向到四向 Migrator、历史 Tool ID 归一化和 layout 专用 Legacy ID Map。`Files`、旧短 ID、GUID 和 V1 字段不会映射为当前 Tool。旧 `layout-v1.json` 保持原样。

运行时在修改 Dock 树之前检查 Tool 已注册、生命周期可用且实例完整。插件缺失或初始化失败、Pane/Dock 不存在、非法比例、重复顺序和应用异常都隔离整份快照；Document 和业务状态不会写入文件。

## 消费者调整

- `PluginContributionActivator` 在解析模型前做最后一道可用性校验，阻止绕过菜单直接激活；
- `ManagementFactory` 过滤 Document 菜单、Tool Descriptor、Tool 创建和布局恢复，并在退出开始后拒绝新建；
- 插件状态 Tool 删除依赖列表，显示 Ready、失败/超时、停止状态和“无需生命周期”；
- Bili Scheduler Tool 删除 Host Manager/状态依赖，生命周期类删除 `Order`；插件内部完整 readiness 仍留给 G12；
- Playback Harness 通过 friend access 调用 internal Coordinator，没有新增 Host public Facade；
- `Program.ShutdownPlugins` 兼容入口已删除，退出只由 `HostRuntime` 编排。

## 专项门禁与证据

专项入口：

```powershell
.\scripts\Test-LayoutLifecycleV2.ps1 -Configuration Release -NoRestore
```

脚本串行运行 Host Unit、Plugin、Headless UI、Plugin SDK 与 BiliDownloader 受影响测试，扫描 Layout V1/Migrator/浮动字段/历史 ID 归一化、Legacy public 生命周期类型和 Bili Host Manager 依赖。机器摘要固定记录：

```json
{
  "aiflow": false,
  "windowsCi": false,
  "windowsSmoke": false,
  "releaseGate": false
}
```

最终专项证据为 Host Unit 42、Plugin 65、Headless UI 24、Plugin SDK 5、BiliDownloader 6，
共 **142/142**。Host 全量为 Unit 172、UI 44、Plugin 173，共 **389/389**；行覆盖率 **83.05%**、
分支覆盖率 **68.65%**。新增/受影响关键文件行覆盖率为：

| 文件 | 行覆盖率 | 门槛 |
| --- | ---: | ---: |
| `DockLayoutSnapshotV2Json.cs` | 100% | 95% |
| `DockLayoutSnapshotV2.cs`（含 Validator） | 100% | 95% |
| `PluginLifecycleCoordinator.cs` | 95.93% | 90% |
| `PluginLifecycleOperationRunner.cs` | 94.05% | 90% |
| `PluginLifecycleStateStore.cs` | 98.04% | 90% |
| `PluginProviderOwner.cs` | 93.33% | 既有 90% |

其余实际通过项：

- `dotnet restore MyAvaloniaManagement.sln --locked-mode`；
- Release 全解决方案 `-warnaserror` 构建，0 警告、0 错误；
- Plugin SDK 单元 **32/32**，Core/UI API v2 基线、7 个破坏性负例与兼容新增审阅通过；
- BiliDownloader **719/719**、DaTangAccountingHelpPlug **64/64**、MySmallTools **183/183**；
- 文档核心门禁与完整门禁通过：46 份文档、282 个本地链接、87 个脚本路径、43 个项目路径。

解决方案构建会按项目引用编译 ReleaseAcceptance 工程，但本阶段没有执行任何 ReleaseAcceptance 测试或
脚本，也没有调用 Windows CI/Smoke、发布包或发布门禁。

## 回滚边界

回滚单位是完整 G8 生产代码、测试、脚本与文档，目标是提交 `3422847` 的 G7 默认布局和无生命周期激活状态。禁止选择性恢复 public Manager、依赖图、`Order`、Migrator 或历史 ID Map；回滚也不得恢复读取、迁移或改写 `layout-v1.json`。
