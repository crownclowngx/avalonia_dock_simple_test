using System.Buffers.Binary;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Dock.Model.Mvvm.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.ViewModels;
using MyAvaloniaManagementCommon.Plugin;
using MySmallTools.Business.SecretVideoPlayer.Decryption;
using MySmallTools.Business.SecretVideoPlayer.Encryption;
using MySmallTools.Business.SecretVideoPlayer.Playback;
using MySmallTools.Constants;
using MySmallTools.Plugin;
using MySmallTools.ViewModels.SecretVideoPlayer;

namespace MySmallTools.Playback.IntegrationHarness;

internal static class Program
{
    private static Microsoft.Extensions.DependencyInjection.ServiceProvider? _provider;
    private static PluginLifecycleManager? _lifecycleManager;

    [STAThread]
    public static int Main(string[] args)
    {
        var options = HarnessOptions.Parse(args);
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            Console.Error.WriteLine("G3 播放集成门禁仅支持 Windows x64。");
            return 2;
        }

        var services = new ServiceCollection();
        services.AddApplicationServices();
        services.AddViewModels();

        // 直接使用真实插件模块；ManagementFactory 和 ViewLocator 会复用已加载程序集，
        // 无需把测试插件伪装成生产部署目录。
        var catalog = PluginModuleCatalog.Discover([typeof(MySmallToolsPluginModule).Assembly]);
        catalog.ConfigureServices(services);
        services.AddSingleton(catalog);
        services.AddSingleton<PluginLifecycleManager>();

        _provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
        MyAvaloniaManagement.Business.Helpers.ServiceProvider.Initialize(_provider);
        _lifecycleManager = _provider.GetRequiredService<PluginLifecycleManager>();
        _lifecycleManager.InitializeAllAsync().GetAwaiter().GetResult();

        var runner = new G3PlaybackHarnessRunner(_provider, options);
        try
        {
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace()
                .AfterSetup(_ =>
                {
                    Dispatcher.UIThread.Post(async () =>
                    {
                        var exitCode = await runner.RunAsync();
                        if (Application.Current?.ApplicationLifetime is
                            IClassicDesktopStyleApplicationLifetime desktop)
                        {
                            desktop.Shutdown(exitCode);
                        }
                    });
                })
                .StartWithClassicDesktopLifetime([]);
        }
        finally
        {
            _lifecycleManager.ShutdownAllAsync().GetAwaiter().GetResult();
            _provider.Dispose();
        }
    }
}

internal sealed class G3PlaybackHarnessRunner(
    IServiceProvider services,
    HarnessOptions options)
{
    private const string Password = "G3-Integration-Public-Password!";
    private readonly List<string> _failures = [];
    private readonly List<long> _nearEndSeekChunkTrace = [];
    private readonly List<long> _randomSeekTargets = [];
    private readonly Dictionary<string, long> _stageDurationsMs = [];
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private long _uiHeartbeatMaxIntervalMs;

    public async Task<int> RunAsync()
    {
        HarnessReport report;
        string? tempDirectory = null;
        try
        {
            await WaitUntilAsync(
                () => Application.Current?.ApplicationLifetime is
                    IClassicDesktopStyleApplicationLifetime { MainWindow: not null },
                TimeSpan.FromSeconds(10),
                "宿主主窗口未能启动。");

            tempDirectory = Path.Combine(
                Path.GetTempPath(),
                $"mysmalltools-g3-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDirectory);
            var stage = Stopwatch.StartNew();
            var assets = await Task.Run(() => PrepareAssetsAsync(tempDirectory));
            _stageDurationsMs["assetPreparation"] = stage.ElapsedMilliseconds;

            var mainWindow = (Application.Current!.ApplicationLifetime as
                IClassicDesktopStyleApplicationLifetime)!.MainWindow!;
            var mainViewModel = (MainWindowViewModel)mainWindow.DataContext!;
            var factory = services.GetRequiredService<ManagementFactory>();
            var documentDock = factory.GetDockable<Dock.Model.Controls.IDocumentDock>("Files")
                as DocumentDock ?? throw new InvalidOperationException("宿主没有 Files DocumentDock。");

            var placeholder = CreateDocument(mainViewModel, documentDock);
            placeholder.Title = "G3 占位标签";
            // 占位标签本身也是一个真实 Document Scope。G3.1 的 PlayerHost、
            // Dispatcher 和 Reaper 在 Scope 创建时即存在，所以中途资源检查应回到
            // “仅占位标签存活”的基线，而不是错误地要求全进程为 default。
            var placeholderResources = SecurePlaybackDiagnostics.CaptureResources();

            await MeasureStageAsync(
                "functionalMatrix",
                () => RunFunctionalMatrixAsync(
                    mainViewModel,
                    factory,
                    documentDock,
                    placeholder,
                    assets,
                    placeholderResources));

            await MeasureStageAsync(
                "lifecycleStress",
                () => RunLifecycleStressAsync(
                    mainViewModel,
                    factory,
                    documentDock,
                    placeholder,
                    assets,
                    placeholderResources));

            factory.CloseDockable(placeholder);
            await DrainDispatcherAsync();

            var finalResources = SecurePlaybackDiagnostics.CaptureResources();
            Require(
                finalResources == default,
                $"最终播放资源未归零: {finalResources}");
            Require(
                CountUnexpectedTopLevelWindows() == 0,
                "检测到 LibVLC 创建的意外独立顶层窗口。");

            await MeasureStageAsync(
                "roundTripAndFileLocks",
                () => Task.Run(() => VerifyRoundTripAndFileLocksAsync(assets)));

            report = BuildReport(
                success: _failures.Count == 0,
                assets,
                finalResources);
        }
        catch (Exception ex)
        {
            _failures.Add($"{ex.GetType().Name}: {ex.Message}");
            report = BuildReport(
                success: false,
                [],
                SecurePlaybackDiagnostics.CaptureResources());
        }
        finally
        {
            if (tempDirectory is not null && Directory.Exists(tempDirectory))
            {
                try
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
                catch (Exception ex)
                {
                    _failures.Add($"临时目录清理失败: {ex.GetType().Name}");
                }
            }
        }

        await WriteReportAsync(report with
        {
            Success = report.Success && _failures.Count == 0,
            Failures = _failures.ToArray()
        });

        Console.WriteLine(
            report.Success && _failures.Count == 0
                ? "G3 Windows x64 播放集成门禁通过。"
                : "G3 Windows x64 播放集成门禁失败。");
        foreach (var failure in _failures)
        {
            Console.Error.WriteLine($"- {failure}");
        }
        return report.Success && _failures.Count == 0 ? 0 : 1;
    }

    private async Task RunFunctionalMatrixAsync(
        MainWindowViewModel mainViewModel,
        ManagementFactory factory,
        DocumentDock documentDock,
        SecretVideoPlayerViewModel placeholder,
        IReadOnlyList<HarnessAsset> assets,
        PlaybackResourceSnapshot expectedResources)
    {
        var document = CreateDocument(mainViewModel, documentDock);
        try
        {
        documentDock.ActiveDockable = document;
        await WaitUntilAsync(
            () => document.PlayerViewModel.IsVideoSurfaceReady,
            TimeSpan.FromSeconds(10),
            "真实视频 HWND 未创建。");

        var first = assets[0];
        Require(
            await document.PlayerViewModel.LoadAndPlayMediaAsync(first.EncryptedPath, Password),
            $"真实 MP4 加载播放失败: {document.PlayerViewModel.StatusMessage}");
        await WaitUntilAsync(
            () => document.PlayerViewModel.PlaybackSnapshot.State == PlaybackState.Playing &&
                  document.PlayerViewModel.PlaybackSnapshot.PositionMs > 100,
            TimeSpan.FromSeconds(8),
            "真实 MP4 未进入播放状态或时间未推进。");
        Require(document.PlayerViewModel.PlaybackSnapshot.HasVideo, "MP4 未识别到视频轨。");
        Require(document.PlayerViewModel.PlaybackSnapshot.HasAudio, "MP4 未识别到音轨。");
        var documentPlayer = document.PlayerViewModel.MediaPlayer;
        var documentSurfaceGeneration =
            document.PlayerViewModel.PlaybackSnapshot.SurfaceGeneration;
        Require(documentPlayer is not null, "Document 未暴露稳定的 MediaPlayer。");

        // G6：真实 LibVLC 倍速必须经过会话端口设置，并反映到不可变控制快照。
        foreach (var rate in new[] { 0.5f, 0.75f, 1.0f, 1.25f, 1.5f, 2.0f })
        {
            var rateResult = await document.PlayerViewModel.SetPlaybackRateAsync(rate);
            Require(rateResult.Success, $"真实媒体设置 {rate} 倍速失败。");
            Require(
                Math.Abs(document.PlayerViewModel.PlaybackSnapshot.Controls.Rate - rate) < 0.001f,
                $"{rate} 倍速未写入控制快照。");
        }
        Require(
            (await document.PlayerViewModel.SetPlaybackRateAsync(1.0f)).Success,
            "真实媒体未能恢复 1.0 倍速。");

        await WaitUntilAsync(
            () => document.PlayerViewModel.PlaybackSnapshot.Controls.AudioTracks.Count >= 2 &&
                  document.PlayerViewModel.PlaybackSnapshot.Controls.SubtitleTracks
                      .Any(track => track.Id >= 0),
            TimeSpan.FromSeconds(5),
            "真实双音轨或内嵌字幕未进入控制快照。");

        // 不只验证“列表能读到”，还逐条执行真实 LibVLC 切换，并确认选中 ID
        // 回写到不可变控制快照。字幕 -1 是产品定义的稳定“关闭字幕”语义。
        foreach (var audioTrack in document.PlayerViewModel.PlaybackSnapshot.Controls.AudioTracks)
        {
            var trackResult = await document.PlayerViewModel.SelectAudioTrackAsync(audioTrack.Id);
            Require(trackResult.Success, $"真实音轨 {audioTrack.Id} 切换失败。");
            Require(
                document.PlayerViewModel.PlaybackSnapshot.Controls.SelectedAudioTrackId ==
                audioTrack.Id,
                $"真实音轨 {audioTrack.Id} 的选择未写入控制快照。");
        }

        var subtitleTrack = document.PlayerViewModel.PlaybackSnapshot.Controls.SubtitleTracks
            .First(track => track.Id >= 0);
        var subtitleResult =
            await document.PlayerViewModel.SelectSubtitleTrackAsync(subtitleTrack.Id);
        Require(subtitleResult.Success, "真实内嵌字幕启用失败。");
        Require(
            document.PlayerViewModel.PlaybackSnapshot.Controls.SelectedSubtitleTrackId ==
            subtitleTrack.Id,
            "真实内嵌字幕选中 ID 未写入控制快照。");
        var disableSubtitleResult =
            await document.PlayerViewModel.SelectSubtitleTrackAsync(-1);
        Require(disableSubtitleResult.Success, "真实字幕关闭失败。");
        Require(
            document.PlayerViewModel.PlaybackSnapshot.Controls.SelectedSubtitleTrackId == -1,
            "关闭字幕后控制快照未记录 -1。");

        // G6 的内容区全屏复用同一个 PlayerShell。真实窗口中完成一次进出，验证
        // OverlayLayer 迁移没有替换 Document 级 MediaPlayer，也没有丢失 HWND 恢复链路。
        Require(
            document.PlayerViewModel.ToggleFullscreenCommand.CanExecute(null),
            "真实媒体加载后全屏命令不可用。");
        document.PlayerViewModel.ToggleFullscreenCommand.Execute(null);
        await WaitUntilAsync(
            () => document.PlayerViewModel.IsFullscreen &&
                  !document.PlayerViewModel.IsFullscreenTransitioning,
            TimeSpan.FromSeconds(8),
            "进入窗口内容区全屏超时。");
        Require(
            ReferenceEquals(documentPlayer, document.PlayerViewModel.MediaPlayer),
            "进入全屏替换了 Document 级 MediaPlayer。");
        document.PlayerViewModel.ToggleFullscreenCommand.Execute(null);
        await WaitUntilAsync(
            () => !document.PlayerViewModel.IsFullscreen &&
                  !document.PlayerViewModel.IsFullscreenTransitioning &&
                  document.PlayerViewModel.IsVideoSurfaceReady,
            TimeSpan.FromSeconds(8),
            "退出窗口内容区全屏超时。");
        Require(
            document.PlayerViewModel.PlaybackSnapshot.SurfaceGeneration >
            documentSurfaceGeneration,
            "全屏进出没有重建 HWND 视频表面。");

        // 后续“普通媒体切换不得重建表面”的断言应当以全屏退出后的当前代次为
        // 基准。若仍与进入全屏前的代次比较，会把全屏按设计产生的 HWND 迁移
        // 错报为普通媒体切换造成的重建。
        documentSurfaceGeneration =
            document.PlayerViewModel.PlaybackSnapshot.SurfaceGeneration;

        var random = new Random(0x4733);
        var randomMaximum = Math.Max(
            251,
            document.PlayerViewModel.PlaybackSnapshot.DurationMs - 1_000);
        for (var index = 0; index < 3; index++)
        {
            var target = random.NextInt64(250, randomMaximum);
            _randomSeekTargets.Add(target);
            var randomSeek = await document.PlayerViewModel.SeekMediaAsync(
                target,
                waitForFrame: true);
            Require(randomSeek.Success, $"固定种子随机 Seek {index + 1} 失败。");
            Require(
                Math.Abs(document.PlayerViewModel.PlaybackSnapshot.PositionMs -
                         target) <= 750,
                $"固定种子随机 Seek {index + 1} 的位置误差超过 750 ms。");
        }

        await document.PlayerViewModel.PauseCommand.ExecuteAsync(null);
        var pausedAt = document.PlayerViewModel.PlaybackSnapshot.PositionMs;
        await Task.Delay(500);
        Require(
            Math.Abs(document.PlayerViewModel.PlaybackSnapshot.PositionMs - pausedAt) <= 250,
            "暂停后播放时间仍持续推进。");

        var middle = document.PlayerViewModel.PlaybackSnapshot.DurationMs / 2;
        var seek = await document.PlayerViewModel.SeekMediaAsync(middle, waitForFrame: true);
        Require(seek.Success, $"暂停 Seek 失败: {seek.Failure?.Code}");
        Require(
            Math.Abs(document.PlayerViewModel.PlaybackSnapshot.PositionMs - middle) <= 750,
            "暂停 Seek 的位置误差超过 750 ms。");

        for (var index = 0; index < options.DockSwitches; index++)
        {
            var expectedPosition =
                document.PlayerViewModel.PlaybackSnapshot.PositionMs;
            documentDock.ActiveDockable = placeholder;
            await WaitUntilAsync(
                () => !document.PlayerViewModel.IsVideoSurfaceReady,
                TimeSpan.FromSeconds(5),
                "暂停态切出 Dock 后旧 HWND 未销毁。");
            documentDock.ActiveDockable = document;
            await WaitUntilAsync(
                () => document.PlayerViewModel.IsVideoSurfaceReady,
                TimeSpan.FromSeconds(5),
                "暂停态切回 Dock 后新 HWND 未创建。");
            await WaitUntilAsync(
                () => document.PlayerViewModel.PlaybackSnapshot.State ==
                    PlaybackState.Paused &&
                    !document.PlayerViewModel.PlaybackSnapshot.IsTransitioning,
                TimeSpan.FromSeconds(5),
                "Dock 恢复后没有保持暂停。");
            var restoredPosition =
                document.PlayerViewModel.PlaybackSnapshot.PositionMs;
            Require(
                Math.Abs(restoredPosition - expectedPosition) <= 750,
                $"暂停态 Dock 恢复位置误差超过 750 ms：expected={expectedPosition}, actual={restoredPosition}。");
        }

        var stopHeartbeat = await MeasureUiHeartbeatAsync(
            () => document.PlayerViewModel.StopCommand.ExecuteAsync(null));
        RecordHeartbeat("stop", stopHeartbeat);
        await WaitUntilAsync(
            () => document.PlayerViewModel.PlaybackSnapshot.State ==
                PlaybackState.Stopped,
            TimeSpan.FromSeconds(5),
            "真实媒体停止命令未进入 Stopped。");
        await document.PlayerViewModel.PlayCommand.ExecuteAsync(null);
        for (var index = 0; index < options.DockSwitches; index++)
        {
            var expectedPosition =
                document.PlayerViewModel.PlaybackSnapshot.PositionMs;
            documentDock.ActiveDockable = placeholder;
            await WaitUntilAsync(
                () => !document.PlayerViewModel.IsVideoSurfaceReady,
                TimeSpan.FromSeconds(5),
                "切出 Dock 后旧 HWND 未销毁。");
            documentDock.ActiveDockable = document;
            await WaitUntilAsync(
                () => document.PlayerViewModel.IsVideoSurfaceReady,
                TimeSpan.FromSeconds(5),
                "切回 Dock 后新 HWND 未创建。");
            await WaitUntilAsync(
                () => document.PlayerViewModel.PlaybackSnapshot.State == PlaybackState.Playing &&
                      !document.PlayerViewModel.PlaybackSnapshot.IsTransitioning,
                TimeSpan.FromSeconds(5),
                "Dock 恢复后没有继续播放。");
            Require(
                Math.Abs(document.PlayerViewModel.PlaybackSnapshot.PositionMs -
                         expectedPosition) <= 750,
                "播放态 Dock 恢复位置误差超过 750 ms。");
        }

        if (options.MediaSwitches > 0)
        {
            var switches = Enumerable.Range(0, options.MediaSwitches)
                .Select(index =>
                {
                    var asset = assets[index % assets.Count];
                    return document.PlayerViewModel.LoadMediaAsync(
                        asset.EncryptedPath,
                        Password);
                })
                .ToArray();
            bool[] switchResults = [];
            var switchHeartbeat = await MeasureUiHeartbeatAsync(
                async () => switchResults = await Task.WhenAll(switches));
            RecordHeartbeat("mediaSwitch", switchHeartbeat);
            Require(
                switchResults[^1] && switchResults.Count(result => result) == 1,
                "快速媒体切换没有做到只有最后请求提交。");
            Require(
                ReferenceEquals(documentPlayer, document.PlayerViewModel.MediaPlayer),
                "普通媒体切换替换了 Document 级 MediaPlayer。");
            Require(
                documentSurfaceGeneration ==
                document.PlayerViewModel.PlaybackSnapshot.SurfaceGeneration,
                "普通媒体切换意外重建了 HWND 视频表面。");

            var expectedLast = assets[(options.MediaSwitches - 1) % assets.Count];
            await document.PlayerViewModel.PlayCommand.ExecuteAsync(null);
            await WaitUntilAsync(
                () => document.PlayerViewModel.PlaybackSnapshot.State ==
                    PlaybackState.Playing,
                TimeSpan.FromSeconds(5),
                "快速媒体切换的最终候选无法播放。");
            Require(
                document.PlayerViewModel.PlaybackSnapshot.HasAudio ==
                expectedLast.FileName.EndsWith(".mp4", StringComparison.Ordinal),
                "快速媒体切换最终提交的不是最后一个请求。");
        }

        var webm = assets.Single(asset => asset.FileName.EndsWith(".webm", StringComparison.Ordinal));
        SecurePlaybackDiagnostics.ClearRecentChunkReads();
        Require(
            await document.PlayerViewModel.LoadAndPlayMediaAsync(webm.EncryptedPath, Password),
            "真实 WebM 加载播放失败。");
        await WaitUntilAsync(
            () => document.PlayerViewModel.PlaybackSnapshot.PositionMs > 100,
            TimeSpan.FromSeconds(8),
            "真实 WebM 播放时间未推进。");
        Require(!document.PlayerViewModel.PlaybackSnapshot.HasAudio, "无声 WebM 被错误识别为有音轨。");
        var nearEnd = Math.Max(0, document.PlayerViewModel.PlaybackSnapshot.DurationMs - 500);
        Require(
            (await document.PlayerViewModel.SeekMediaAsync(nearEnd)).Success,
            "接近结尾 Seek 失败。");
        try
        {
            await WaitUntilAsync(
                () => document.PlayerViewModel.PlaybackSnapshot.State == PlaybackState.Ended,
                TimeSpan.FromSeconds(8),
                "接近结尾 Seek 后没有到达媒体结束状态。");
        }
        catch (TimeoutException ex)
        {
            var snapshot = document.PlayerViewModel.PlaybackSnapshot;
            throw new TimeoutException(
                $"{ex.Message} state={snapshot.State}, position={snapshot.PositionMs}, duration={snapshot.DurationMs}");
        }

        _nearEndSeekChunkTrace.AddRange(
            SecurePlaybackDiagnostics.CaptureRecentChunkReads());
        Require(
            _nearEndSeekChunkTrace.Distinct().Count() >= 3,
            "WebM 真实播放与 Seek 没有跨越至少三个 SECVID03 块。");

        await VerifyTamperedSeekAsync(document, webm, nearEnd);
        await VerifyUnavailableInputAsync(document, webm);
        }
        finally
        {
            documentDock.ActiveDockable = placeholder;
            await DrainDispatcherAsync();
            factory.CloseDockable(document);
            await DrainDispatcherAsync();
        }

        var functionalResources = SecurePlaybackDiagnostics.CaptureResources();
        Require(
            functionalResources == expectedResources,
            $"功能矩阵关闭 Document 后资源未回到占位基线: {functionalResources}。");
    }

    private async Task VerifyTamperedSeekAsync(
        SecretVideoPlayerViewModel document,
        HarnessAsset asset,
        long nearEnd)
    {
        await document.PlayerViewModel.CleanupMediaAsync();
        var tamperedPath = $"{asset.EncryptedPath}.tampered";
        File.Copy(asset.EncryptedPath, tamperedPath, overwrite: true);
        var targetChunk = _nearEndSeekChunkTrace.Last();
        FlipSecvid03ChunkByte(tamperedPath, targetChunk);

        var started = await document.PlayerViewModel.LoadAndPlayMediaAsync(
            tamperedPath,
            Password);
        if (started)
        {
            await ObserveUntilAsync(
                () => document.PlayerViewModel.PlaybackSnapshot.State is
                    PlaybackState.Playing or PlaybackState.Faulted,
                TimeSpan.FromSeconds(5));

            if (document.PlayerViewModel.LastFailure?.Code !=
                PlaybackFailureCode.CorruptedContent)
            {
                _ = await document.PlayerViewModel.SeekMediaAsync(
                    nearEnd,
                    waitForFrame: true);
            }
        }

        var corrupted = await ObserveUntilAsync(
            () => document.PlayerViewModel.LastFailure?.Code ==
                PlaybackFailureCode.CorruptedContent,
            TimeSpan.FromSeconds(8));
        Require(corrupted, "篡改块播放未稳定报告 CorruptedContent。");

        await document.PlayerViewModel.CleanupMediaAsync();
        File.Delete(tamperedPath);
    }

    private async Task VerifyUnavailableInputAsync(
        SecretVideoPlayerViewModel document,
        HarnessAsset asset)
    {
        var unavailablePath = $"{asset.EncryptedPath}.unavailable";
        File.Copy(asset.EncryptedPath, unavailablePath, overwrite: true);
        File.Delete(unavailablePath);

        var loaded = await document.PlayerViewModel.LoadMediaAsync(
            unavailablePath,
            Password);
        Require(!loaded, "已删除的扫描结果仍被加载成功。");
        Require(
            document.PlayerViewModel.LastFailure?.Code ==
            PlaybackFailureCode.InputUnavailable,
            "已删除的扫描结果没有报告 InputUnavailable。");
    }

    private static void FlipSecvid03ChunkByte(string path, long chunkIndex)
    {
        const int fixedHeaderSize = 256;
        const int tagSize = 16;
        Span<byte> header = stackalloc byte[fixedHeaderSize];
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);
        stream.ReadExactly(header);
        var encryptedDataOffset = BinaryPrimitives.ReadInt64LittleEndian(
            header.Slice(48, 8));
        var chunkSize = BinaryPrimitives.ReadInt32LittleEndian(
            header.Slice(56, 4));
        var chunkCount =
            (BinaryPrimitives.ReadInt64LittleEndian(header.Slice(40, 8)) +
             chunkSize - 1) / chunkSize;
        if (chunkIndex < 0 || chunkIndex >= chunkCount)
        {
            throw new InvalidDataException("Seek 轨迹包含无效 SECVID03 块编号。");
        }

        var physicalOffset = checked(
            encryptedDataOffset + chunkIndex * (chunkSize + tagSize));
        stream.Position = physicalOffset + 17;
        var value = stream.ReadByte();
        if (value < 0)
        {
            throw new EndOfStreamException("无法篡改目标 SECVID03 块。");
        }
        stream.Position--;
        stream.WriteByte((byte)(value ^ 0x01));
        stream.Flush(flushToDisk: true);
    }

    private async Task RunLifecycleStressAsync(
        MainWindowViewModel mainViewModel,
        ManagementFactory factory,
        DocumentDock documentDock,
        SecretVideoPlayerViewModel placeholder,
        IReadOnlyList<HarnessAsset> assets,
        PlaybackResourceSnapshot expectedResources)
    {
        for (var cycle = 0; cycle < options.Cycles; cycle++)
        {
            var document = CreateDocument(mainViewModel, documentDock);
            documentDock.ActiveDockable = document;
            await WaitUntilAsync(
                () => document.PlayerViewModel.IsVideoSurfaceReady,
                TimeSpan.FromSeconds(8),
                $"第 {cycle + 1} 轮 HWND 未创建。");

            var asset = assets[cycle % assets.Count];
            Require(
                await document.PlayerViewModel.LoadAndPlayMediaAsync(asset.EncryptedPath, Password),
                $"第 {cycle + 1} 轮加载播放失败。");
            await WaitUntilAsync(
                () => document.PlayerViewModel.PlaybackSnapshot.PositionMs > 50,
                TimeSpan.FromSeconds(6),
                $"第 {cycle + 1} 轮没有产生真实读取。");

            documentDock.ActiveDockable = placeholder;
            await WaitUntilAsync(
                () => !document.PlayerViewModel.IsVideoSurfaceReady,
                TimeSpan.FromSeconds(5),
                $"第 {cycle + 1} 轮旧 HWND 未销毁。");
            factory.CloseDockable(document);
            await DrainDispatcherAsync();

            var cycleResources = SecurePlaybackDiagnostics.CaptureResources();
            Require(
                cycleResources == expectedResources,
                $"第 {cycle + 1} 轮关闭后资源未回到占位基线: {cycleResources}。");
            Require(
                CountUnexpectedTopLevelWindows() == 0,
                $"第 {cycle + 1} 轮检测到意外顶层视频窗口。");
        }
    }

    private static SecretVideoPlayerViewModel CreateDocument(
        MainWindowViewModel mainViewModel,
        DocumentDock documentDock)
    {
        mainViewModel.CreateDocument(DocumentTypeIdConstant.SecretVideoDocumentId);
        var document = documentDock.VisibleDockables?
            .OfType<SecretVideoPlayerViewModel>()
            .LastOrDefault();
        return document ?? throw new InvalidOperationException("无法创建安全视频 Document。");
    }

    private static async Task<IReadOnlyList<HarnessAsset>> PrepareAssetsAsync(string directory)
    {
        var sourceDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "TestAssets",
            "RealMedia");
        var names = new[]
        {
            "synthetic-multitrack-subtitles.mp4",
            "synthetic-av-short.mp4",
            "synthetic-silent-multiblock.webm"
        };
        var encryptor = new Secvid03Encryptor();
        var assets = new List<HarnessAsset>();
        foreach (var name in names)
        {
            var source = Path.Combine(sourceDirectory, name);
            if (!File.Exists(source))
            {
                throw new FileNotFoundException("真实媒体测试资产缺失。", name);
            }

            var encrypted = Path.Combine(directory, $"{name}.secvid");
            await encryptor.EncryptAsync(
                source,
                encrypted,
                Password,
                Path.GetFileNameWithoutExtension(name),
                "G3 integration fixture");
            await using var sourceStream = File.OpenRead(source);
            var sourceHash = await SHA256.HashDataAsync(sourceStream);
            assets.Add(new HarnessAsset(
                name,
                source,
                encrypted,
                Convert.ToHexString(sourceHash)));
        }
        return assets;
    }

    private static async Task VerifyRoundTripAndFileLocksAsync(
        IReadOnlyList<HarnessAsset> assets)
    {
        var decryptor = new Secvid03Decryptor();
        foreach (var asset in assets)
        {
            var decrypted = Path.Combine(
                Path.GetDirectoryName(asset.EncryptedPath)!,
                $"roundtrip-{asset.FileName}");
            await decryptor.DecryptAsync(asset.EncryptedPath, decrypted, Password);
            string hash;
            await using (var stream = File.OpenRead(decrypted))
            {
                hash = Convert.ToHexString(await SHA256.HashDataAsync(stream));
            }
            if (!hash.Equals(asset.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"资产 {asset.FileName} 解密哈希不一致。");
            }
            File.Delete(decrypted);

            var renamed = $"{asset.EncryptedPath}.lock-check";
            File.Move(asset.EncryptedPath, renamed);
            File.Move(renamed, asset.EncryptedPath);
        }
    }

    private static async Task WaitUntilAsync(
        Func<bool> condition,
        TimeSpan timeout,
        string failureMessage)
    {
        var started = Stopwatch.StartNew();
        while (!condition())
        {
            if (started.Elapsed >= timeout)
            {
                throw new TimeoutException(failureMessage);
            }
            await Task.Delay(50);
        }
    }

    private static async Task<bool> ObserveUntilAsync(
        Func<bool> condition,
        TimeSpan timeout)
    {
        var started = Stopwatch.StartNew();
        while (!condition())
        {
            if (started.Elapsed >= timeout)
            {
                return false;
            }
            await Task.Delay(50);
        }
        return true;
    }

    private static async Task DrainDispatcherAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        await Task.Delay(100);
    }

    /// <summary>
    /// 在真实 Avalonia Dispatcher 上测量操作期间的消息泵心跳。
    /// </summary>
    /// <remarks>
    /// 这里不规定 Stop 或切换必须在固定毫秒内结束；不同机器和编码格式的原生释放
    /// 时间本来就会变化。门禁只验证：当操作不是瞬时完成时，操作尚未结束期间
    /// UI 定时器仍得到调度。若 LibVLC Stop 被错误地放回 UI 线程，心跳会归零。
    /// </remarks>
    private static async Task<UiHeartbeatResult> MeasureUiHeartbeatAsync(Func<Task> operation)
    {
        var clock = Stopwatch.StartNew();
        var previousTick = clock.ElapsedMilliseconds;
        var maximumGap = 0L;
        var activeTicks = 0;
        var operationActive = false;
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(10)
        };
        EventHandler tick = (_, _) =>
        {
            var now = clock.ElapsedMilliseconds;
            maximumGap = Math.Max(maximumGap, now - previousTick);
            previousTick = now;
            if (operationActive)
            {
                activeTicks++;
            }
        };
        timer.Tick += tick;

        try
        {
            timer.Start();
            await Task.Delay(30);
            var startedAt = clock.ElapsedMilliseconds;
            operationActive = true;
            await operation();
            operationActive = false;
            var elapsed = clock.ElapsedMilliseconds - startedAt;
            await Task.Delay(30);
            return new UiHeartbeatResult(elapsed, activeTicks, maximumGap);
        }
        finally
        {
            timer.Stop();
            timer.Tick -= tick;
        }
    }

    private void RecordHeartbeat(string operation, UiHeartbeatResult heartbeat)
    {
        _stageDurationsMs[$"{operation}UiHeartbeatTicks"] = heartbeat.ActiveTicks;
        _stageDurationsMs[$"{operation}ElapsedMs"] = heartbeat.OperationElapsedMs;
        _stageDurationsMs[$"{operation}UiHeartbeatMaxGapMs"] = heartbeat.MaximumGapMs;
        _uiHeartbeatMaxIntervalMs = Math.Max(
            _uiHeartbeatMaxIntervalMs,
            heartbeat.MaximumGapMs);

        // 小于一个采样周期的操作无需强求 tick；超过采样周期则必须看到活动期心跳。
        Require(
            heartbeat.OperationElapsedMs < 10 || heartbeat.ActiveTicks > 0,
            $"{operation} 执行期间 Avalonia UI heartbeat 停止。");
    }

    private async Task MeasureStageAsync(string name, Func<Task> operation)
    {
        var elapsed = Stopwatch.StartNew();
        try
        {
            await operation();
        }
        finally
        {
            _stageDurationsMs[name] = elapsed.ElapsedMilliseconds;
        }
    }

    private void Require(bool condition, string failure)
    {
        if (!condition)
        {
            _failures.Add(failure);
        }
    }

    private HarnessReport BuildReport(
        bool success,
        IReadOnlyList<HarnessAsset> assets,
        PlaybackResourceSnapshot resources) =>
        new(
            3,
            success,
            DateTimeOffset.UtcNow,
            _elapsed.ElapsedMilliseconds,
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(),
            Environment.Version.ToString(),
            typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown",
            typeof(ISecureVideoPlaybackSession).Assembly.GetName().Version?.ToString() ?? "unknown",
            typeof(Avalonia.Application).Assembly.GetName().Version?.ToString() ?? "unknown",
            typeof(Dock.Model.Core.IDockable).Assembly.GetName().Version?.ToString() ?? "unknown",
            typeof(LibVLCSharp.Shared.LibVLC).Assembly.GetName().Version?.ToString() ?? "unknown",
            GetNativeLibVlcVersion(),
            options.Cycles,
            options.DockSwitches,
            options.MediaSwitches,
            assets.Select(asset => new HarnessAssetReport(
                asset.FileName,
                asset.Sha256)).ToArray(),
            resources,
            new Dictionary<string, long>(_stageDurationsMs),
            _uiHeartbeatMaxIntervalMs,
            _randomSeekTargets.ToArray(),
            _nearEndSeekChunkTrace.ToArray(),
            _failures.ToArray());

    private static string GetNativeLibVlcVersion()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "native",
            "win-x64",
            "libvlc",
            "libvlc.dll");
        if (!File.Exists(path))
        {
            return "unavailable";
        }

        return FileVersionInfo.GetVersionInfo(path).ProductVersion ??
               FileVersionInfo.GetVersionInfo(path).FileVersion ??
               "unknown";
    }

    private async Task WriteReportAsync(HarnessReport report)
    {
        var path = Path.GetFullPath(options.ReportPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        await File.WriteAllTextAsync(path, json);
        Console.WriteLine($"G3 report: {path}");
    }

    private static int CountUnexpectedTopLevelWindows()
    {
        var processId = (uint)Environment.ProcessId;
        var mainHandle = (Application.Current?.ApplicationLifetime as
            IClassicDesktopStyleApplicationLifetime)?.MainWindow?
            .TryGetPlatformHandle()?.Handle ?? nint.Zero;
        var unexpected = 0;
        EnumWindows((handle, _) =>
        {
            GetWindowThreadProcessId(handle, out var owner);
            if (owner == processId &&
                handle != mainHandle &&
                IsWindowVisible(handle))
            {
                unexpected++;
            }
            return true;
        }, nint.Zero);
        return unexpected;
    }

    private delegate bool EnumWindowsCallback(nint handle, nint parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint handle, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint handle);
}

internal sealed record HarnessAsset(
    string FileName,
    string SourcePath,
    string EncryptedPath,
    string Sha256);

internal sealed record HarnessAssetReport(string FileName, string Sha256);

internal sealed record HarnessReport(
    int SchemaVersion,
    bool Success,
    DateTimeOffset ExecutedAtUtc,
    long ElapsedMs,
    string OperatingSystem,
    string Architecture,
    string DotNetVersion,
    string HostAssemblyVersion,
    string MySmallToolsAssemblyVersion,
    string AvaloniaVersion,
    string DockVersion,
    string LibVlcSharpVersion,
    string NativeLibVlcVersion,
    int Cycles,
    int DockSwitches,
    int MediaSwitches,
    IReadOnlyList<HarnessAssetReport> Assets,
    PlaybackResourceSnapshot FinalResources,
    IReadOnlyDictionary<string, long> StageDurationsMs,
    long UiHeartbeatMaxIntervalMs,
    IReadOnlyList<long> RandomSeekTargets,
    IReadOnlyList<long> NearEndSeekChunkTrace,
    IReadOnlyList<string> Failures);

internal readonly record struct UiHeartbeatResult(
    long OperationElapsedMs,
    int ActiveTicks,
    long MaximumGapMs);

internal sealed record HarnessOptions(
    int Cycles,
    int DockSwitches,
    int MediaSwitches,
    string ReportPath)
{
    public static HarnessOptions Parse(string[] args)
    {
        var cycles = ReadInt(args, "--cycles", 100);
        var dockSwitches = ReadInt(args, "--dock-switches", 20);
        var mediaSwitches = ReadInt(args, "--media-switches", 30);
        var report = ReadString(
            args,
            "--report",
            Path.Combine("TestResults", "G3", "g3-playback-windows-x64.json"));
        return new HarnessOptions(cycles, dockSwitches, mediaSwitches, report);
    }

    private static int ReadInt(string[] args, string name, int fallback)
    {
        var value = ReadString(args, name, fallback.ToString());
        if (!int.TryParse(value, out var parsed) || parsed < 0)
        {
            throw new ArgumentException($"{name} 必须是非负整数。");
        }
        return parsed;
    }

    private static string ReadString(string[] args, string name, string fallback)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
    }
}
