namespace MyAvaloniaManagementCommon.Plugin;

/// <summary>描述插件生命周期在当前宿主进程中的可观察状态。</summary>
public enum PluginLifecycleStatus
{
    /// <summary>尚未执行初始化。</summary>
    NotStarted,
    /// <summary>正在执行初始化。</summary>
    Initializing,
    /// <summary>初始化成功，可提供服务。</summary>
    Ready,
    /// <summary>生命周期回调失败或被宿主取消。</summary>
    Failed,
    /// <summary>正在执行关闭回调。</summary>
    Stopping,
    /// <summary>关闭回调成功完成。</summary>
    Stopped,
    /// <summary>因依赖缺失、循环或上游失败而未执行。</summary>
    Blocked,
    /// <summary>生命周期回调超过宿主期限。</summary>
    TimedOut,
}

/// <summary>标识状态产生于初始化、关闭还是尚未进入回调阶段。</summary>
public enum PluginLifecycleStage
{
    /// <summary>尚未进入生命周期回调。</summary>
    None,
    /// <summary>状态产生于初始化阶段。</summary>
    Initialization,
    /// <summary>状态产生于关闭阶段。</summary>
    Shutdown,
}

/// <summary>插件生命周期在某一时刻的不可变诊断快照。</summary>
/// <param name="PluginId">状态所属插件的稳定身份。</param>
/// <param name="Status">当前状态。</param>
/// <param name="ErrorMessage">可展示的失败摘要；成功状态为 <see langword="null"/>。</param>
public sealed record PluginLifecycleState(
    PluginId PluginId,
    PluginLifecycleStatus Status,
    string? ErrorMessage = null)
{
    /// <summary>获取产生当前状态的生命周期阶段。</summary>
    public PluginLifecycleStage Stage { get; init; }

    /// <summary>获取供自动化与诊断使用的稳定错误码。</summary>
    public string? ErrorCode { get; init; }

    /// <summary>获取已完成或超时操作的实际耗时。</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>获取插件声明的直接生命周期依赖。</summary>
    public IReadOnlyList<PluginId> RequiredPluginIds { get; init; } = [];

    /// <summary>获取直接阻止当前插件初始化的依赖身份。</summary>
    public PluginId? BlockingPluginId { get; init; }

    /// <summary>获取插件是否已经初始化成功并可被宿主使用。</summary>
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
    private readonly Dictionary<PluginId, PluginLifecycleState> _states;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initializationCompleted;
    private bool _shutdownCompleted;

    /// <summary>使用宿主默认期限创建生命周期协调器。</summary>
    /// <param name="registrations">本次 Runtime 中由 manifest 身份绑定的全部生命周期注册项。</param>
    public PluginLifecycleManager(IEnumerable<PluginLifecycleRegistration> registrations)
        : this(registrations, new PluginLifecycleOptions())
    {
    }

    /// <summary>使用显式期限创建生命周期协调器并预先构建依赖计划。</summary>
    /// <param name="registrations">本次 Runtime 中由 manifest 身份绑定的全部生命周期注册项。</param>
    /// <param name="options">由宿主统一拥有的超时设置。</param>
    /// <exception cref="ArgumentException">插件身份或依赖声明无效。</exception>
    /// <exception cref="ArgumentOutOfRangeException">任一期限不大于零。</exception>
    public PluginLifecycleManager(
        IEnumerable<PluginLifecycleRegistration> registrations,
        PluginLifecycleOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
        _plan = PluginLifecyclePlanBuilder.Build(registrations);
        _states = new Dictionary<PluginId, PluginLifecycleState>(_plan.InitialStates);
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
                    .OrderBy(state => state.PluginId.Value, StringComparer.Ordinal)
                    .ToArray();
            }
        }
    }

    /// <summary>按稳定身份查找当前状态快照。</summary>
    /// <param name="pluginId">需要查询的插件身份。</param>
    /// <returns>已知状态；没有对应生命周期注册时为 <see langword="null"/>。</returns>
    public PluginLifecycleState? GetState(PluginId pluginId)
    {
        lock (_states)
        {
            return _states.GetValueOrDefault(pluginId);
        }
    }

    /// <summary>按依赖顺序初始化尚未执行的插件。</summary>
    /// <param name="cancellationToken">宿主停止启动流程时使用的取消信号。</param>
    /// <remarks>该操作幂等；单个插件失败会记录状态并阻塞其依赖方，不会重复初始化成功实例。</remarks>
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

    /// <summary>按成功初始化的相反顺序关闭插件。</summary>
    /// <param name="cancellationToken">宿主强制结束关闭流程时使用的取消信号。</param>
    /// <remarks>该操作幂等，只关闭已成功初始化的实例。</remarks>
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
        PluginId pluginId,
        string stage,
        Exception? exception) =>
        Console.Error.WriteLine(
            $"PluginLifecycle errorCode=LIFECYCLE_{stage.ToUpperInvariant()}_FAILED pluginId={pluginId} type={exception?.GetType().Name ?? "Unknown"} message={exception?.Message ?? "-"}");

    private static void ReportTimeout(
        PluginId pluginId,
        string stage,
        TimeSpan duration) =>
        Console.Error.WriteLine(
            $"PluginLifecycle errorCode=LIFECYCLE_UNRESPONSIVE pluginId={pluginId} stage={stage} durationMs={duration.TotalMilliseconds:0}");
}
