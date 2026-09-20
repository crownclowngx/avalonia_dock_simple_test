using MyAvaloniaManagement.Business.Startup;
using MyAvaloniaManagement.Business.Plugins.Enablement;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Composition;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Documents.Ownership;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>进度必须来自实际 DLL 发现及生命周期执行，测试同时核对事实和报告，避免只验证调用次数。</summary>
public sealed class StartupPluginProgressTests
{
    [Fact]
    public void 实际程序集加载报告开始和完成_缓存读取不伪造再次加载()
    {
        using var files = new EnablementPluginFiles();
        var progress = new Collector();
        var discovery = AssemblyLoaderHelper.Discover(files.Root, dataRoot: files.DataRoot, progress: progress);
        Assert.Single(discovery.Assemblies);
        var loading = progress.Events.Where(item => item.Stage == StartupStage.Loading && item.PluginId is not null).ToArray();
        Assert.Equal(2, loading.Length);
        Assert.Equal(StartupOutcome.Started, loading[0].Outcome);
        Assert.Equal(StartupOutcome.Succeeded, loading[1].Outcome);
        Assert.Equal(1, loading[1].Completed);
        Assert.Equal(1, loading[1].Total);
        var repeated = new Collector();
        Assert.Same(discovery, AssemblyLoaderHelper.Discover(files.Root, dataRoot: files.DataRoot, progress: repeated));
        Assert.Empty(repeated.Events);
    }

    [Fact]
    public void 禁用插件只报告跳过而不构造加载上下文()
    {
        using var files = new EnablementPluginFiles();
        var progress = new Collector();
        var discovery = AssemblyLoaderHelper.Discover(files.Root, new(new([PluginEnablementLoadingTests.Owner]), "test"),
            files.DataRoot, progress);
        Assert.Empty(discovery.Assemblies);
        var skipped = Assert.Single(progress.Events, item => item.Outcome == StartupOutcome.Skipped);
        Assert.Equal(0, skipped.Total);
        Assert.Equal(0, skipped.Completed);
        PluginEnablementLoadingTests.AssertNoLoadedFiles(files.Root);
    }

    [Fact]
    public void DLL损坏报告失败且已处理数到达总数()
    {
        using var files = new EnablementPluginFiles();
        File.Delete(Path.Combine(files.PluginDirectory, "PluginIsolation.PluginV1.dll"));
        var progress = new Collector();
        var discovery = AssemblyLoaderHelper.Discover(files.Root, dataRoot: files.DataRoot, progress: progress);
        Assert.Empty(discovery.Assemblies);
        Assert.NotEmpty(discovery.Diagnostics);
        var failure = Assert.Single(progress.Events, item => item.Stage == StartupStage.Loading && item.Outcome == StartupOutcome.Failed);
        Assert.Equal(1, failure.Completed);
        Assert.Equal(failure.Total, failure.Completed);
    }

    [Fact]
    public void 扫描时取消不进入任何DLL加载()
    {
        using var files = new EnablementPluginFiles();
        using var cancellation = new CancellationTokenSource();
        var progress = new Collector(item => { if (item.Stage == StartupStage.Scanning) cancellation.Cancel(); });
        Assert.Throws<OperationCanceledException>(() => AssemblyLoaderHelper.Discover(files.Root,
            dataRoot: files.DataRoot, progress: progress, cancellationToken: cancellation.Token));
        PluginEnablementLoadingTests.AssertNoLoadedFiles(files.Root);
    }

    [Fact]
    public async Task 初始化失败与成功均推进实际计数且保持执行顺序()
    {
        var calls = new List<string>();
        var first = new CallbackLifecycle(() => { calls.Add("a"); return Task.FromException(new IOException("failure")); });
        var second = new CallbackLifecycle(() => { calls.Add("b"); return Task.CompletedTask; });
        var registry = new PluginRegistry([], [], [], [new(new PluginId("a"), first.GetType()), new(new PluginId("b"), second.GetType())]);
        var states = new PluginLifecycleStateStore(registry);
        var coordinator = new PluginLifecycleCoordinator(registry, new Resolver(new() { ["a"] = first, ["b"] = second }), states);
        var progress = new Collector();
        await coordinator.InitializeAllAsync(progress: progress);
        Assert.Equal(new[] { "a", "b" }, calls);
        var completed = progress.Events.Where(item => item.Outcome != StartupOutcome.Started).ToArray();
        Assert.Equal(new[] { StartupOutcome.Failed, StartupOutcome.Succeeded }, completed.Select(item => item.Outcome));
        Assert.Equal(new[] { 1, 2 }, completed.Select(item => item.Completed));
        Assert.All(completed, item => Assert.Equal(2, item.Total));
        Assert.False(states.IsAvailable(new("a")));
        Assert.True(states.IsAvailable(new("b")));
        await coordinator.ShutdownAllAsync();
    }

    [Fact]
    public async Task 空生命周期阶段不虚构插件执行()
    {
        var registry = new PluginRegistry([], []);
        var coordinator = new PluginLifecycleCoordinator(registry, new Resolver([]), new(registry));
        var progress = new Collector();
        await coordinator.InitializeAllAsync(progress: progress);
        Assert.Equal(0, Assert.Single(progress.Events).Total);
    }

    [Fact]
    public void 注册失败不妨碍后继插件_计数来自真实Provider组合()
    {
        using var files = new EnablementPluginFiles();
        using var diagnostics = HostDiagnosticSession.Start(files.DataRoot);
        var services = new ServiceCollection();
        services.AddApplicationServices();
        using var host = services.BuildServiceProvider();
        using var owners = new PluginProviderOwner();
        var catalog = PluginModuleCatalog.CreateForTests(new (PluginId, IPluginModule)[]
        { (new("myavalonia.plugin.a-failed"), new Module(true)), (new("myavalonia.plugin.b-ok"), new Module(false)) });
        var progress = new Collector();
        owners.Compose(catalog, host, new PluginRegistryBuilder(), new DocumentScopeRegistry(), diagnostics, progress);
        var outcomes = progress.Events.Where(item => item.Outcome != StartupOutcome.Started).ToArray();
        Assert.Equal(new[] { StartupOutcome.Failed, StartupOutcome.Succeeded }, outcomes.Select(item => item.Outcome));
        Assert.Equal(2, outcomes[^1].Completed);
        Assert.Equal("myavalonia.plugin.b-ok", Assert.Single(owners.AvailablePluginIds).Value);
    }

    [Fact]
    public async Task 初始化超时报告失败_迟到成功不回写启动进度()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lifecycle = new CallbackLifecycle(() => release.Task);
        var registry = new PluginRegistry([], [], [], [new(new PluginId("slow"), lifecycle.GetType())]);
        var states = new PluginLifecycleStateStore(registry);
        var coordinator = new PluginLifecycleCoordinator(registry, new Resolver(new() { ["slow"] = lifecycle }), states,
            null, new(TimeSpan.FromMilliseconds(30), TimeSpan.FromSeconds(1)));
        var progress = new Collector();
        await coordinator.InitializeAllAsync(progress: progress);
        var last = progress.Events[^1];
        Assert.Equal(StartupOutcome.Failed, last.Outcome);
        Assert.Equal(PluginLifecycleStatus.InitializationTimedOut, states.GetState(new("slow"))!.Status);
        release.SetResult();
        await coordinator.ShutdownAllAsync();
        Assert.Same(last, progress.Events[^1]);
    }

    private sealed class Module(bool fail) : IPluginModule
    { public void Configure(IPluginRegistration registration) { if (fail) throw new InvalidOperationException("配置失败"); } }

    private sealed class Collector(Action<StartupProgress>? report = null) : IStartupProgressSink
    {
        internal List<StartupProgress> Events { get; } = [];
        public void Report(StartupProgress progress) { Events.Add(progress); report?.Invoke(progress); }
    }
    private sealed class CallbackLifecycle(Func<Task> initialize) : IPluginLifecycle
    {
        public Task InitializeAsync(CancellationToken cancellationToken) => initialize();
        public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
    private sealed class Resolver(Dictionary<string, CallbackLifecycle> items) : IPluginLifecycleResolver
    {
        public PluginLifecycleCallbacks GetRequiredLifecycle(PluginId pluginId, Type implementationType) =>
            new(items[pluginId.Value].InitializeAsync, items[pluginId.Value].ShutdownAsync);
    }
}
