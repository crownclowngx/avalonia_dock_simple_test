using System;
using MyAvaloniaManagement.Business.Commands.Catalog;
using MyAvaloniaManagement.Business.Commands.Context;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.Business.Commands.State;

/// <summary>描述一条命令在当前 Context 中的稳定可用性分类。</summary>
internal enum WorkbenchCommandStateStatus
{
    CommandNotFound,
    OwnerUnavailable,
    TargetUnavailable,
    Disabled,
    Enabled,
}

/// <summary>表示一次状态查询的不可变结果。</summary>
internal readonly record struct WorkbenchCommandState(
    WorkbenchCommandStateStatus Status,
    long ContextRevision);

/// <summary>携带一次 Host internal 状态失效通知。</summary>
internal sealed class WorkbenchCommandStateInvalidatedEventArgs(
    CommandId? commandId,
    long contextRevision,
    bool isFullRefresh) : EventArgs
{
    internal CommandId? CommandId { get; } = commandId;
    internal long ContextRevision { get; } = contextRevision;
    internal bool IsFullRefresh { get; } = isFullRefresh;
}

/// <summary>保存 Executor 单次判断使用的 Catalog 与 Context 原子捕获。</summary>
internal sealed record WorkbenchCommandRoute(
    WorkbenchCommandCatalogEntry? Entry,
    WorkbenchContextCapture Capture,
    WorkbenchCommandStateStatus StructuralStatus);

/// <summary>统一解析 Host/插件命令状态，并只监听当前活动 Document Target。</summary>
/// <remarks>
/// Query 不缓存 <c>CanExecute</c>。展示查询和执行入口每次都会重新调用真实 Handler/Target；事件只负责
/// 通知消费者“应重新查询”。Target 可以从工作线程发出事件，本层仅做身份与 revision 过滤，不接触 UI Dispatcher。
/// </remarks>
internal sealed class WorkbenchCommandStateQuery : IDisposable
{
    private readonly object _gate = new();
    private readonly WorkbenchCommandCatalog _catalog;
    private readonly PluginAvailabilityReadModel _availability;
    private readonly WorkbenchContextStore _context;
    private readonly IHostDiagnosticSink? _diagnostics;
    private IWorkbenchDocumentCommandTarget? _subscribedTarget;
    private long _subscribedRevision;
    private bool _subscriptionHealthy = true;
    private bool _disposed;

    internal WorkbenchCommandStateQuery(
        WorkbenchCommandCatalog catalog,
        PluginAvailabilityReadModel availability,
        WorkbenchContextStore context,
        IHostDiagnosticSink? diagnostics = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _availability = availability ?? throw new ArgumentNullException(nameof(availability));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _diagnostics = diagnostics;
        _context.ContextChanged += OnContextChanged;
        SwitchTarget(_context.Capture());
    }

    /// <summary>当 Context 全量变化或当前 Target 定向状态变化时发生。</summary>
    internal event EventHandler<WorkbenchCommandStateInvalidatedEventArgs>? StateInvalidated;

    /// <summary>查询一条命令此刻的状态，不保存插件返回值。</summary>
    internal WorkbenchCommandState Query(CommandId commandId)
    {
        var route = Resolve(commandId);
        return Evaluate(route);
    }

    /// <summary>解析不可执行原因和当前实例，但暂不调用插件 <c>CanExecute</c>。</summary>
    internal WorkbenchCommandRoute Resolve(CommandId commandId)
    {
        ArgumentNullException.ThrowIfNull(commandId);
        var capture = _context.Capture();
        if (!_catalog.TryGet(commandId, out var entry))
        {
            return new WorkbenchCommandRoute(null, capture, WorkbenchCommandStateStatus.CommandNotFound);
        }
        if (entry is HostWorkbenchCommandCatalogEntry)
        {
            return new WorkbenchCommandRoute(entry, capture, WorkbenchCommandStateStatus.Enabled);
        }

        var plugin = (PluginWorkbenchCommandCatalogEntry)entry;
        if (!_availability.IsAvailable(plugin.OwnerId))
        {
            return new WorkbenchCommandRoute(entry, capture, WorkbenchCommandStateStatus.OwnerUnavailable);
        }

        var snapshot = capture.Snapshot;
        if (!snapshot.HasActiveDocument ||
            snapshot.ActiveDocumentOwnerId != plugin.OwnerId ||
            snapshot.ActiveDocumentTypeId != plugin.TargetDocumentTypeId ||
            capture.Document is null ||
            capture.Target is null)
        {
            return new WorkbenchCommandRoute(entry, capture, WorkbenchCommandStateStatus.TargetUnavailable);
        }

        return new WorkbenchCommandRoute(entry, capture, WorkbenchCommandStateStatus.Enabled);
    }

    /// <summary>对已经解析的当前路由执行最后一次无缓存状态检查。</summary>
    internal WorkbenchCommandState Evaluate(WorkbenchCommandRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (route.StructuralStatus != WorkbenchCommandStateStatus.Enabled || route.Entry is null)
        {
            return new WorkbenchCommandState(
                route.StructuralStatus,
                route.Capture.Snapshot.Revision);
        }

        try
        {
            var enabled = route.Entry switch
            {
                HostWorkbenchCommandCatalogEntry host =>
                    host.Handler.CanExecute(route.Capture.Snapshot),
                PluginWorkbenchCommandCatalogEntry =>
                    IsSubscriptionHealthy(route) &&
                    route.Capture.Target!.CanExecute(route.Entry.Descriptor.CommandId),
                _ => false,
            };
            return new WorkbenchCommandState(
                enabled ? WorkbenchCommandStateStatus.Enabled : WorkbenchCommandStateStatus.Disabled,
                route.Capture.Snapshot.Revision);
        }
        catch (Exception exception)
        {
            ReportStateFailure(route, exception);
            return new WorkbenchCommandState(
                WorkbenchCommandStateStatus.Disabled,
                route.Capture.Snapshot.Revision);
        }
    }

    /// <summary>确认解析完成后 Context、实例和 owner 可用性仍未变化。</summary>
    internal bool IsCurrent(WorkbenchCommandRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        var current = _context.Capture();
        if (current.Snapshot.Revision != route.Capture.Snapshot.Revision ||
            !ReferenceEquals(current.Document, route.Capture.Document) ||
            !ReferenceEquals(current.Target, route.Capture.Target))
        {
            return false;
        }
        return route.Entry is not PluginWorkbenchCommandCatalogEntry plugin ||
               _availability.IsAvailable(plugin.OwnerId);
    }

    /// <summary>命令完成后，在捕获实例仍为当前目标时发布一次定向刷新。</summary>
    internal void NotifyExecuted(WorkbenchCommandRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (route.Entry is not null && IsCurrent(route))
        {
            PublishInvalidation(
                route.Entry.Descriptor.CommandId,
                route.Capture.Snapshot.Revision,
                isFullRefresh: false);
        }
    }

    private void OnContextChanged(object? sender, WorkbenchContextChangedEventArgs args)
    {
        var capture = _context.Capture();
        SwitchTarget(capture);
        PublishInvalidation(null, args.Snapshot.Revision, isFullRefresh: true);
    }

    private void SwitchTarget(WorkbenchContextCapture capture)
    {
        IWorkbenchDocumentCommandTarget? previous;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            previous = _subscribedTarget;
            _subscribedTarget = capture.Target;
            _subscribedRevision = capture.Snapshot.Revision;
            _subscriptionHealthy = true;
        }

        if (previous is not null)
        {
            try
            {
                previous.CommandStateChanged -= OnTargetStateChanged;
            }
            catch (Exception exception)
            {
                ReportSubscriptionFailure(capture, exception);
            }
        }

        if (capture.Target is null)
        {
            return;
        }
        try
        {
            capture.Target.CommandStateChanged += OnTargetStateChanged;
        }
        catch (Exception exception)
        {
            lock (_gate)
            {
                if (ReferenceEquals(_subscribedTarget, capture.Target) &&
                    _subscribedRevision == capture.Snapshot.Revision)
                {
                    _subscribedTarget = null;
                    _subscriptionHealthy = false;
                }
            }
            ReportSubscriptionFailure(capture, exception);
        }
    }

    private void OnTargetStateChanged(
        object? sender,
        WorkbenchCommandStateChangedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        WorkbenchContextCapture capture;
        lock (_gate)
        {
            if (_disposed ||
                !ReferenceEquals(sender, _subscribedTarget) ||
                !_subscriptionHealthy)
            {
                return;
            }
            capture = _context.Capture();
            if (capture.Snapshot.Revision != _subscribedRevision ||
                !ReferenceEquals(capture.Target, sender))
            {
                return;
            }
        }

        if (!_catalog.TryGet(args.CommandId, out var entry) ||
            entry is not PluginWorkbenchCommandCatalogEntry plugin ||
            capture.Snapshot.ActiveDocumentOwnerId != plugin.OwnerId ||
            capture.Snapshot.ActiveDocumentTypeId != plugin.TargetDocumentTypeId)
        {
            return;
        }

        PublishInvalidation(args.CommandId, capture.Snapshot.Revision, isFullRefresh: false);
    }

    private bool IsSubscriptionHealthy(WorkbenchCommandRoute route)
    {
        lock (_gate)
        {
            return !_disposed &&
                   _subscriptionHealthy &&
                   _subscribedRevision == route.Capture.Snapshot.Revision &&
                   ReferenceEquals(_subscribedTarget, route.Capture.Target);
        }
    }

    private void PublishInvalidation(CommandId? commandId, long revision, bool isFullRefresh)
    {
        var handlers = StateInvalidated;
        if (handlers is null)
        {
            return;
        }
        var args = new WorkbenchCommandStateInvalidatedEventArgs(
            commandId,
            revision,
            isFullRefresh);
        foreach (EventHandler<WorkbenchCommandStateInvalidatedEventArgs> handler in
                 handlers.GetInvocationList())
        {
            try
            {
                handler(this, args);
            }
            catch (Exception exception)
            {
                _diagnostics?.Report(new HostDiagnosticDraft(
                    HostDiagnosticCodes.WorkbenchCommandStateObserverFailed,
                    HostDiagnosticPhase.WorkbenchCommand)
                {
                    StableId = commandId?.Value,
                    Exception = exception,
                });
            }
        }
    }

    private void ReportStateFailure(WorkbenchCommandRoute route, Exception exception) =>
        _diagnostics?.Report(new HostDiagnosticDraft(
            HostDiagnosticCodes.WorkbenchCommandTargetStateFailed,
            HostDiagnosticPhase.WorkbenchCommand)
        {
            PluginId = (route.Entry as PluginWorkbenchCommandCatalogEntry)?.OwnerId,
            StableId = route.Entry?.Descriptor.CommandId.Value,
            Exception = exception,
        });

    private void ReportSubscriptionFailure(
        WorkbenchContextCapture capture,
        Exception exception) =>
        _diagnostics?.Report(new HostDiagnosticDraft(
            HostDiagnosticCodes.WorkbenchCommandTargetSubscriptionFailed,
            HostDiagnosticPhase.WorkbenchCommand)
        {
            PluginId = capture.Snapshot.ActiveDocumentOwnerId,
            StableId = capture.Snapshot.ActiveDocumentTypeId?.Value,
            Exception = exception,
        });

    public void Dispose()
    {
        IWorkbenchDocumentCommandTarget? target;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            target = _subscribedTarget;
            _subscribedTarget = null;
            _subscriptionHealthy = false;
        }
        _context.ContextChanged -= OnContextChanged;
        if (target is not null)
        {
            try
            {
                target.CommandStateChanged -= OnTargetStateChanged;
            }
            catch (Exception exception)
            {
                ReportSubscriptionFailure(_context.Capture(), exception);
            }
        }
    }
}
