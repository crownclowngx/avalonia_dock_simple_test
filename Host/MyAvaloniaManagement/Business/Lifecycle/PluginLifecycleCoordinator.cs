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
    private readonly IHostDiagnosticSink? _diagnostics;
    private readonly PluginLifecycleTimeouts _timeouts;
    private readonly PluginLifecycleOperationRunner _runner = new();
    private readonly List<StartedLifecycle> _started = [];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initializationCompleted;
    private bool _shutdownCompleted;

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
        PluginLifecycleTimeouts timeouts)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _lifecycleResolver = lifecycleResolver ?? throw new ArgumentNullException(nameof(lifecycleResolver));
        _states = states ?? throw new ArgumentNullException(nameof(states));
        _diagnostics = diagnostics;
        _timeouts = timeouts ?? throw new ArgumentNullException(nameof(timeouts));
        if (_timeouts.Initialization <= TimeSpan.Zero || _timeouts.Shutdown <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeouts), "插件生命周期期限必须大于零。");
        }
    }

    internal async Task InitializeAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initializationCompleted)
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
                                exception))
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

                CommitInitializationResult(declaration.OwnerId, lifecycle, result);
            }

            _initializationCompleted = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task ShutdownAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_shutdownCompleted)
            {
                return;
            }

            for (var index = _started.Count - 1; index >= 0; index--)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = _started[index];
                _states.SetState(new PluginLifecycleState(
                    item.PluginId,
                    PluginLifecycleStatus.Stopping)
                {
                    Stage = PluginLifecycleStage.Shutdown,
                });
                var result = await _runner.RunAsync(
                        item.Lifecycle.ShutdownAsync,
                        _timeouts.Shutdown,
                        cancellationToken,
                        exception => ReportCancellationFailure(
                            item.PluginId,
                            PluginLifecycleStage.Shutdown,
                            exception))
                    .ConfigureAwait(false);
                CommitShutdownResult(item.PluginId, result);
            }

            _shutdownCompleted = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void CommitInitializationResult(
        PluginId pluginId,
        PluginLifecycleCallbacks lifecycle,
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
        if (state.Status == PluginLifecycleStatus.Ready)
        {
            _started.Add(new StartedLifecycle(pluginId, lifecycle));
        }
        else
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

        _diagnostics?.Report(new HostDiagnosticDraft(
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
        _diagnostics?.Report(new HostDiagnosticDraft(
            HostDiagnosticCodes.LifecycleCancellationFailed,
            HostDiagnosticPhase.PluginLifecycle)
        {
            PluginId = pluginId,
            LifecycleStage = stage,
            Exception = exception,
        });

    private sealed record StartedLifecycle(
        PluginId PluginId,
        PluginLifecycleCallbacks Lifecycle);
}
