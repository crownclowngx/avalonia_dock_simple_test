using System;
using System.Collections.Generic;
using System.Linq;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using Dock.Model.Mvvm.Core;
using MyAvaloniaManagement.Business.Workspace;
using MyAvaloniaManagement.Views;

namespace MyAvaloniaManagement.Business.Layout;

internal sealed partial class DockLayoutWorkspaceState
{
    /// <summary>
    /// 将严格验证后的工具结构应用到既有模型。先建立候选容器，再转移原实例并关闭已清空的旧窗口；
    /// Document 的 Scope 和 View 始终不重建。短期检查点负责失败回滚，调用者负责保存抑制。
    /// </summary>
    internal IRootDock Apply(WorkspaceSession session, DockLayoutSnapshotV3 snapshot)
    {
        DockLayoutV3Validator.Validate(snapshot);
        var factory = session.DockFactory;
        var checkpoint = new DockLayoutTransferCheckpoint(session);
        var rememberedBefore = Remembered;
        var documents = session.GetDocuments().ToArray();
        var activeDocument = session.GetActiveDocument();
        var documentDock = new DocumentDock
        {
            Id = DockLayoutIds.Documents, Title = "Files", IsCollapsable = false,
            CanFloat = false, VisibleDockables = factory.CreateList<IDockable>([.. documents]),
            ActiveDockable = activeDocument ?? documents.FirstOrDefault(),
        };
        var states = snapshot.Tools.ToDictionary(tool => tool.Id, StringComparer.Ordinal);
        var groups = new Dictionary<string, ToolDock>(StringComparer.Ordinal);
        var content = BuildNode(session, snapshot.MainWindow.Root, states, groups, documentDock) ?? documentDock;
        var root = CreateMainRoot(session, content);
        var windows = new List<(DockLayoutWindow Saved, IDockWindow Model)>();
        foreach (var saved in snapshot.FloatingWindows)
        {
            if (BuildNode(session, saved.Root, states, groups, null) is not { } floatingContent) continue;
            var floatingRoot = CreateWindowRoot(session, floatingContent);
            var model = new DockWindow { Layout = floatingRoot };
            floatingRoot.Window = model;
            SetModelBounds(model, saved.Bounds);
            _windowIds.Add(model, new(saved.Id));
            windows.Add((saved, model));
        }

        using var change = factory.BeginLayoutChange(captureBefore: false);
        try
        {
            var oldRoot = session.RootDock;
            var oldWindows = oldRoot is null ? [] : DockTreeNavigator.EnumerateWindows(oldRoot).ToArray();
            foreach (var item in documents.Cast<IDockable>().Concat(session.CreatedTools.Values)) Detach(session, item);
            foreach (var old in oldWindows) CloseEmptyWindow(session, old);
            session.CommitRestoredLayout(root, documentDock);
            factory.InitLayout(root);
            foreach (var (_, model) in windows) factory.AddWindow(root, model);
            Remembered = snapshot;

            // 隐藏项只挂在根的 Hidden 集合，OriginalOwner 仅借用仍存在的组。完全隐藏的窗口
            // 不创建原生对象，稍后显式显示工具时再根据纯数据恢复位置。
            foreach (var pair in session.CreatedTools)
            {
                if (!states.TryGetValue(pair.Key, out var state) || state.State == "hidden")
                {
                    AddHidden(root, pair.Value);
                    var groupId = FindSavedGroup(snapshot, pair.Key)?.Id;
                    pair.Value.OriginalOwner = groupId is not null ? groups.GetValueOrDefault(groupId) : null;
                }
                else if (state.State == "autoHidden")
                {
                    var group = pair.Value.Owner as IToolDock;
                    var bounds = factory.WindowContext.CaptureBounds(factory.WindowContext.MainWindow);
                    factory.PinDockable(pair.Value);
                    // 恢复发生在新树首轮测量前，框架可能捕获 50 像素的最小正文尺寸。
                    // 依据窗口逻辑尺寸与组比例设置展开下限，避免重启后边栏只露出标题栏。
                    var proportion = group?.Proportion is > 0 and <= 1 ? group.Proportion : 0.25;
                    var horizontal = group?.Alignment is Alignment.Left or Alignment.Right;
                    pair.Value.SetPinnedBounds(0, 0, Math.Max(320, horizontal ? bounds.Width * proportion : bounds.Width),
                        Math.Max(240, horizontal ? bounds.Height : bounds.Height * proportion));
                }
            }
            if (factory.WindowContext.MainWindow is { } main) factory.WindowContext.ApplyBounds(main, snapshot.MainWindow.Bounds);
            foreach (var (saved, model) in windows)
            {
                model.Present(false);
                if (model.Host is Avalonia.Controls.Window host) factory.WindowContext.ApplyBounds(host, saved.Bounds);
            }
            if (activeDocument is not null) factory.SetActiveDockable(activeDocument);
            factory.UpdateFloatingPolicies();
            return root;
        }
        catch
        {
            checkpoint.Restore(session);
            Remembered = rememberedBefore;
            throw;
        }
    }

    /// <summary>隐藏工具原本属于浮窗时，在原组恢复单个目标；其他隐藏成员继续隐藏。</summary>
    internal bool TryRestoreFloatingTool(WorkspaceSession session, Tool tool)
    {
        if (Remembered is not { } remembered || session.RootDock is not { } mainRoot) return false;
        var saved = remembered.FloatingWindows.FirstOrDefault(window => DockLayoutTree.Enumerate(window.Root).Any(node => node.ToolIds.Contains(tool.Id)));
        if (saved is null || session.DockFactory.WindowContext.HasFullscreenContent) return false;
        if (DockTreeNavigator.IsDockableAttached(mainRoot, tool) || DockTreeNavigator.IsToolPinned(mainRoot, tool)) return false;
        var factory = session.DockFactory;
        using var change = factory.BeginLayoutChange(captureBefore: false);
        var current = DockTreeNavigator.EnumerateWindows(mainRoot).FirstOrDefault(window => GetWindowId(window) == saved.Id);
        var stateMap = remembered.Tools.ToDictionary(state => state.Id, StringComparer.Ordinal);
        stateMap[tool.Id] = stateMap[tool.Id] with { State = "visible" };
        var groups = new Dictionary<string, ToolDock>(StringComparer.Ordinal);
        var content = BuildNode(session, saved.Root, stateMap, groups, null);
        if (content is null) return false;
        var existingDocuments = current?.Layout is { } currentRoot
            ? DockTreeNavigator.Enumerate(currentRoot).OfType<Document>().ToArray() : [];
        foreach (var item in groups.Values.SelectMany(group => group.VisibleDockables ?? []).Concat(existingDocuments).ToArray())
            Detach(session, item);
        if (existingDocuments.Length != 0)
        {
            // 工具布局不持久化 Document，但运行期重建一个隐藏分支不能吞掉同窗中的文档实例。
            var documentDock = new DocumentDock
            {
                VisibleDockables = factory.CreateList<IDockable>([.. existingDocuments]),
                ActiveDockable = existingDocuments[0], IsCollapsable = true,
            };
            content = new ProportionalDock
            {
                Orientation = Orientation.Horizontal,
                VisibleDockables = factory.CreateList<IDockable>(content, new ProportionalDockSplitter(), documentDock),
            };
        }
        var root = CreateWindowRoot(session, content);
        if (current is null)
        {
            current = new DockWindow { Layout = root };
            root.Window = current;
            SetModelBounds(current, saved.Bounds);
            _windowIds.Add(current, new(saved.Id));
            factory.AddWindow(mainRoot, current);
            current.Present(false);
            if (current.Host is Avalonia.Controls.Window host) factory.WindowContext.ApplyBounds(host, saved.Bounds);
        }
        else
        {
            root.Window = current;
            current.Layout = root;
            factory.InitDockWindow(current, current.Owner, current.Host);
            current.Host?.SetLayout(root);
        }
        Remembered = remembered with { Tools = stateMap.Values.ToArray() };
        factory.UpdateFloatingPolicies();
        factory.SetActiveDockable(tool);
        return true;
    }

    private IDockable? BuildNode(WorkspaceSession session, DockLayoutNode node, IReadOnlyDictionary<string, DockLayoutTool> states,
        IDictionary<string, ToolDock> groups, DocumentDock? documents)
    {
        var factory = session.DockFactory;
        if (node.Kind == "documents")
        {
            if (documents is not null) documents.Proportion = node.Proportion;
            return documents;
        }
        IDockable result;
        if (node.Kind == "tools")
        {
            var tools = node.ToolIds.Where(id => states[id].State != "hidden" && session.CreatedTools.ContainsKey(id) && session.IsToolAvailable(id))
                .Select(id => session.CreatedTools[id]).ToArray();
            if (tools.Length == 0) return null;
            ToolDockPlacement.TryGetAlignmentFromDockId(states[tools[0].Id].ReturnDockId, out var alignment);
            var group = new ToolDock
            {
                Id = "v3-" + node.Id, Alignment = alignment, GripMode = GripMode.Visible,
                IsCollapsable = true, VisibleDockables = factory.CreateList<IDockable>([.. tools]),
                ActiveDockable = tools.FirstOrDefault(tool => tool.Id == node.ActiveToolId) ?? tools[0],
            };
            groups[node.Id] = group;
            result = group;
        }
        else
        {
            var children = node.Children.Select(child => BuildNode(session, child, states, groups, documents)).OfType<IDockable>().ToArray();
            if (children.Length == 0) return null;
            if (children.Length == 1) { children[0].Proportion = node.Proportion; return children[0]; }
            var arranged = new List<IDockable>();
            foreach (var child in children)
            {
                if (arranged.Count != 0) arranged.Add(new ProportionalDockSplitter());
                arranged.Add(child);
            }
            result = new ProportionalDock
            {
                Id = "v3-" + node.Id, Orientation = node.Orientation == "vertical" ? Orientation.Vertical : Orientation.Horizontal,
                VisibleDockables = factory.CreateList<IDockable>([.. arranged]), IsCollapsable = true,
            };
        }
        result.Proportion = node.Proportion;
        _nodeIds.Add(result, new(node.Id));
        return result;
    }

    private static IRootDock CreateMainRoot(WorkspaceSession session, IDockable content)
    {
        var factory = session.DockFactory;
        var columns = new ProportionalDock
        {
            Id = DockLayoutIds.WorkspaceColumns, Orientation = Orientation.Horizontal, IsCollapsable = false,
            CanFloat = false, VisibleDockables = factory.CreateList<IDockable>(content), ActiveDockable = content,
        };
        var rows = new ProportionalDock
        {
            Id = DockLayoutIds.WorkspaceRows, Orientation = Orientation.Vertical, IsCollapsable = false,
            CanFloat = false, VisibleDockables = factory.CreateList<IDockable>(columns), ActiveDockable = columns,
        };
        var workspace = CreateWindowRoot(session, rows);
        workspace.Id = DockLayoutIds.Workspace;
        var root = CreateWindowRoot(session, workspace);
        root.Id = DockLayoutIds.Root;
        return root;
    }

    private static IRootDock CreateWindowRoot(WorkspaceSession session, IDockable content) => new RootDock
    {
        CanFloat = false, IsCollapsable = false, VisibleDockables = session.DockFactory.CreateList<IDockable>(content),
        ActiveDockable = content, DefaultDockable = content,
    };

    private static void Detach(WorkspaceSession session, IDockable item)
    {
        if (session.RootDock is { } root)
            foreach (var current in DockTreeNavigator.EnumerateWorkspace(root).OfType<IRootDock>().ToArray())
            {
                current.HiddenDockables?.Remove(item);
                current.LeftPinnedDockables?.Remove(item);
                current.RightPinnedDockables?.Remove(item);
                current.TopPinnedDockables?.Remove(item);
                current.BottomPinnedDockables?.Remove(item);
            }
        if (item.Owner is IDock { VisibleDockables: { } visible } && visible.Contains(item))
            session.DockFactory.RemoveDockable(item, collapse: false);
        item.Owner = null;
        item.OriginalOwner = null;
    }

    private static void AddHidden(IRootDock root, Tool tool)
    {
        root.HiddenDockables ??= root.Factory!.CreateList<IDockable>();
        if (!root.HiddenDockables.Contains(tool)) root.HiddenDockables.Add(tool);
        tool.Owner = root;
        tool.Factory = root.Factory;
    }

    private static DockLayoutNode? FindSavedGroup(DockLayoutSnapshotV3 snapshot, string id) =>
        new[] { snapshot.MainWindow }.Concat(snapshot.FloatingWindows).SelectMany(window => DockLayoutTree.Enumerate(window.Root))
            .FirstOrDefault(node => node.ToolIds.Contains(id));

    private static void SetModelBounds(IDockWindow window, DockWindowBounds bounds)
    {
        window.X = bounds.X; window.Y = bounds.Y; window.Width = bounds.Width; window.Height = bounds.Height;
        window.WindowState = bounds.Maximized ? DockWindowState.Maximized : DockWindowState.Normal;
    }

    private static void CloseEmptyWindow(WorkspaceSession session, IDockWindow window)
    {
        if (window.Host is HostFloatingWindow host)
        {
            host.CloseForLayoutTransfer();
            if (host.IsVisible) throw new InvalidOperationException("浮窗拒绝布局转移关闭。");
        }
        else window.Exit();
        if (window.Owner is not null) session.DockFactory.RemoveWindow(window);
    }
}
