using Avalonia.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>
/// V3 把不可用工具保留为数据，当前窗口只投影可用项；只读迁移不隔离合法旧文件。
/// </summary>
public sealed class DockLayoutAvailabilityTests
{
    [Fact]
    public async Task 生命周期未就绪的工具不投影但保留可见意图和原V2字节()
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
        var store = new DockLayoutStore(
            workspace.LayoutPath,
            (code, stableId) => diagnostics.Add($"{code}:{stableId}"));
        store.Save(CreateSnapshot(toolTypeId.Value, proportion: 0.73));
        var original = File.ReadAllBytes(workspace.LayoutPath);
        using var v3 = new DockLayoutV3Store(workspace.DirectoryPath);
        using var lifecycle = new DockLayoutLifecycle(v3);

        lifecycle.Prepare(factory);
        var applied = lifecycle.ApplyPending(factory);
        Assert.False(DockTreeNavigator.IsDockableAttached(applied, tool));
        lifecycle.Save(factory);
        await lifecycle.FlushAsync();
        Assert.Equal("visible", Assert.Single(v3.Load()!.Tools).State);
        Assert.Equal(original, File.ReadAllBytes(workspace.LayoutPath));
        Assert.Empty(Directory.EnumerateFiles(workspace.DirectoryPath, "*.invalid.bak"));
        factory.Dispose();
    }

    [Fact]
    public async Task 未注册工具保留在V3且不隔离合法V2输入()
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
        var store = new DockLayoutStore(
            workspace.LayoutPath,
            (code, stableId) => diagnostics.Add($"{code}:{stableId}"));
        store.Save(CreateSnapshot(
            "myavalonia.plugin.not-installed.tool.sample",
            proportion: 0.73));
        var original = File.ReadAllBytes(workspace.LayoutPath);
        using var v3 = new DockLayoutV3Store(workspace.DirectoryPath);
        using var lifecycle = new DockLayoutLifecycle(v3);

        lifecycle.Prepare(factory);
        lifecycle.ApplyPending(factory);
        lifecycle.Save(factory);
        await lifecycle.FlushAsync();
        var retained = Assert.Single(v3.Load()!.Tools);
        Assert.Equal("myavalonia.plugin.not-installed.tool.sample", retained.Id);
        Assert.Equal("visible", retained.State);
        Assert.Equal(original, File.ReadAllBytes(workspace.LayoutPath));
        Assert.Empty(Directory.EnumerateFiles(workspace.DirectoryPath, "*.invalid.bak"));
        factory.Dispose();
    }

    private static DockLayoutSnapshotV2 CreateSnapshot(string toolId, double proportion) =>
        new()
        {
            Panes = [new DockPaneSnapshotV2
            {
                Id = DockLayoutIds.LeftPane,
                Proportion = proportion,
            }],
            Tools = [new DockToolSnapshotV2
            {
                Id = toolId,
                DockId = DockLayoutIds.LeftTools,
                Order = 0,
                IsVisible = true,
                IsPinned = false,
            }],
            ActiveToolId = toolId,
        };

    private static T FindDock<T>(IDock root, string id)
        where T : class, IDock
        => FindDockOrDefault<T>(root, id)
           ?? throw new InvalidOperationException($"未找到 Dock：{id}。");

    private static T? FindDockOrDefault<T>(IDock root, string id)
        where T : class, IDock
    {
        if (root is T match && root.Id == id)
        {
            return match;
        }

        foreach (var child in root.VisibleDockables?.OfType<IDock>() ?? [])
        {
            var result = FindDockOrDefault<T>(child, id);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

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
            LayoutPath = Path.Combine(DirectoryPath, DockLayoutStore.LayoutFileName);
        }

        internal string DirectoryPath { get; }
        internal string LayoutPath { get; }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
    }
}
