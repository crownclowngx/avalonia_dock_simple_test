# G3：持久化与恢复闭环

> 2026-08-02 复核更新：本文件中原“有界 Channel + Marker”方案已被“每任务最新完整快照 + 去重唤醒 + 不可丢控制命令”替代。`FlushAsync` 现在等待最新完整快照真实提交，SQLite busy/locked 有限重试后仍失败会向调用方传播；恢复同时识别 `.tmp` 与 `.chunkN`，删除会清理追踪状态。复核基线为 `589f5b3`，自动化证据包含高频洪泛、并发 Flush、重复 Shutdown 和分块恢复。

> 实施日期：2026-08-01
>
> 状态：已完成
>
> 平台范围：Windows、Linux；与 G2 相同

## 1. 完成目标

G3 解决的是进度持久化不可靠和恢复事实不一致的问题：

- 高频进度写入从 fire-and-forget 改为 Channel 串行队列，消除旧进度覆盖新进度的风险。
- 阶段边界（完成/失败/暂停/取消）先 Flush 待写入进度，再写终态，再通知 UI。
- 应用关闭时 ShutdownAsync 强制 drain Channel，确保最后一份进度不丢失。
- 错误分类填充 ErrorType 和 IsRetryable，供 UI 展示和重试判断。
- 初始化时以磁盘临时文件事实为准修正数据库中的断点字节数。
- HTTP 206 / Content-Range 精确校验已在 G0 完善，本轮回归验证通过。

## 2. 关键决策

### 2.1 Channel 串行写入 + 序列号保护 + 写入合并

使用 `System.Threading.Channels` 的有界 Channel（容量 256，DropOldest）替代 fire-and-forget。单一消费循环保证写入顺序；写入合并（coalescing）在消费端自然发生：drain 同一 taskId 的所有待写入，只取序列号最大的一条写入 DB。

**为什么不用批量事务**：当前 1-5 并发 + 500ms 节流，实际写入 QPS ≤ 10/s。批量事务需要修改 `IDownloadTaskRepository` 接口，破坏 35+ 项测试，收益不足。

### 2.2 per-task FlushAsync + 全局 ShutdownAsync

`FlushAsync(string taskId)` 向 Channel 写入 per-task FlushMarker，消费循环遇到 marker 时完成对应的 TaskCompletionSource。超时保护 2 秒，超时后记录警告但不阻塞状态转换。

**为什么 per-task 而非全局**：完成/失败时只需 flush 当前任务，不应等待其他 99 个任务的写入。

### 2.3 接口变更最小化

`IDownloadProgressTracker` 仅新增 2 个方法（FlushAsync、ShutdownAsync），不改变现有 4 个方法签名。不修改 `IDownloadTaskRepository` 接口。

### 2.4 错误分类在 Coordinator 而非 Executor

G0 边界设计规定 Executor 只负责执行，错误分类是 Coordinator 的编排职责。使用静态工具类 `DownloadErrorClassifier`（纯函数，无状态，不需要 DI）。

### 2.5 临时文件校验以磁盘事实为准

初始化时对 Interrupted/Paused 任务，以 `FileInfo.Length` 为准修正数据库字节数。只读文件大小，不读内容，不启动下载，不删除文件。

## 3. 代码边界

| 文件 | 职责 |
|------|------|
| `Services/Download/ProgressWriteChannel.cs` | 串行写入队列（Channel + 序列号 + 合并 + Flush 语义） |
| `Services/Download/IDownloadProgressTracker.cs` | 接口扩展：+FlushAsync(taskId) +ShutdownAsync() |
| `Services/Download/DownloadProgressTracker.cs` | 重构：Channel 入队替代 fire-and-forget |
| `Services/Download/BiliDownloadCoordinator.cs` | 阶段边界 Flush + Shutdown + 错误分类 + 临时文件校验 |
| `Services/Download/DownloadErrorClassifier.cs` | 静态错误分类工具（Exception → ErrorType + IsRetryable） |

## 4. 进度写入时序

```
OnProgressChanged (入口节流 500ms)
    │
    ├─ 更新 task 内存状态（UI 绑定立即生效）
    ├─ 递增序列号 → Enqueue 到 Channel
    └─ BroadcastProgress（同步立即广播，不受节流影响）

Channel 消费循环（单线程串行）
    │
    ├─ Drain 所有可读请求
    ├─ 按 (TaskId, Kind) 分组，每组取 max Version
    ├─ 串行 await repository.UpdateXxxAsync(...)
    └─ 处理 FlushMarker → 完成对应 TCS

阶段边界（完成/失败/暂停/取消）
    │
    ├─ await _tracker.FlushAsync(taskId)  ← 等待待写入落盘
    ├─ await _repository.MarkXxxAsync(...)  ← 写终态
    └─ _tracker.BroadcastXxx(task)          ← 通知 UI
```

## 5. 错误分类表

| 异常类型 | ErrorType | IsRetryable | 说明 |
|----------|-----------|-------------|------|
| `DownloadProtocolException` | `cdn` | `true` | CDN Range 响应异常 |
| `HttpRequestException` | `network` | `true` | 网络连接/超时 |
| `TaskCanceledException` | `network` | `true` | 请求超时 |
| `UnauthorizedAccessException` | `auth` | `false` | 权限/登录失效 |
| 消息含 "ffmpeg" | `ffmpeg` | `false` | 合并失败 |
| `IOException`（非 Protocol） | `disk` | `false` | 磁盘/文件错误 |
| 其他 | `unknown` | `false` | 未分类（保守策略） |

## 6. 初始化恢复流程

```
InitializeCoreAsync
    │
    ├─ repository.InitAsync()（建表 + 迁移）
    ├─ 运行中状态 → Interrupted（与 G0 相同）
    └─ G3: 对 Interrupted/Paused 任务调用 ReconcileTempFilesAsync
         │
         ├─ 临时目录不存在 → 字节数归零
         ├─ video.tmp/audio.tmp 存在 → 以 FileInfo.Length 修正
         └─ 文件不存在 → 对应字节数归零
```

## 7. 自动化验证

| 测试文件 | 数量 | 覆盖范围 |
|---------|------|---------|
| `BiliDownloadCoordinatorG3Tests.cs` | 17 | Flush 时序、错误分类、临时文件校验、Channel 合并、重启不自动恢复 |
| `ExtrasAndProgressTests.cs`（G3 适配） | 2 | 节流写入 + 字节更新（适配异步 Channel） |
| 现有测试回归 | 206 | 全部通过，无破坏性变更 |
| **总计** | **225** | |

## 8. 退出条件验证

| 退出条件 | 验证方式 |
|----------|----------|
| 不会出现旧进度覆盖新进度 | 序列号保护 + 写入合并；G3 测试验证 |
| 完成通知不早于数据库提交 | FlushAsync 在 MarkCompleted 之前；RecordingProgressTracker 验证时序 |
| 视频/音频/合并阶段异常→正确显示失败 | 错误分类测试覆盖 network/cdn/ffmpeg/disk/unknown |
| 错误 Range 响应不生成损坏文件 | DownloadProtocolTests 回归通过（G0 已完善） |
| 应用重启不自动恢复网络请求 | 初始化测试验证 executor.ExecuteCount == 0 |
| 关闭时最后一份进度不丢失 | ShutdownAsync drain Channel；G3 测试验证 |

## 9. 明确限制与后续工作

- 进度写入节流间隔硬编码 500ms。G4 任务中心产品化时可考虑根据并发数动态调整。
- FlushAsync 不再静默超时；SQLite 锁有限重试后将失败传播给命令调用方。
- 错误分类已使用下载、鉴权、磁盘、ffmpeg 和资源异常类型；仍可在后续增加更细的协议子类型。
- 临时文件校验只读大小，不验证内容完整性。P2 专业归档阶段的完整性检查可补充哈希校验。
- 删除任务会同步清理最新快照和节流状态。
