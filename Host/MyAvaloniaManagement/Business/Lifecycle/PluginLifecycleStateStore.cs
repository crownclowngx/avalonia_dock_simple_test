using System;
using System.Collections.Generic;
using System.Linq;
using MyAvaloniaManagement.Business.Plugins.Registration;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.Business.Lifecycle;

/// <summary>表示 Host internal 生命周期状态；这些类型不得进入 Plugin SDK。</summary>
internal enum PluginLifecycleStatus
{
    NotStarted,
    Initializing,
    Ready,
    InitializationFailed,
    InitializationTimedOut,
    HostCancelled,
    Stopping,
    Stopped,
    ShutdownFailed,
    ShutdownTimedOut,
}

/// <summary>标识诊断来自启动还是关闭阶段。</summary>
internal enum PluginLifecycleStage
{
    Initialization,
    Shutdown,
}

/// <summary>生命周期状态 Tool 消费的不可变快照。</summary>
internal sealed record PluginLifecycleState(
    PluginId PluginId,
    PluginLifecycleStatus Status)
{
    internal PluginLifecycleStage Stage { get; init; } = PluginLifecycleStage.Initialization;
    internal string? ErrorCode { get; init; }
    internal TimeSpan? Duration { get; init; }
}

/// <summary>携带一个插件贡献可用性的真实布尔变化。</summary>
internal sealed class PluginAvailabilityChangedEventArgs(
    PluginId pluginId,
    bool isAvailable) : EventArgs
{
    internal PluginId PluginId { get; } =
        pluginId ?? throw new ArgumentNullException(nameof(pluginId));

    internal bool IsAvailable { get; } = isAvailable;
}

/// <summary>
/// 独占插件可用性与生命周期状态的可变事实；Coordinator 是唯一写入者，其他组件只能通过只读投影查询。
/// </summary>
internal sealed class PluginLifecycleStateStore
{
    private readonly object _gate = new();
    private readonly Dictionary<PluginId, PluginLifecycleState> _states;
    private readonly HashSet<PluginId> _availablePluginIds;
    private bool _acceptingContributions = true;

    /// <summary>
    /// 当某个 owner 的贡献可用性真正改变时发生；通知在 Store 锁外发布，观察者不能反向阻塞查询。
    /// </summary>
    internal event EventHandler<PluginAvailabilityChangedEventArgs>? AvailabilityChanged;

    internal PluginLifecycleStateStore(PluginRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var lifecycleIds = registry.Lifecycles
            .Select(item => item.OwnerId)
            .ToHashSet();
        _states = lifecycleIds.ToDictionary(
            id => id,
            id => new PluginLifecycleState(id, PluginLifecycleStatus.NotStarted));
        _availablePluginIds = registry.DeclaredOwnerIds
            .Where(id => !lifecycleIds.Contains(id))
            .ToHashSet();
    }

    internal IReadOnlyList<PluginLifecycleState> Snapshot
    {
        get
        {
            lock (_gate)
            {
                return _states.Values
                    .OrderBy(state => state.PluginId.Value, StringComparer.Ordinal)
                    .ToArray();
            }
        }
    }

    internal PluginLifecycleState? GetState(PluginId pluginId)
    {
        ArgumentNullException.ThrowIfNull(pluginId);
        lock (_gate)
        {
            return _states.GetValueOrDefault(pluginId);
        }
    }

    internal bool IsAvailable(PluginId pluginId)
    {
        ArgumentNullException.ThrowIfNull(pluginId);
        lock (_gate)
        {
            return _acceptingContributions && _availablePluginIds.Contains(pluginId);
        }
    }

    internal void SetState(PluginLifecycleState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        bool wasAvailable;
        bool isAvailable;
        lock (_gate)
        {
            wasAvailable = _acceptingContributions &&
                           _availablePluginIds.Contains(state.PluginId);
            _states[state.PluginId] = state;
            if (state.Status == PluginLifecycleStatus.Ready)
            {
                _availablePluginIds.Add(state.PluginId);
            }
            else
            {
                // 生命周期所有者只有 Ready 才可被激活。进入停止、失败、超时或取消状态后立即
                // 撤回可用性，即使调用方绕过 HostRuntime 的全局 BeginShutdown 也不会继续创建贡献。
                _availablePluginIds.Remove(state.PluginId);
            }
            isAvailable = _acceptingContributions &&
                          _availablePluginIds.Contains(state.PluginId);
        }
        if (wasAvailable != isAvailable)
        {
            PublishAvailabilityChanged(state.PluginId, isAvailable);
        }
    }

    /// <summary>在释放 UI 与 Scope 前关闭所有新的贡献激活入口。</summary>
    internal void BeginShutdown()
    {
        PluginId[] unavailableOwners;
        lock (_gate)
        {
            if (!_acceptingContributions)
            {
                return;
            }
            unavailableOwners = _availablePluginIds.ToArray();
            _acceptingContributions = false;
        }
        foreach (var owner in unavailableOwners)
        {
            PublishAvailabilityChanged(owner, isAvailable: false);
        }
    }

    private void PublishAvailabilityChanged(PluginId pluginId, bool isAvailable)
    {
        var args = new PluginAvailabilityChangedEventArgs(pluginId, isAvailable);
        foreach (EventHandler<PluginAvailabilityChangedEventArgs> handler in
                 AvailabilityChanged?.GetInvocationList() ?? [])
        {
            try
            {
                handler(this, args);
            }
            catch
            {
                // 生命周期 Store 是可用性唯一写入者。展示观察者失败不能让状态写入半途回滚，
                // 也不能阻断其他投影收到同一 owner 的撤回通知。
            }
        }
    }
}

/// <summary>
/// 菜单、Dock、布局与状态 Tool 使用的窄只读投影；它不提供任何状态修改或生命周期执行入口。
/// </summary>
internal sealed class PluginAvailabilityReadModel(PluginLifecycleStateStore store)
{
    private readonly PluginLifecycleStateStore _store =
        store ?? throw new ArgumentNullException(nameof(store));

    /// <summary>转发 Store 的只读可用性变化，不暴露任何状态修改入口。</summary>
    internal event EventHandler<PluginAvailabilityChangedEventArgs>? AvailabilityChanged
    {
        add => _store.AvailabilityChanged += value;
        remove => _store.AvailabilityChanged -= value;
    }

    internal IReadOnlyList<PluginLifecycleState> LifecycleStates => _store.Snapshot;

    internal PluginLifecycleState? GetLifecycleState(PluginId pluginId) =>
        _store.GetState(pluginId);

    internal bool IsAvailable(PluginId pluginId) => _store.IsAvailable(pluginId);
}
