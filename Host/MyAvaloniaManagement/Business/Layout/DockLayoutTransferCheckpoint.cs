using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.Business.Workspace;
using MyAvaloniaManagement.Views;

namespace MyAvaloniaManagement.Business.Layout;

/// <summary>
/// 一次 UI 布局转移的短期检查点，只借用原容器和业务实例。原生窗口可能拒绝关闭，也可能在
/// 建立候选窗时失败；恢复原集合并复用仍存活的窗口，已关闭窗口只重建展示壳，文档 Scope 不变。
/// 检查点不进入持久化文件，方法结束后即可释放所有临时强引用。
/// </summary>
internal sealed class DockLayoutTransferCheckpoint
{
    private readonly IRootDock _root;
    private readonly DocumentDock _documents;
    private readonly Dictionary<IDock, IDockable[]> _visible;
    private readonly Dictionary<IDock, IDockable?> _active;
    private readonly RootState[] _roots;
    private readonly WindowState[] _windows;
    private readonly Dictionary<IDockable, IDockable?> _originalOwners;

    internal DockLayoutTransferCheckpoint(WorkspaceSession session)
    {
        _root = session.RootDock!;
        _documents = session.DockFactory.GetDockable<DocumentDock>(DockLayoutIds.Documents)!;
        var docks = DockTreeNavigator.EnumerateWorkspace(_root).OfType<IDock>().Distinct().ToArray();
        _visible = docks.ToDictionary(dock => dock, dock => (dock.VisibleDockables ?? []).ToArray());
        _active = docks.ToDictionary(dock => dock, dock => dock.ActiveDockable);
        _roots = docks.OfType<IRootDock>().Select(root => new RootState(root,
            (root.HiddenDockables ?? []).ToArray(), (root.LeftPinnedDockables ?? []).ToArray(),
            (root.RightPinnedDockables ?? []).ToArray(), (root.TopPinnedDockables ?? []).ToArray(),
            (root.BottomPinnedDockables ?? []).ToArray())).ToArray();
        _windows = DockTreeNavigator.EnumerateWindows(_root).Select(window => new WindowState(window,
            window.Layout!, window.Owner!, window.Host, window.Host is Window host
                ? session.DockFactory.WindowContext.CaptureBounds(host) : DockWindowBounds.Default)).ToArray();
        _originalOwners = session.GetDocuments().Cast<IDockable>().Concat(session.CreatedTools.Values)
            .ToDictionary(item => item, item => item.OriginalOwner);
    }

    internal void Restore(WorkspaceSession session)
    {
        var factory = session.DockFactory;
        // 清空失败候选容器，关闭过程不能观察到业务内容，也就不会误释放文档。
        var candidates = session.RootDock is { } current ? DockTreeNavigator.EnumerateWindows(current).ToArray() : [];
        foreach (var item in _originalOwners.Keys)
            if (item.Owner is IDock { VisibleDockables: { } list }) list.Remove(item);
        foreach (var window in candidates.Where(window => !_windows.Any(old => ReferenceEquals(old.Window, window))))
        {
            if (window.Host is HostFloatingWindow host) host.CloseForLayoutTransfer();
            else window.Exit();
        }
        foreach (var pair in _visible) pair.Key.VisibleDockables = factory.CreateList<IDockable>(pair.Value);
        foreach (var saved in _roots)
        {
            saved.Root.Windows = factory.CreateList<IDockWindow>();
            saved.Root.HiddenDockables = factory.CreateList<IDockable>(saved.Hidden);
            saved.Root.LeftPinnedDockables = factory.CreateList<IDockable>(saved.Left);
            saved.Root.RightPinnedDockables = factory.CreateList<IDockable>(saved.Right);
            saved.Root.TopPinnedDockables = factory.CreateList<IDockable>(saved.Top);
            saved.Root.BottomPinnedDockables = factory.CreateList<IDockable>(saved.Bottom);
        }
        session.CommitRestoredLayout(_root, _documents);
        factory.InitLayout(_root);
        foreach (var saved in _windows)
        {
            var window = saved.Window;
            window.Layout = saved.Layout;
            if (saved.Layout is IRootDock root) root.Window = window;
            if (saved.Host is Window { IsVisible: true })
            {
                ((IRootDock)saved.Owner).Windows!.Add(window);
                factory.InitDockWindow(window, saved.Owner, saved.Host);
                saved.Host.SetLayout(saved.Layout);
            }
            else
            {
                factory.AddWindow((IRootDock)saved.Owner, window);
                window.Present(false);
            }
            if (window.Host is Window host) factory.WindowContext.ApplyBounds(host, saved.Bounds);
        }
        foreach (var pair in _originalOwners) pair.Key.OriginalOwner = pair.Value;
        foreach (var pair in _active) pair.Key.ActiveDockable = pair.Value;
        factory.UpdateFloatingPolicies();
    }

    private sealed record RootState(IRootDock Root, IDockable[] Hidden, IDockable[] Left,
        IDockable[] Right, IDockable[] Top, IDockable[] Bottom);
    private sealed record WindowState(IDockWindow Window, IRootDock Layout, IDockable Owner,
        IHostWindow? Host, DockWindowBounds Bounds);
}
