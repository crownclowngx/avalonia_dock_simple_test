# G2：单任务控制内核

> 实施日期：2026-07-23
>
> 状态：已完成
>
> 平台范围：Windows、Linux；与 G1 相同

## 1. 完成目标

G2 解决的是下载调度器缺乏 per-task 精确控制的问题：

- 暂停单个任务不影响其他并发任务，暂停保留断点（已下载字节数）。
- 恢复暂停任务从断点继续，状态回到 Ready 由队列重新调度。
- 取消单个任务通过 per-task CTS 传播，不影响其他任务；取消删除临时文件但保留成品。
- 重新开始任务清理旧断点和临时文件，从零执行。
- 删除活动任务不再停止全部队列再重启，而是通过 per-task CTS 精确取消。
- 无有效登录态时任务进入 WaitingForLogin，登录成功后自动恢复。
- 并发数下调时优雅暂停超额任务（LIFO），而非暴力取消。
- 批量控制入口：全部暂停、全部恢复、全部取消、全部重新开始。

## 2. 关键决策

### 2.1 使用 LinkedTokenSource 而非独立 CTS

每个活动任务创建 `CancellationTokenSource.CreateLinkedTokenSource(parentToken)`，链接全局取消与单任务取消。全局停止（StopProcessingAsync）取消父令牌，自动传播到所有子令牌；单任务取消只取消自己的子令牌，不影响其他任务。

### 2.2 暂停语义：取消+标记而非阻塞

暂停不是让线程阻塞在 `ManualResetEventSlim.Wait()` 上（会导致任务永远留在活动集合中无法重新调度）。而是：
1. `RequestStop(TaskStopReason.Pause)` 固化停止原因并取消 per-task CTS
2. 执行器在下一个取消点抛出 `OperationCanceledException`
3. Coordinator 根据不可变的 `context.StopReason` 将状态设为 Paused
4. 暂停命令等待旧执行完成状态写入并从 `_activeRuns` 移除，恢复时由队列创建全新上下文重新调度

`Context` 与执行完成任务封装在同一个 `ActiveTaskRun` 中，并在执行器启动前原子登记。控制命令因此不会遇到“已观察到执行开始，却还找不到可等待的执行任务”的窗口。

### 2.3 不改变 IDownloadTaskExecutor 接口

取消检查点在 `ExecuteAsync` 调用前（`context.ThrowIfCancellationRequested()`）。执行中的暂停响应依赖 CancellationToken 传播。不增加接口重载，避免破坏现有实现和测试。

### 2.4 四种 OperationCanceledException 语义区分

| 条件 | 状态 | 含义 |
|------|------|------|
| `context.StopReason == TaskStopReason.Pause` | Paused | 用户暂停，保留断点 |
| `_isShuttingDown` | Interrupted | 宿主关闭，需手动恢复 |
| `context.IsParentCancelled` | Ready | 全局停止，放回队列 |
| 其他 | Canceled | 单任务取消 |

### 2.5 构造函数向后兼容

`IBiliCredentialProvider` 参数设为可选（`= null`），未注入时使用内部 `NullCredentialProvider`（`IsLoggedIn => true`），确保现有测试和调用点无需修改。

## 3. 代码边界

| 文件 | 职责 |
|------|------|
| `Services/Download/TaskRuntimeContext.cs` | per-task 控制原语（CTS + 不可变停止原因 + 父令牌引用） |
| `Services/Download/BiliDownloadCoordinator.cs` | 调度器：原子登记的 ActiveTaskRun、控制方法、批量方法、WaitingForLogin |
| `ViewModels/BiliScheduler/SchedulerTaskListViewModel.cs` | UI 命令绑定：Pause/Resume/Cancel/Restart/PauseAll/ResumeAll |
| `Converters/TaskControlConverters.cs` | 状态可见性转换器（4 个） |
| `Converters/StatusToColorConverter.cs` | 新增暂停/等待登录/已取消颜色 |
| `Views/BiliScheduler/SchedulerTaskListView.axaml` | 任务行控制按钮 |
| `Views/BiliSchedulerToolView.axaml` | 工具栏批量按钮 |

## 4. 状态流转

```
Ready → FetchingMetadata → DownloadingVideo → DownloadingAudio → Merging → Completed
  ↑                                                                        ↓
  |←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←|
  |                              (全局停止 → Ready)                         |
  |                                                                        |
  |←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←|
  |                              (暂停 → Paused → 恢复)                     |
  |                                                                        |
  |←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←←|
  |                         (重新开始 → 清理 → Ready)                       |
  ↓
WaitingForLogin → (登录成功) → Ready
Canceled (终态，可重新开始)
Failed (终态，可重试/重新开始)
Interrupted (终态，可重试/重新开始)
```

## 5. 文件操作语义

| 操作 | 临时文件 | 成品文件 | 数据库记录 | 可恢复 |
|------|---------|---------|-----------|--------|
| 暂停 | 保留 | 不影响 | Paused | 是 |
| 继续 | 从断点继续 | 不影响 | Ready→运行中 | - |
| 取消 | 删除 | 保留 | Canceled | 可重新开始 |
| 重新开始 | 删除 | 保留 | Ready(进度0) | - |
| 删除 | 删除 | 删除 | 移除 | 否 |

## 6. 批量控制

| 方法 | 行为 |
|------|------|
| `PauseAllActiveAsync` | 暂停所有活动上下文中的任务 |
| `ResumeAllPausedAsync` | 恢复所有状态为 Paused 的任务 |
| `CancelAllActiveAsync` | 取消所有活动上下文中的任务 |
| `RestartAllStalledAsync` | 重新开始所有 Failed/Interrupted/Canceled 任务 |

## 7. 自动化验证

| 测试文件 | 数量 | 覆盖范围 |
|---------|------|---------|
| `TaskRuntimeContextTests.cs` | 12 | 控制原语语义（不可变停止原因、取消传播、新上下文恢复、清理竞态与幂等） |
| `BiliDownloadCoordinatorG2Tests.cs` | 36 | Coordinator 集成（暂停/取消/重新开始/删除/WaitingForLogin/并发/批量/竞态回归） |
| `PresentationLogicTests.cs` (G2 部分) | 5 | Converter 状态识别和颜色映射 |
| `BiliDownloader.Tests` 全套（2026-08-10） | 724 | 全部通过，无破坏性变更 |

## 8. 明确限制与后续工作

- Coordinator 在 `ExecuteAsync` 调用前检查一次取消；进入执行器后的暂停响应取决于内部对 CancellationToken 的响应速度。下载循环应持续保留取消检查点。
- 并发数缩减有 200ms 等待自然完成的延迟，极端情况下可能短暂超额。
- WaitingForLogin 仅在执行前检查。执行中登出不会中断正在进行的下载。
- 暂停后恢复是"重新调度"语义（从 Ready 重新开始），不是"线程恢复"语义。对于已执行到一半的任务，恢复后执行器需要自行利用断点字节数实现续传。
