using Dock.Model;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Business.Docking;
using MyAvaloniaManagement.Business.Workspace;
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
        HostDockFactory.DisableFloating(root);

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

    private static FactoryContext CreateFactory()
    {
        var services = new ServiceCollection();
        services.AddSingleton<DocumentScopeManager>();
        var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<DocumentScopeManager>();
        var extensions = new PluginRegistry([], []);
        var factory = PluginTestWorkspaceSession.Create(extensions, manager);
        return new FactoryContext(provider, factory);
    }

    private static IRootDock CreateRoot(WorkspaceSession factory)
    {
        var root = factory.CreateRootDock();
        root.Id = DockLayoutIds.Root;
        root.VisibleDockables = factory.CreateList<IDockable>();
        root.HiddenDockables = factory.CreateList<IDockable>();
        root.Windows = factory.CreateList<IDockWindow>();
        return root;
    }

    private static ToolDock CreateToolDock(
        WorkspaceSession factory,
        string id) =>
        new()
        {
            Id = id,
            VisibleDockables = factory.CreateList<IDockable>()
        };

    private static DocumentDock CreateDocumentDock(
        WorkspaceSession factory) =>
        new()
        {
            Id = DockLayoutIds.Documents,
            VisibleDockables = factory.CreateList<IDockable>()
        };

    private sealed class FactoryContext(
        Microsoft.Extensions.DependencyInjection.ServiceProvider provider,
        WorkspaceSession factory) : IDisposable
    {
        public WorkspaceSession Factory { get; } = factory;

        public void Dispose() => provider.Dispose();
    }
}
