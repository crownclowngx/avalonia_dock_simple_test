namespace MyAvaloniaManagementCommon.Plugin;

public enum PluginLifecycleStatus
{
    NotStarted,
    Initializing,
    Ready,
    Failed,
    Stopping,
    Stopped,
}

public sealed record PluginLifecycleState(
    string PluginId,
    PluginLifecycleStatus Status,
    string? ErrorMessage = null);

/// <summary>
/// 串行编排显式接入插件的初始化，并在宿主退出时按相反顺序关闭。
/// <para>
/// 生命周期实例完全来自依赖注入容器中的 <see cref="IPluginLifecycle"/> 注册。
/// 未实现新模块接口的历史插件不会产生此注册，因此不会进入本管理器，原有初始化流程保持不变。
/// </para>
/// </summary>
public sealed class PluginLifecycleManager
{
    private readonly IReadOnlyList<IPluginLifecycle> _lifecycles;
    private readonly List<IPluginLifecycle> _initialized = [];
    private readonly Dictionary<string, PluginLifecycleState> _states = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initializationCompleted;
    private bool _shutdownCompleted;

    public PluginLifecycleManager(IEnumerable<IPluginLifecycle> lifecycles)
    {
        _lifecycles = lifecycles
            .OrderBy(x => x.Order)
            .ThenBy(x => x.PluginId, StringComparer.Ordinal)
            .ToArray();

        foreach (var lifecycle in _lifecycles)
        {
            _states[lifecycle.PluginId] = new PluginLifecycleState(
                lifecycle.PluginId,
                PluginLifecycleStatus.NotStarted);
        }
    }

    /// <summary>
    /// 获取所有托管插件的当前状态快照。返回值不暴露内部可变字典。
    /// </summary>
    public IReadOnlyCollection<PluginLifecycleState> States
    {
        get
        {
            lock (_states)
            {
                return _states.Values.ToArray();
            }
        }
    }

    /// <summary>
    /// 按插件标识读取当前生命周期状态；历史插件或未知标识返回 <see langword="null"/>。
    /// </summary>
    public PluginLifecycleState? GetState(string pluginId)
    {
        lock (_states)
        {
            return _states.GetValueOrDefault(pluginId);
        }
    }

    /// <summary>
    /// 按 Order 和 PluginId 串行初始化所有托管插件。
    /// 单个插件失败只记录该插件状态，不阻止其他互不依赖的插件继续初始化。
    /// </summary>
    public async Task InitializeAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_initializationCompleted)
            {
                return;
            }

            foreach (var lifecycle in _lifecycles)
            {
                SetState(lifecycle.PluginId, PluginLifecycleStatus.Initializing);
                try
                {
                    await lifecycle.InitializeAsync(cancellationToken);
                    _initialized.Add(lifecycle);
                    SetState(lifecycle.PluginId, PluginLifecycleStatus.Ready);
                }
                catch (Exception ex)
                {
                    SetState(lifecycle.PluginId, PluginLifecycleStatus.Failed, ex.Message);
                    Console.Error.WriteLine($"插件 {lifecycle.PluginId} 初始化失败: {ex}");
                }
            }

            _initializationCompleted = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 仅关闭本次启动中成功初始化的插件，并严格按照初始化顺序反向执行。
    /// 这样可以让后初始化、可能依赖前置服务的插件先释放自身资源。
    /// </summary>
    public async Task ShutdownAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_shutdownCompleted)
            {
                return;
            }

            for (var index = _initialized.Count - 1; index >= 0; index--)
            {
                var lifecycle = _initialized[index];
                SetState(lifecycle.PluginId, PluginLifecycleStatus.Stopping);
                try
                {
                    await lifecycle.ShutdownAsync(cancellationToken);
                    SetState(lifecycle.PluginId, PluginLifecycleStatus.Stopped);
                }
                catch (Exception ex)
                {
                    SetState(lifecycle.PluginId, PluginLifecycleStatus.Failed, ex.Message);
                    Console.Error.WriteLine($"插件 {lifecycle.PluginId} 关闭失败: {ex}");
                }
            }

            _shutdownCompleted = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void SetState(string pluginId, PluginLifecycleStatus status, string? errorMessage = null)
    {
        lock (_states)
        {
            _states[pluginId] = new PluginLifecycleState(pluginId, status, errorMessage);
        }
    }
}
