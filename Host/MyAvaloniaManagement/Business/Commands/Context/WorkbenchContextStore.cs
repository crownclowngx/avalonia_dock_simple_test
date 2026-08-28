using System;
using System.Threading;
using MyAvaloniaManagement.Business.Docking;
using MyAvaloniaManagement.Business.Workspace;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.Business.Commands.Context;

/// <summary>携带一次原子读取到的 Context 快照和 Host internal 活动路由对象。</summary>
/// <remarks>
/// 该捕获对象不会进入 SDK、Catalog 或持久化格式。把 Adapter/Target 与公开事实快照分开，
/// 可以让 Executor 安全路由当前实例，同时保证 Context v1 本身没有对象图泄漏。
/// </remarks>
internal sealed record WorkbenchContextCapture(
    WorkbenchContextSnapshot Snapshot,
    ManagedDocumentDockable? Document,
    IWorkbenchDocumentCommandTarget? Target,
    CancellationToken ClosingToken);

/// <summary>通知 Host internal 消费者活动 Document Context 已经切换。</summary>
internal sealed class WorkbenchContextChangedEventArgs(
    WorkbenchContextSnapshot snapshot) : EventArgs
{
    /// <summary>获取切换完成后的不可变快照。</summary>
    internal WorkbenchContextSnapshot Snapshot { get; } =
        snapshot ?? throw new ArgumentNullException(nameof(snapshot));
}

/// <summary>订阅 Workspace 的唯一活动 Document 事实源并维护 Context v1。</summary>
/// <remarks>
/// Store 不遍历 RootDock，也不监听 LayoutChanged。锁只保护引用与快照替换，插件事件、
/// Dock 操作和下游通知始终在锁外执行，避免用户代码重入 Host 状态锁。
/// </remarks>
internal sealed class WorkbenchContextStore : IDisposable
{
    private readonly object _gate = new();
    private readonly WorkspaceSession? _workspace;
    private WorkbenchContextSnapshot _snapshot = WorkbenchContextSnapshot.Empty();
    private ManagedDocumentDockable? _document;
    private bool _disposed;

    /// <summary>创建生产 Context Store，并立即捕获 Session 当前已经提交的活动 Document。</summary>
    internal WorkbenchContextStore(WorkspaceSession workspace)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        var initial = workspace.GetActiveDocument();
        if (initial is not null)
        {
            _document = initial;
            _snapshot = CreateSnapshot(initial, revision: 1);
        }
        workspace.ActiveDocumentChanged += OnActiveDocumentChanged;
    }

    /// <summary>创建空的无订阅 Store，供无 Workspace 的纯 Catalog/Executor 单元测试使用。</summary>
    internal WorkbenchContextStore()
    {
    }

    /// <summary>当活动 Document Context 完成原子切换后发生。</summary>
    internal event EventHandler<WorkbenchContextChangedEventArgs>? ContextChanged;

    /// <summary>原子捕获同一代次的快照、Adapter、Target 和关闭令牌。</summary>
    internal WorkbenchContextCapture Capture()
    {
        lock (_gate)
        {
            var document = _document;
            return new WorkbenchContextCapture(
                _snapshot,
                document,
                document?.Model as IWorkbenchDocumentCommandTarget,
                document?.ClosingToken ?? CancellationToken.None);
        }
    }

    private void OnActiveDocumentChanged(
        object? sender,
        ActiveDocumentChangedEventArgs args)
    {
        WorkbenchContextSnapshot next;
        lock (_gate)
        {
            if (_disposed || ReferenceEquals(_document, args.Document))
            {
                return;
            }

            var revision = checked(_snapshot.Revision + 1);
            _document = args.Document;
            _snapshot = args.Document is null
                ? WorkbenchContextSnapshot.Empty(revision)
                : CreateSnapshot(args.Document, revision);
            next = _snapshot;
        }

        ContextChanged?.Invoke(this, new WorkbenchContextChangedEventArgs(next));
    }

    /// <summary>只在捕获边界把 Adapter 投影为 Context v1 的稳定值。</summary>
    private static WorkbenchContextSnapshot CreateSnapshot(
        ManagedDocumentDockable document,
        long revision) => WorkbenchContextSnapshot.ActiveDocument(
            document.Registration.Descriptor.DocumentTypeId,
            document.PluginRegistration?.OwnerId,
            document.Registration.IsPersistable,
            revision);

    /// <summary>解除唯一 Workspace 订阅；重复释放安全返回。</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _document = null;
            _snapshot = WorkbenchContextSnapshot.Empty(_snapshot.Revision);
        }

        if (_workspace is not null)
        {
            _workspace.ActiveDocumentChanged -= OnActiveDocumentChanged;
        }
    }
}
