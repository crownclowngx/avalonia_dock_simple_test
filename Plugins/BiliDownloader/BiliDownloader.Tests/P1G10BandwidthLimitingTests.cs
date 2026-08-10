using System.Collections.Concurrent;
using System.Diagnostics;
using BiliDownloader.Models;
using BiliDownloader.Services.Download;
using BiliDownloader.Services.Infrastructure;
using BiliDownloader.Services.Persistence;
using BiliDownloader.ViewModels.BiliDownloader;
using BiliDownloader.ViewModels.BiliScheduler;

namespace BiliDownloader.Tests;

public sealed class P1G10BandwidthLimitingTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(64, 65_536)]
    [InlineData(1024, 1_048_576)]
    public void KiB输入使用统一边界并精确换算(long kib, long expectedBytes)
        => Assert.Equal(expectedBytes, BandwidthLimitPolicy.FromKibibytesPerSecond(kib));

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(63)]
    public void 非法或低于最小粒度的KiB输入会被拒绝(long kib)
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => BandwidthLimitPolicy.FromKibibytesPerSecond(kib));

    [Fact]
    public async Task Limiter输入契约覆盖不限速空量子任务ID上限和释放后调用()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BandwidthLimitPolicy.Validate(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => BandwidthLimitPolicy.Validate(-1, "named"));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BandwidthLimitPolicy.FromKibibytesPerSecond(long.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BandwidthLimitPolicy.ToKibibytesPerSecond(1));
        Assert.Equal(64, BandwidthLimitPolicy.ToKibibytesPerSecond(64 * 1024));
        var limiter = new GlobalBandwidthLimiter(
            new ManualBandwidthClock(), new PluginLogger("g10-test"));

        await limiter.AcquireAsync(0, "", CancellationToken.None);
        await limiter.AcquireAsync(1024, "unlimited", CancellationToken.None);
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            Assert.Throws<OperationCanceledException>(() =>
                limiter.AcquireAsync(1, "cancelled", cancelled.Token));
        }
        Assert.Throws<ArgumentException>(() =>
            limiter.AcquireAsync(1, " ", CancellationToken.None));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            limiter.AcquireAsync(BandwidthLimitPolicy.ReadQuantumBytes + 1, "task", CancellationToken.None));

        limiter.Dispose();
        limiter.Dispose();
        Assert.Throws<ObjectDisposedException>(() =>
            limiter.AcquireAsync(1, "task", CancellationToken.None));
    }

    [Fact]
    public async Task 任务Limiter注册表覆盖缺失更新重复激活和幂等释放()
    {
        var clock = new ManualBandwidthClock();
        var manager = new TaskBandwidthLimitManager(clock, new PluginLogger("g10-task-test"));
        await manager.AcquireAsync(1, "not-active", CancellationToken.None);
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            Assert.Throws<OperationCanceledException>(() =>
                manager.AcquireAsync(1, "not-active", cancelled.Token));
        }
        Assert.False(manager.TryUpdateLimit("not-active", 0, "test missing"));

        var lease = manager.Activate("active", 64 * 1024);
        Assert.Throws<InvalidOperationException>(() => manager.Activate("active", 64 * 1024));
        Assert.True(manager.TryUpdateLimit("active", 128 * 1024, "test active"));
        lease.Dispose();
        lease.Dispose();

        var stillActive = manager.Activate("dispose-with-manager", 0);
        manager.Dispose();
        manager.Dispose();
        stillActive.Dispose();
        Assert.Throws<ObjectDisposedException>(() => manager.Activate("late", 0));
    }

    [Fact]
    public async Task 全局限制使用单调时钟且运行中恢复不限速会立即唤醒等待者()
    {
        var clock = new ManualBandwidthClock();
        using var limiter = new GlobalBandwidthLimiter(clock);
        limiter.UpdateLimit(64 * 1024, "test initial limit");

        await limiter.AcquireAsync(8192, "task-a", CancellationToken.None);
        var waiting = limiter.AcquireAsync(8192, "task-a", CancellationToken.None).AsTask();
        await WaitUntilAsync(() => clock.PendingDelayCount > 0);
        Assert.False(waiting.IsCompleted);

        limiter.UpdateLimit(0, "test resume unlimited");

        await waiting.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(0, limiter.LimitBytesPerSecond);
    }

    [Fact]
    public async Task 任务内多个连接共享同一额度且取消等待不会死锁()
    {
        var clock = new ManualBandwidthClock();
        using var manager = new TaskBandwidthLimitManager(clock);
        using var activation = manager.Activate("task-a", 64 * 1024);
        await manager.AcquireAsync(8192, "task-a", CancellationToken.None);

        using var cts = new CancellationTokenSource();
        var firstConnection = manager.AcquireAsync(8192, "task-a", CancellationToken.None).AsTask();
        var secondConnection = manager.AcquireAsync(8192, "task-a", cts.Token).AsTask();
        await WaitUntilAsync(() => clock.PendingDelayCount > 0);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => secondConnection);

        clock.Advance(TimeSpan.FromMilliseconds(125));
        await firstConnection.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task 释放Limiter会取消全部排队请求且不会遗留等待()
    {
        var clock = new ManualBandwidthClock();
        var limiter = new GlobalBandwidthLimiter(clock);
        limiter.UpdateLimit(64 * 1024, "dispose test");
        await limiter.AcquireAsync(8192, "task", CancellationToken.None);
        var first = limiter.AcquireAsync(8192, "task", CancellationToken.None).AsTask();
        var second = limiter.AcquireAsync(8192, "other", CancellationToken.None).AsTask();
        await WaitUntilAsync(() => clock.PendingDelayCount > 0);

        limiter.Dispose();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
    }

    [Fact]
    public async Task 极高限速仍使用最小延时量子而不会忙等()
    {
        var clock = new ManualBandwidthClock();
        using var limiter = new GlobalBandwidthLimiter(clock);
        limiter.UpdateLimit(1024L * 1024 * 1024, "minimum delay branch");
        await limiter.AcquireAsync(8192, "task", CancellationToken.None);
        var waiting = limiter.AcquireAsync(1, "task", CancellationToken.None).AsTask();
        await WaitUntilAsync(() => clock.PendingDelayCount > 0);

        clock.Advance(TimeSpan.FromMilliseconds(1));

        await waiting.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task 多任务共享全局额度时按任务轮询而不是让单任务连接淹没队列()
    {
        var clock = new ManualBandwidthClock();
        using var limiter = new GlobalBandwidthLimiter(clock);
        limiter.UpdateLimit(64 * 1024, "fairness test");
        await limiter.AcquireAsync(8192, "warmup", CancellationToken.None);

        var a1 = limiter.AcquireAsync(8192, "task-a", CancellationToken.None).AsTask();
        var a2 = limiter.AcquireAsync(8192, "task-a", CancellationToken.None).AsTask();
        var b1 = limiter.AcquireAsync(8192, "task-b", CancellationToken.None).AsTask();
        await WaitUntilAsync(() => clock.PendingDelayCount > 0);

        clock.Advance(TimeSpan.FromMilliseconds(125));
        await WaitUntilAsync(() => a1.IsCompleted);
        Assert.False(a2.IsCompleted);
        Assert.False(b1.IsCompleted);

        await WaitUntilAsync(() => clock.PendingDelayCount > 0);
        clock.Advance(TimeSpan.FromMilliseconds(125));
        await WaitUntilAsync(() => b1.IsCompleted);
        Assert.False(a2.IsCompleted);

        await WaitUntilAsync(() => clock.PendingDelayCount > 0);
        clock.Advance(TimeSpan.FromMilliseconds(125));
        await Task.WhenAll(a1, a2, b1).WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task 下载器在每次网络读取前申请不超过8KiB且携带真实任务ID()
    {
        var payload = Enumerable.Range(0, 80_000).Select(i => (byte)(i % 251)).ToArray();
        await using var server = LoopbackHttpServer.Create(_ => LoopbackResponse.Bytes(payload));
        using var paths = new TestDataPaths();
        Directory.CreateDirectory(paths.RootDirectory);
        var output = Path.Combine(paths.RootDirectory, "limited.bin");
        var spy = new RecordingBandwidthLimiter();
        using var client = new HttpClient();
        using var downloader = new MultiConnectionDownloader(
            client, new SystemDownloadRuntime(), chunkCount: 1, bandwidthLimiter: spy);

        await downloader.DownloadAsync(
            [server.Url("media")], output, "", "real-task-id", (_, _, _) => { }, CancellationToken.None);

        Assert.Equal(payload, await File.ReadAllBytesAsync(output));
        Assert.NotEmpty(spy.Requests);
        Assert.All(spy.Requests, request =>
        {
            Assert.Equal("real-task-id", request.TaskId);
            Assert.InRange(request.Bytes, 1, BandwidthLimitPolicy.ReadQuantumBytes);
        });
        Assert.True(spy.Requests.Sum(request => (long)request.Bytes) >= payload.LongLength);
    }

    [Fact]
    public async Task 全局设置损坏时回退不限速而有效更新先持久化再热应用()
    {
        var settings = new MemorySettingsRepository
        {
            Values = { [GlobalBandwidthLimitService.SettingKey] = "1024" },
        };
        var controller = new RecordingGlobalController();
        var service = new GlobalBandwidthLimitService(settings, controller);

        await service.InitializeAsync();
        Assert.Equal(0, service.CurrentBytesPerSecond);
        Assert.Equal(0, controller.LimitBytesPerSecond);
        Assert.Equal("1024", settings.Values[GlobalBandwidthLimitService.SettingKey]);

        await service.UpdateAsync(128 * 1024, "test UI");
        Assert.Equal(128 * 1024, service.CurrentBytesPerSecond);
        Assert.Equal("131072", settings.Values[GlobalBandwidthLimitService.SettingKey]);
        Assert.Equal("131072", controller.PersistedValueObservedWhenApplied);
    }

    [Fact]
    public async Task 全局设置初始化幂等并覆盖缺失值有效值和非法更新()
    {
        var missingSettings = new MemorySettingsRepository();
        var missingController = new RecordingGlobalController();
        var missing = new GlobalBandwidthLimitService(
            missingSettings, missingController, new PluginLogger("g10-settings-test"));
        await missing.InitializeAsync();
        await missing.InitializeAsync();
        Assert.Equal(0, missing.CurrentBytesPerSecond);

        var validSettings = new MemorySettingsRepository
        {
            Values = { [GlobalBandwidthLimitService.SettingKey] = "65536" },
        };
        var valid = new GlobalBandwidthLimitService(validSettings, new RecordingGlobalController());
        await valid.InitializeAsync();
        Assert.Equal(64 * 1024, valid.CurrentBytesPerSecond);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            valid.UpdateAsync(1, "invalid"));
    }

    [Fact]
    public async Task SQLite迁移往返并拒绝修改已完成任务的限速历史事实()
    {
        using var paths = new TestDataPaths();
        await new BiliLocalStateInitializer(paths).InitializeAsync();
        var store = new DownloadTaskStore(paths);
        await store.InitAsync();
        var record = CreateRecord("rate-task", 96 * 1024);
        await store.InsertBatchAsync([record]);

        var loaded = Assert.Single(await store.GetAllAsync());
        Assert.Equal(96 * 1024, loaded.TaskRateLimitBytesPerSecond);
        await store.UpdateTaskRateLimitAsync(record.TaskId, 256 * 1024, DateTime.Now);
        Assert.Equal(256 * 1024, Assert.Single(await store.GetAllAsync()).TaskRateLimitBytesPerSecond);

        await store.MarkCompletedAsync(record.TaskId, "done.mp4", null, DateTime.Now);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.UpdateTaskRateLimitAsync(record.TaskId, 0, DateTime.Now));
    }

    [Fact]
    public async Task Coordinator限速更新覆盖活动非活动完成和非法值且不改变任务状态()
    {
        using var paths = new TestDataPaths();
        var repository = new InMemoryDownloadTaskRepository();
        var manager = new RecordingTaskLimitManager();
        var coordinator = new BiliDownloadCoordinator(
            repository,
            new IsolatedMessengerService(),
            new NoOpDownloadProgressTracker(),
            new FakeDownloadTaskExecutor(),
            paths,
            new FakeCredentialProvider(),
            taskBandwidthLimits: manager);
        var pending = CreateRecord("pending-rate", 0);
        repository.Seed(pending);

        manager.UpdateResult = false;
        await coordinator.UpdateTaskRateLimitAsync(pending, 64 * 1024);
        Assert.Equal("pending", pending.Status);
        Assert.Equal(64 * 1024, pending.TaskRateLimitBytesPerSecond);
        Assert.False(manager.LastUpdateApplied);

        manager.UpdateResult = true;
        await coordinator.UpdateTaskRateLimitAsync(pending, 128 * 1024);
        Assert.True(manager.LastUpdateApplied);
        Assert.Equal(128 * 1024, manager.LastBytesPerSecond);

        var completed = CreateRecord("complete-rate", 0);
        completed.Status = "done";
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.UpdateTaskRateLimitAsync(completed, 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            coordinator.UpdateTaskRateLimitAsync(pending, 1));
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public void Document限速编辑器在启用关闭换算和非法值之间保持一致()
    {
        var vm = new DownloadConfigViewModel(new MemorySettingsRepository());

        Assert.False(vm.IsPerTaskRateLimitEnabled);
        Assert.Equal(1024, vm.PerTaskRateLimitKiBPerSecond);
        vm.IsPerTaskRateLimitEnabled = false;
        vm.IsPerTaskRateLimitEnabled = true;
        vm.IsPerTaskRateLimitEnabled = true;
        Assert.Equal(BandwidthLimitPolicy.DefaultEditorBytesPerSecond,
            vm.PerTaskRateLimitBytesPerSecond);

        vm.PerTaskRateLimitKiBPerSecond = 64;
        Assert.Equal(64 * 1024, vm.PerTaskRateLimitBytesPerSecond);
        vm.IsPerTaskRateLimitEnabled = false;
        Assert.Equal(0, vm.PerTaskRateLimitBytesPerSecond);
        Assert.Throws<ArgumentOutOfRangeException>(() => vm.PerTaskRateLimitKiBPerSecond = 63);

        vm.ApplyPreset(new DownloadPreset
        {
            Id = "legacy-invalid-limit",
            Name = "legacy",
            PerTaskRateLimitBytesPerSecond = 1,
        });
        Assert.Equal(0, vm.PerTaskRateLimitBytesPerSecond);
    }

    [Fact]
    public async Task Tool全局限速编辑覆盖加载应用关闭无服务和保存失败分支()
    {
        var service = new StubGlobalLimitService { CurrentBytesPerSecond = 128 * 1024 };
        var vm = new SchedulerSettingsViewModel(
            new MemorySettingsRepository(), new FakeFfmpegService(), globalBandwidthLimit: service);

        await vm.LoadSettingsAsync();
        Assert.True(vm.IsGlobalRateLimitEnabled);
        Assert.Equal(128, vm.GlobalRateLimitKiBPerSecond);

        vm.IsGlobalRateLimitEnabled = false;
        await vm.ApplyGlobalRateLimitCommand.ExecuteAsync(null);
        Assert.Equal(0, service.LastUpdatedBytesPerSecond);
        Assert.Contains("取消", vm.BandwidthLimitStatus, StringComparison.Ordinal);

        vm.IsGlobalRateLimitEnabled = true;
        vm.GlobalRateLimitKiBPerSecond = 64;
        await vm.ApplyGlobalRateLimitCommand.ExecuteAsync(null);
        Assert.Equal(64 * 1024, service.LastUpdatedBytesPerSecond);
        Assert.Contains("64 KiB/s", vm.BandwidthLimitStatus, StringComparison.Ordinal);

        var noService = new SchedulerSettingsViewModel(
            new MemorySettingsRepository(), new FakeFfmpegService());
        await noService.ApplyGlobalRateLimitCommand.ExecuteAsync(null);
        Assert.Contains("未提供", noService.BandwidthLimitStatus, StringComparison.Ordinal);

        service.UpdateException = new IOException("disk unavailable");
        await vm.ApplyGlobalRateLimitCommand.ExecuteAsync(null);
        Assert.Contains("原设置保持不变", vm.BandwidthLimitStatus, StringComparison.Ordinal);

        var unlimitedService = new StubGlobalLimitService { CurrentBytesPerSecond = 0 };
        var unlimitedVm = new SchedulerSettingsViewModel(
            new MemorySettingsRepository(), new FakeFfmpegService(), globalBandwidthLimit: unlimitedService);
        await unlimitedVm.LoadSettingsAsync();
        Assert.False(unlimitedVm.IsGlobalRateLimitEnabled);
        Assert.Contains("不限速", unlimitedVm.BandwidthLimitStatus, StringComparison.Ordinal);
        unlimitedVm.DefaultOutputDirectory = "";
        unlimitedVm.MaxConcurrentDownloads = 2;

        var installerVm = new SchedulerSettingsViewModel(
            new MemorySettingsRepository(), new FakeFfmpegService(), new FailingFfmpegInstaller());
        await installerVm.InstallOrRepairFfmpegCommand.ExecuteAsync(null);
        Assert.Contains("test install rejected", installerVm.FfmpegStatus, StringComparison.Ordinal);
    }

    [Fact]
    public void 任务卡片区分配置事实与观测速率并验证编辑边界()
    {
        var record = CreateRecord("item", 0);
        record.SpeedText = "observed 2 MiB/s";
        var item = new DownloadTaskItemViewModel(record);

        Assert.True(item.IsRateLimitEditable);
        Assert.False(item.IsRateLimitEnabled);
        Assert.Equal("任务限速：不限速", item.TaskRateLimitDisplayText);
        Assert.Equal("observed 2 MiB/s", item.SpeedText);

        item.IsRateLimitEnabled = true;
        item.RateLimitKiBPerSecond = 64;
        Assert.Equal(64 * 1024, item.GetRequestedRateLimitBytesPerSecond());
        item.MarkRateLimitApplied(64 * 1024);
        Assert.Contains("64 KiB/s", item.TaskRateLimitDisplayText, StringComparison.Ordinal);

        item.RefreshFrom(record);
        var externallyChanged = CreateRecord("item", 256 * 1024);
        externallyChanged.SpeedText = "new observation";
        item.RefreshFrom(externallyChanged);
        Assert.Equal(256, item.RateLimitKiBPerSecond);

        item.RateLimitKiBPerSecond = 63;
        Assert.Throws<ArgumentOutOfRangeException>(() => item.GetRequestedRateLimitBytesPerSecond());
        item.IsRateLimitEnabled = false;
        Assert.Equal(0, item.GetRequestedRateLimitBytesPerSecond());

        var completed = CreateRecord("done", 0);
        completed.Status = "done";
        Assert.False(new DownloadTaskItemViewModel(completed).IsRateLimitEditable);

        var noFailure = CreateRecord("no-failure", 0);
        noFailure.ErrorMessage = "plain message";
        noFailure.ErrorType = null;
        var noFailureItem = new DownloadTaskItemViewModel(noFailure);
        Assert.Equal("plain message", noFailureItem.ErrorMessage);
        var authFailure = CreateRecord("auth-failure", 0);
        authFailure.ErrorType = "auth";
        Assert.Null(new DownloadTaskItemViewModel(authFailure).SecondaryFailureActionRequest);
    }

    private static DownloadTaskRecord CreateRecord(string taskId, long limit) => new()
    {
        TaskId = taskId,
        DocumentId = "doc",
        SourceDocumentTitle = "source",
        SeriesTitle = "series",
        ItemTitle = "item",
        Bvid = "BV1TEST0001",
        QualityId = 80,
        SubmissionSnapshotVersion = 4,
        TaskRateLimitBytesPerSecond = limit,
        OutputDirectory = "output",
        Status = "pending",
        CreatedAt = DateTime.Now,
        LastUpdatedAt = DateTime.Now,
    };

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = Stopwatch.StartNew();
        while (!condition())
        {
            if (timeout.Elapsed > TimeSpan.FromSeconds(2))
                throw new TimeoutException("异步限速器未在门限内进入预期状态。");
            await Task.Yield();
        }
    }

    private sealed class RecordingBandwidthLimiter : IBandwidthLimiter
    {
        public ConcurrentQueue<(int Bytes, string TaskId)> Requests { get; } = new();
        public ValueTask AcquireAsync(int bytes, string taskId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Enqueue((bytes, taskId));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ManualBandwidthClock : IBandwidthClock
    {
        private sealed record Delay(long Due, TaskCompletionSource Completion);
        private readonly object _sync = new();
        private readonly List<Delay> _delays = [];
        private long _timestamp;

        public int PendingDelayCount { get { lock (_sync) return _delays.Count; } }
        public long GetTimestamp() => Interlocked.Read(ref _timestamp);
        public TimeSpan GetElapsedTime(long startingTimestamp, long endingTimestamp)
            => TimeSpan.FromTicks(Math.Max(0, endingTimestamp - startingTimestamp));

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            if (delay <= TimeSpan.Zero) return Task.CompletedTask;
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var scheduled = new Delay(GetTimestamp() + delay.Ticks, completion);
            lock (_sync) _delays.Add(scheduled);
            cancellationToken.Register(() =>
            {
                lock (_sync) _delays.Remove(scheduled);
                completion.TrySetCanceled(cancellationToken);
            });
            return completion.Task;
        }

        public void Advance(TimeSpan elapsed)
        {
            var now = Interlocked.Add(ref _timestamp, elapsed.Ticks);
            List<Delay> due;
            lock (_sync)
            {
                due = _delays.Where(item => item.Due <= now).ToList();
                foreach (var item in due) _delays.Remove(item);
            }
            foreach (var item in due) item.Completion.TrySetResult();
        }
    }

    private sealed class MemorySettingsRepository : ISettingsRepository
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
        public Task InitAsync() => Task.CompletedTask;
        public Task<string?> GetSettingAsync(string key)
            => Task.FromResult(Values.GetValueOrDefault(key));
        public Task SetSettingAsync(string key, string value)
        {
            Values[key] = value;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingGlobalController : IGlobalBandwidthLimitController
    {
        public long LimitBytesPerSecond { get; private set; }
        public string? PersistedValueObservedWhenApplied { get; private set; }
        public void UpdateLimit(long bytesPerSecond, string reason)
        {
            LimitBytesPerSecond = bytesPerSecond;
            PersistedValueObservedWhenApplied = bytesPerSecond.ToString();
        }
    }

    private sealed class StubGlobalLimitService : IGlobalBandwidthLimitService
    {
        public long CurrentBytesPerSecond { get; set; }
        public long LastUpdatedBytesPerSecond { get; private set; } = -1;
        public Exception? UpdateException { get; set; }
        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
        public Task UpdateAsync(long bytesPerSecond, string reason, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (UpdateException is not null) throw UpdateException;
            LastUpdatedBytesPerSecond = bytesPerSecond;
            CurrentBytesPerSecond = bytesPerSecond;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingTaskLimitManager : ITaskBandwidthLimitManager
    {
        public bool UpdateResult { get; set; }
        public bool LastUpdateApplied { get; private set; }
        public long LastBytesPerSecond { get; private set; }
        public IDisposable Activate(string taskId, long bytesPerSecond) => new NoOpLease();
        public bool TryUpdateLimit(string taskId, long bytesPerSecond, string reason)
        {
            LastBytesPerSecond = bytesPerSecond;
            LastUpdateApplied = UpdateResult;
            return UpdateResult;
        }
        public ValueTask AcquireAsync(int bytes, string taskId, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
        private sealed class NoOpLease : IDisposable { public void Dispose() { } }
    }

    private sealed class FailingFfmpegInstaller : IFfmpegPackageInstaller
    {
        public bool IsInstalling => false;
        public event Action<FfmpegInstallProgress>? ProgressChanged { add { } remove { } }
        public Task<FfmpegInstallResult> InstallOrRepairAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(FfmpegInstallResult.Failed("test install rejected"));
    }
}
