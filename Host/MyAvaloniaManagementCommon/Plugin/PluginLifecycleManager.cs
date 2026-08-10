namespace MyAvaloniaManagementCommon.Plugin;

public enum PluginLifecycleStatus
{
    NotStarted,
    Initializing,
    Ready,
    Failed,
    Stopping,
    Stopped,
    Blocked,
    TimedOut,
}

public enum PluginLifecycleStage
{
    None,
    Initialization,
    Shutdown,
}

public sealed record PluginLifecycleState(
    string PluginId,
    PluginLifecycleStatus Status,
    string? ErrorMessage = null)
{
    public PluginLifecycleStage Stage { get; init; }

    public string? ErrorCode { get; init; }

    public TimeSpan? Duration { get; init; }

    public IReadOnlyList<string> RequiredPluginIds { get; init; } = [];

    public string? BlockingPluginId { get; init; }

    public bool IsAvailable => Status == PluginLifecycleStatus.Ready;
}

/// <summary>
/// 按依赖计划串行初始化插件，并在宿主退出时反向关闭成功初始化的实例。
/// </summary>
public sealed class PluginLifecycleManager
{
    private readonly PluginLifecyclePlan _plan;
    private readonly PluginLifecycleOptions _options;
    private readonly PluginLifecycleOperationRunner _runner = new();
    private readonly List<PluginLifecyclePlanNode> _initialized = [];
    private readonly Dictionary<string, PluginLifecycleState> _states;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initializationCompleted;
    private bool _shutdownCompleted;

    public PluginLifecycleManager(IEnumerable<IPluginLifecycle> lifecycles)
        : this(lifecycles, new PluginLifecycleOptions())
    {
    }

    public PluginLifecycleManager(
        IEnumerable<IPluginLifecycle> lifecycles,
        PluginLifecycleOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
        _plan = PluginLifecyclePlanBuilder.Build(lifecycles);
        _states = new Dictionary<string, PluginLifecycleState>(
            _plan.InitialStates,
            StringComparer.Ordinal);
    }

    /// <summary>
    /// 获取按 PluginId 排序的不可变状态快照。
    /// </summary>
    public IReadOnlyCollection<PluginLifecycleState> States
    {
        get
        {
            lock (_states)
            {
                return _states.Values
                    .OrderBy(state => state.PluginId, StringComparer.Ordinal)
                    .ToArray();
            }
        }
    }

    public PluginLifecycleState? GetState(string pluginId)
    {
        lock (_states)
        {
            return _states.GetValueOrDefault(pluginId);
        }
    }

    public async Task InitializeAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initializationCompleted)
            {
                return;
            }

            foreach (var node in _plan.OrderedNodes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var currentState = GetState(node.PluginId);
                if (currentState?.Status != PluginLifecycleStatus.NotStarted)
                {
                    continue;
                }

                var blockingDependency = node.RequiredPluginIds.FirstOrDefault(
                    dependency => GetState(dependency)?.Status != PluginLifecycleStatus.Ready);
                if (blockingDependency is not null)
                {
                    SetState(new PluginLifecycleState(
                        node.PluginId,
                        PluginLifecycleStatus.Blocked,
                        $"依赖插件 {blockingDependency} 未成功初始化。")
                    {
                        Stage = PluginLifecycleStage.Initialization,
                        ErrorCode = "LIFECYCLE_DEPENDENCY_BLOCKED",
                        RequiredPluginIds = node.RequiredPluginIds,
                        BlockingPluginId = blockingDependency,
                    });
                    continue;
                }

                SetState(new PluginLifecycleState(
                    node.PluginId,
                    PluginLifecycleStatus.Initializing)
                {
                    Stage = PluginLifecycleStage.Initialization,
                    RequiredPluginIds = node.RequiredPluginIds,
                });

                PluginLifecycleOperationResult result;
                try
                {
                    result = await _runner.RunAsync(
                        node.Lifecycle.InitializeAsync,
                        _options.InitializationTimeout,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    SetState(new PluginLifecycleState(
                        node.PluginId,
                        PluginLifecycleStatus.Failed,
                        "宿主取消了插件初始化。")
                    {
                        Stage = PluginLifecycleStage.Initialization,
                        ErrorCode = "LIFECYCLE_HOST_CANCELLED",
                        RequiredPluginIds = node.RequiredPluginIds,
                    });
                    throw;
                }
                switch (result.Outcome)
                {
                    case PluginLifecycleOperationOutcome.Succeeded:
                        _initialized.Add(node);
                        SetState(new PluginLifecycleState(
                            node.PluginId,
                            PluginLifecycleStatus.Ready)
                        {
                            Stage = PluginLifecycleStage.Initialization,
                            Duration = result.Duration,
                            RequiredPluginIds = node.RequiredPluginIds,
                        });
                        break;

                    case PluginLifecycleOperationOutcome.Failed:
                        SetState(new PluginLifecycleState(
                            node.PluginId,
                            PluginLifecycleStatus.Failed,
                            result.Exception?.Message)
                        {
                            Stage = PluginLifecycleStage.Initialization,
                            ErrorCode = "LIFECYCLE_INITIALIZE_FAILED",
                            Duration = result.Duration,
                            RequiredPluginIds = node.RequiredPluginIds,
                        });
                        ReportFailure(node.PluginId, "initialize", result.Exception);
                        break;

                    case PluginLifecycleOperationOutcome.TimedOut:
                        SetState(new PluginLifecycleState(
                            node.PluginId,
                            PluginLifecycleStatus.TimedOut,
                            $"插件初始化超过 {_options.InitializationTimeout.TotalSeconds:0.###} 秒，插件可能未响应，建议重启应用。")
                        {
                            Stage = PluginLifecycleStage.Initialization,
                            ErrorCode = "LIFECYCLE_UNRESPONSIVE",
                            Duration = result.Duration,
                            RequiredPluginIds = node.RequiredPluginIds,
                        });
                        ReportTimeout(node.PluginId, "initialize", result.Duration);
                        break;
                }
            }

            _initializationCompleted = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ShutdownAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_shutdownCompleted)
            {
                return;
            }

            for (var index = _initialized.Count - 1; index >= 0; index--)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var node = _initialized[index];
                if (GetState(node.PluginId)?.Status == PluginLifecycleStatus.Stopped)
                {
                    continue;
                }

                SetState(new PluginLifecycleState(
                    node.PluginId,
                    PluginLifecycleStatus.Stopping)
                {
                    Stage = PluginLifecycleStage.Shutdown,
                    RequiredPluginIds = node.RequiredPluginIds,
                });

                var result = await _runner.RunAsync(
                    node.Lifecycle.ShutdownAsync,
                    _options.ShutdownTimeout,
                    cancellationToken).ConfigureAwait(false);
                switch (result.Outcome)
                {
                    case PluginLifecycleOperationOutcome.Succeeded:
                        SetState(new PluginLifecycleState(
                            node.PluginId,
                            PluginLifecycleStatus.Stopped)
                        {
                            Stage = PluginLifecycleStage.Shutdown,
                            Duration = result.Duration,
                            RequiredPluginIds = node.RequiredPluginIds,
                        });
                        break;

                    case PluginLifecycleOperationOutcome.Failed:
                        SetState(new PluginLifecycleState(
                            node.PluginId,
                            PluginLifecycleStatus.Failed,
                            result.Exception?.Message)
                        {
                            Stage = PluginLifecycleStage.Shutdown,
                            ErrorCode = "LIFECYCLE_SHUTDOWN_FAILED",
                            Duration = result.Duration,
                            RequiredPluginIds = node.RequiredPluginIds,
                        });
                        ReportFailure(node.PluginId, "shutdown", result.Exception);
                        break;

                    case PluginLifecycleOperationOutcome.TimedOut:
                        SetState(new PluginLifecycleState(
                            node.PluginId,
                            PluginLifecycleStatus.TimedOut,
                            $"插件关闭超过 {_options.ShutdownTimeout.TotalSeconds:0.###} 秒，宿主将继续退出。")
                        {
                            Stage = PluginLifecycleStage.Shutdown,
                            ErrorCode = "LIFECYCLE_UNRESPONSIVE",
                            Duration = result.Duration,
                            RequiredPluginIds = node.RequiredPluginIds,
                        });
                        ReportTimeout(node.PluginId, "shutdown", result.Duration);
                        break;
                }
            }

            _shutdownCompleted = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void SetState(PluginLifecycleState state)
    {
        lock (_states)
        {
            _states[state.PluginId] = state;
        }
    }

    private static void ReportFailure(
        string pluginId,
        string stage,
        Exception? exception) =>
        Console.Error.WriteLine(
            $"PluginLifecycle errorCode=LIFECYCLE_{stage.ToUpperInvariant()}_FAILED pluginId={pluginId} type={exception?.GetType().Name ?? "Unknown"} message={exception?.Message ?? "-"}");

    private static void ReportTimeout(
        string pluginId,
        string stage,
        TimeSpan duration) =>
        Console.Error.WriteLine(
            $"PluginLifecycle errorCode=LIFECYCLE_UNRESPONSIVE pluginId={pluginId} stage={stage} durationMs={duration.TotalMilliseconds:0}");
}
