using Dock.Model;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Business.Workspace;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>
/// V11-P1 使用真实 DockService 操作纯模型。覆盖 V3 可产生的任意分割形状，
/// 不依赖某个插件的 View，也不把“不抛异常”当作结构正确的充分条件。
/// </summary>
public sealed partial class DockFourWayLayoutTests
{
    public static IEnumerable<object[]> P1LocalSplits()
    {
        foreach (var operation in new[] { DockOperation.Top, DockOperation.Bottom })
        foreach (var position in new[] { 0, 1, 2 })
        foreach (var orientation in new[] { Orientation.Horizontal, Orientation.Vertical })
        foreach (var nested in new[] { false, true })
            yield return [operation, position, orientation, nested];
    }

    [Theory]
    [MemberData(nameof(P1LocalSplits))]
    public void P1工具局部上下分割保留目标区域和邻居(
        DockOperation operation, int position, Orientation orientation, bool nested)
    {
        using var context = CreateFactory(("moved", "Right"), ("sibling", "Right"));
        var session = context.Factory;
        var moved = RegisterTool(session, "moved", "Right");
        var sibling = RegisterTool(session, "sibling", "Right");
        var documents = CreateDocumentDock(session);
        var root = session.CreateWorkspaceLayout(documents);
        session.InitLayout(root);
        var target = (ToolDock)sibling.Owner!;
        var parent = ArrangeP1(session, root, target, documents, position, orientation, nested);
        var neighbors = parent.VisibleDockables!.Where(item => item is not IProportionalDockSplitter && item != target).ToArray();
        var neighborShares = neighbors.Select(item => item.Proportion).ToArray();
        var originalShare = target.Proportion;

        Assert.True(new DockService().SplitDockable(moved, target, target, operation, bExecute: true));

        var inserted = Assert.IsAssignableFrom<IToolDock>(moved.Owner);
        Assert.NotSame(target, inserted);
        var local = Assert.IsAssignableFrom<IProportionalDock>(target.Owner);
        Assert.Same(local, inserted.Owner);
        Assert.Equal(Orientation.Vertical, local.Orientation);
        // Dock 新建不同方向容器时以 NaN 表示默认均分；同方向直接插入则写入各半的显式比例。
        var splitShare = orientation == Orientation.Vertical ? originalShare / 2 : double.NaN;
        Assert.Equal(splitShare, inserted.Proportion);
        Assert.Equal(splitShare, target.Proportion);
        Assert.Equal(splitShare, inserted.CollapsedProportion);
        if (orientation == Orientation.Horizontal) Assert.Equal(originalShare, local.Proportion);
        var children = local.VisibleDockables!.Where(item => item is not IProportionalDockSplitter).ToArray();
        var targetIndex = Array.IndexOf(children, target);
        Assert.Same(inserted, children[targetIndex + (operation == DockOperation.Top ? -1 : 1)]);
        Assert.All(neighbors, item => Assert.Same(parent, item.Owner));
        Assert.Equal(neighborShares, neighbors.Select(item => item.Proportion));
        Assert.Null(FindDockOrDefault<ToolDock>(root, operation == DockOperation.Top ? DockLayoutIds.TopTools : DockLayoutIds.BottomTools));
        AssertP1Tree(root);
        Assert.Single(EnumerateDocks(root).SelectMany(dock => dock.VisibleDockables ?? []), item => ReferenceEquals(item, moved));
        Assert.Single(EnumerateDocks(root).SelectMany(dock => dock.VisibleDockables ?? []), item => ReferenceEquals(item, sibling));
    }

    [Theory]
    [InlineData(DockOperation.Top, 0)]
    [InlineData(DockOperation.Bottom, 0)]
    [InlineData(DockOperation.Top, 1)]
    [InlineData(DockOperation.Bottom, 1)]
    [InlineData(DockOperation.Top, 2)]
    [InlineData(DockOperation.Bottom, 2)]
    public void P1文档区域全宽停靠替换临时容器保留分隔条和引用(DockOperation operation, int position)
    {
        using var context = CreateFactory(("moved", "Right"), ("sibling", "Right"));
        var session = context.Factory;
        var moved = RegisterTool(session, "moved", "Right");
        RegisterTool(session, "sibling", "Right");
        var documents = CreateDocumentDock(session);
        var root = session.CreateWorkspaceLayout(documents);
        session.InitLayout(root);
        var source = (ToolDock)moved.Owner!;
        var parent = ArrangeP1(session, root, documents, source, position, Orientation.Horizontal, nested: true);
        var before = parent.VisibleDockables!.ToArray();
        var share = documents.Proportion;
        parent.ActiveDockable = documents;
        parent.DefaultDockable = documents;

        Assert.True(new DockService().SplitDockable(moved, source, documents, operation, bExecute: true));

        Assert.Equal(operation == DockOperation.Top ? DockLayoutIds.TopTools : DockLayoutIds.BottomTools, moved.Owner!.Id);
        Assert.Equal(before, parent.VisibleDockables);
        Assert.Same(parent, documents.Owner);
        Assert.Same(documents, parent.ActiveDockable);
        Assert.Same(documents, parent.DefaultDockable);
        Assert.Equal(share, documents.Proportion);
        Assert.Equal(share, documents.CollapsedProportion);
        AssertP1Tree(root);
    }

    private static ProportionalDock ArrangeP1(WorkspaceSession session, IRootDock root,
        IDock target, IDock companion, int position, Orientation orientation, bool nested)
    {
        var columns = FindDock<ProportionalDock>(root, DockLayoutIds.WorkspaceColumns);
        var parent = nested ? new ProportionalDock { Id = "restored-parent", IsCollapsable = false } : columns;
        parent.Orientation = orientation;
        var nodes = new List<IDockable> { companion, new DocumentDock { Id = "secondary-documents", IsCollapsable = false } };
        nodes.Insert(position, target);
        var arranged = new List<IDockable>();
        for (var index = 0; index < nodes.Count; index++)
        {
            nodes[index].Proportion = nodes[index].CollapsedProportion = new[] { 0.2, 0.3, 0.5 }[index];
            if (index != 0) arranged.Add(new ProportionalDockSplitter());
            arranged.Add(nodes[index]);
        }
        parent.VisibleDockables = session.CreateList<IDockable>([.. arranged]);
        parent.ActiveDockable = parent.DefaultDockable = target;
        if (nested)
        {
            columns.VisibleDockables = session.CreateList<IDockable>(parent);
            columns.ActiveDockable = parent;
        }
        session.InitLayout(root);
        return parent;
    }

    [Theory]
    [InlineData(DockOperation.Top, DockLayoutIds.WorkspaceColumns)]
    [InlineData(DockOperation.Bottom, DockLayoutIds.WorkspaceColumns)]
    [InlineData(DockOperation.Top, DockLayoutIds.WorkspaceRows)]
    [InlineData(DockOperation.Bottom, DockLayoutIds.WorkspaceRows)]
    public void P1主骨架全局目标仍使用全宽停靠(DockOperation operation, string targetId)
    {
        using var context = CreateFactory(("moved", "Right"), ("sibling", "Right"));
        var session = context.Factory;
        var moved = RegisterTool(session, "moved", "Right");
        RegisterTool(session, "sibling", "Right");
        var root = session.CreateWorkspaceLayout(CreateDocumentDock(session));
        session.InitLayout(root);
        var target = FindDock<ProportionalDock>(root, targetId);
        Assert.True(new DockManager(new DockService()).ValidateDockable(moved, target, DragAction.Move, operation, bExecute: true));
        Assert.Equal(operation == DockOperation.Top ? DockLayoutIds.TopTools : DockLayoutIds.BottomTools, moved.Owner!.Id);
        AssertP1Tree(root);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void P1主骨架缺失保留布局且诊断失败不改变控制流(bool brokenDiagnostics)
    {
        using var context = CreateFactory(("moved", "Right"));
        var session = context.Factory;
        var tool = RegisterTool(session, "moved", "Right");
        var document = CreateDocumentDock(session);
        var root = session.CreateWorkspaceLayout(document);
        session.InitLayout(root);
        FindDock<ProportionalDock>(root, DockLayoutIds.WorkspaceRows).Id = "missing-stable-rows";
        var before = EnumerateDocks(root).ToArray();
        var owner = tool.Owner;
        var reasons = new List<string>();
        var coordinator = new ToolDockCoordinator(session.DockFactory, new DockWorkspaceBuilder(session.DockFactory),
            _ => Alignment.Right, reason => { reasons.Add(reason); if (brokenDiagnostics) throw new IOException("诊断故障"); });
        coordinator.OnDockSplitCompleted(document, owner!, DockOperation.Bottom, root);
        Assert.Equal(["main-skeleton-unavailable"], reasons);
        Assert.Same(owner, tool.Owner);
        Assert.Equal(before, EnumerateDocks(root));
        AssertP1Tree(root);
    }

    [Fact]
    public void P1普通分割冒用稳定ID也不触发全宽策略()
    {
        using var context = CreateFactory(("moved", "Right"), ("sibling", "Right"));
        var session = context.Factory;
        var moved = RegisterTool(session, "moved", "Right");
        RegisterTool(session, "sibling", "Right");
        var documents = CreateDocumentDock(session);
        var root = session.CreateWorkspaceLayout(documents);
        session.InitLayout(root);
        var source = (ToolDock)moved.Owner!;
        var target = ArrangeP1(session, root, documents, source, 0, Orientation.Horizontal, nested: true);
        target.Id = DockLayoutIds.WorkspaceColumns;
        Assert.True(new DockService().SplitDockable(moved, source, target, DockOperation.Bottom, bExecute: true));
        Assert.NotEqual(DockLayoutIds.BottomTools, moved.Owner!.Id);
        AssertP1Tree(root);
    }

    private static void AssertP1Tree(IDock root)
    {
        var visited = new HashSet<IDockable>(ReferenceEqualityComparer.Instance);
        Visit(root);
        void Visit(IDockable item)
        {
            Assert.True(visited.Add(item), "节点不能重复挂接或形成回边。");
            if (item is not IDock dock || dock.VisibleDockables is not { } children) return;
            if (dock is IProportionalDock && children.Count > 1)
            {
                Assert.True(children.Count % 2 == 1, "分割必须以内容开始和结束。");
                for (var i = 0; i < children.Count; i++)
                    Assert.Equal(i % 2 == 1, children[i] is IProportionalDockSplitter);
            }
            foreach (var child in children) { Assert.Same(dock, child.Owner); Visit(child); }
            if (dock.ActiveDockable is { } active) Assert.Contains(active, children);
            if (dock.DefaultDockable is { } defaultItem) Assert.Contains(defaultItem, children);
        }
    }
}
