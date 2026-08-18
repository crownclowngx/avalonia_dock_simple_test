using Dock.Model;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace MyAvaloniaManagement.PluginTests;

public sealed class DockFloatingDisabledTests
{
    [Fact]
    public void FloatingIsDisabledWhileDragDropAndInternalMoveRemainEnabled()
    {
        using var context = CreateFactory();
        var factory = context.Factory;
        var root = CreateRoot(factory);
        ManagementFactory.DisableFloating(root);

        var leftDock = CreateToolDock(factory, DockLayoutIds.LeftTools);
        var rightDock = CreateToolDock(factory, DockLayoutIds.RightTools);
        var documentDock = CreateDocumentDock(factory);
        var tool = (Tool)factory.CreateTool();
        tool.Id = "testTool";
        var document = (Document)factory.CreateDocument();
        document.Id = "testDocument";

        factory.AddDockable(root, leftDock);
        factory.AddDockable(root, documentDock);
        factory.AddDockable(root, rightDock);
        factory.AddDockable(leftDock, tool);
        factory.AddDockable(documentDock, document);

        Assert.False(DockCapabilityResolver.IsEnabled(tool, DockCapability.Float));
        Assert.False(DockCapabilityResolver.IsEnabled(document, DockCapability.Float));
        Assert.True(DockCapabilityResolver.IsEnabled(tool, DockCapability.Drag));
        Assert.True(DockCapabilityResolver.IsEnabled(tool, DockCapability.Drop));
        Assert.True(DockCapabilityResolver.IsEnabled(document, DockCapability.Drag));
        Assert.True(DockCapabilityResolver.IsEnabled(document, DockCapability.Drop));

        factory.FloatDockable(tool);
        factory.FloatDockable(document, null);
        factory.FloatAllDockables(tool);
        factory.FloatAllDockables(document, null);

        Assert.Same(leftDock, tool.Owner);
        Assert.Same(documentDock, document.Owner);
        Assert.Empty(root.Windows!);

        factory.MoveDockable(leftDock, rightDock, tool, null);

        Assert.Same(rightDock, tool.Owner);
        Assert.DoesNotContain(tool, leftDock.VisibleDockables!);
        Assert.Contains(tool, rightDock.VisibleDockables!);
    }

    [Fact]
    public void LegacyFloatingToolIsRestoredToItsMainWindowDockAndNormalizedOnSave()
    {
        using var context = CreateFactory();
        var factory = context.Factory;
        var root = CreateRoot(factory);
        ManagementFactory.DisableFloating(root);

        var leftDock = CreateToolDock(factory, DockLayoutIds.LeftTools);
        var rightDock = CreateToolDock(factory, DockLayoutIds.RightTools);
        var tool = (Tool)factory.CreateTool();
        tool.Id = "legacyFloatingTool";

        factory.AddDockable(root, leftDock);
        factory.AddDockable(root, rightDock);
        factory.AddDockable(leftDock, tool);
        ((Dictionary<string, Tool>)factory.CreatedTools).Add(tool.Id, tool);

        var snapshot = new DockLayoutSnapshotV1
        {
            Tools =
            [
                new DockToolSnapshotV1
                {
                    Id = tool.Id,
                    DockId = DockLayoutIds.RightTools,
                    Order = 0,
                    IsVisible = true,
                    IsFloating = true,
                    FloatingBounds = new DockFloatingBoundsV1
                    {
                        X = 100,
                        Y = 120,
                        Width = 640,
                        Height = 480
                    }
                }
            ],
            ActiveToolId = tool.Id
        };

        Assert.Null(DockLayoutSnapshotValidator.Validate(snapshot));

        DockLayoutLifecycle.ApplySnapshot(snapshot, root, factory);

        Assert.Same(rightDock, tool.Owner);
        Assert.Same(tool, rightDock.ActiveDockable);
        Assert.Empty(root.Windows!);

        var normalized = DockLayoutLifecycle.Capture(root, factory);
        var toolState = Assert.Single(normalized.Tools);
        Assert.False(toolState.IsFloating);
        Assert.Null(toolState.FloatingBounds);
        Assert.Equal(DockLayoutIds.RightTools, toolState.DockId);
        Assert.True(toolState.IsVisible);
        Assert.Equal(tool.Id, normalized.ActiveToolId);
    }

    private static FactoryContext CreateFactory()
    {
        var services = new ServiceCollection();
        services.AddSingleton<DocumentScopeManager>();
        var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<DocumentScopeManager>();
        var extensions = new PluginRegistry([], []);
        var factory = new ManagementFactory(
            extensions,
            manager,
            new MyAvaloniaManagement.Business.Events.HostEventBus());
        return new FactoryContext(provider, factory);
    }

    private static IRootDock CreateRoot(ManagementFactory factory)
    {
        var root = factory.CreateRootDock();
        root.Id = DockLayoutIds.Root;
        root.VisibleDockables = factory.CreateList<IDockable>();
        root.HiddenDockables = factory.CreateList<IDockable>();
        root.Windows = factory.CreateList<IDockWindow>();
        return root;
    }

    private static ToolDock CreateToolDock(
        ManagementFactory factory,
        string id) =>
        new()
        {
            Id = id,
            VisibleDockables = factory.CreateList<IDockable>()
        };

    private static DocumentDock CreateDocumentDock(
        ManagementFactory factory) =>
        new()
        {
            Id = DockLayoutIds.Documents,
            VisibleDockables = factory.CreateList<IDockable>()
        };

    private sealed class FactoryContext(
        Microsoft.Extensions.DependencyInjection.ServiceProvider provider,
        ManagementFactory factory) : IDisposable
    {
        public ManagementFactory Factory { get; } = factory;

        public void Dispose() => provider.Dispose();
    }
}
