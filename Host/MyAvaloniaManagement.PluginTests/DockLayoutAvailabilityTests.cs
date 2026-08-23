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
/// 验证布局恢复在修改 Dock 树之前同时检查注册事实和生命周期可用性。任何一个 Tool 不满足条件，
/// 整份文件都必须隔离并保留默认布局，不能只跳过坏项形成难以解释的部分恢复。
/// </summary>
public sealed class DockLayoutAvailabilityTests
{
    [Fact]
    public void 生命周期未就绪的插件Tool导致整个V2快照隔离且默认布局不被部分修改()
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
        var lifecycle = new DockLayoutLifecycle(store);

        var defaultRoot = lifecycle.Prepare(factory);
        var leftPane = FindDock<ProportionalDock>(defaultRoot, DockLayoutIds.LeftPane);
        var defaultProportion = leftPane.Proportion;
        var applied = lifecycle.ApplyPending(factory);

        Assert.Same(defaultRoot, applied);
        Assert.Equal(defaultProportion, leftPane.Proportion);
        Assert.Contains(
            $"LAYOUT_PLUGIN_UNAVAILABLE:{toolTypeId.Value}",
            diagnostics);
        Assert.False(File.Exists(workspace.LayoutPath));
        Assert.Single(Directory.EnumerateFiles(workspace.DirectoryPath, "*.invalid.bak"));
        factory.Dispose();
    }

    [Fact]
    public void 未注册Tool导致整份V2快照隔离而不是部分应用Pane比例()
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
        var lifecycle = new DockLayoutLifecycle(store);

        var defaultRoot = lifecycle.Prepare(factory);
        var leftPane = FindDock<ProportionalDock>(defaultRoot, DockLayoutIds.LeftPane);
        var defaultProportion = leftPane.Proportion;
        lifecycle.ApplyPending(factory);

        Assert.Equal(defaultProportion, leftPane.Proportion);
        Assert.Contains(
            "LAYOUT_PLUGIN_MISSING:myavalonia.plugin.not-installed.tool.sample",
            diagnostics);
        Assert.False(File.Exists(workspace.LayoutPath));
        Assert.Single(Directory.EnumerateFiles(workspace.DirectoryPath, "*.invalid.bak"));
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
