using System.IO.Compression;
using System.Security.Cryptography;
using BiliDownloader.Models;
using BiliDownloader.Services.Auth;
using BiliDownloader.Services.Download;
using BiliDownloader.Services.Infrastructure;
using BiliDownloader.Services.Persistence;

namespace BiliDownloader.Tests;

/// <summary>
/// G7 可信安装、结构化错误行动与仅合并重试验收。
/// 测试全部使用临时目录、内存 ZIP 和假进程，不访问真实下载站或启动本机 ffmpeg。
/// </summary>
public sealed class G7FfmpegAndFailureActionTests
{
    [Fact]
    public async Task 固定摘要正确_安全解压验证后原子激活托管版本()
    {
        using var paths = new TestDataPaths();
        Directory.CreateDirectory(paths.TempDirectory);
        var package = CreatePackage(("ffmpeg-8.1.2-essentials_build/bin/ffmpeg.exe", "binary"));
        var processFactory = ValidProcessFactory();
        var locator = new FfmpegService(processFactory, paths);
        var installer = CreateInstaller(paths, package, locator, Hash(package));

        var result = await installer.InstallOrRepairAsync();

        Assert.True(result.Success, result.Message);
        Assert.NotNull(result.ExecutablePath);
        Assert.True(File.Exists(result.ExecutablePath));
        Assert.True(File.Exists(paths.FfmpegCurrentPointerPath));
        Assert.Equal(Path.GetFullPath(result.ExecutablePath!), Path.GetFullPath(locator.ResolveFfmpegPath()!));
        Assert.Empty(Directory.GetDirectories(paths.TempDirectory, "ffmpeg-install-*"));
    }

    [Fact]
    public async Task 摘要不匹配_旧活动指针保持逐字不变且不激活新包()
    {
        using var paths = new TestDataPaths();
        Directory.CreateDirectory(paths.TempDirectory);
        Directory.CreateDirectory(paths.FfmpegDependencyDirectory);
        const string oldPointer = "{\"Version\":\"old\",\"RelativeExecutablePath\":\"versions/old/bin/ffmpeg.exe\"}";
        await File.WriteAllTextAsync(paths.FfmpegCurrentPointerPath, oldPointer);
        var package = CreatePackage(("build/bin/ffmpeg.exe", "binary"));
        var locator = new FfmpegService(ValidProcessFactory(), paths);
        var installer = CreateInstaller(paths, package, locator, new string('0', 64));

        var result = await installer.InstallOrRepairAsync();

        Assert.False(result.Success);
        Assert.Contains("SHA-256", result.Message, StringComparison.Ordinal);
        Assert.Equal(oldPointer, await File.ReadAllTextAsync(paths.FfmpegCurrentPointerPath));
        Assert.Empty(Directory.GetDirectories(paths.TempDirectory, "ffmpeg-install-*"));
    }

    [Fact]
    public async Task Zip路径穿越被拒绝_目标目录外不会产生文件()
    {
        using var paths = new TestDataPaths();
        Directory.CreateDirectory(paths.TempDirectory);
        var package = CreatePackage(
            ("build/bin/ffmpeg.exe", "binary"),
            ("../escape.txt", "forbidden"));
        var locator = new FfmpegService(ValidProcessFactory(), paths);
        var installer = CreateInstaller(paths, package, locator, Hash(package));

        var result = await installer.InstallOrRepairAsync();

        Assert.False(result.Success);
        Assert.Contains("越过目标目录", result.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(paths.TempDirectory, "escape.txt")));
        Assert.False(File.Exists(paths.FfmpegCurrentPointerPath));
    }

    [Theory]
    [InlineData(false, "当前平台")]
    [InlineData(true, "安装包必须且只能包含一个")]
    public async Task 不支持平台或缺少可执行文件时安全失败(bool supported, string expected)
    {
        using var paths = new TestDataPaths();
        Directory.CreateDirectory(paths.TempDirectory);
        var package = CreatePackage(("README.txt", "no executable"));
        var locator = new FfmpegService(ValidProcessFactory(), paths);
        var installer = CreateInstaller(paths, package, locator, Hash(package), supported);

        var result = await installer.InstallOrRepairAsync();

        Assert.False(result.Success);
        Assert.Contains(expected, result.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(paths.FfmpegCurrentPointerPath));
    }

    [Fact]
    public async Task 并发修复由单一安装锁串行化()
    {
        using var paths = new TestDataPaths();
        Directory.CreateDirectory(paths.TempDirectory);
        var package = CreatePackage(("build/bin/ffmpeg.exe", "binary"));
        var downloader = new TrackingPackageDownloader(package, TimeSpan.FromMilliseconds(40));
        var locator = new FfmpegService(ValidProcessFactory(), paths);
        var installer = new FfmpegPackageInstaller(
            downloader, locator, paths, new FixedPlatform(true),
            new("test", new Uri("https://example.invalid/ffmpeg.zip"), Hash(package)));

        var results = await Task.WhenAll(
            installer.InstallOrRepairAsync(), installer.InstallOrRepairAsync());

        Assert.All(results, result => Assert.True(result.Success, result.Message));
        Assert.Equal(1, downloader.MaximumConcurrency);
    }

    [Fact]
    public async Task 原子切换后复检失败_回滚旧指针和旧自定义路径()
    {
        using var paths = new TestDataPaths();
        Directory.CreateDirectory(paths.TempDirectory);
        Directory.CreateDirectory(paths.FfmpegDependencyDirectory);
        const string oldPointer = "{\"Version\":\"old\",\"RelativeExecutablePath\":\"versions/old/bin/ffmpeg.exe\"}";
        await File.WriteAllTextAsync(paths.FfmpegCurrentPointerPath, oldPointer);
        var package = CreatePackage(("build/bin/ffmpeg.exe", "binary"));
        var locator = new FailAfterActivationLocator { CustomPath = "old-custom.exe" };
        var installer = CreateInstaller(paths, package, locator, Hash(package));

        var result = await installer.InstallOrRepairAsync();

        Assert.False(result.Success);
        Assert.Equal(oldPointer, await File.ReadAllTextAsync(paths.FfmpegCurrentPointerPath));
        Assert.Equal("old-custom.exe", locator.CustomPath);
    }

    [Fact]
    public async Task 下载阶段取消_不创建活动指针并清理临时目录()
    {
        using var paths = new TestDataPaths();
        Directory.CreateDirectory(paths.TempDirectory);
        var locator = new FfmpegService(ValidProcessFactory(), paths);
        var installer = new FfmpegPackageInstaller(
            new CancelledPackageDownloader(), locator, paths, new FixedPlatform(true),
            new("test", new Uri("https://example.invalid/ffmpeg.zip"), new string('0', 64)));

        await Assert.ThrowsAsync<OperationCanceledException>(() => installer.InstallOrRepairAsync());

        Assert.False(File.Exists(paths.FfmpegCurrentPointerPath));
        Assert.Empty(Directory.GetDirectories(paths.TempDirectory, "ffmpeg-install-*"));
    }

    [Fact]
    public async Task 损坏Zip即使摘要正确也不会激活且清理本次目录()
    {
        using var paths = new TestDataPaths();
        Directory.CreateDirectory(paths.TempDirectory);
        var damaged = "not-a-zip"u8.ToArray();
        var installer = CreateInstaller(
            paths, damaged, new FfmpegService(ValidProcessFactory(), paths), Hash(damaged));

        var result = await installer.InstallOrRepairAsync();

        Assert.False(result.Success);
        Assert.False(File.Exists(paths.FfmpegCurrentPointerPath));
        Assert.Empty(Directory.GetDirectories(paths.TempDirectory, "ffmpeg-install-*"));
    }

    [Fact]
    public async Task Zip重复目标被拒绝且不会留下活动指针()
    {
        using var paths = new TestDataPaths();
        Directory.CreateDirectory(paths.TempDirectory);
        var package = CreatePackage(
            ("build/bin/ffmpeg.exe", "first"),
            ("build/bin/ffmpeg.exe", "second"));
        var installer = CreateInstaller(
            paths, package, new FfmpegService(ValidProcessFactory(), paths), Hash(package));

        var result = await installer.InstallOrRepairAsync();

        Assert.False(result.Success);
        Assert.Contains("重复目标", result.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(paths.FfmpegCurrentPointerPath));
    }

    [Fact]
    public async Task 候选进程版本输出无效时拒绝激活安装包()
    {
        using var paths = new TestDataPaths();
        Directory.CreateDirectory(paths.TempDirectory);
        var package = CreatePackage(("build/bin/ffmpeg.exe", "binary"));
        var invalidProcess = new FakeFfmpegProcessFactory();
        var installer = CreateInstaller(
            paths, package, new FfmpegService(invalidProcess, paths), Hash(package));

        var result = await installer.InstallOrRepairAsync();

        Assert.False(result.Success);
        Assert.Contains("版本探测", result.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(paths.FfmpegCurrentPointerPath));
    }

    [Fact]
    public async Task 下载器发生磁盘异常时返回安全失败并保留旧指针()
    {
        using var paths = new TestDataPaths();
        Directory.CreateDirectory(paths.TempDirectory);
        Directory.CreateDirectory(paths.FfmpegDependencyDirectory);
        const string oldPointer = "{\"Version\":\"old\",\"RelativeExecutablePath\":\"versions/old/bin/ffmpeg.exe\"}";
        await File.WriteAllTextAsync(paths.FfmpegCurrentPointerPath, oldPointer);
        var installer = new FfmpegPackageInstaller(
            new FailingPackageDownloader(new IOException("模拟磁盘写入失败")),
            new FfmpegService(ValidProcessFactory(), paths), paths, new FixedPlatform(true),
            new("test", new Uri("https://example.invalid/ffmpeg.zip"), new string('0', 64)));

        var result = await installer.InstallOrRepairAsync();

        Assert.False(result.Success);
        Assert.Contains("磁盘写入失败", result.Message, StringComparison.Ordinal);
        Assert.Equal(oldPointer, await File.ReadAllTextAsync(paths.FfmpegCurrentPointerPath));
        Assert.Empty(Directory.GetDirectories(paths.TempDirectory, "ffmpeg-install-*"));
    }

    [Fact]
    public async Task 运行时优先自定义路径_无效自定义路径回退托管版本且每次可强制复检()
    {
        using var paths = new TestDataPaths();
        Directory.CreateDirectory(paths.FfmpegDependencyDirectory);
        var managed = Path.Combine(paths.FfmpegDependencyDirectory, "versions", "managed", "bin", "ffmpeg.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(managed)!);
        await File.WriteAllTextAsync(managed, "managed");
        var relative = Path.GetRelativePath(paths.FfmpegDependencyDirectory, managed).Replace('\\', '/');
        await File.WriteAllTextAsync(paths.FfmpegCurrentPointerPath,
            $$"""{"Version":"8.1.2","RelativeExecutablePath":"{{relative}}","Sha256":"test","InstalledAt":"2026-08-04T00:00:00+08:00"}""");
        var custom = Path.Combine(paths.RootDirectory, "custom-ffmpeg.exe");
        await File.WriteAllTextAsync(custom, "custom");
        var process = ValidProcessFactory();
        var locator = new FfmpegService(process, paths) { CustomPath = custom };

        var customStatus = await locator.DetectAsync();
        locator.CustomPath = Path.Combine(paths.RootDirectory, "missing.exe");
        var managedStatus = await locator.DetectAsync();
        process.Process.StandardOutput = "";
        var forcedStatus = await locator.DetectAsync();

        Assert.Equal(FfmpegRuntimeSource.Custom, customStatus.Source);
        Assert.Equal(FfmpegRuntimeSource.Managed, managedStatus.Source);
        Assert.False(forcedStatus.IsReady);
        Assert.False(locator.IsReady);
    }

    [Fact]
    public async Task 已存在但验证失败的自定义文件不会遮蔽托管版本或合并调用()
    {
        using var paths = new TestDataPaths();
        Directory.CreateDirectory(paths.FfmpegDependencyDirectory);
        var managed = Path.Combine(paths.FfmpegDependencyDirectory, "versions", "managed", "bin", "ffmpeg.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(managed)!);
        await File.WriteAllTextAsync(managed, "managed");
        var relative = Path.GetRelativePath(paths.FfmpegDependencyDirectory, managed).Replace('\\', '/');
        await File.WriteAllTextAsync(paths.FfmpegCurrentPointerPath,
            $$"""{"Version":"8.1.2","RelativeExecutablePath":"{{relative}}","Sha256":"test","InstalledAt":"2026-08-04T00:00:00+08:00"}""");
        var custom = Path.Combine(paths.RootDirectory, "broken-custom.exe");
        await File.WriteAllTextAsync(custom, "broken");
        var processFactory = new PathAwareProcessFactory(managed);
        var locator = new FfmpegService(processFactory, paths) { CustomPath = custom };

        var status = await locator.DetectAsync();
        await locator.MergeAsync("video.tmp", "audio.tmp", Path.Combine(paths.RootDirectory, "output.mp4"));

        Assert.Equal(FfmpegRuntimeSource.Managed, status.Source);
        Assert.Equal(Path.GetFullPath(managed), Path.GetFullPath(processFactory.StartedFiles[^1]));
    }

    [Theory]
    [InlineData("auth", DownloadFailureActionKind.LoginAndContinue)]
    [InlineData("ffmpeg", DownloadFailureActionKind.InstallOrRepairFfmpeg)]
    [InlineData("directory", DownloadFailureActionKind.ChangeOutputDirectory)]
    [InlineData("disk", DownloadFailureActionKind.Continue)]
    [InlineData("network", DownloadFailureActionKind.Retry)]
    [InlineData("cdn", DownloadFailureActionKind.Retry)]
    [InlineData("resource", DownloadFailureActionKind.Retry)]
    [InlineData("merge", DownloadFailureActionKind.RetryMerge)]
    [InlineData("conflict", DownloadFailureActionKind.ChangeOutputDirectory)]
    [InlineData("unknown", DownloadFailureActionKind.OpenLogs)]
    public void 十类持久化错误始终映射到明确主行动(
        string errorType,
        DownloadFailureActionKind expectedAction)
    {
        var presentation = new DownloadFailurePresentationPolicy().Resolve(errorType);

        Assert.False(string.IsNullOrWhiteSpace(presentation.UserMessage));
        Assert.Equal(expectedAction, presentation.PrimaryAction.Kind);
        Assert.False(string.IsNullOrWhiteSpace(presentation.PrimaryAction.Label));
    }

    [Fact]
    public void 缺失运行时与进程合并失败使用不同错误分类()
    {
        Assert.Equal(("ffmpeg", false),
            DownloadErrorClassifier.Classify(new FfmpegUnavailableException("missing")));
        Assert.Equal(("merge", true),
            DownloadErrorClassifier.Classify(new MediaMergeException("exit 1")));
    }

    [Fact]
    public async Task 错误行动服务复用登录流程并将任务重新排队执行()
    {
        using var paths = new TestDataPaths();
        var repository = new InMemoryDownloadTaskRepository();
        var task = Record("login-action", DownloadTaskStatus.Failed);
        task.ErrorType = "auth";
        repository.Seed(task);
        var executor = new MergeOnlyExecutor(paths);
        var coordinator = new BiliDownloadCoordinator(
            repository, new IsolatedMessengerService(), new NoOpDownloadProgressTracker(), executor, paths);
        var service = new DownloadFailureActionService(
            coordinator, new StubInstaller(), new FakeFfmpegService(), new StubLoginDialog(true),
            new StubPrompt(), new RecordingRevealService(), paths, new InMemorySettingsRepository());

        var result = await service.ExecuteAsync(task, DownloadFailureActionKind.LoginAndContinue);
        await AsyncTest.EventuallyAsync(() => task.Status == "done");

        Assert.True(result.Success, result.Message);
        Assert.Equal(1, executor.FullExecuteCount);
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task 错误行动取消和执行异常都转为安全反馈且不抛出()
    {
        using var paths = new TestDataPaths();
        var repository = new InMemoryDownloadTaskRepository();
        var task = Record("safe-action", DownloadTaskStatus.Failed);
        repository.Seed(task);
        var coordinator = new BiliDownloadCoordinator(
            repository, new IsolatedMessengerService(), new NoOpDownloadProgressTracker(),
            new MergeOnlyExecutor(paths), paths);
        var reveal = new RecordingRevealService { Error = new InvalidOperationException("模拟打开失败") };
        var service = new DownloadFailureActionService(
            coordinator, new StubInstaller(), new FakeFfmpegService(), new StubLoginDialog(false),
            new StubPrompt(), reveal, paths, new InMemorySettingsRepository());

        var loginCancelled = await service.ExecuteAsync(task, DownloadFailureActionKind.LoginAndContinue);
        var pickerCancelled = await service.ExecuteAsync(task, DownloadFailureActionKind.SelectCustomFfmpeg);
        var openFailed = await service.ExecuteAsync(task, DownloadFailureActionKind.OpenLogs);

        Assert.False(loginCancelled.Success);
        Assert.Contains("保持原状态", loginCancelled.Message, StringComparison.Ordinal);
        Assert.False(pickerCancelled.Success);
        Assert.Contains("取消", pickerCancelled.Message, StringComparison.Ordinal);
        Assert.False(openFailed.Success);
        Assert.Contains("打开失败", openFailed.Message, StringComparison.Ordinal);
        Assert.Equal("failed", task.Status);
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task 合并检查点先落库_随后合并失败仍保留可信事实()
    {
        using var paths = new TestDataPaths();
        var repository = new InMemoryDownloadTaskRepository();
        var task = Record("checkpoint", DownloadTaskStatus.Ready);
        repository.Seed(task);
        var executor = new CheckpointThenFailExecutor();
        var coordinator = new BiliDownloadCoordinator(
            repository, new IsolatedMessengerService(), new NoOpDownloadProgressTracker(), executor, paths);

        coordinator.StartProcessingAsync();
        await AsyncTest.EventuallyAsync(() => task.Status == "failed");

        Assert.Equal(123, task.ExpectedVideoBytes);
        Assert.Equal(45, task.ExpectedAudioBytes);
        Assert.True(task.VideoIntegrityPassed);
        Assert.True(task.AudioIntegrityPassed);
        Assert.Equal("merge", task.ErrorType);
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task 仅合并重试复用临时媒体_不会调用完整下载入口()
    {
        using var paths = new TestDataPaths();
        var repository = new InMemoryDownloadTaskRepository();
        var task = Record("merge-only", DownloadTaskStatus.Failed);
        task.ErrorType = "merge";
        task.OutputPathKey = "reserved";
        task.TempDirectory = Path.Combine(paths.TempDirectory, task.TaskId);
        Directory.CreateDirectory(task.TempDirectory);
        await File.WriteAllBytesAsync(Path.Combine(task.TempDirectory, "video.tmp"), new byte[3]);
        await File.WriteAllBytesAsync(Path.Combine(task.TempDirectory, "audio.tmp"), new byte[2]);
        task.ExpectedVideoBytes = 3;
        task.ExpectedAudioBytes = 2;
        task.VideoIntegrityPassed = true;
        task.AudioIntegrityPassed = true;
        repository.Seed(task);
        var executor = new MergeOnlyExecutor(paths);
        var coordinator = new BiliDownloadCoordinator(
            repository, new IsolatedMessengerService(), new NoOpDownloadProgressTracker(), executor, paths);

        await coordinator.RetryMergeAsync(task.TaskId);

        Assert.Equal(0, executor.FullExecuteCount);
        Assert.Equal(1, executor.MergeExecuteCount);
        Assert.Equal("done", task.Status);
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task 临时媒体长度变化时拒绝仅合并且不调用执行器()
    {
        using var paths = new TestDataPaths();
        var repository = new InMemoryDownloadTaskRepository();
        var task = Record("invalid-checkpoint", DownloadTaskStatus.Failed);
        task.ErrorType = "ffmpeg";
        task.OutputPathKey = "reserved";
        task.TempDirectory = Path.Combine(paths.TempDirectory, task.TaskId);
        Directory.CreateDirectory(task.TempDirectory);
        await File.WriteAllBytesAsync(Path.Combine(task.TempDirectory, "video.tmp"), new byte[4]);
        await File.WriteAllBytesAsync(Path.Combine(task.TempDirectory, "audio.tmp"), new byte[2]);
        task.ExpectedVideoBytes = 3;
        task.ExpectedAudioBytes = 2;
        task.VideoIntegrityPassed = true;
        task.AudioIntegrityPassed = true;
        repository.Seed(task);
        var executor = new MergeOnlyExecutor(paths);
        var coordinator = new BiliDownloadCoordinator(
            repository, new IsolatedMessengerService(), new NoOpDownloadProgressTracker(), executor, paths);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => coordinator.RetryMergeAsync(task.TaskId));

        Assert.Contains("长度", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, executor.MergeExecuteCount);
        Assert.Equal("failed", task.Status);
        await coordinator.ShutdownAsync();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("integrity")]
    [InlineData("reservation")]
    public async Task 检查点文件缺失完整性失败或路径保留失效时拒绝仅合并(string invalidFact)
    {
        using var paths = new TestDataPaths();
        var repository = new InMemoryDownloadTaskRepository();
        var task = Record("invalid-" + invalidFact, DownloadTaskStatus.Failed);
        task.ErrorType = "merge";
        task.OutputPathKey = "reserved";
        task.TempDirectory = Path.Combine(paths.TempDirectory, task.TaskId);
        Directory.CreateDirectory(task.TempDirectory);
        var video = Path.Combine(task.TempDirectory, "video.tmp");
        var audio = Path.Combine(task.TempDirectory, "audio.tmp");
        await File.WriteAllBytesAsync(video, new byte[3]);
        await File.WriteAllBytesAsync(audio, new byte[2]);
        task.ExpectedVideoBytes = 3;
        task.ExpectedAudioBytes = 2;
        task.VideoIntegrityPassed = true;
        task.AudioIntegrityPassed = true;
        if (invalidFact == "missing") File.Delete(video);
        if (invalidFact == "integrity") task.AudioIntegrityPassed = false;
        if (invalidFact == "reservation") repository.OwnsOutputReservation = false;
        repository.Seed(task);
        var executor = new MergeOnlyExecutor(paths);
        var coordinator = new BiliDownloadCoordinator(
            repository, new IsolatedMessengerService(), new NoOpDownloadProgressTracker(), executor, paths);

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.RetryMergeAsync(task.TaskId));

        Assert.Equal(0, executor.MergeExecuteCount);
        Assert.Equal("failed", task.Status);
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task SQLite目录迁移原子更新路径并清除旧覆盖授权()
    {
        using var paths = new TestDataPaths();
        var store = new DownloadTaskStore(paths);
        await store.InitAsync();
        var oldDirectory = Path.Combine(paths.RootDirectory, "old");
        var newDirectory = Path.Combine(paths.RootDirectory, "new");
        Directory.CreateDirectory(oldDirectory);
        Directory.CreateDirectory(newDirectory);
        var task = Record("relocate", DownloadTaskStatus.Paused);
        task.OutputDirectory = oldDirectory;
        task.OutputFilePath = Path.Combine(oldDirectory, "video.mp4");
        task.OutputPathKey = Path.GetFullPath(task.OutputFilePath).ToUpperInvariant();
        task.ConflictPolicy = FileConflictPolicy.Overwrite;
        task.OverwriteConfirmed = true;
        await store.InsertBatchAsync([task]);
        var newPath = Path.Combine(newDirectory, "video.mp4");
        var newKey = Path.GetFullPath(newPath).ToUpperInvariant();

        await store.RelocateOutputAsync(task.TaskId, newDirectory, newPath, newKey);
        var reloaded = Assert.Single(await store.GetAllAsync());

        Assert.Equal(newDirectory, reloaded.OutputDirectory);
        Assert.Equal(newPath, reloaded.OutputFilePath);
        Assert.Equal(FileConflictPolicy.AutoNumber, reloaded.ConflictPolicy);
        Assert.False(reloaded.OverwriteConfirmed);
        Assert.True(await store.OwnsOutputPathReservationAsync(task.TaskId, newKey));
    }

    private static FfmpegPackageInstaller CreateInstaller(
        TestDataPaths paths,
        byte[] package,
        IFfmpegRuntimeLocator locator,
        string hash,
        bool supported = true)
        => new(
            new TrackingPackageDownloader(package), locator, paths, new FixedPlatform(supported),
            new("test", new Uri("https://example.invalid/ffmpeg.zip"), hash));

    private static FakeFfmpegProcessFactory ValidProcessFactory()
    {
        var factory = new FakeFfmpegProcessFactory();
        factory.Process.StandardOutput = "ffmpeg version 8.1.2 test";
        return factory;
    }

    private static byte[] CreatePackage(params (string Path, string Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var item in entries)
            {
                var entry = archive.CreateEntry(item.Path);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(item.Content);
            }
        }
        return stream.ToArray();
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static DownloadTaskRecord Record(string id, DownloadTaskStatus status) => new()
    {
        TaskId = id,
        DocumentId = "doc",
        ItemTitle = id,
        Status = DownloadTaskStatusMapper.ToStorageString(status),
        OutputDirectory = Path.GetTempPath(),
    };

    private sealed class FixedPlatform(bool supported) : IFfmpegInstallPlatform
    {
        public bool SupportsManagedInstallation => supported;
    }

    private sealed class TrackingPackageDownloader(byte[] package, TimeSpan? delay = null)
        : IFfmpegPackageDownloader
    {
        private int _active;
        public int MaximumConcurrency { get; private set; }

        public async Task DownloadAsync(
            Uri source,
            string destination,
            long maximumBytes,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
        {
            var active = Interlocked.Increment(ref _active);
            MaximumConcurrency = Math.Max(MaximumConcurrency, active);
            try
            {
                if (delay is not null) await Task.Delay(delay.Value, cancellationToken);
                Assert.True(package.Length <= maximumBytes);
                await File.WriteAllBytesAsync(destination, package, cancellationToken);
                progress?.Report(100);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }
    }

    private sealed class CancelledPackageDownloader : IFfmpegPackageDownloader
    {
        public Task DownloadAsync(
            Uri source,
            string destination,
            long maximumBytes,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
            => Task.FromException(new OperationCanceledException());
    }

    private sealed class FailingPackageDownloader(Exception error) : IFfmpegPackageDownloader
    {
        public Task DownloadAsync(
            Uri source,
            string destination,
            long maximumBytes,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
            => Task.FromException(error);
    }

    private sealed class FailAfterActivationLocator : IFfmpegRuntimeLocator
    {
        public string? CustomPath { get; set; }
        public string? ResolvedPath => null;
        public bool IsReady => false;
        public string? ResolveFfmpegPath() => null;
        public Task<bool> ValidatePathAsync(string path, CancellationToken ct = default)
            => Task.FromResult(true);
        public Task<FfmpegRuntimeStatus> DetectAsync(CancellationToken ct = default)
            => Task.FromResult(new FfmpegRuntimeStatus(
                false, null, null, FfmpegRuntimeSource.None, "复检失败"));
    }

    private sealed class PathAwareProcessFactory(string validExecutable) : IFfmpegProcessFactory
    {
        public List<string> StartedFiles { get; } = [];

        public IFfmpegProcess Start(System.Diagnostics.ProcessStartInfo startInfo)
        {
            StartedFiles.Add(startInfo.FileName);
            return new FakeFfmpegProcess
            {
                StandardOutput = Path.GetFullPath(startInfo.FileName).Equals(
                    Path.GetFullPath(validExecutable), StringComparison.OrdinalIgnoreCase)
                    ? "ffmpeg version 8.1.2 test"
                    : "invalid executable",
            };
        }
    }

    private sealed class StubInstaller : IFfmpegPackageInstaller
    {
        public bool IsInstalling => false;
        public event Action<FfmpegInstallProgress>? ProgressChanged { add { } remove { } }
        public Task<FfmpegInstallResult> InstallOrRepairAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(FfmpegInstallResult.Failed("测试未执行安装"));
    }

    private sealed class StubLoginDialog(bool result) : ILoginDialogService
    {
        public Task<bool> EnsureLoggedInAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(result);
    }

    private sealed class StubPrompt : IUserPromptService
    {
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(false);
        public Task<DeleteTaskPromptResult> ConfirmDeleteAsync(int taskCount, bool hasOutputFiles)
            => Task.FromResult(DeleteTaskPromptResult.Cancelled);
        public Task<bool> ConfirmSubmissionAsync(SubmissionPreflightReport report) => Task.FromResult(false);
        public Task<string?> PickFolderAsync(string title, string? suggestedDirectory = null)
            => Task.FromResult<string?>(null);
        public Task<string?> PickFfmpegExecutableAsync() => Task.FromResult<string?>(null);
    }

    private sealed class RecordingRevealService : IFileRevealService
    {
        public Exception? Error { get; init; }
        public string? RevealedPath { get; private set; }

        public Task RevealAsync(string path)
        {
            RevealedPath = path;
            return Error is null ? Task.CompletedTask : Task.FromException(Error);
        }
    }

    private sealed class CheckpointThenFailExecutor : IDownloadTaskExecutor
    {
        public Task<DownloadExecutionResult> ExecuteAsync(
            DownloadTaskRecord task,
            Action<DownloadProgressInfo> onProgress,
            Action<long, long> onBytesChanged,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public async Task<DownloadExecutionResult> ExecuteAsync(
            DownloadTaskRecord task,
            DownloadExecutionCallbacks callbacks,
            CancellationToken cancellationToken)
        {
            await callbacks.OnMediaReadyAsync(new(123, 45, true, true));
            throw new MediaMergeException("exit 1");
        }
    }

    private sealed class MergeOnlyExecutor(TestDataPaths paths) : IDownloadTaskExecutor, IMediaMergeRetryExecutor
    {
        public int FullExecuteCount { get; private set; }
        public int MergeExecuteCount { get; private set; }

        public Task<DownloadExecutionResult> ExecuteAsync(
            DownloadTaskRecord task,
            Action<DownloadProgressInfo> onProgress,
            Action<long, long> onBytesChanged,
            CancellationToken cancellationToken)
        {
            FullExecuteCount++;
            return Task.FromResult(new DownloadExecutionResult(null, null));
        }

        public Task<DownloadExecutionResult> ExecuteMergeOnlyAsync(
            DownloadTaskRecord task,
            Action<DownloadProgressInfo> onProgress,
            CancellationToken cancellationToken)
        {
            MergeExecuteCount++;
            var output = Path.Combine(paths.RootDirectory, task.TaskId + ".mp4");
            return Task.FromResult(new DownloadExecutionResult(output, null));
        }
    }
}
