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

/// <summary>
/// 独占插件可用性与生命周期状态的可变事实；Coordinator 是唯一写入者，其他组件只能通过只读投影查询。
/// </summary>
internal sealed class PluginLifecycleStateStore
{
    private readonly object _gate = new();
    private readonly Dictionary<PluginId, PluginLifecycleState> _states;
    private readonly HashSet<PluginId> _availablePluginIds;
    private bool _acceptingContributions = true;

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
        lock (_gate)
        {
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
        }
    }

    /// <summary>在释放 UI 与 Scope 前关闭所有新的贡献激活入口。</summary>
    internal void BeginShutdown()
    {
        lock (_gate)
        {
            _acceptingContributions = false;
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

    internal IReadOnlyList<PluginLifecycleState> LifecycleStates => _store.Snapshot;

    internal PluginLifecycleState? GetLifecycleState(PluginId pluginId) =>
        _store.GetState(pluginId);

    internal bool IsAvailable(PluginId pluginId) => _store.IsAvailable(pluginId);
}
