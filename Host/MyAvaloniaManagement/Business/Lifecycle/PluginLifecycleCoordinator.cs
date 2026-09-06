using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Plugins.Registration;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.Business.Lifecycle;

/// <summary>协调器解析生命周期 singleton 所需的唯一 Provider 端口。</summary>
internal interface IPluginLifecycleResolver
{
    PluginLifecycleCallbacks GetRequiredLifecycle(PluginId pluginId, Type implementationType);
}

/// <summary>
/// Host 执行生命周期所需的最小回调句柄。解析端口只接受最终 V3 SDK 生命周期，
/// 不承担旧版本分派；协调器本身也不依赖任何 public 编排模型。
/// </summary>
internal sealed record PluginLifecycleCallbacks(
    Func<CancellationToken, Task> InitializeAsync,
    Func<CancellationToken, Task> ShutdownAsync);

/// <summary>Host 独占的生命周期期限；插件不能读取或覆盖这些政策。</summary>
internal sealed record PluginLifecycleTimeouts(
    TimeSpan Initialization,
    TimeSpan Shutdown)
{
    internal TimeSpan CancellationGrace { get; init; } = TimeSpan.FromSeconds(2);

    internal static PluginLifecycleTimeouts Default { get; } = new(
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(10));
}

/// <summary>
/// 按 PluginId 确定性启动插件，并反向停止成功启动项。声明发现、Provider 所有权和状态展示均委托给专门组件。
/// </summary>
internal sealed class PluginLifecycleCoordinator
{
    private readonly PluginRegistry _registry;
    private readonly IPluginLifecycleResolver _lifecycleResolver;
    private readonly PluginLifecycleStateStore _states;
    private readonly PluginLifecycleDiagnosticReporter _diagnostics;
    private readonly PluginLifecycleTimeouts _timeouts;
    private readonly PluginLifecycleOperationRunner _runner;
    private readonly List<LifecycleAttempt> _attempts = [];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initializationCompleted;
    private PluginLifecycleShutdownResult? _shutdownResult;

    internal PluginLifecycleCoordinator(
        PluginRegistry registry,
        IPluginLifecycleResolver lifecycleResolver,
        PluginLifecycleStateStore states,
        IHostDiagnosticSink? diagnostics = null)
        : this(registry, lifecycleResolver, states, diagnostics, PluginLifecycleTimeouts.Default)
    {
    }

    internal PluginLifecycleCoordinator(
        PluginRegistry registry,
        IPluginLifecycleResolver lifecycleResolver,
        PluginLifecycleStateStore states,
        IHostDiagnosticSink? diagnostics,
        PluginLifecycleTimeouts timeouts, TimeProvider? time = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _lifecycleResolver = lifecycleResolver ?? throw new ArgumentNullException(nameof(lifecycleResolver));
        _states = states ?? throw new ArgumentNullException(nameof(states));
        _diagnostics = new PluginLifecycleDiagnosticReporter(diagnostics);
        _timeouts = timeouts ?? throw new ArgumentNullException(nameof(timeouts));
        _runner = new PluginLifecycleOperationRunner(_timeouts.CancellationGrace, time);
        if (_timeouts.Initialization <= TimeSpan.Zero || _timeouts.Shutdown <= TimeSpan.Zero || _timeouts.CancellationGrace <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeouts), "插件生命周期期限必须大于零。");
        }
    }

    internal async Task InitializeAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initializationCompleted || _shutdownResult is not null)
            {
                return;
            }

            foreach (var declaration in _registry.Lifecycles
                         .OrderBy(item => item.OwnerId.Value, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_states.GetState(declaration.OwnerId)?.Status !=
                    PluginLifecycleStatus.NotStarted)
                {
                    continue;
                }

                _states.SetState(new PluginLifecycleState(
                    declaration.OwnerId,
                    PluginLifecycleStatus.Initializing));
                var lifecycle = _lifecycleResolver.GetRequiredLifecycle(
                    declaration.OwnerId,
                    declaration.ImplementationType);
                var attempt = new LifecycleAttempt(declaration.OwnerId, lifecycle);
                _attempts.Add(attempt);
                PluginLifecycleOperationResult result;
                try
                {
                    result = await _runner.RunAsync(
                            lifecycle.InitializeAsync,
                            _timeouts.Initialization,
                            cancellationToken,
                            exception => ReportCancellationFailure(
                                declaration.OwnerId,
                                PluginLifecycleStage.Initialization,
                                exception),
                        operation => attempt.Initialization = operation)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    var cancelled = new PluginLifecycleState(
                        declaration.OwnerId,
                        PluginLifecycleStatus.HostCancelled)
                    {
                        ErrorCode = HostDiagnosticCodes.LifecycleHostCancelled,
                    };
                    _states.SetState(cancelled);
                    Report(cancelled, null);
                    throw;
                }

                CommitInitializationResult(declaration.OwnerId, result);
            }

            _initializationCompleted = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 关闭判定只执行一次。列表顺序来自初始化尝试，不来自异步完成时间；迟到项不得插回已越过的位置。
    /// 异常、未排空及遗漏关闭责任均转为明确保留原因，不能让 Runtime 将“方法返回”误当作“可以释放”。
    /// </summary>
    internal async Task<PluginLifecycleShutdownResult> ShutdownAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_shutdownResult is not null) return _shutdownResult;
            _states.BeginShutdown();
            var retained = new List<PluginLifecycleRetention>();
            for (var index = _attempts.Count - 1; index >= 0; index--)
            {
                var item = _attempts[index];
                var stage = PluginLifecycleStage.Initialization;
                try
                {
                    var initialization = item.Initialization;
                    if (initialization is null)
                    {
                        retained.Add(new(item.PluginId, stage, PluginLifecycleRetentionReason.CheckFailed));
                        continue;
                    }
                    if (!await initialization.WaitForCompletionAsync().ConfigureAwait(false))
                    {
                        retained.Add(new(item.PluginId, stage, PluginLifecycleRetentionReason.OperationRunning));
                        continue;
                    }
                    // 失败初始化仍由插件承担局部清理；不引入对半初始化实例强制 Shutdown 的新契约。
                    if (!initialization.Execution.IsCompletedSuccessfully) continue;
                    stage = PluginLifecycleStage.Shutdown;
                    if (cancellationToken.IsCancellationRequested)
                    {
                        retained.Add(new(item.PluginId, stage, PluginLifecycleRetentionReason.ShutdownNotExecuted));
                        continue;
                    }
                    _states.SetState(new PluginLifecycleState(item.PluginId, PluginLifecycleStatus.Stopping)
                    {
                        Stage = stage,
                    });
                    var result = await _runner.RunAsync(item.Lifecycle.ShutdownAsync, _timeouts.Shutdown,
                        cancellationToken,
                        exception => ReportCancellationFailure(item.PluginId, PluginLifecycleStage.Shutdown, exception),
                        operation => item.Shutdown = operation).ConfigureAwait(false);
                    CommitShutdownResult(item.PluginId, result);
                    if (!await item.Shutdown!.WaitForCompletionAsync().ConfigureAwait(false))
                        retained.Add(new(item.PluginId, stage, PluginLifecycleRetentionReason.OperationRunning));
                    else if (!item.Shutdown.Execution.IsCompletedSuccessfully)
                        retained.Add(new(item.PluginId, stage, PluginLifecycleRetentionReason.ShutdownFailed));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // 调用方取消也只是停止正常等待，不能绕过同一操作已经固定的取消宽限。
                    // 只对当前已发出的 Shutdown 收尾；后续项由上面的取消检查记录为未执行。
                    if (item.Shutdown is null)
                        retained.Add(new(item.PluginId, stage, PluginLifecycleRetentionReason.ShutdownNotExecuted));
                    else
                    {
                        CommitShutdownResult(item.PluginId, new PluginLifecycleOperationResult(
                            PluginLifecycleOperationOutcome.Failed, TimeSpan.Zero,
                            new OperationCanceledException(cancellationToken)));
                        if (!await item.Shutdown.WaitForCompletionAsync().ConfigureAwait(false))
                            retained.Add(new(item.PluginId, stage, PluginLifecycleRetentionReason.OperationRunning));
                        else if (!item.Shutdown.Execution.IsCompletedSuccessfully)
                            retained.Add(new(item.PluginId, stage, PluginLifecycleRetentionReason.ShutdownFailed));
                    }
                }
                catch (Exception)
                {
                    retained.Add(new(item.PluginId, stage, PluginLifecycleRetentionReason.CheckFailed));
                }
            }
            // 最后再次核对责任：Task 刚好成功不代表对应 Shutdown 已执行，不能在竞态窗口误放行。
            foreach (var item in _attempts)
            {
                if (item.Initialization?.Execution.IsCompletedSuccessfully == true && item.Shutdown is null)
                    retained.Add(new(item.PluginId, PluginLifecycleStage.Shutdown,
                        PluginLifecycleRetentionReason.ShutdownNotExecuted));
            }
            _shutdownResult = new PluginLifecycleShutdownResult(retained.Distinct().ToArray());
            foreach (var item in _shutdownResult.Retentions)
            {
                var code = item.Reason switch
                {
                    PluginLifecycleRetentionReason.OperationRunning => HostDiagnosticCodes.LifecycleOperationRetained,
                    PluginLifecycleRetentionReason.ShutdownFailed => HostDiagnosticCodes.LifecycleFailedShutdownRetained,
                    PluginLifecycleRetentionReason.ShutdownNotExecuted => HostDiagnosticCodes.LifecycleShutdownSkipped,
                    _ => HostDiagnosticCodes.LifecycleDrainCheckFailed,
                };
                _diagnostics.Report(new HostDiagnosticDraft(code, HostDiagnosticPhase.PluginLifecycle)
                {
                    PluginId = item.PluginId,
                    LifecycleStage = item.Stage,
                });
            }
            return _shutdownResult;
        }
        finally { _gate.Release(); }
    }

    /// <summary>退出交接前封闭迟到报告；不改变任务执行，也不触发后台释放。</summary>
    internal void CloseDiagnostics() => _diagnostics.Close();

    private void CommitInitializationResult(
        PluginId pluginId,
        PluginLifecycleOperationResult result)
    {
        var state = result.Outcome switch
        {
            PluginLifecycleOperationOutcome.Succeeded => new PluginLifecycleState(
                pluginId, PluginLifecycleStatus.Ready)
            {
                Duration = result.Duration,
            },
            PluginLifecycleOperationOutcome.Failed => new PluginLifecycleState(
                pluginId, PluginLifecycleStatus.InitializationFailed)
            {
                ErrorCode = HostDiagnosticCodes.LifecycleInitializeFailed,
                Duration = result.Duration,
            },
            PluginLifecycleOperationOutcome.TimedOut => new PluginLifecycleState(
                pluginId, PluginLifecycleStatus.InitializationTimedOut)
            {
                ErrorCode = HostDiagnosticCodes.LifecycleInitializeTimeout,
                Duration = result.Duration,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(result)),
        };
        _states.SetState(state);
        if (state.Status != PluginLifecycleStatus.Ready)
        {
            Report(state, result.Exception);
        }
    }

    private void CommitShutdownResult(
        PluginId pluginId,
        PluginLifecycleOperationResult result)
    {
        var state = result.Outcome switch
        {
            PluginLifecycleOperationOutcome.Succeeded => new PluginLifecycleState(
                pluginId, PluginLifecycleStatus.Stopped)
            {
                Stage = PluginLifecycleStage.Shutdown,
                Duration = result.Duration,
            },
            PluginLifecycleOperationOutcome.Failed => new PluginLifecycleState(
                pluginId, PluginLifecycleStatus.ShutdownFailed)
            {
                Stage = PluginLifecycleStage.Shutdown,
                ErrorCode = HostDiagnosticCodes.LifecycleShutdownFailed,
                Duration = result.Duration,
            },
            PluginLifecycleOperationOutcome.TimedOut => new PluginLifecycleState(
                pluginId, PluginLifecycleStatus.ShutdownTimedOut)
            {
                Stage = PluginLifecycleStage.Shutdown,
                ErrorCode = HostDiagnosticCodes.LifecycleShutdownTimeout,
                Duration = result.Duration,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(result)),
        };
        _states.SetState(state);
        if (state.Status != PluginLifecycleStatus.Stopped)
        {
            Report(state, result.Exception);
        }
    }

    private void Report(PluginLifecycleState state, Exception? exception)
    {
        if (state.ErrorCode is null)
        {
            return;
        }

        _diagnostics.Report(new HostDiagnosticDraft(
            state.ErrorCode,
            HostDiagnosticPhase.PluginLifecycle)
        {
            PluginId = state.PluginId,
            LifecycleStage = state.Stage,
            Duration = state.Duration,
            Exception = exception,
        });
    }

    private void ReportCancellationFailure(
        PluginId pluginId,
        PluginLifecycleStage stage,
        Exception exception) =>
        _diagnostics.Report(new HostDiagnosticDraft(
            HostDiagnosticCodes.LifecycleCancellationFailed,
            HostDiagnosticPhase.PluginLifecycle)
        {
            PluginId = pluginId,
            LifecycleStage = stage,
            Exception = exception,
        });

    /// <summary>只在协调器串行门内修改；操作自身负责跨线程的终态和取消资源。</summary>
    private sealed class LifecycleAttempt(PluginId pluginId, PluginLifecycleCallbacks lifecycle)
    {
        internal PluginId PluginId { get; } = pluginId;
        internal PluginLifecycleCallbacks Lifecycle { get; } = lifecycle;
        internal PluginLifecycleOperation? Initialization { get; set; }
        internal PluginLifecycleOperation? Shutdown { get; set; }
    }
}
