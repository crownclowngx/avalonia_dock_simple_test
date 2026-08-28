using System;
using System.Threading;
using System.Threading.Tasks;
using MyAvaloniaManagement.Business.Commands.Catalog;
using MyAvaloniaManagement.Business.Commands.State;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.Business.Commands.Execution;

/// <summary>定义 HostRuntime 关闭工作台命令所需的最小端口。</summary>
internal interface IWorkbenchCommandShutdownParticipant
{
    TimeSpan ShutdownGrace { get; }

    void BeginShutdown();

    Task<bool> WaitForDrainAsync(TimeSpan timeout);
}

/// <summary>统一查询并执行 Host 或插件工作台命令的无 UI 执行器。</summary>
/// <remarks>
/// Executor 不提供并发预算、重试、队列、业务超时、授权或独立 Scope，因此不会复制 Workflow Action Runtime。
/// 插件路由只使用状态查询返回的当前 Adapter/Target 捕获；锁内只维护接受状态和全局在途计数，绝不执行插件代码。
/// </remarks>
internal sealed class WorkbenchCommandExecutor :
    IWorkbenchCommandShutdownParticipant,
    IDisposable
{
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(10);
    private readonly object _gate = new();
    private readonly WorkbenchCommandStateQuery _states;
    private readonly WorkbenchDocumentCommandLeaseStore _documentLeases;
    private readonly IHostDiagnosticSink? _diagnostics;
    private readonly CancellationTokenSource _shutdown = new();
    private TaskCompletionSource _drained = CompletedDrainSource();
    private int _activeInvocations;
    private bool _accepting = true;
    private bool _disposed;

    internal WorkbenchCommandExecutor(
        WorkbenchCommandStateQuery states,
        WorkbenchDocumentCommandLeaseStore documentLeases,
        IHostDiagnosticSink? diagnostics = null)
    {
        _states = states ?? throw new ArgumentNullException(nameof(states));
        _documentLeases = documentLeases ?? throw new ArgumentNullException(nameof(documentLeases));
        _diagnostics = diagnostics;
    }

    public TimeSpan ShutdownGrace => Grace;

    /// <summary>执行指定稳定身份，并把预期拒绝、取消和失败映射为结果而不是未观察异常。</summary>
    internal async ValueTask<WorkbenchCommandExecutionResult> ExecuteAsync(
        CommandId commandId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commandId);
        if (!TryBeginInvocation())
        {
            return WorkbenchCommandExecutionResult.FromStatus(
                WorkbenchCommandExecutionStatus.RejectedDuringShutdown);
        }

        try
        {
            var route = _states.Resolve(commandId);
            if (route.StructuralStatus != WorkbenchCommandStateStatus.Enabled || route.Entry is null)
            {
                return FromState(route.StructuralStatus);
            }

            WorkbenchDocumentCommandLease? documentLease = null;
            if (route.Entry is PluginWorkbenchCommandCatalogEntry)
            {
                if (route.Capture.Document is null ||
                    !_documentLeases.TryAcquire(route.Capture.Document, out documentLease))
                {
                    return WorkbenchCommandExecutionResult.FromStatus(
                        WorkbenchCommandExecutionStatus.TargetUnavailable);
                }
            }

            try
            {
                // Lease 取得后再次验证 revision/实例和 owner，阻止关闭或标签切换竞态把调用送到旧 Target。
                if (!_states.IsCurrent(route))
                {
                    return WorkbenchCommandExecutionResult.FromStatus(
                        WorkbenchCommandExecutionStatus.TargetUnavailable);
                }

                var state = _states.Evaluate(route);
                if (state.Status != WorkbenchCommandStateStatus.Enabled)
                {
                    return FromState(state.Status);
                }
                if (!_states.IsCurrent(route))
                {
                    return WorkbenchCommandExecutionResult.FromStatus(
                        WorkbenchCommandExecutionStatus.TargetUnavailable);
                }

                using var linked = documentLease is null
                    ? CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken,
                        _shutdown.Token)
                    : CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken,
                        documentLease.ClosingToken,
                        route.Capture.ClosingToken,
                        _shutdown.Token);
                try
                {
                    if (route.Entry is HostWorkbenchCommandCatalogEntry host)
                    {
                        await host.Handler.ExecuteAsync(linked.Token);
                    }
                    else
                    {
                        await route.Capture.Target!.ExecuteAsync(commandId, linked.Token);
                    }
                    return WorkbenchCommandExecutionResult.FromStatus(
                        WorkbenchCommandExecutionStatus.Succeeded);
                }
                catch (OperationCanceledException) when (linked.IsCancellationRequested)
                {
                    return WorkbenchCommandExecutionResult.FromStatus(
                        WorkbenchCommandExecutionStatus.Canceled);
                }
                catch (Exception exception)
                {
                    // 诊断只接收稳定身份和异常类型；白名单策略不会保存异常正文、路径或 Payload。
                    _diagnostics?.Report(new HostDiagnosticDraft(
                        HostDiagnosticCodes.WorkbenchCommandExecutionFailed,
                        HostDiagnosticPhase.WorkbenchCommand)
                    {
                        PluginId = (route.Entry as PluginWorkbenchCommandCatalogEntry)?.OwnerId,
                        StableId = commandId.Value,
                        Exception = exception,
                    });
                    return route.Entry is PluginWorkbenchCommandCatalogEntry
                        ? WorkbenchCommandExecutionResult.PluginFailure
                        : WorkbenchCommandExecutionResult.Failure;
                }
                finally
                {
                    _states.NotifyExecuted(route);
                }
            }
            finally
            {
                // 定向刷新先于 Lease 释放，防止关闭 continuation 先释放 Target 再被状态层访问。
                documentLease?.Dispose();
            }
        }
        finally
        {
            EndInvocation();
        }
    }

    /// <summary>幂等关闭新入口，并在锁外向在途 Handler 传播 Host 取消。</summary>
    public void BeginShutdown()
    {
        var shouldCancel = false;
        lock (_gate)
        {
            if (_accepting)
            {
                _accepting = false;
                shouldCancel = true;
            }
        }

        if (shouldCancel)
        {
            _shutdown.Cancel(throwOnFirstException: false);
        }
    }

    /// <summary>在给定宽限内等待所有已经登记的调用真实退出。</summary>
    public async Task<bool> WaitForDrainAsync(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "排空宽限必须大于零。");
        }

        Task wait;
        lock (_gate)
        {
            wait = _activeInvocations == 0 ? Task.CompletedTask : _drained.Task;
        }

        try
        {
            await wait.WaitAsync(timeout).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shutdown.Dispose();
    }

    private bool TryBeginInvocation()
    {
        lock (_gate)
        {
            if (!_accepting)
            {
                return false;
            }

            if (_activeInvocations == 0)
            {
                _drained = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
            }
            _activeInvocations++;
            return true;
        }
    }

    private void EndInvocation()
    {
        TaskCompletionSource? drained = null;
        lock (_gate)
        {
            _activeInvocations--;
            if (_activeInvocations == 0)
            {
                drained = _drained;
            }
        }
        drained?.TrySetResult();
    }

    private static TaskCompletionSource CompletedDrainSource()
    {
        var source = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }

    private static WorkbenchCommandExecutionResult FromState(
        WorkbenchCommandStateStatus status) => status switch
    {
        WorkbenchCommandStateStatus.CommandNotFound =>
            WorkbenchCommandExecutionResult.FromStatus(
                WorkbenchCommandExecutionStatus.CommandNotFound),
        WorkbenchCommandStateStatus.OwnerUnavailable =>
            WorkbenchCommandExecutionResult.FromStatus(
                WorkbenchCommandExecutionStatus.OwnerUnavailable),
        WorkbenchCommandStateStatus.TargetUnavailable =>
            WorkbenchCommandExecutionResult.FromStatus(
                WorkbenchCommandExecutionStatus.TargetUnavailable),
        WorkbenchCommandStateStatus.Disabled =>
            WorkbenchCommandExecutionResult.FromStatus(
                WorkbenchCommandExecutionStatus.CommandDisabled),
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "命令状态不能直接映射为执行结果。"),
    };
}
