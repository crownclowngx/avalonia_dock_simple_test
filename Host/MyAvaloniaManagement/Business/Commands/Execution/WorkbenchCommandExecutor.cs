using System;
using System.Threading;
using System.Threading.Tasks;
using MyAvaloniaManagement.Business.Commands.Catalog;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Lifecycle;
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
/// G2 只执行 Host Handler；插件 Target 路由在 G3 加入。Executor 不提供并发预算、重试、队列、
/// 超时、授权或独立 Scope，因此不会复制 Workflow Action Runtime。锁内只维护接受状态和在途计数，
/// 绝不执行 Handler、诊断写入或取消回调。
/// </remarks>
internal sealed class WorkbenchCommandExecutor :
    IWorkbenchCommandShutdownParticipant,
    IDisposable
{
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(10);
    private readonly object _gate = new();
    private readonly WorkbenchCommandCatalog _catalog;
    private readonly PluginAvailabilityReadModel _availability;
    private readonly IHostDiagnosticSink? _diagnostics;
    private readonly CancellationTokenSource _shutdown = new();
    private TaskCompletionSource _drained = CompletedDrainSource();
    private int _activeInvocations;
    private bool _accepting = true;
    private bool _disposed;

    internal WorkbenchCommandExecutor(
        WorkbenchCommandCatalog catalog,
        PluginAvailabilityReadModel availability,
        IHostDiagnosticSink? diagnostics = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _availability = availability ?? throw new ArgumentNullException(nameof(availability));
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
            if (!_catalog.TryGet(commandId, out var entry))
            {
                return WorkbenchCommandExecutionResult.FromStatus(
                    WorkbenchCommandExecutionStatus.CommandNotFound);
            }

            if (entry is PluginWorkbenchCommandCatalogEntry plugin)
            {
                if (!_availability.IsAvailable(plugin.OwnerId))
                {
                    return WorkbenchCommandExecutionResult.FromStatus(
                        WorkbenchCommandExecutionStatus.OwnerUnavailable);
                }

                // G2 刻意不读取 WorkspaceSession 或 Document 模型。已知且 owner 可用的插件命令
                // 只有在 G3 建立活动实例 Target 后才能执行。
                return WorkbenchCommandExecutionResult.FromStatus(
                    WorkbenchCommandExecutionStatus.TargetUnavailable);
            }

            var host = (HostWorkbenchCommandCatalogEntry)entry;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _shutdown.Token);
            try
            {
                await host.Handler.ExecuteAsync(linked.Token);
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
                // 诊断只接收稳定 ID 和异常类型；白名单策略不会保存异常正文、路径或 Payload。
                _diagnostics?.Report(new HostDiagnosticDraft(
                    HostDiagnosticCodes.WorkbenchCommandExecutionFailed,
                    HostDiagnosticPhase.WorkbenchCommand)
                {
                    StableId = commandId.Value,
                    Exception = exception,
                });
                return WorkbenchCommandExecutionResult.Failure;
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
}
