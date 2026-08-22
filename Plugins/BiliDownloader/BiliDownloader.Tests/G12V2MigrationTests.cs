using System.Text.Json;
using System.Text.Json.Nodes;
using BiliDownloader.Constants;
using BiliDownloader.Models;
using BiliDownloader.Plugin;
using BiliDownloader.Services.Api;
using BiliDownloader.Services.Auth;
using BiliDownloader.Services.ContentSources;
using BiliDownloader.Services.Download;
using BiliDownloader.Services.Infrastructure;
using BiliDownloader.Services.Persistence;
using BiliDownloader.ViewModels;
using BiliDownloader.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace BiliDownloader.Tests;

/// <summary>
/// G12 最终 Host V2 边界测试。业务算法由既有分组覆盖，本类专门证明贡献、Scope、
/// 原子恢复、readiness 与程序集依赖不再回退到 Legacy/Dock。
/// </summary>
public sealed class G12V2MigrationTests
{
    [Fact]
    public void 模块精确声明一个Document一个Tool一个Lifecycle和两个创建意图()
    {
        var services = new ServiceCollection();
        var registration = new TestPluginRegistrationContext(
            BiliDownloaderContributionIds.Plugin,
            services);

        new BiliDownloaderPluginModule().Configure(registration);

        var document = Assert.Single(registration.Contributions, item => item.Kind == "Document");
        var documentDescriptor = Assert.IsType<DocumentDescriptor>(document.Descriptor);
        Assert.Equal(typeof(BiliDownloaderViewModel), document.ModelType);
        Assert.Equal(typeof(BiliDownloaderView), document.ViewType);
        Assert.Equal(BiliDownloaderContributionIds.DownloadDocument, documentDescriptor.DocumentTypeId);
        Assert.Equal("下载", documentDescriptor.DisplayName);
        Assert.Equal("Bilibili下载器", documentDescriptor.MenuCategory);
        Assert.Equal(
            ["quick-url", "personal-source"],
            documentDescriptor.CreationIntents.Select(intent => intent.IntentId.Value));

        var tool = Assert.Single(registration.Contributions, item => item.Kind == "Tool");
        var toolDescriptor = Assert.IsType<ToolDescriptor>(tool.Descriptor);
        Assert.Equal(typeof(BiliSchedulerToolViewModel), tool.ModelType);
        Assert.Equal(typeof(BiliSchedulerToolView), tool.ViewType);
        Assert.Equal(BiliDownloaderContributionIds.SchedulerTool, toolDescriptor.ToolTypeId);
        Assert.Equal(ToolDockSide.Right, toolDescriptor.DockSide);
        Assert.Equal(ToolCloseBehavior.Hide, toolDescriptor.CloseBehavior);

        var lifecycle = Assert.Single(registration.Contributions, item => item.Kind == "Lifecycle");
        Assert.Equal(typeof(BiliDownloaderPluginLifecycle), lifecycle.ModelType);
        Assert.Equal(ServiceLifetime.Scoped,
            Assert.Single(services, item => item.ServiceType == typeof(BiliDownloaderViewModel)).Lifetime);
        Assert.Equal(ServiceLifetime.Singleton,
            Assert.Single(services, item => item.ServiceType == typeof(BiliSchedulerToolViewModel)).Lifetime);
    }

    [Fact]
    public async Task Document是普通模型且多实例隔离并投影标题与创建意图()
    {
        using var first = CreateDocument();
        using var second = CreateDocument();
        var titleChanges = 0;
        first.PresentationChanged += (_, _) => titleChanges++;

        await first.InitializeAsync(
            new DocumentActivationContext(
                "个人来源",
                BiliDownloaderContributionIds.PersonalSourceIntent),
            CancellationToken.None);
        await second.InitializeAsync(
            new DocumentActivationContext("", BiliDownloaderContributionIds.QuickUrlIntent),
            CancellationToken.None);
        first.VideoParse.Url = "BV1-first";

        Assert.Equal(typeof(ObservableObject), typeof(BiliDownloaderViewModel).BaseType);
        Assert.Equal("个人来源", first.Presentation.Title);
        Assert.Equal(1, titleChanges);
        Assert.True(first.SourceWorkflow.IsPersonalSource);
        Assert.True(second.SourceWorkflow.IsQuickUrl);
        Assert.Equal("Bilibili下载", second.Presentation.Title);
        Assert.NotEqual(first.DocumentId, second.DocumentId);
        Assert.Equal(string.Empty, second.VideoParse.Url);
    }

    [Fact]
    public async Task Document未知意图与损坏恢复在应用任何状态前失败()
    {
        using var unknown = CreateDocument();
        await Assert.ThrowsAsync<ArgumentException>(() => unknown.InitializeAsync(
                new DocumentActivationContext("未知", new CreationIntentId("unknown")),
                CancellationToken.None)
            .AsTask());

        using var damaged = CreateDocument();
        damaged.VideoParse.Url = "before";
        var invalid = new DocumentContent(
            DocumentSaveCodec.CurrentContentSchemaVersion,
            JsonSerializer.SerializeToElement(new { DocumentId = "changed", Url = 42 }));

        await Assert.ThrowsAsync<InvalidDataException>(() => damaged.InitializeAsync(
                new DocumentActivationContext("不应应用", restoredContent: invalid),
                CancellationToken.None)
            .AsTask());
        Assert.Equal("before", damaged.VideoParse.Url);
        Assert.Equal("Bilibili下载", damaged.Title);
    }

    [Fact]
    public async Task Document关闭令牌取消会终止初始化而不发布候选状态()
    {
        using var lifetime = new TestDocumentLifetime();
        using var document = CreateDocument(lifetime);
        lifetime.Close();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => document.InitializeAsync(
                new DocumentActivationContext("不应发布"),
                CancellationToken.None)
            .AsTask());

        Assert.Equal("Bilibili下载", document.Title);
        Assert.False(document.IsDirty);
    }

    [Fact]
    public async Task 捕获内容不提交脏状态且Host提交点幂等()
    {
        using var document = CreateDocument();
        await document.InitializeAsync(
            new DocumentActivationContext("保存测试"),
            CancellationToken.None);
        var dirtyChanges = 0;
        document.IsDirtyChanged += (_, _) => dirtyChanges++;
        document.VideoParse.Url = "BV1dirty";
        Assert.True(document.IsDirty);

        var snapshot = await document.CaptureSaveSnapshotAsync(CancellationToken.None);
        var content = snapshot.Content;

        Assert.Equal(3, content.SchemaVersion);
        Assert.Equal(JsonValueKind.Object, content.Payload.ValueKind);
        Assert.True(document.IsDirty);
        document.NamingTemplate.Template = "{title}-captured-later";
        document.AcceptChanges(snapshot.Revision);
        Assert.True(document.IsDirty);
        var current = await document.CaptureSaveSnapshotAsync(CancellationToken.None);
        document.AcceptChanges(current.Revision);
        document.AcceptChanges(current.Revision);
        Assert.False(document.IsDirty);
        Assert.Equal(2, dirtyChanges);
    }

    [Fact]
    public async Task readiness覆盖全部状态且Tool未就绪不读取设置SQLite或Ffmpeg()
    {
        var readiness = new BiliDownloaderPluginReadiness();
        var observed = new List<BiliDownloaderReadinessStatus>();
        readiness.Changed += (_, _) => observed.Add(readiness.Snapshot.Status);
        readiness.MarkInitializing();
        readiness.MarkReady();
        readiness.MarkStopping();
        readiness.MarkStopped();
        readiness.MarkFaulted();
        Assert.Equal(
            [
                BiliDownloaderReadinessStatus.Initializing,
                BiliDownloaderReadinessStatus.Ready,
                BiliDownloaderReadinessStatus.Stopping,
                BiliDownloaderReadinessStatus.Stopped,
                BiliDownloaderReadinessStatus.Faulted,
            ], observed);

        using var paths = new TestDataPaths();
        var repository = new InMemoryDownloadTaskRepository();
        var settings = new InMemorySettingsRepository();
        var coordinator = new BiliDownloadCoordinator(
            repository,
            new IsolatedHostEventBus(),
            new NoOpDownloadProgressTracker(),
            new FakeDownloadTaskExecutor(),
            paths);
        var unavailable = new BiliDownloaderPluginReadiness();
        using var tool = new BiliSchedulerToolViewModel(
            coordinator,
            repository,
            settings,
            new FakeFfmpegService(),
            unavailable,
            uiDispatcher: new InlineUiDispatcher());

        await tool.ActivateAsync();

        Assert.False(tool.IsPluginReady);
        Assert.Equal(0, settings.InitializeCount);
        Assert.Equal(0, repository.InitializeCount);
        Assert.Equal(unavailable.Snapshot.Message, tool.SchedulerStatus);
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task Tool释放后抑制已经排队的迟到回调且重复释放安全()
    {
        using var paths = new TestDataPaths();
        var repository = new InMemoryDownloadTaskRepository();
        var readiness = new BiliDownloaderPluginReadiness();
        var dispatcher = new DeferredUiDispatcher();
        var coordinator = new BiliDownloadCoordinator(
            repository,
            new IsolatedHostEventBus(),
            new NoOpDownloadProgressTracker(),
            new FakeDownloadTaskExecutor(),
            paths);
        var tool = new BiliSchedulerToolViewModel(
            coordinator,
            repository,
            new InMemorySettingsRepository(),
            new FakeFfmpegService(),
            readiness,
            uiDispatcher: dispatcher);

        // 两类回调先进入 UI 队列，再释放 Tool。释放后的队列即使迟到执行，
        // 也只能看到 disposed 门闩，不能再改写已经脱离 Host 的界面投影。
        coordinator.SetMaxConcurrentDownloads(2);
        readiness.MarkStopped();
        tool.Dispose();
        tool.Dispose();
        dispatcher.Drain();

        Assert.Equal("插件尚未初始化。", tool.SchedulerStatus);
        Assert.False(tool.IsPluginReady);
        await coordinator.ShutdownAsync();
    }

    [Fact]
    public async Task Lifecycle失败取消与全新对象图恢复均留下可诊断readiness()
    {
        using var failedGraph = CreateLifecycleGraph(new ThrowingLocalStateInitializer());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            failedGraph.Lifecycle.InitializeAsync(CancellationToken.None));
        Assert.Equal(BiliDownloaderReadinessStatus.Faulted, failedGraph.Readiness.Snapshot.Status);
        Assert.DoesNotContain("secret", failedGraph.Readiness.Snapshot.Message, StringComparison.OrdinalIgnoreCase);

        using var cancelledGraph = CreateLifecycleGraph(new NoOpLocalStateInitializer());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            cancelledGraph.Lifecycle.InitializeAsync(cancelled.Token));
        Assert.Equal(BiliDownloaderReadinessStatus.Faulted, cancelledGraph.Readiness.Snapshot.Status);

        using var recoveredGraph = CreateLifecycleGraph(new NoOpLocalStateInitializer());
        await recoveredGraph.Lifecycle.InitializeAsync(CancellationToken.None);
        Assert.Equal(BiliDownloaderReadinessStatus.Ready, recoveredGraph.Readiness.Snapshot.Status);
        await recoveredGraph.Lifecycle.ShutdownAsync(CancellationToken.None);
        await recoveredGraph.Lifecycle.ShutdownAsync(CancellationToken.None);
        Assert.Equal(BiliDownloaderReadinessStatus.Stopped, recoveredGraph.Readiness.Snapshot.Status);
    }

    [Fact]
    public void 生产程序集引用闭包不包含LegacyDockHost或旧Json库()
    {
        var references = typeof(BiliDownloaderPluginModule).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .ToArray();

        Assert.DoesNotContain("MyAvaloniaManagementCommon", references);
        Assert.DoesNotContain("Dock.Model", references);
        Assert.DoesNotContain("MyAvaloniaManagement", references);
        Assert.DoesNotContain("Newtonsoft.Json", references);
        Assert.Contains("MyAvaloniaManagement.PluginSdk", references);
        Assert.Contains("MyAvaloniaManagement.PluginSdk.UI", references);
    }

    [Fact]
    public void STJ远端读取器保留数字字符串容错并拒绝非对象根节点()
    {
        Assert.Equal(42, JsonValue.Create("42").Value<int>());
        Assert.Equal(4_200_000_000L, JsonValue.Create("4200000000").Value<long>());
        Assert.Equal(1.25, JsonValue.Create("1.25").Value<double>());
        Assert.True(JsonValue.Create("true").Value<bool>());
        Assert.Equal("42", JsonValue.Create(42).Value<string>());
        Assert.Null(JsonValue.Create("not-a-number").Value<int?>());
        Assert.Null(JsonValue.Create("not-a-number").Value<long?>());
        Assert.Null(JsonValue.Create("not-a-number").Value<double?>());
        Assert.Null(JsonValue.Create("not-a-bool").Value<bool?>());
        Assert.Equal(default, JsonValue.Create(1).Value<DateTime>());
        Assert.Equal(default, JsonNodeReader.Value<int>(null));

        Assert.Equal(0, JsonNodeReader.ParseObject("{\"code\":0}")["code"]!.Value<int>());
        Assert.Throws<JsonException>(() => JsonNodeReader.ParseObject("[]"));
        Assert.ThrowsAny<JsonException>(() => JsonNodeReader.ParseObject("{"));
    }

    [Fact]
    public void 迁移后输出选项文案保持既有兼容映射()
    {
        Assert.Equal("自动兼容", VideoCodecPreference.AutoCompatibility.ToDisplayText());
        Assert.Equal("AVC/H.264", VideoCodecPreference.Avc.ToDisplayText());
        Assert.Equal("HEVC/H.265", VideoCodecPreference.Hevc.ToDisplayText());
        Assert.Equal("AV1", VideoCodecPreference.Av1.ToDisplayText());
        Assert.Equal("未知", ((VideoCodecPreference)999).ToDisplayText());

        Assert.Equal("MP4", OutputContainer.Mp4.ToDisplayText());
        Assert.Equal("MKV", OutputContainer.Mkv.ToDisplayText());
        Assert.Equal("原生音频", OutputContainer.NativeAudio.ToDisplayText());
        Assert.Equal("未知", ((OutputContainer)999).ToDisplayText());

        Assert.Equal("音视频", OutputMediaMode.AudioVideo.ToDisplayText());
        Assert.Equal("仅视频", OutputMediaMode.VideoOnly.ToDisplayText());
        Assert.Equal("仅音频", OutputMediaMode.AudioOnly.ToDisplayText());
        Assert.Equal("未知", ((OutputMediaMode)999).ToDisplayText());

        Assert.Equal("AVC/H.264", OutputOptionDisplay.ActualCodecToDisplayText(" AVC "));
        Assert.Equal("HEVC/H.265", OutputOptionDisplay.ActualCodecToDisplayText("hevc"));
        Assert.Equal("AV1", OutputOptionDisplay.ActualCodecToDisplayText("av1"));
        Assert.Equal("未知", OutputOptionDisplay.ActualCodecToDisplayText(null));

        Assert.Equal("高规格未知", ((MediaFeatureFlags?)null).ToDisplayText());
        Assert.Equal("标准规格", ((MediaFeatureFlags?)MediaFeatureFlags.None).ToDisplayText());
        Assert.Equal(
            "杜比视界 + 杜比全景声",
            ((MediaFeatureFlags?)(MediaFeatureFlags.DolbyVision | MediaFeatureFlags.DolbyAtmos)).ToDisplayText());
        Assert.Equal(
            "HDR + Hi-Res",
            ((MediaFeatureFlags?)(MediaFeatureFlags.Hdr | MediaFeatureFlags.HiResAudio)).ToDisplayText());
    }

    [Fact]
    public void 历史快照能力只由持久化版本和完整字段共同决定()
    {
        var record = new DownloadTaskRecord();
        var legacy = TaskHistoryEntry.FromRecord(record);
        Assert.False(legacy.HasExactSubmissionSnapshot);
        Assert.False(legacy.HasExactHighSpecificationSnapshot);
        Assert.False(legacy.HasExactExtrasSnapshot);

        record.SubmissionSnapshotVersion = 2;
        record.SelectedAudioFeaturePreference = AudioFeaturePreference.Standard;
        Assert.False(TaskHistoryEntry.FromRecord(record).HasExactHighSpecificationSnapshot);
        record.SelectedVideoDynamicRangePreference = VideoDynamicRangePreference.Auto;
        record.SelectedAudioFeaturePreference = null;
        Assert.False(TaskHistoryEntry.FromRecord(record).HasExactHighSpecificationSnapshot);
        record.SelectedAudioFeaturePreference = AudioFeaturePreference.Standard;
        var highSpecification = TaskHistoryEntry.FromRecord(record);
        Assert.True(highSpecification.HasExactSubmissionSnapshot);
        Assert.True(highSpecification.HasExactHighSpecificationSnapshot);
        Assert.False(highSpecification.HasExactExtrasSnapshot);

        record.SubmissionSnapshotVersion = 3;
        record.SubtitleOptions = null!;
        Assert.False(TaskHistoryEntry.FromRecord(record).HasExactExtrasSnapshot);
        record.SubtitleOptions = SubtitleOptions.None;
        record.DanmakuOptions = null!;
        Assert.False(TaskHistoryEntry.FromRecord(record).HasExactExtrasSnapshot);
        record.DanmakuOptions = DanmakuOptions.None;
        var extras = TaskHistoryEntry.FromRecord(record);
        Assert.True(extras.HasExactExtrasSnapshot);
    }

    private static BiliDownloaderViewModel CreateDocument(IDocumentLifetime? documentLifetime = null)
    {
        var events = new IsolatedHostEventBus();
        var repository = new InMemoryDownloadTaskRepository();
        var login = new BiliLoginStateService(
            new InMemoryBiliCredentialStore(),
            new StubBiliSessionApi(),
            events);
        var api = new BiliApiService();
        var credentials = new FakeCredentialProvider();
        return new BiliDownloaderViewModel(
            events,
            repository,
            new InMemorySettingsRepository(),
            login,
            new BiliLoginService(),
            new ContentSourceProviderRegistry([new DirectLinkProvider(api, credentials)]),
            api,
            credentials,
            new FakeFfmpegService(),
            new BiliDownloaderDocumentStateMapper(),
            documentLifetime ?? new TestDocumentLifetime());
    }

    private static LifecycleGraph CreateLifecycleGraph(IBiliLocalStateInitializer initializer)
    {
        var paths = new TestDataPaths();
        var repository = new InMemoryDownloadTaskRepository();
        var coordinator = new BiliDownloadCoordinator(
            repository,
            new IsolatedHostEventBus(),
            new NoOpDownloadProgressTracker(),
            new FakeDownloadTaskExecutor(),
            paths);
        var events = new IsolatedHostEventBus();
        var login = new BiliLoginStateService(
            new InMemoryBiliCredentialStore(),
            new StubBiliSessionApi(),
            events);
        var readiness = new BiliDownloaderPluginReadiness();
        var lifecycle = new BiliDownloaderPluginLifecycle(
            initializer,
            login,
            coordinator,
            new InMemorySettingsRepository(),
            new FakeFfmpegService { ReadyOverride = true },
            new NoOpGlobalBandwidthLimitService(),
            readiness);
        return new LifecycleGraph(paths, lifecycle, readiness);
    }

    private sealed class ThrowingLocalStateInitializer : IBiliLocalStateInitializer
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("secret initialization detail");
    }

    private sealed class NoOpGlobalBandwidthLimitService : IGlobalBandwidthLimitService
    {
        public long CurrentBytesPerSecond => 0;

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task UpdateAsync(
            long bytesPerSecond,
            string reason,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class DeferredUiDispatcher : IUiDispatcher
    {
        private readonly Queue<Action> _pending = new();

        public void Post(Action action) => _pending.Enqueue(action);

        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        public Task InvokeAsync(Func<Task> action) => action();

        public void Drain()
        {
            while (_pending.TryDequeue(out var action)) action();
        }
    }

    private sealed record LifecycleGraph(
        TestDataPaths Paths,
        BiliDownloaderPluginLifecycle Lifecycle,
        BiliDownloaderPluginReadiness Readiness) : IDisposable
    {
        public void Dispose() => Paths.Dispose();
    }
}
