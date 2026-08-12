using Dock.Model;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.CompilerServices;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.ViewModels;
using MyAvaloniaManagementCommon.ToolCreation;

namespace MyAvaloniaManagement.PluginTests;

public sealed class DockFourWayLayoutTests
{
    private static readonly ConditionalWeakTable<ManagementFactory, Dictionary<string, Tool>> ToolMaps = new();

    [Theory]
    [InlineData("Left", Alignment.Left, DockLayoutIds.LeftTools)]
    [InlineData("RIGHT", Alignment.Right, DockLayoutIds.RightTools)]
    [InlineData("top", Alignment.Top, DockLayoutIds.TopTools)]
    [InlineData("Bottom", Alignment.Bottom, DockLayoutIds.BottomTools)]
    [InlineData("", Alignment.Left, DockLayoutIds.LeftTools)]
    [InlineData("Diagonal", Alignment.Left, DockLayoutIds.LeftTools)]
    public void AlignmentMetadataMapsToSupportedDock(
        string metadataValue,
        Alignment expectedAlignment,
        string expectedDockId)
    {
        Assert.Equal(
            expectedAlignment,
            ToolDockPlacement.ParseAlignment(metadataValue));
        Assert.Equal(
            expectedDockId,
            ToolDockPlacement.GetDockId(metadataValue));
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

    [Fact]
    public void HiddenBottomToolCanBeRestoredAfterLayoutRestart()
    {
        DockLayoutSnapshotV1 snapshot;
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
            snapshot = DockLayoutLifecycle.Capture(
                firstRoot,
                firstFactory);
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

        DockLayoutLifecycle.ApplySnapshot(
            snapshot,
            secondRoot,
            secondFactory);

        Assert.NotNull(FindDockOrDefault<ToolDock>(
            secondRoot,
            DockLayoutIds.BottomTools));
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
        DockLayoutSnapshotV1 snapshot;
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

            snapshot = DockLayoutLifecycle.Capture(
                firstRoot,
                firstFactory);
            Assert.Equal(
                expectedDockId,
                snapshot.Tools.Single(tool => tool.Id == movedTool.Id).DockId);
            Assert.Contains(
                snapshot.Panes,
                pane => pane.Id == expectedPaneId);
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

        DockLayoutLifecycle.ApplySnapshot(
            snapshot,
            secondRoot,
            secondFactory);

        var stableVerticalDock = FindDock<ToolDock>(
            secondRoot,
            expectedDockId);
        Assert.Contains(restoredTool, stableVerticalDock.VisibleDockables!);
        Assert.Same(stableVerticalDock, restoredTool.Owner);
        Assert.False(stableVerticalDock.IsEmpty);
        Assert.Same(
            FindDock<ProportionalDock>(
                secondRoot,
                DockLayoutIds.WorkspaceRows),
            FindDock<ProportionalDock>(secondRoot, expectedPaneId).Owner);
    }

    [Fact]
    public void LegacyTwoWaySnapshotMigratesVerticalToolsAndPreservesHorizontalState()
    {
        using var context = CreateFactory(
            ("legacyLeftTool", "Left"),
            ("legacyTopTool", "Top"),
            ("legacyBottomTool", "Bottom"));
        var factory = context.Factory;
        var left = RegisterTool(factory, "legacyLeftTool", "Left");
        var top = RegisterTool(factory, "legacyTopTool", "Top");
        var bottom = RegisterTool(factory, "legacyBottomTool", "Bottom");
        var root = factory.CreateWorkspaceLayout(CreateDocumentDock(factory));
        factory.InitLayout(root);

        var legacy = new DockLayoutSnapshotV1
        {
            Panes =
            [
                new DockPaneSnapshotV1
                {
                    Id = DockLayoutIds.LeftPane,
                    Proportion = 0.18
                }
            ],
            Tools =
            [
                new DockToolSnapshotV1
                {
                    Id = left.Id,
                    DockId = DockLayoutIds.LeftTools,
                    Order = 0,
                    IsVisible = false
                },
                new DockToolSnapshotV1
                {
                    Id = top.Id,
                    DockId = DockLayoutIds.LeftTools,
                    Order = 1,
                    IsVisible = false
                },
                new DockToolSnapshotV1
                {
                    Id = bottom.Id,
                    DockId = DockLayoutIds.LeftTools,
                    Order = 2,
                    IsVisible = true,
                    IsFloating = true,
                    FloatingBounds = new DockFloatingBoundsV1
                    {
                        X = 10,
                        Y = 20,
                        Width = 600,
                        Height = 400
                    }
                }
            ]
        };

        var migrated = DockLayoutLifecycle.NormalizeLegacyTwoWaySnapshot(
            legacy,
            factory);

        Assert.False(migrated.Tools.Single(tool => tool.Id == left.Id).IsVisible);
        var migratedTop = migrated.Tools.Single(tool => tool.Id == top.Id);
        Assert.Equal(DockLayoutIds.TopTools, migratedTop.DockId);
        Assert.True(migratedTop.IsVisible);
        Assert.Equal(0, migratedTop.Order);
        var migratedBottom = migrated.Tools.Single(tool => tool.Id == bottom.Id);
        Assert.Equal(DockLayoutIds.BottomTools, migratedBottom.DockId);
        Assert.True(migratedBottom.IsVisible);
        Assert.False(migratedBottom.IsFloating);
        Assert.Null(migratedBottom.FloatingBounds);
        Assert.Null(DockLayoutSnapshotValidator.Validate(migrated));

        DockLayoutLifecycle.ApplySnapshot(migrated, root, factory);

        Assert.Same(
            FindDock<ToolDock>(root, DockLayoutIds.TopTools),
            top.Owner);
        Assert.Same(
            FindDock<ToolDock>(root, DockLayoutIds.BottomTools),
            bottom.Owner);
        var rows = FindDock<ProportionalDock>(
            root,
            DockLayoutIds.WorkspaceRows);
        Assert.Same(
            rows,
            FindDock<ProportionalDock>(
                root,
                DockLayoutIds.TopPane).Owner);
        Assert.Same(
            rows,
            FindDock<ProportionalDock>(
                root,
                DockLayoutIds.BottomPane).Owner);
        Assert.Contains(
            left,
            factory.FindRoot(left, _ => true)!.HiddenDockables!);

        var captured = DockLayoutLifecycle.Capture(root, factory);
        Assert.Contains(
            captured.Panes,
            pane => pane.Id == DockLayoutIds.TopPane);
        Assert.Contains(
            captured.Panes,
            pane => pane.Id == DockLayoutIds.BottomPane);
        Assert.Equal(
            DockLayoutIds.TopTools,
            captured.Tools.Single(tool => tool.Id == top.Id).DockId);
        Assert.Equal(
            DockLayoutIds.BottomTools,
            captured.Tools.Single(tool => tool.Id == bottom.Id).DockId);
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
        DockLayoutSnapshotV1 snapshot;
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
            snapshot = DockLayoutLifecycle.Capture(firstRoot, firstFactory);
            var state = Assert.Single(snapshot.Tools);
            Assert.Equal(expectedDockId, state.DockId);
            Assert.True(state.IsVisible);
            Assert.True(state.IsPinned);
            Assert.False(state.IsFloating);
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

        DockLayoutLifecycle.ApplySnapshot(snapshot, secondRoot, secondFactory);

        var restoredRoot = secondFactory.FindRoot(restoredTool, _ => true)!;
        Assert.Contains(
            restoredTool,
            GetPinnedDockables(restoredRoot, expectedAlignment)!);
        Assert.DoesNotContain(restoredTool, restoredRoot.HiddenDockables ?? []);
        Assert.DoesNotContain(
            restoredTool,
            FindDock<ToolDock>(secondRoot, expectedDockId).VisibleDockables!);
        var recaptured = DockLayoutLifecycle.Capture(secondRoot, secondFactory);
        var recapturedState = Assert.Single(recaptured.Tools);
        Assert.True(recapturedState.IsVisible);
        Assert.True(recapturedState.IsPinned);
        Assert.Equal(expectedDockId, recapturedState.DockId);
    }

    [Fact]
    public void ExpandedPinnedAndHiddenToolsPreserveDistinctStatesAndPinnedOrder()
    {
        DockLayoutSnapshotV1 snapshot;
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
            snapshot = DockLayoutLifecycle.Capture(firstRoot, firstFactory);

            Assert.Equal((true, false), GetToolState(snapshot, expanded.Id));
            Assert.Equal((true, true), GetToolState(snapshot, pinnedFirst.Id));
            Assert.Equal((true, true), GetToolState(snapshot, pinnedSecond.Id));
            Assert.Equal((false, false), GetToolState(snapshot, hidden.Id));
            Assert.True(
                snapshot.Tools.Single(tool => tool.Id == pinnedFirst.Id).Order <
                snapshot.Tools.Single(tool => tool.Id == pinnedSecond.Id).Order);
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

        DockLayoutLifecycle.ApplySnapshot(snapshot, secondRoot, secondFactory);

        var leftDock = FindDock<ToolDock>(secondRoot, DockLayoutIds.LeftTools);
        Assert.Contains(restoredExpanded, leftDock.VisibleDockables!);
        var restoredRoot = secondFactory.FindRoot(restoredPinnedFirst, _ => true)!;
        Assert.Equal(
            new[] { restoredPinnedFirst, restoredPinnedSecond },
            GetPinnedDockables(restoredRoot, Alignment.Left));
        Assert.Contains(restoredHidden, restoredRoot.HiddenDockables!);
    }

    private static (bool IsVisible, bool IsPinned) GetToolState(
        DockLayoutSnapshotV1 snapshot,
        string toolId)
    {
        var state = snapshot.Tools.Single(tool => tool.Id == toolId);
        return (state.IsVisible, state.IsPinned);
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
        ManagementFactory factory,
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
        ManagementFactory factory) =>
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
        var strategies = toolDefinitions.Select(definition =>
        {
            var dockSide = Enum.Parse<ToolDockSide>(definition.Alignment, ignoreCase: true);
            var typeId = CreateTestToolTypeId(definition.Id);
            return (IToolCreationStrategy)new StubToolStrategy(
                tools[definition.Id],
                new ToolMetadata(typeId, definition.Id, dockSide, [new ToolTypeId(definition.Id)])
                {
                    Description = definition.Id
                });
        }).ToArray();
        var extensions = new HostExtensionRegistry([], strategies);
        var factory = new ManagementFactory(
            extensions,
            manager,
            new MyAvaloniaManagementCommon.Message.MessengerService());
        ToolMaps.Add(factory, tools);
        return new FactoryContext(provider, factory);
    }

    private static ToolTypeId CreateTestToolTypeId(string id) =>
        new($"myavalonia.host.tool.test.{id.ToLowerInvariant()}");

    private sealed class StubToolStrategy(
        Tool tool,
        ToolMetadata metadata) : IToolCreationStrategy
    {
        public Tool CreateTool() => tool;

        public ToolMetadata GetMetadata() => metadata;
    }

    private sealed class FactoryContext(
        Microsoft.Extensions.DependencyInjection.ServiceProvider provider,
        ManagementFactory factory) : IDisposable
    {
        public ManagementFactory Factory { get; } = factory;

        public void Dispose() => provider.Dispose();
    }
}
