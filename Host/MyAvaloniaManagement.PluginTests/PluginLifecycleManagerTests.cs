using MyAvaloniaManagementCommon.Plugin;

namespace MyAvaloniaManagement.PluginTests;

public sealed class PluginLifecycleManagerTests
{
    [Fact]
    public async Task 初始化按顺序执行_关闭按相反顺序执行()
    {
        var calls = new List<string>();
        var manager = new PluginLifecycleManager([
            new RecordingLifecycle("second", 20, calls),
            new RecordingLifecycle("first", 10, calls),
        ]);

        await manager.InitializeAllAsync();
        await manager.ShutdownAllAsync();

        Assert.Equal([
            "init:first",
            "init:second",
            "shutdown:second",
            "shutdown:first",
        ], calls);
    }

    [Fact]
    public async Task 初始化与关闭均为幂等操作()
    {
        var calls = new List<string>();
        var manager = new PluginLifecycleManager([
            new RecordingLifecycle("only", 0, calls),
        ]);

        await Task.WhenAll(manager.InitializeAllAsync(), manager.InitializeAllAsync());
        await Task.WhenAll(manager.ShutdownAllAsync(), manager.ShutdownAllAsync());

        Assert.Equal(["init:only", "shutdown:only"], calls);
    }

    [Fact]
    public async Task 单个插件初始化失败_不会阻止其他插件且失败插件不参与关闭()
    {
        var calls = new List<string>();
        var manager = new PluginLifecycleManager([
            new RecordingLifecycle("broken", 0, calls, failInitialization: true),
            new RecordingLifecycle("healthy", 1, calls),
        ]);

        await manager.InitializeAllAsync();
        await manager.ShutdownAllAsync();

        Assert.Equal([
            "init:broken",
            "init:healthy",
            "shutdown:healthy",
        ], calls);
        Assert.Equal(PluginLifecycleStatus.Failed, manager.GetState("broken")?.Status);
        Assert.Equal(PluginLifecycleStatus.Stopped, manager.GetState("healthy")?.Status);
    }

    private sealed class RecordingLifecycle : IPluginLifecycle
    {
        private readonly List<string> _calls;
        private readonly bool _failInitialization;

        public RecordingLifecycle(
            string pluginId,
            int order,
            List<string> calls,
            bool failInitialization = false)
        {
            PluginId = pluginId;
            Order = order;
            _calls = calls;
            _failInitialization = failInitialization;
        }

        public string PluginId { get; }

        public int Order { get; }

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            _calls.Add($"init:{PluginId}");
            return _failInitialization
                ? Task.FromException(new InvalidOperationException("预期的初始化失败"))
                : Task.CompletedTask;
        }

        public Task ShutdownAsync(CancellationToken cancellationToken)
        {
            _calls.Add($"shutdown:{PluginId}");
            return Task.CompletedTask;
        }
    }
}
