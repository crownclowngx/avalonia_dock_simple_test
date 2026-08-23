using System.Collections;
using System.Reflection;
using BiliDownloader.Models;
using BiliDownloader.Services.Download;

namespace BiliDownloader.Tests;

/// <summary>
/// G14 覆盖率确定性测试。
/// </summary>
/// <remarks>
/// 这些测试不增加产品能力，只把原本依赖线程碰巧交错的关闭与合并路径改成显式同步。
/// 每个用例只验证一个基础设施职责：协调器收敛或进度单写者合并，避免把多个生产对象
/// 拼成难以定位失败原因的“大集成测试”。
/// </remarks>
public sealed class G14CoverageDeterminismTests
{
    [Fact]
    public async Task Coordinator_PrivateConvergencePaths_AreAwaitedDeterministically()
    {
        var repository = new InMemoryDownloadTaskRepository();
        using var eventBus = new IsolatedBiliDownloaderEventBus();
        using var paths = new TestDataPaths();
        var coordinator = new BiliDownloadCoordinator(
            repository,
            eventBus,
            new NoOpDownloadProgressTracker(),
            new FakeDownloadTaskExecutor(),
            paths,
            new FakeCredentialProvider());
        var processingNotifications = new List<bool>();
        coordinator.IsProcessingChanged += processingNotifications.Add;

        // 预取消令牌使队列循环在第一次条件判断就退出，稳定覆盖“尚未领取任务即关闭”分支。
        // 方法保持私有，因为它只属于协调器实现；测试通过反射等待既有 Task，而不污染 public API。
        SetPrivateField(coordinator, "_isProcessing", true);
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            await InvokePrivateTaskAsync(
                coordinator,
                "ProcessQueueAsync",
                cancellation.Token);
        }

        Assert.False(coordinator.IsProcessing);
        Assert.Contains(false, processingNotifications);

        // StopProcessing 的活动任务数组是兜底收敛路径：普通下载循环通常已经自行移除任务，
        // 若用真实线程竞速则覆盖率会随机器负载变化。这里登记一个已取消完成项，精确验证
        // WhenAll 的取消被吸收、上下文被释放、字典被清空，不改变任何生产可见入口。
        using var parentCancellation = new CancellationTokenSource();
        var context = TaskRuntimeContext.CreateLinked("g14-active", parentCancellation.Token);
        parentCancellation.Cancel();
        var cancelledCompletion = Task.FromCanceled(parentCancellation.Token);
        var activeRunsField = GetPrivateField(coordinator, "_activeRuns");
        var activeRuns = (IDictionary)(activeRunsField.GetValue(coordinator)
            ?? throw new InvalidOperationException("活动任务字典为空。"));
        var activeRunType = typeof(BiliDownloadCoordinator).GetNestedType(
            "ActiveTaskRun",
            BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("找不到活动任务记录类型。");
        var activeRun = Activator.CreateInstance(
            activeRunType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [context, cancelledCompletion],
            culture: null)
            ?? throw new InvalidOperationException("无法创建活动任务记录。 ");
        activeRuns.Add("g14-active", activeRun);

        await InvokePrivateTaskAsync(coordinator, "StopProcessingInternalAsync");

        Assert.Empty(activeRuns);

        // ShutdownCore 还需要收敛一个“队列循环已以取消完成、但字段尚未清空”的窗口。
        // 直接登记已取消 Task，避免依赖 continuation 与 Shutdown 谁先取得命令锁。
        var processingCancellation = new CancellationTokenSource();
        processingCancellation.Cancel();
        SetPrivateField(coordinator, "_processingCts", processingCancellation);
        SetPrivateField(
            coordinator,
            "_processingTask",
            Task.FromCanceled(processingCancellation.Token));
        SetPrivateField(coordinator, "_isProcessing", true);
        await coordinator.ShutdownAsync();
        Assert.False(coordinator.IsProcessing);

        // await 已取消任务会直接跳入 catch，不会经过 await 后的序列点；再用一个已成功
        // 完成项调用同一幂等关闭核心，确保“正常完成”和“取消完成”两种收敛均被固定覆盖。
        var completedProcessingCancellation = new CancellationTokenSource();
        SetPrivateField(coordinator, "_processingCts", completedProcessingCancellation);
        SetPrivateField(coordinator, "_processingTask", Task.CompletedTask);
        SetPrivateField(coordinator, "_isProcessing", true);
        await InvokePrivateTaskAsync(coordinator, "ShutdownCoreAsync");
        Assert.False(coordinator.IsProcessing);
    }

    [Fact]
    public async Task ProgressChannel_DirtyDuringSignalAndFlush_PersistsLatestVersion()
    {
        var repository = new InMemoryDownloadTaskRepository();
        repository.Seed(new DownloadTaskRecord
        {
            TaskId = "g14-progress",
            DocumentId = "g14",
            ItemTitle = "g14-progress",
            Status = "pending"
        });

        var firstEntered = NewSignal();
        var releaseFirst = NewSignal();
        var secondEntered = NewSignal();
        var releaseSecond = NewSignal();
        ProgressWriteChannel? channel = null;
        repository.BeforeStageProgressUpdateAsync = async updateNumber =>
        {
            switch (updateNumber)
            {
                case 1:
                    firstEntered.TrySetResult();
                    await releaseFirst.Task;
                    break;
                case 2:
                    secondEntered.TrySetResult();
                    await releaseSecond.Task;
                    break;
                case 3:
                    // Flush 正在持久化版本 3 时再上报版本 4，迫使 Flush 的 do/while
                    // 明确执行第二轮；最终实体值因此也能证明“最新版本获胜”。
                    channel!.Enqueue(StageRequest(progress: 40));
                    break;
            }
        };

        channel = new ProgressWriteChannel(repository);
        channel.Enqueue(StageRequest(progress: 10));
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        channel.Enqueue(StageRequest(progress: 20));
        releaseFirst.TrySetResult();

        await secondEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var flush = channel.FlushAsync("g14-progress");
        channel.Enqueue(StageRequest(progress: 30));
        releaseSecond.TrySetResult();

        await flush.WaitAsync(TimeSpan.FromSeconds(2));
        await channel.ShutdownAsync();

        Assert.Equal(
            40,
            repository.Tasks.Single(task => task.TaskId == "g14-progress").Progress);
    }

    private static ProgressWriteRequest StageRequest(double progress) =>
        new(
            "g14-progress",
            Version: (long)progress,
            ProgressWriteKind.StageProgress,
            Progress: progress,
            Status: "downloading_video");

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static FieldInfo GetPrivateField(object target, string name) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"找不到字段 {name}。");

    private static void SetPrivateField(object target, string name, object value) =>
        GetPrivateField(target, name).SetValue(target, value);

    private static async Task InvokePrivateTaskAsync(
        object target,
        string methodName,
        params object?[] arguments)
    {
        var method = target.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"找不到方法 {methodName}。");
        var task = method.Invoke(target, arguments) as Task
            ?? throw new InvalidOperationException($"方法 {methodName} 没有返回 Task。");
        await task.WaitAsync(TimeSpan.FromSeconds(2));
    }
}
