using Dock.Model;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.CompilerServices;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Business.Workspace;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;
using MyAvaloniaManagement.ViewModels;

namespace MyAvaloniaManagement.PluginTests;

public sealed partial class DockFourWayLayoutTests
{
    private static readonly ConditionalWeakTable<WorkspaceSession, Dictionary<string, Tool>> ToolMaps = new();

    [Theory]
    [InlineData(ToolDockSide.Left, Alignment.Left, DockLayoutIds.LeftTools)]
    [InlineData(ToolDockSide.Right, Alignment.Right, DockLayoutIds.RightTools)]
    [InlineData(ToolDockSide.Top, Alignment.Top, DockLayoutIds.TopTools)]
    [InlineData(ToolDockSide.Bottom, Alignment.Bottom, DockLayoutIds.BottomTools)]
    public void Tool方向映射到对应Dock(
        ToolDockSide dockSide,
        Alignment expectedAlignment,
        string expectedDockId)
    {
        Assert.Equal(
            expectedAlignment,
            ToolDockPlacement.ToAlignment(dockSide));
        Assert.Equal(
            expectedDockId,
            ToolDockPlacement.GetDockId(expectedAlignment));
    }

    [Fact]
    public void FourWayLayoutPlacesTopAndBottomAcrossFullWorkspaceWidth()
    {
        using var context = CreateFactory(
            ("leftTool", "Left"),
            ("rightTool", "Right"),
            ("topTool", "Top"),
            ("bottomTool", "Bottom"));
        var factory = context.Factory;
        var left = RegisterTool(factory, "leftTool", "Left");
        var right = RegisterTool(factory, "rightTool", "Right");
        var top = RegisterTool(factory, "topTool", "TOP");
        var bottom = RegisterTool(factory, "bottomTool", "bottom");
        var documentDock = CreateDocumentDock(factory);

        var root = factory.CreateWorkspaceLayout(documentDock);
        factory.InitLayout(root);

        var columns = FindDock<ProportionalDock>(
            root,
            DockLayoutIds.WorkspaceColumns);
        var rows = FindDock<ProportionalDock>(
            root,
            DockLayoutIds.WorkspaceRows);
        var leftPane = FindDock<ProportionalDock>(
            root,
            DockLayoutIds.LeftPane);
        var rightPane = FindDock<ProportionalDock>(
            root,
            DockLayoutIds.RightPane);
        var topPane = FindDock<ProportionalDock>(root, DockLayoutIds.TopPane);
        var bottomPane = FindDock<ProportionalDock>(
            root,
            DockLayoutIds.BottomPane);
        var leftDock = FindDock<ToolDock>(root, DockLayoutIds.LeftTools);
        var rightDock = FindDock<ToolDock>(root, DockLayoutIds.RightTools);
        var topDock = FindDock<ToolDock>(root, DockLayoutIds.TopTools);
        var bottomDock = FindDock<ToolDock>(root, DockLayoutIds.BottomTools);

        Assert.Equal(Orientation.Vertical, rows.Orientation);
        Assert.Equal(
            new IDockable[] { topPane, columns, bottomPane },
            rows.VisibleDockables!
                .Where(dockable => dockable is not IProportionalDockSplitter));

        Assert.Equal(Orientation.Horizontal, columns.Orientation);
        Assert.Equal(
            new IDockable[] { leftPane, documentDock, rightPane },
            columns.VisibleDockables!
                .Where(dockable => dockable is not IProportionalDockSplitter));
        Assert.DoesNotContain(topPane, columns.VisibleDockables!);
        Assert.DoesNotContain(bottomPane, columns.VisibleDockables!);
        Assert.Same(rows, topPane.Owner);
        Assert.Same(rows, bottomPane.Owner);
        Assert.Same(columns, leftPane.Owner);
        Assert.Same(columns, rightPane.Owner);
        Assert.Same(columns, documentDock.Owner);

        Assert.Equal(0.20, topPane.Proportion);
        Assert.Equal(0.20, topPane.CollapsedProportion);
        Assert.Equal(0.20, bottomPane.Proportion);
        Assert.Equal(0.20, bottomPane.CollapsedProportion);
        Assert.Equal(Alignment.Top, topDock.Alignment);
        Assert.Equal(Alignment.Bottom, bottomDock.Alignment);
        Assert.Same(leftDock, left.Owner);
        Assert.Same(rightDock, right.Owner);
        Assert.Same(topDock, top.Owner);
        Assert.Same(bottomDock, bottom.Owner);
    }

    [Fact]
    public void EmptyVerticalAlignmentsDoNotCreateBlankWorkspaceRows()
    {
        using var context = CreateFactory(("leftOnlyTool", "Left"));
        var factory = context.Factory;
        RegisterTool(factory, "leftOnlyTool", "Left");
        var documentDock = CreateDocumentDock(factory);

        var root = factory.CreateWorkspaceLayout(documentDock);
        factory.InitLayout(root);
        var rows = FindDock<ProportionalDock>(
            root,
            DockLayoutIds.WorkspaceRows);
        var columns = FindDock<ProportionalDock>(
            root,
            DockLayoutIds.WorkspaceColumns);

        Assert.Single(rows.VisibleDockables!);
        Assert.Same(columns, rows.VisibleDockables![0]);
        Assert.Equal(
            new IDockable[]
            {
                FindDock<ProportionalDock>(root, DockLayoutIds.LeftPane),
                documentDock,
                FindDock<ProportionalDock>(root, DockLayoutIds.RightPane)
            },
            columns.VisibleDockables!
                .Where(dockable => dockable is not IProportionalDockSplitter));
        Assert.Null(FindDockOrDefault<ToolDock>(root, DockLayoutIds.TopTools));
        Assert.Null(FindDockOrDefault<ToolDock>(root, DockLayoutIds.BottomTools));
    }

    [Fact]
    public void HidingLastTopToolCollapsesPaneAndRestoreExpandsIt()
    {
        using var context = CreateFactory(("collapsibleTopTool", "Top"));
        var factory = context.Factory;
        var top = RegisterTool(factory, "collapsibleTopTool", "Top");
        var root = factory.CreateWorkspaceLayout(CreateDocumentDock(factory));
        factory.InitLayout(root);
        var topDock = FindDock<ToolDock>(root, DockLayoutIds.TopTools);
        var topPane = FindDock<ProportionalDock>(root, DockLayoutIds.TopPane);

        factory.HideDockable(top);
        var owningRoot = factory.FindRoot(top, _ => true)!;

        Assert.Empty(topDock.VisibleDockables!);
        Assert.True(topDock.IsEmpty);
        Assert.True(topPane.IsEmpty);
        Assert.Contains(top, owningRoot.HiddenDockables!);
        Assert.Equal(0.20, topPane.CollapsedProportion);

        factory.RemoveDockable(topDock, collapse: false);
        Assert.Null(FindDockOrDefault<ToolDock>(
            root,
            DockLayoutIds.TopTools));

        Assert.True(factory.RestoreTool(root, top));
        var restoredTopDock = FindDock<ToolDock>(
            root,
            DockLayoutIds.TopTools);

        Assert.Contains(top, restoredTopDock.VisibleDockables!);
        Assert.Same(restoredTopDock, top.Owner);
        Assert.False(restoredTopDock.IsEmpty);
        Assert.False(topPane.IsEmpty);
        Assert.DoesNotContain(top, owningRoot.HiddenDockables!);
        Assert.Equal(0.20, topPane.CollapsedProportion);
    }

    [Theory]
    [InlineData("Left", Alignment.Left)]
    [InlineData("Right", Alignment.Right)]
    [InlineData("Top", Alignment.Top)]
    [InlineData("Bottom", Alignment.Bottom)]
    public void HiddenToolRestoresAfterItsEntirePaneWasRemoved(string side, Alignment alignment)
    {
        using var context = CreateFactory(("detachedTool", side));
        var session = context.Factory;
        var tool = RegisterTool(session, "detachedTool", side);
        var documentDock = CreateDocumentDock(session);
        var root = session.CreateWorkspaceLayout(documentDock);
        session.InitLayout(root);
        var paneId = ToolDockPlacement.GetPaneId(alignment);
        var dockId = ToolDockPlacement.GetDockId(alignment);
        var oldPane = FindDock<ProportionalDock>(root, paneId);
        var parent = Assert.IsType<ProportionalDock>(oldPane.Owner);
        var originalOrder = parent.VisibleDockables!.Select(item => item.Id).ToArray();

        session.HideDockable(tool);
        var owningRoot = session.FindRoot(tool, _ => true)!;
        session.RemoveDockable(oldPane, collapse: true);
        Assert.Null(FindDockOrDefault<ProportionalDock>(root, paneId));
        Assert.Null(FindDockOrDefault<ToolDock>(root, dockId));

        Assert.True(session.RestoreTool(root, tool));

        var restoredPane = FindDock<ProportionalDock>(root, paneId);
        var restoredDock = FindDock<ToolDock>(root, dockId);
        Assert.NotSame(oldPane, restoredPane);
        Assert.Same(parent, restoredPane.Owner);
        Assert.Same(restoredPane, restoredDock.Owner);
        Assert.Same(restoredDock, tool.Owner);
        Assert.Same(tool, Assert.Single(restoredDock.VisibleDockables!));
        Assert.Same(tool, restoredDock.ActiveDockable);
        Assert.False(restoredPane.IsEmpty);
        Assert.False(restoredDock.IsEmpty);
        Assert.DoesNotContain(tool, owningRoot.HiddenDockables!);
        Assert.Null(tool.OriginalOwner);
        Assert.Equal(originalOrder, parent.VisibleDockables!.Select(item => item.Id));
        Assert.Same(documentDock, FindDock<DocumentDock>(root, DockLayoutIds.Documents));
        Assert.Same(restoredDock, session.EnsureToolDock(root, alignment));
        Assert.Single(EnumerateDocks(root), dock => dock.Id == paneId);
        Assert.Single(EnumerateDocks(root), dock => dock.Id == dockId);
    }

    [Fact]
    public void HiddenBottomToolCanBeRestoredAfterLayoutRestart()
    {
        DockLayoutSnapshotV3 snapshot;
        using (var firstContext = CreateFactory(("restartBottomTool", "Bottom")))
        {
            var firstFactory = firstContext.Factory;
            var firstBottom = RegisterTool(
                firstFactory,
                "restartBottomTool",
                "Bottom");
            var firstRoot = firstFactory.CreateWorkspaceLayout(
                CreateDocumentDock(firstFactory));
            firstFactory.InitLayout(firstRoot);

            firstFactory.HideDockable(firstBottom);
            snapshot = firstFactory.LayoutState.Capture(firstFactory);
        }

        using var secondContext = CreateFactory(("restartBottomTool", "Bottom"));
        var secondFactory = secondContext.Factory;
        var secondBottom = RegisterTool(
            secondFactory,
            "restartBottomTool",
            "Bottom");
        var secondRoot = secondFactory.CreateWorkspaceLayout(
            CreateDocumentDock(secondFactory));
        secondFactory.InitLayout(secondRoot);

        // V3 会提交新容器，必须继续观察 Session 返回的新根；Tool 实例仍属于当前会话。
        secondRoot = secondFactory.LayoutState.Apply(secondFactory, snapshot);

        Assert.Contains(secondBottom, secondRoot.HiddenDockables!);
        Assert.Equal("hidden", Assert.Single(snapshot.Tools).State);
        Assert.True(secondFactory.RestoreTool(secondRoot, secondBottom));

        var restoredBottomDock = FindDock<ToolDock>(
            secondRoot,
            DockLayoutIds.BottomTools);
        Assert.Contains(secondBottom, restoredBottomDock.VisibleDockables!);
        Assert.Same(restoredBottomDock, secondBottom.Owner);
        Assert.False(restoredBottomDock.IsEmpty);
        Assert.False(FindDock<ProportionalDock>(
            secondRoot,
            DockLayoutIds.BottomPane).IsEmpty);
    }

    [Theory]
    [InlineData(
        DockOperation.Top,
        Alignment.Top,
        DockLayoutIds.TopPane,
        DockLayoutIds.TopTools)]
    [InlineData(
        DockOperation.Bottom,
        Alignment.Bottom,
        DockLayoutIds.BottomPane,
        DockLayoutIds.BottomTools)]
    public void RuntimeVerticalSplitImmediatelyUsesFullWidthStableDockAndRestores(
        DockOperation operation,
        Alignment expectedAlignment,
        string expectedPaneId,
        string expectedDockId)
    {
        DockLayoutSnapshotV3 snapshot;
        using (var firstContext = CreateFactory(
                   ("runtimeVerticalTool", "Right"),
                   ("rightSiblingTool", "Right")))
        {
            var firstFactory = firstContext.Factory;
            var movedTool = RegisterTool(
                firstFactory,
                "runtimeVerticalTool",
                "Right");
            RegisterTool(firstFactory, "rightSiblingTool", "Right");
            var documentDock = CreateDocumentDock(firstFactory);
            var firstRoot = firstFactory.CreateWorkspaceLayout(documentDock);
            firstFactory.InitLayout(firstRoot);
            var sourceDock = FindDock<ToolDock>(
                firstRoot,
                DockLayoutIds.RightTools);

            var dockService = new DockService();
            Assert.True(dockService.SplitDockable(
                movedTool,
                sourceDock,
                documentDock,
                operation,
                bExecute: true));
            var runtimeVerticalDock = Assert.IsType<ToolDock>(movedTool.Owner);
            Assert.Equal(expectedAlignment, runtimeVerticalDock.Alignment);
            Assert.Equal(expectedDockId, runtimeVerticalDock.Id);
            var workspaceRows = FindDock<ProportionalDock>(
                firstRoot,
                DockLayoutIds.WorkspaceRows);
            var workspaceColumns = FindDock<ProportionalDock>(
                firstRoot,
                DockLayoutIds.WorkspaceColumns);
            var verticalPane = FindDock<ProportionalDock>(
                firstRoot,
                expectedPaneId);
            Assert.Same(workspaceRows, verticalPane.Owner);
            Assert.Same(verticalPane, runtimeVerticalDock.Owner);
            Assert.Same(workspaceColumns, documentDock.Owner);
            Assert.Equal(0.20, verticalPane.Proportion);
            Assert.Equal(0.20, verticalPane.CollapsedProportion);
            Assert.DoesNotContain(
                EnumerateDocks(firstRoot).OfType<ToolDock>(),
                dock => !DockLayoutIds.IsToolDockId(dock.Id) &&
                        dock.Alignment == expectedAlignment);

            snapshot = firstFactory.LayoutState.Capture(firstFactory);
            Assert.Equal(
                expectedDockId,
                snapshot.Tools.Single(tool => tool.Id == movedTool.Id).ReturnDockId);
            var savedGroup = Assert.Single(DockLayoutTree.Enumerate(snapshot.MainWindow.Root),
                node => node.ToolIds.Contains(movedTool.Id));
            // 未经过 UI 测量的模型中，中间区域权重为 1，上下区域为 0.2。
            // V3 保存归一化后的占比 1/6，不能把运行时权重直接当作线格式比例。
            Assert.Equal(1.0 / 6, savedGroup.Proportion, precision: 6);
            Assert.Equal("vertical", snapshot.MainWindow.Root.Orientation);
            Assert.Same(savedGroup, operation == DockOperation.Top
                ? snapshot.MainWindow.Root.Children[0] : snapshot.MainWindow.Root.Children[^1]);
        }

        using var secondContext = CreateFactory(
            ("runtimeVerticalTool", "Right"),
            ("rightSiblingTool", "Right"));
        var secondFactory = secondContext.Factory;
        var restoredTool = RegisterTool(
            secondFactory,
            "runtimeVerticalTool",
            "Right");
        RegisterTool(secondFactory, "rightSiblingTool", "Right");
        var secondRoot = secondFactory.CreateWorkspaceLayout(
            CreateDocumentDock(secondFactory));
        secondFactory.InitLayout(secondRoot);
        Assert.Null(FindDockOrDefault<ToolDock>(
            secondRoot,
            expectedDockId));

        // V3 会提交新容器，必须继续观察 Session 返回的新根；Tool 实例仍属于当前会话。
        secondRoot = secondFactory.LayoutState.Apply(secondFactory, snapshot);

        var stableVerticalDock = Assert.IsType<ToolDock>(restoredTool.Owner);
        Assert.Equal(expectedAlignment, stableVerticalDock.Alignment);
        Assert.Equal(1.0 / 6, stableVerticalDock.Proportion, precision: 6);
        Assert.Contains(restoredTool, stableVerticalDock.VisibleDockables!);
        Assert.Same(stableVerticalDock, restoredTool.Owner);
        Assert.False(stableVerticalDock.IsEmpty);
        var restoredRows = Assert.IsType<ProportionalDock>(stableVerticalDock.Owner);
        Assert.Equal(Orientation.Vertical, restoredRows.Orientation);
        var children = restoredRows.VisibleDockables!.Where(item => item is not IProportionalDockSplitter).ToArray();
        Assert.Same(stableVerticalDock, operation == DockOperation.Top ? children[0] : children[^1]);
        Assert.Contains(EnumerateDocks(restoredRows), dock => dock.Id == DockLayoutIds.Documents);
        Assert.Same(restoredTool, secondFactory.CreatedTools[restoredTool.Id]);
    }

    [Theory]
    [InlineData("Left", Alignment.Left, DockLayoutIds.LeftTools)]
    [InlineData("Right", Alignment.Right, DockLayoutIds.RightTools)]
    [InlineData("Top", Alignment.Top, DockLayoutIds.TopTools)]
    [InlineData("Bottom", Alignment.Bottom, DockLayoutIds.BottomTools)]
    public void PinnedToolRoundTripsAsCollapsedEdgeTab(
        string metadataAlignment,
        Alignment expectedAlignment,
        string expectedDockId)
    {
        DockLayoutSnapshotV3 snapshot;
        using (var firstContext = CreateFactory(
                   ($"pinned{metadataAlignment}Tool", metadataAlignment)))
        {
            var firstFactory = firstContext.Factory;
            var firstTool = RegisterTool(
                firstFactory,
                $"pinned{metadataAlignment}Tool",
                metadataAlignment);
            var firstRoot = firstFactory.CreateWorkspaceLayout(
                CreateDocumentDock(firstFactory));
            firstFactory.InitLayout(firstRoot);

            firstFactory.PinDockable(firstTool);

            var owningRoot = firstFactory.FindRoot(firstTool, _ => true)!;
            Assert.Contains(
                firstTool,
                GetPinnedDockables(owningRoot, expectedAlignment)!);
            snapshot = firstFactory.LayoutState.Capture(firstFactory);
            var state = Assert.Single(snapshot.Tools);
            Assert.Equal(expectedDockId, state.ReturnDockId);
            Assert.Equal("autoHidden", state.State);
        }

        using var secondContext = CreateFactory(
            ($"pinned{metadataAlignment}Tool", metadataAlignment));
        var secondFactory = secondContext.Factory;
        var restoredTool = RegisterTool(
            secondFactory,
            $"pinned{metadataAlignment}Tool",
            metadataAlignment);
        var secondRoot = secondFactory.CreateWorkspaceLayout(
            CreateDocumentDock(secondFactory));
        secondFactory.InitLayout(secondRoot);

        // V3 会提交新容器，必须继续观察 Session 返回的新根；Tool 实例仍属于当前会话。
        secondRoot = secondFactory.LayoutState.Apply(secondFactory, snapshot);

        var restoredRoot = secondFactory.FindRoot(restoredTool, _ => true)!;
        Assert.Contains(
            restoredTool,
            GetPinnedDockables(restoredRoot, expectedAlignment)!);
        Assert.DoesNotContain(restoredTool, restoredRoot.HiddenDockables ?? []);
        Assert.False(DockTreeNavigator.IsDockableAttached(secondRoot, restoredTool));
        var recaptured = secondFactory.LayoutState.Capture(secondFactory);
        var recapturedState = Assert.Single(recaptured.Tools);
        Assert.Equal("autoHidden", recapturedState.State);
        Assert.Equal(expectedDockId, recapturedState.ReturnDockId);
    }

    [Fact]
    public void ExpandedPinnedAndHiddenToolsPreserveDistinctStatesAndPinnedOrder()
    {
        DockLayoutSnapshotV3 snapshot;
        using (var firstContext = CreateFactory(
                   ("expandedTool", "Left"),
                   ("pinnedFirstTool", "Left"),
                   ("pinnedSecondTool", "Left"),
                   ("hiddenTool", "Left")))
        {
            var firstFactory = firstContext.Factory;
            var expanded = RegisterTool(firstFactory, "expandedTool", "Left");
            var pinnedFirst = RegisterTool(firstFactory, "pinnedFirstTool", "Left");
            var pinnedSecond = RegisterTool(firstFactory, "pinnedSecondTool", "Left");
            var hidden = RegisterTool(firstFactory, "hiddenTool", "Left");
            var firstRoot = firstFactory.CreateWorkspaceLayout(
                CreateDocumentDock(firstFactory));
            firstFactory.InitLayout(firstRoot);

            firstFactory.PinDockable(pinnedFirst);
            firstFactory.PinDockable(pinnedSecond);
            firstFactory.HideDockable(hidden);
            snapshot = firstFactory.LayoutState.Capture(firstFactory);

            Assert.Equal("visible", GetToolState(snapshot, expanded.Id));
            Assert.Equal("autoHidden", GetToolState(snapshot, pinnedFirst.Id));
            Assert.Equal("autoHidden", GetToolState(snapshot, pinnedSecond.Id));
            Assert.Equal("hidden", GetToolState(snapshot, hidden.Id));
            Assert.True(
                snapshot.Tools.Single(tool => tool.Id == pinnedFirst.Id).ReturnOrder <
                snapshot.Tools.Single(tool => tool.Id == pinnedSecond.Id).ReturnOrder);
        }

        using var secondContext = CreateFactory(
            ("expandedTool", "Left"),
            ("pinnedFirstTool", "Left"),
            ("pinnedSecondTool", "Left"),
            ("hiddenTool", "Left"));
        var secondFactory = secondContext.Factory;
        var restoredExpanded = RegisterTool(secondFactory, "expandedTool", "Left");
        var restoredPinnedFirst = RegisterTool(secondFactory, "pinnedFirstTool", "Left");
        var restoredPinnedSecond = RegisterTool(secondFactory, "pinnedSecondTool", "Left");
        var restoredHidden = RegisterTool(secondFactory, "hiddenTool", "Left");
        var secondRoot = secondFactory.CreateWorkspaceLayout(
            CreateDocumentDock(secondFactory));
        secondFactory.InitLayout(secondRoot);

        // V3 会提交新容器，必须继续观察 Session 返回的新根；Tool 实例仍属于当前会话。
        secondRoot = secondFactory.LayoutState.Apply(secondFactory, snapshot);

        var leftDock = Assert.IsType<ToolDock>(restoredExpanded.Owner);
        Assert.Equal(Alignment.Left, leftDock.Alignment);
        Assert.Contains(restoredExpanded, leftDock.VisibleDockables!);
        var restoredRoot = secondFactory.FindRoot(restoredPinnedFirst, _ => true)!;
        Assert.Equal(
            new[] { restoredPinnedFirst, restoredPinnedSecond },
            GetPinnedDockables(restoredRoot, Alignment.Left));
        Assert.Contains(restoredHidden, secondRoot.HiddenDockables!);
        Assert.Same(restoredHidden, secondFactory.CreatedTools[restoredHidden.Id]);
    }

    private static string GetToolState(
        DockLayoutSnapshotV3 snapshot,
        string toolId)
    {
        var state = snapshot.Tools.Single(tool => tool.Id == toolId);
        return state.State;
    }

    private static IList<IDockable>? GetPinnedDockables(
        IRootDock root,
        Alignment alignment) =>
        alignment switch
        {
            Alignment.Right => root.RightPinnedDockables,
            Alignment.Top => root.TopPinnedDockables,
            Alignment.Bottom => root.BottomPinnedDockables,
            _ => root.LeftPinnedDockables
        };

    private static Tool RegisterTool(
        WorkspaceSession factory,
        string id,
        string alignment)
    {
        var tools = ToolMaps.GetValue(factory, _ => []);
        if (!tools.TryGetValue(id, out var tool))
        {
            throw new InvalidOperationException(
                $"测试 Tool '{id}' 必须在构建不可变注册表之前声明，不能运行时补注册。" );
        }

        // 这里只模拟 Factory 已创建实例的缓存状态；策略与元数据已在构造 Factory 前原子提交，
        // 因而不会重新引入生产路径已经删除的“运行时后注册”语义。
        ((Dictionary<string, Tool>)factory.CreatedTools).TryAdd(tool.Id!, tool);
        return tool;
    }

    private static DocumentDock CreateDocumentDock(
        WorkspaceSession factory) =>
        new()
        {
            Id = DockLayoutIds.Documents,
            IsCollapsable = false,
            Proportion = double.NaN,
            VisibleDockables = factory.CreateList<IDockable>()
        };

    private static T FindDock<T>(IDock root, string id)
        where T : class, IDock =>
        FindDockOrDefault<T>(root, id)
        ?? throw new InvalidOperationException($"Dock '{id}' was not found.");

    private static IEnumerable<IDock> EnumerateDocks(IDock root)
    {
        yield return root;
        if (root.VisibleDockables is null)
        {
            yield break;
        }

        foreach (var child in root.VisibleDockables.OfType<IDock>())
        {
            foreach (var descendant in EnumerateDocks(child))
            {
                yield return descendant;
            }
        }
    }

    private static T? FindDockOrDefault<T>(IDock root, string id)
        where T : class, IDock
    {
        if (root is T typed && root.Id == id)
        {
            return typed;
        }

        if (root.VisibleDockables is null)
        {
            return null;
        }

        foreach (var child in root.VisibleDockables.OfType<IDock>())
        {
            var result = FindDockOrDefault<T>(child, id);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    private static FactoryContext CreateFactory(params (string Id, string Alignment)[] toolDefinitions)
    {
        var services = new ServiceCollection();
        services.AddSingleton<DocumentScopeManager>();
        var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<DocumentScopeManager>();
        var tools = toolDefinitions.ToDictionary(
            definition => definition.Id,
            definition => new Tool
            {
                Id = CreateTestToolTypeId(definition.Id).Value,
                Title = definition.Id,
                CanClose = true
            },
            StringComparer.Ordinal);
        var extensions = new PluginRegistry(
            [],
            toolDefinitions.Select(definition =>
            {
                var dockSide = Enum.Parse<ToolDockSide>(
                    definition.Alignment,
                    ignoreCase: true);
                var typeId = CreateTestToolTypeId(definition.Id);
                return new PluginToolRegistration(
                    new PluginId("myavalonia.plugin.layout-tests"),
                    new ToolDescriptor(
                        typeId,
                        definition.Id,
                        definition.Id,
                        dockSide,
                        ToolCloseBehavior.Hide),
                    tools[definition.Id].GetType(),
                    typeof(Avalonia.Controls.UserControl),
                    static () => new Avalonia.Controls.UserControl());
            }).ToArray());
        var factory = PluginTestWorkspaceSession.Create(extensions, manager);
        ToolMaps.Add(factory, tools);
        return new FactoryContext(provider, factory);
    }

    private static ToolTypeId CreateTestToolTypeId(string id) =>
        new($"myavalonia.plugin.layout-tests.tool.{id.ToLowerInvariant()}");

    private sealed class FactoryContext(
        Microsoft.Extensions.DependencyInjection.ServiceProvider provider,
        WorkspaceSession factory) : IDisposable
    {
        public WorkspaceSession Factory { get; } = factory;

        public void Dispose() => provider.Dispose();
    }
}
