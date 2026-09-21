using Avalonia.Controls;
using Dock.Model.Mvvm.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>
/// V3 把不可用工具保留为数据，当前窗口只投影可用项；当前文件读取和保存不丢失缺失插件的恢复意图。
/// </summary>
public sealed class DockLayoutAvailabilityTests
{
    [Fact]
    public async Task 生命周期未就绪的工具不投影但保留V3可见意图()
    {
        using var workspace = new TemporaryWorkspace();
        using var services = new ServiceCollection()
            .AddSingleton<DocumentScopeManager>()
            .BuildServiceProvider();
        var pluginId = new PluginId("myavalonia.plugin.unavailable");
        var toolTypeId = new ToolTypeId("myavalonia.plugin.unavailable.tool.sample");
        var registration = new PluginToolRegistration(
            pluginId,
            new ToolDescriptor(
                toolTypeId,
                "不可用测试 Tool",
                "仅用于验证生命周期门控",
                ToolDockSide.Left,
                ToolCloseBehavior.Hide),
            typeof(Tool),
            typeof(UserControl),
            static () => new UserControl());
        var registry = new PluginRegistry(
            [],
            [],
            [registration],
            [new PluginLifecycleDeclaration(pluginId, typeof(TestLifecycle))]);
        var states = new PluginLifecycleStateStore(registry);
        var factory = PluginTestWorkspaceSession.Create(
            registry,
            services.GetRequiredService<DocumentScopeManager>(),
            new PluginAvailabilityReadModel(states));
        var tool = new Tool { Id = toolTypeId.Value, Title = "不可用测试 Tool" };
        ((Dictionary<string, Tool>)factory.CreatedTools).Add(tool.Id, tool);
        var diagnostics = new List<string>();
        using var v3 = new DockLayoutV3Store(workspace.DirectoryPath, (code, _) => diagnostics.Add(code));
        v3.Save(CreateSnapshot(toolTypeId.Value, proportion: 0.73));
        using var lifecycle = new DockLayoutLifecycle(v3);

        lifecycle.Prepare(factory);
        var applied = lifecycle.ApplyPending(factory);
        Assert.False(DockTreeNavigator.IsDockableAttached(applied, tool));
        lifecycle.Save(factory);
        await lifecycle.FlushAsync();
        Assert.Equal("visible", Assert.Single(v3.Load()!.Tools).State);
        Assert.Empty(diagnostics);
        var savedGroup = Assert.Single(DockLayoutTree.Enumerate(v3.Load()!.MainWindow.Root), node => node.Kind == "tools");
        Assert.Equal(0.73, savedGroup.Proportion, precision: 6);
        Assert.Empty(Directory.EnumerateFiles(workspace.DirectoryPath, "*.invalid.bak"));
        factory.Dispose();
    }

    [Fact]
    public async Task 未注册工具不创建实例且保留V3恢复记录()
    {
        using var workspace = new TemporaryWorkspace();
        using var services = new ServiceCollection()
            .AddSingleton<DocumentScopeManager>()
            .BuildServiceProvider();
        var registry = new PluginRegistry([], []);
        var factory = PluginTestWorkspaceSession.Create(
            registry,
            services.GetRequiredService<DocumentScopeManager>());
        var diagnostics = new List<string>();
        using var v3 = new DockLayoutV3Store(workspace.DirectoryPath, (code, _) => diagnostics.Add(code));
        v3.Save(CreateSnapshot(
            "myavalonia.plugin.not-installed.tool.sample",
            proportion: 0.73));
        using var lifecycle = new DockLayoutLifecycle(v3);

        lifecycle.Prepare(factory);
        lifecycle.ApplyPending(factory);
        lifecycle.Save(factory);
        await lifecycle.FlushAsync();
        Assert.Empty(factory.CreatedTools);
        var retained = Assert.Single(v3.Load()!.Tools);
        Assert.Equal("myavalonia.plugin.not-installed.tool.sample", retained.Id);
        Assert.Equal("visible", retained.State);
        Assert.Empty(diagnostics);
        var savedGroup = Assert.Single(DockLayoutTree.Enumerate(v3.Load()!.MainWindow.Root), node => node.Kind == "tools");
        Assert.Equal(0.73, savedGroup.Proportion, precision: 6);
        Assert.Empty(Directory.EnumerateFiles(workspace.DirectoryPath, "*.invalid.bak"));
        factory.Dispose();
    }

    /// <summary>直接表达当前恢复记录；缺失工具仍占有组位置，但不会因此创建业务实例。</summary>
    private static DockLayoutSnapshotV3 CreateSnapshot(string toolId, double proportion) => new(3,
        new("main", DockWindowBounds.Default, DockLayoutNode.Split("split", "horizontal",
            [DockLayoutNode.Group("group", [toolId], proportion, toolId),
                DockLayoutNode.Documents() with { Proportion = 1 - proportion }])), [],
        [new(toolId, "visible", DockLayoutIds.LeftTools, 0)]);

    private sealed class TestLifecycle : IPluginLifecycle
    {
        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        internal TemporaryWorkspace()
        {
            DirectoryPath = Path.Combine(
                Path.GetTempPath(),
                $"myavalonia-layout-availability-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(DirectoryPath);
        }

        internal string DirectoryPath { get; }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
    }
}
