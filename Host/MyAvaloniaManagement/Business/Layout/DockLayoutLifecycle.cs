using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using Dock.Model.Controls;
using MyAvaloniaManagement.Business.Workspace;

namespace MyAvaloniaManagement.Business.Layout;

/// <summary>
/// 协调 V3 恢复、UI 捕获和后台串行保存。Workspace 拥有模型，Store 拥有文件锁，
/// 本类只拥有订阅和调度；退出快照冻结后不接受拆窗通知，取消退出则重新开启捕获。
/// </summary>
internal sealed class DockLayoutLifecycle : IDisposable
{
    private readonly DockLayoutV3Store _store;
    private readonly DockLayoutSaveQueue _queue;
    private readonly HashSet<INotifyPropertyChanged> _observed = [];
    private WorkspaceSession? _session;
    private DockLayoutSnapshotV3? _pending;
    private bool _ready;
    private bool _frozen;
    private bool _posted;
    private bool _disposed;
    private string _message = string.Empty;
    internal string Message => _message;
    internal event EventHandler? StatusChanged;

    public DockLayoutLifecycle(DockLayoutV3Store store)
    {
        _store = store;
        _queue = new DockLayoutSaveQueue(store.Save, exception =>
        {
            if (exception is not null) store.Report("LAYOUT_SAVE_FAILED", exception);
            SetMessage(exception is null ? string.Empty : "布局保存未完成，可重试；当前窗口仍可使用。");
        });
    }

    internal IRootDock Prepare(WorkspaceSession session)
    {
        if (_session is not null) return session.RootDock!;
        _session = session;
        _pending = _store.Load();
        if (!_store.CanWrite) SetMessage("布局以只读方式打开，本次调整不会保存。");
        var root = session.RootDock ?? session.DockFactory.CreateLayout();
        session.DockFactory.InitLayout(root);
        session.HideAllTools();
        session.LayoutChanged += OnChanged;
        session.DockFactory.WindowContext.WindowPlacementChanged += OnChanged;
        return root;
    }

    internal IRootDock ApplyPending(WorkspaceSession session)
    {
        if (_ready) return session.RootDock!;
        try
        {
            if (_pending is { } snapshot) session.LayoutState.Apply(session, snapshot);
        }
        catch (Exception exception)
        {
            _store.Report("LAYOUT_APPLY_FAILED", exception);
            // Apply 的转移检查点已经恢复原运行树及原实例；不能再关闭一次刚拒绝关闭的原生窗口。
            SetMessage("布局恢复未完成，已使用默认布局；原布局文件仍保留。");
        }
        finally { _pending = null; _ready = true; ObserveTree(); }
        return session.RootDock!;
    }

    internal bool Save(WorkspaceSession session)
    {
        if (_frozen || _disposed || !_store.CanWrite) return false;
        try { _queue.Submit(session.LayoutState.Capture(session)); return true; }
        catch (Exception exception)
        {
            _store.Report("LAYOUT_CAPTURE_FAILED", exception);
            SetMessage("布局保存未完成，可重试；当前窗口仍可使用。");
            return false;
        }
    }

    internal Task<bool> FreezeAndFlushAsync(WorkspaceSession session)
    {
        var captured = Save(session);
        _frozen = true;
        // 仅最终原生关闭使用同步排空；写入队列不捕获 UI 上下文，不会相互等待。
        // 干净且无在途命令的窗口继续保留原生一次关闭语义，日常自动保存仍在后台进行。
        _queue.FlushAsync().GetAwaiter().GetResult();
        return Task.FromResult(!_store.CanWrite || captured && string.IsNullOrEmpty(_message));
    }

    internal void Resume() => _frozen = false;
    internal Task FlushAsync() => _queue.FlushAsync();

    internal IRootDock Reset(WorkspaceSession session)
    {
        if (session.DockFactory.WindowContext.HasFullscreenContent)
            throw new InvalidOperationException("请先退出内容全屏再重置布局。");
        _ready = false;
        try { return session.LayoutState.Apply(session, CreateDefault(session)); }
        finally { _ready = true; ObserveTree(); Save(session); }
    }

    private static DockLayoutSnapshotV3 CreateDefault(WorkspaceSession session)
    {
        var tools = session.CreatedTools.Keys.Select(id => new DockLayoutTool(id, "hidden",
            ToolDockPlacement.GetDockId(session.GetToolAlignment(id)), 0)).ToArray();
        var groups = tools.GroupBy(tool => tool.ReturnDockId).Select(group =>
            DockLayoutNode.Group("default-" + group.Key, group.Select(tool => tool.Id).ToArray())).ToArray();
        var root = DockLayoutNode.Documents();
        if (groups.Length != 0)
        {
            var children = groups.Append(root).Select(node => node with { Proportion = 1d / (groups.Length + 1) }).ToArray();
            root = DockLayoutNode.Split("default-split", "horizontal", children);
        }
        return new(3, new("main", session.DockFactory.WindowContext.CaptureBounds(session.DockFactory.WindowContext.MainWindow), root), [], tools);
    }

    private void OnChanged(object? sender, EventArgs args)
    {
        if (!_ready || _frozen || _disposed || _posted) return;
        _posted = true;
        // 框架事件可能处在一次拖放中间，先完成当前 UI 操作再捕获最终树。
        Dispatcher.UIThread.Post(() =>
        {
            _posted = false;
            if (!_ready || _frozen || _disposed || _session is null) return;
            ObserveTree();
            Save(_session);
        }, DispatcherPriority.Background);
    }

    private void OnNodeChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is "Proportion" or "ActiveDockable" or "VisibleDockables") OnChanged(sender, EventArgs.Empty);
    }

    private void ObserveTree()
    {
        foreach (var item in _observed) item.PropertyChanged -= OnNodeChanged;
        _observed.Clear();
        if (_session?.RootDock is not { } root) return;
        foreach (var item in DockTreeNavigator.EnumerateWorkspace(root).OfType<INotifyPropertyChanged>())
            if (_observed.Add(item)) item.PropertyChanged += OnNodeChanged;
    }

    private void SetMessage(string message)
    {
        _message = message;
        // 文件线程只发布纯状态，绑定通知回 UI；迟到通知必须尊重释放。
        Dispatcher.UIThread.Post(() => { if (!_disposed) StatusChanged?.Invoke(this, EventArgs.Empty); });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_session is { } session)
        {
            session.LayoutChanged -= OnChanged;
            session.DockFactory.WindowContext.WindowPlacementChanged -= OnChanged;
        }
        foreach (var item in _observed) item.PropertyChanged -= OnNodeChanged;
        _observed.Clear();
        _queue.Dispose();
    }
}
