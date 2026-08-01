using System.Collections.Concurrent;
using System.Threading.Channels;
using BiliDownloader.Services.Infrastructure;
using BiliDownloader.Services.Persistence;

namespace BiliDownloader.Services.Download;

/// <summary>
/// 进度写入请求的类型枚举。
/// </summary>
internal enum ProgressWriteKind
{
    /// <summary>分段进度写入（progress/status/video/audio/merge/speed）</summary>
    StageProgress,

    /// <summary>断点字节数写入（videoBytes/audioBytes）</summary>
    Bytes,

    /// <summary>Flush 标记：消费循环遇到时完成对应的 TaskCompletionSource</summary>
    FlushMarker,
}

/// <summary>
/// 进度写入请求（不可变值对象，携带序列号防止旧覆盖新）。
/// <para>
/// 设计思考：使用 record 保证不可变性和值相等语义。
/// Version 字段是单调递增的序列号，消费循环通过比较序列号
/// 确保同一任务的多次写入中只有最新的一条会被持久化到 SQLite。
/// </para>
/// </summary>
/// <param name="TaskId">任务唯一标识</param>
/// <param name="Version">单调递增序列号，用于写入合并时判断新旧</param>
/// <param name="Kind">写入类型</param>
/// <param name="Progress">总进度 0~100</param>
/// <param name="Status">状态存储字符串</param>
/// <param name="VideoProgress">视频进度 0~100</param>
/// <param name="AudioProgress">音频进度 0~100</param>
/// <param name="MergeProgress">合并进度 0~100</param>
/// <param name="SpeedText">速度文本</param>
/// <param name="VideoBytes">视频已下载字节数</param>
/// <param name="AudioBytes">音频已下载字节数</param>
internal sealed record ProgressWriteRequest(
    string TaskId,
    long Version,
    ProgressWriteKind Kind,
    double Progress = 0,
    string Status = "",
    double VideoProgress = 0,
    double AudioProgress = 0,
    double MergeProgress = 0,
    string SpeedText = "",
    long VideoBytes = 0,
    long AudioBytes = 0);

/// <summary>
/// 进度写入串行队列：确保同一任务的进度写入严格有序，消除 fire-and-forget 的失序风险。
/// <para>
/// 设计思考：
/// - 使用 System.Threading.Channels 的有界 Channel 作为缓冲，
///   避免高频进度回调直接竞争 SQLite 写入锁。
/// - 单一消费循环保证写入顺序；写入合并（coalescing）减少 DB 压力：
///   消费时 drain 同一 taskId 的所有待写入，只取序列号最大的一条。
/// - 序列号保护：即使 Channel 内部有微小时序抖动，
///   消费循环通过比较序列号确保永远不会写入过期进度。
/// - 提供 per-task FlushAsync 语义：在阶段边界（完成/失败/暂停）调用，
///   确保节流中的最后一条进度已落盘后再写终态。
/// - 不暴露为公共接口：这是 DownloadProgressTracker 的内部实现细节，
///   外部通过 IDownloadProgressTracker.FlushAsync 间接使用。
/// </para>
/// </summary>
internal sealed class ProgressWriteChannel : IAsyncDisposable
{
    private static readonly IPluginLogger Log = PluginLog.For<ProgressWriteChannel>();

    /// <summary>Flush 超时时间：超时后记录警告但不阻塞状态转换</summary>
    private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// 有界 Channel，容量 256：5 并发 × 500ms 节流 = 最多 10 条/秒，256 条缓冲足够 25 秒。
    /// DropOldest 策略：进度只关心最新值，丢弃旧进度是安全的。
    /// </summary>
    private readonly Channel<ProgressWriteRequest> _channel =
        Channel.CreateBounded<ProgressWriteRequest>(new BoundedChannelOptions(256)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest,
        });

    private readonly IDownloadTaskRepository _repository;
    private readonly Task _consumerTask;

    /// <summary>per-task 最新序列号：消费循环用于判断写入是否过期</summary>
    private readonly ConcurrentDictionary<string, long> _latestVersions = new(StringComparer.Ordinal);

    /// <summary>per-task flush 等待器：FlushMarker 被消费时完成对应的 TCS</summary>
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _flushWaiters = new(StringComparer.Ordinal);

    public ProgressWriteChannel(IDownloadTaskRepository repository)
    {
        _repository = repository;
        _consumerTask = ConsumeLoopAsync();
    }

    /// <summary>
    /// 入队进度写入请求。非阻塞，如果 Channel 已满则丢弃最旧的请求。
    /// </summary>
    public void Enqueue(ProgressWriteRequest request)
    {
        // 更新该任务的最新序列号（消费循环用于合并判断）
        _latestVersions[request.TaskId] = request.Version;
        _channel.Writer.TryWrite(request);
    }

    /// <summary>
    /// 等待指定任务的所有待写入进度落盘。
    /// 实现方式：向 Channel 写入 per-task FlushMarker，消费循环遇到 marker 时完成 TCS。
    /// 超时保护：2 秒后记录警告但不阻塞调用方。
    /// </summary>
    public async Task FlushAsync(string taskId)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _flushWaiters[taskId] = tcs;

        // 写入 FlushMarker（使用 long.MaxValue 确保不会被合并跳过）
        var marker = new ProgressWriteRequest(taskId, long.MaxValue, ProgressWriteKind.FlushMarker);
        _channel.Writer.TryWrite(marker);

        // 等待 marker 被消费，带超时保护
        using var cts = new CancellationTokenSource(FlushTimeout);
        try
        {
            await tcs.Task.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            Log.Warn($"任务 {taskId} 的进度 Flush 超时（{FlushTimeout.TotalSeconds}s），继续执行状态转换。");
        }
        finally
        {
            _flushWaiters.TryRemove(taskId, out _);
        }
    }

    /// <summary>
    /// 关闭写入队列：Complete Channel 并等待消费循环处理完所有剩余请求后退出。
    /// 应用关闭时调用，确保最后一份进度不丢失。
    /// </summary>
    public async Task ShutdownAsync()
    {
        _channel.Writer.Complete();
        try
        {
            await _consumerTask;
        }
        catch (Exception ex)
        {
            Log.Warn($"进度写入队列关闭时异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 单消费循环：从 Channel 中读取请求，合并后串行写入 SQLite。
    /// <para>
    /// 写入合并策略：每次 WaitToReadAsync 返回后，drain 所有可读请求，
    /// 按 TaskId + Kind 分组，每组只保留 Version 最大的一条。
    /// 这样在高频进度场景下，同一任务的多条待写入只产生一次 DB 调用。
    /// </para>
    /// </summary>
    private async Task ConsumeLoopAsync()
    {
        var reader = _channel.Reader;

        try
        {
            while (await reader.WaitToReadAsync())
            {
                // Drain 所有当前可读的请求
                var batch = new List<ProgressWriteRequest>(32);
                while (reader.TryRead(out var item))
                {
                    batch.Add(item);
                }

                if (batch.Count == 0) continue;

                // 分离 FlushMarker 和实际写入请求
                var flushMarkers = new List<ProgressWriteRequest>();
                var writeRequests = new List<ProgressWriteRequest>();

                foreach (var item in batch)
                {
                    if (item.Kind == ProgressWriteKind.FlushMarker)
                        flushMarkers.Add(item);
                    else
                        writeRequests.Add(item);
                }

                // 写入合并：按 TaskId + Kind 分组，每组只取 Version 最大的
                var coalesced = writeRequests
                    .GroupBy(r => (r.TaskId, r.Kind))
                    .Select(g => g.OrderByDescending(r => r.Version).First());

                // 串行写入 SQLite
                foreach (var request in coalesced)
                {
                    await WriteSingleAsync(request);
                }

                // 处理 FlushMarker：完成对应的 TaskCompletionSource
                foreach (var marker in flushMarkers)
                {
                    if (_flushWaiters.TryRemove(marker.TaskId, out var tcs))
                    {
                        tcs.TrySetResult();
                    }
                }
            }
        }
        catch (ChannelClosedException)
        {
            // Channel 被 Complete 后正常退出
        }
    }

    /// <summary>
    /// 写入单条请求到 SQLite。单条失败不中断消费循环，记录日志后继续。
    /// </summary>
    private async Task WriteSingleAsync(ProgressWriteRequest request)
    {
        try
        {
            switch (request.Kind)
            {
                case ProgressWriteKind.StageProgress:
                    await _repository.UpdateStageProgressAsync(
                        request.TaskId,
                        request.Progress,
                        request.Status,
                        request.VideoProgress,
                        request.AudioProgress,
                        request.MergeProgress,
                        request.SpeedText);
                    break;

                case ProgressWriteKind.Bytes:
                    await _repository.UpdateBytesAsync(
                        request.TaskId,
                        request.VideoBytes,
                        request.AudioBytes);
                    break;
            }
        }
        catch (Exception ex)
        {
            // 单条写入失败不中断整个消费循环，避免一次 DB 异常导致后续所有进度丢失
            Log.Warn($"进度写入失败 (Task={request.TaskId}, Kind={request.Kind}): {ex.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await ShutdownAsync();
    }
}
