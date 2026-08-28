using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;
using Avalonia.Threading;
using MyAvaloniaManagement.Business.Commands.Catalog;
using MyAvaloniaManagement.Business.Commands.Execution;
using MyAvaloniaManagement.Business.Commands.State;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.Business.Plugins.Registration;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.Business.Presentation.Commands;

/// <summary>定义菜单 View 消费的只读声明式投影。</summary>
internal interface IWorkbenchMenuProjection
{
    /// <summary>当目标、命令状态或插件可用性变化，需要重新读取菜单快照时发生。</summary>
    event EventHandler? Changed;

    /// <summary>取得指定 Host 共享位置当前可见的命令和分隔符快照。</summary>
    IReadOnlyList<WorkbenchMenuProjectionEntry> GetItems(MenuLocationId locationId);
}

/// <summary>定义 Window 消费的只读快捷键投影。</summary>
internal interface IWorkbenchKeyBindingProjection
{
    /// <summary>当插件可用性变化，需要重新安装活动快捷键时发生。</summary>
    event EventHandler? Changed;

    /// <summary>取得已经完成冲突治理的活动快捷键快照。</summary>
    IReadOnlyList<WorkbenchKeyBindingProjectionEntry> Items { get; }
}

/// <summary>表示菜单投影中的一个 Host-owned 展示条目。</summary>
internal abstract record WorkbenchMenuProjectionEntry;

/// <summary>表示由 Host View 最终创建为 MenuItem 的命令条目。</summary>
internal sealed record WorkbenchMenuCommandProjectionEntry(
    CommandPlacementId PlacementId,
    CommandId CommandId,
    string Header,
    IWorkbenchPresentationCommandBinding Command)
    : WorkbenchMenuProjectionEntry;

/// <summary>表示由 Host 根据可见分组边界自动创建的分隔符。</summary>
internal sealed record WorkbenchMenuSeparatorProjectionEntry : WorkbenchMenuProjectionEntry;

/// <summary>表示由 Host Window 最终创建为 KeyBinding 的活动快捷键条目。</summary>
internal sealed record WorkbenchKeyBindingProjectionEntry(
    CommandPlacementId PlacementId,
    CommandId CommandId,
    Key Key,
    KeyModifiers Modifiers,
    IWorkbenchPresentationCommandBinding Command);

/// <summary>为菜单和快捷键复用同一 CommandId 的 Avalonia ICommand Adapter。</summary>
/// <remarks>
/// Store 只缓存 Host internal Adapter，不缓存 Target、Document、Provider 或控件。即使某个插件暂时
/// 不可用，缓存对象也只持有统一 State Query 和 Executor；Runtime Dispose 时会一次性退订全部状态源。
/// </remarks>
internal sealed class WorkbenchPresentationCommandStore : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<CommandId, WorkbenchPresentationCommand> _commands = [];
    private readonly WorkbenchCommandCatalog _catalog;
    private readonly WorkbenchCommandStateQuery _states;
    private readonly WorkbenchCommandExecutor _executor;
    private readonly Dispatcher _dispatcher;
    private readonly IHostDiagnosticSink? _diagnostics;
    private bool _disposed;

    internal WorkbenchPresentationCommandStore(
        WorkbenchCommandCatalog catalog,
        WorkbenchCommandStateQuery states,
        WorkbenchCommandExecutor executor,
        Dispatcher dispatcher,
        IHostDiagnosticSink? diagnostics = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _states = states ?? throw new ArgumentNullException(nameof(states));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _diagnostics = diagnostics;
    }

    /// <summary>取得指定命令唯一的展示 Adapter；未知命令表示组合事实损坏并立即失败。</summary>
    internal WorkbenchPresentationCommand Get(CommandId commandId)
    {
        ArgumentNullException.ThrowIfNull(commandId);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_commands.TryGetValue(commandId, out var existing))
            {
                return existing;
            }
            if (!_catalog.TryGet(commandId, out _))
            {
                throw new InvalidOperationException($"投影引用了未知 CommandId：{commandId.Value}。");
            }

            var created = new WorkbenchPresentationCommand(
                commandId,
                _states,
                _executor,
                _dispatcher,
                _diagnostics);
            _commands.Add(commandId, created);
            return created;
        }
    }

    /// <summary>释放所有唯一 Adapter 对状态源的订阅。</summary>
    public void Dispose()
    {
        WorkbenchPresentationCommand[] commands;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            commands = _commands.Values.ToArray();
            _commands.Clear();
        }
        foreach (var command in commands)
        {
            command.Dispose();
        }
    }
}

/// <summary>从 Host/Plugin 不可变声明确定性生成当前菜单快照。</summary>
internal sealed class WorkbenchMenuProjection : IWorkbenchMenuProjection, IDisposable
{
    private readonly object _gate = new();
    private readonly HostWorkbenchCommandProjectionCatalog _host;
    private readonly PluginRegistry _plugins;
    private readonly WorkbenchCommandCatalog _commands;
    private readonly WorkbenchCommandStateQuery _states;
    private readonly PluginAvailabilityReadModel _availability;
    private readonly WorkbenchPresentationCommandStore _presentationCommands;
    private readonly Dispatcher _dispatcher;
    private readonly IHostDiagnosticSink? _diagnostics;
    private bool _refreshQueued;
    private bool _disposed;

    internal WorkbenchMenuProjection(
        HostWorkbenchCommandProjectionCatalog host,
        PluginRegistry plugins,
        WorkbenchCommandCatalog commands,
        WorkbenchCommandStateQuery states,
        PluginAvailabilityReadModel availability,
        WorkbenchPresentationCommandStore presentationCommands,
        Dispatcher dispatcher,
        IHostDiagnosticSink? diagnostics = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _plugins = plugins ?? throw new ArgumentNullException(nameof(plugins));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _states = states ?? throw new ArgumentNullException(nameof(states));
        _availability = availability ?? throw new ArgumentNullException(nameof(availability));
        _presentationCommands = presentationCommands ??
            throw new ArgumentNullException(nameof(presentationCommands));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _diagnostics = diagnostics;
        _states.StateInvalidated += OnStateInvalidated;
        _availability.AvailabilityChanged += OnAvailabilityChanged;
    }

    public event EventHandler? Changed;

    /// <summary>
    /// 读取时重新查询真实状态并按可见项重算分隔符，避免缓存的 Target 状态制造悬空 Separator。
    /// </summary>
    public IReadOnlyList<WorkbenchMenuProjectionEntry> GetItems(MenuLocationId locationId)
    {
        ArgumentNullException.ThrowIfNull(locationId);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        var result = new List<WorkbenchMenuProjectionEntry>();
        foreach (var descriptor in _host.MenuContributions
                     .Where(item => item.LocationId == locationId)
                     .OrderBy(item => item.Order)
                     .ThenBy(item => item.PlacementId.Value, StringComparer.Ordinal))
        {
            result.Add(CreateCommandEntry(descriptor));
        }

        // View 菜单已有 Host 静态“主题”项；其他位置的前置事实来自上面的 Host Contribution。
        var hasPrecedingVisibleItem = result.Count > 0 ||
                                      locationId == WorkbenchMenuLocations.ViewShared;
        string? previousNonEmptyGroup = null;
        foreach (var contribution in _plugins.MenuCommandContributions
                     .Where(item => item.Descriptor.LocationId == locationId)
                     .OrderBy(item => item.Descriptor.Group, StringComparer.Ordinal)
                     .ThenBy(item => item.Descriptor.Order)
                     .ThenBy(item => item.Descriptor.PlacementId.Value, StringComparer.Ordinal))
        {
            if (!TryCreatePluginEntry(contribution, out var entry))
            {
                continue;
            }

            var group = contribution.Descriptor.Group;
            if (group.Length > 0 &&
                hasPrecedingVisibleItem &&
                !string.Equals(previousNonEmptyGroup, group, StringComparison.Ordinal))
            {
                result.Add(new WorkbenchMenuSeparatorProjectionEntry());
            }
            result.Add(entry);
            hasPrecedingVisibleItem = true;
            if (group.Length > 0)
            {
                previousNonEmptyGroup = group;
            }
        }

        return result;
    }

    private bool TryCreatePluginEntry(
        PluginMenuCommandContribution contribution,
        out WorkbenchMenuCommandProjectionEntry entry)
    {
        entry = null!;
        if (!_availability.IsAvailable(contribution.OwnerId) ||
            !_commands.TryGet(contribution.Descriptor.CommandId, out _))
        {
            return false;
        }

        var state = _states.Query(contribution.Descriptor.CommandId).Status;
        if (state is WorkbenchCommandStateStatus.OwnerUnavailable or
            WorkbenchCommandStateStatus.CommandNotFound ||
            state == WorkbenchCommandStateStatus.TargetUnavailable &&
            contribution.Descriptor.TargetUnavailableBehavior ==
                MenuCommandTargetUnavailableBehavior.Hide)
        {
            return false;
        }

        entry = CreateCommandEntry(contribution.Descriptor);
        return true;
    }

    private WorkbenchMenuCommandProjectionEntry CreateCommandEntry(
        MenuCommandContributionDescriptor descriptor)
    {
        if (!_commands.TryGet(descriptor.CommandId, out var command))
        {
            throw new InvalidOperationException(
                $"菜单投影引用了未知 CommandId：{descriptor.CommandId.Value}。");
        }
        return new WorkbenchMenuCommandProjectionEntry(
            descriptor.PlacementId,
            descriptor.CommandId,
            command.Descriptor.DisplayName,
            _presentationCommands.Get(descriptor.CommandId));
    }

    private void OnStateInvalidated(
        object? sender,
        WorkbenchCommandStateInvalidatedEventArgs args) => QueueChanged();

    private void OnAvailabilityChanged(
        object? sender,
        PluginAvailabilityChangedEventArgs args) => QueueChanged();

    private void QueueChanged()
    {
        lock (_gate)
        {
            if (_disposed || _refreshQueued)
            {
                return;
            }
            _refreshQueued = true;
        }
        if (_dispatcher.CheckAccess())
        {
            PublishChanged();
        }
        else
        {
            _dispatcher.Post(PublishChanged, DispatcherPriority.Normal);
        }
    }

    private void PublishChanged()
    {
        Delegate[] handlers;
        lock (_gate)
        {
            _refreshQueued = false;
            if (_disposed)
            {
                return;
            }
            handlers = Changed?.GetInvocationList() ?? [];
        }
        foreach (EventHandler handler in handlers)
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch (Exception exception)
            {
                ReportObserverFailure(exception);
            }
        }
    }

    private void ReportObserverFailure(Exception exception)
    {
        try
        {
            _diagnostics?.Report(new HostDiagnosticDraft(
                HostDiagnosticCodes.WorkbenchCommandStateObserverFailed,
                HostDiagnosticPhase.WorkbenchCommand)
            {
                Exception = exception,
            });
        }
        catch
        {
            // 展示刷新与诊断均是边界观察者，任何一个失败都不能中断另一个菜单位置。
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _refreshQueued = false;
            Changed = null;
        }
        _states.StateInvalidated -= OnStateInvalidated;
        _availability.AvailabilityChanged -= OnAvailabilityChanged;
    }
}

/// <summary>应用 Host 优先、跨插件双禁用政策并生成活动快捷键快照。</summary>
internal sealed class WorkbenchKeyBindingProjection : IWorkbenchKeyBindingProjection, IDisposable
{
    private readonly object _gate = new();
    private readonly HostWorkbenchCommandProjectionCatalog _host;
    private readonly PluginRegistry _plugins;
    private readonly PluginAvailabilityReadModel _availability;
    private readonly WorkbenchPresentationCommandStore _presentationCommands;
    private readonly Dispatcher _dispatcher;
    private readonly HashSet<CommandPlacementId> _inactivePluginPlacements;
    private bool _refreshQueued;
    private bool _disposed;

    internal WorkbenchKeyBindingProjection(
        HostWorkbenchCommandProjectionCatalog host,
        PluginRegistry plugins,
        PluginAvailabilityReadModel availability,
        WorkbenchPresentationCommandStore presentationCommands,
        Dispatcher dispatcher,
        IHostDiagnosticSink? diagnostics = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _plugins = plugins ?? throw new ArgumentNullException(nameof(plugins));
        _availability = availability ?? throw new ArgumentNullException(nameof(availability));
        _presentationCommands = presentationCommands ??
            throw new ArgumentNullException(nameof(presentationCommands));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _inactivePluginPlacements = ResolveConflicts(diagnostics);
        _availability.AvailabilityChanged += OnAvailabilityChanged;
    }

    public event EventHandler? Changed;

    public IReadOnlyList<WorkbenchKeyBindingProjectionEntry> Items
    {
        get
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
            }

            var host = _host.KeyBindingContributions.Select(CreateEntry);
            var plugins = _plugins.KeyBindingContributions
                .Where(item => !_inactivePluginPlacements.Contains(item.Descriptor.PlacementId) &&
                               _availability.IsAvailable(item.OwnerId))
                .OrderBy(item => item.Descriptor.PlacementId.Value, StringComparer.Ordinal)
                .Select(item => CreateEntry(item.Descriptor));
            return host.Concat(plugins).ToArray();
        }
    }

    private HashSet<CommandPlacementId> ResolveConflicts(IHostDiagnosticSink? diagnostics)
    {
        var inactive = new HashSet<CommandPlacementId>();
        var hostGestures = _host.KeyBindingContributions
            .Select(item => new WorkbenchKeyGesture(item.Key, item.Modifiers))
            .ToHashSet();
        foreach (var group in _plugins.KeyBindingContributions.GroupBy(item =>
                     new WorkbenchKeyGesture(item.Descriptor.Key, item.Descriptor.Modifiers)))
        {
            var conflictsWithHost = hostGestures.Contains(group.Key);
            var conflictsAcrossPlugins = group.Select(item => item.OwnerId).Distinct().Count() > 1;
            // 同 owner 重复 Gesture 应在 G1 Seal 时拒绝；此处把损坏快照也按最安全政策全部禁用。
            var duplicateInSnapshot = group.Count() > 1;
            if (!conflictsWithHost && !conflictsAcrossPlugins && !duplicateInSnapshot)
            {
                continue;
            }
            foreach (var contribution in group)
            {
                inactive.Add(contribution.Descriptor.PlacementId);
                try
                {
                    diagnostics?.Report(new HostDiagnosticDraft(
                        HostDiagnosticCodes.WorkbenchKeyGestureConflict,
                        HostDiagnosticPhase.WorkbenchCommand)
                    {
                        PluginId = contribution.OwnerId,
                        StableId = contribution.Descriptor.PlacementId.Value,
                    });
                }
                catch
                {
                    // 冲突事实已经安全禁用；诊断设施失败不能重新激活有歧义的快捷键。
                }
            }
        }
        return inactive;
    }

    private WorkbenchKeyBindingProjectionEntry CreateEntry(
        KeyBindingContributionDescriptor descriptor) => new(
        descriptor.PlacementId,
        descriptor.CommandId,
        descriptor.Key,
        descriptor.Modifiers,
        _presentationCommands.Get(descriptor.CommandId));

    private void OnAvailabilityChanged(
        object? sender,
        PluginAvailabilityChangedEventArgs args)
    {
        lock (_gate)
        {
            if (_disposed || _refreshQueued)
            {
                return;
            }
            _refreshQueued = true;
        }
        if (_dispatcher.CheckAccess())
        {
            PublishChanged();
        }
        else
        {
            _dispatcher.Post(PublishChanged, DispatcherPriority.Normal);
        }
    }

    private void PublishChanged()
    {
        Delegate[] handlers;
        lock (_gate)
        {
            _refreshQueued = false;
            if (_disposed)
            {
                return;
            }
            handlers = Changed?.GetInvocationList() ?? [];
        }
        foreach (EventHandler handler in handlers)
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch
            {
                // View 观察者失败不能阻断同一投影的其他窗口刷新或插件生命周期推进。
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _refreshQueued = false;
            Changed = null;
        }
        _availability.AvailabilityChanged -= OnAvailabilityChanged;
    }

    private readonly record struct WorkbenchKeyGesture(Key Key, KeyModifiers Modifiers);
}

/// <summary>组合并拥有 G5 菜单、快捷键和共享命令 Adapter 的根级展示模型。</summary>
internal sealed class WorkbenchCommandPresentation :
    IWorkbenchCommandPresentationBindings,
    IDisposable
{
    private readonly WorkbenchPresentationCommandStore _commands;
    private readonly WorkbenchMenuProjection _menu;
    private readonly WorkbenchKeyBindingProjection _keyBindings;
    private bool _disposed;

    internal WorkbenchCommandPresentation(
        HostWorkbenchCommandProjectionCatalog host,
        PluginRegistry plugins,
        WorkbenchCommandCatalog catalog,
        WorkbenchCommandStateQuery states,
        WorkbenchCommandExecutor executor,
        PluginAvailabilityReadModel availability,
        Dispatcher dispatcher,
        IHostDiagnosticSink? diagnostics = null)
    {
        _commands = new WorkbenchPresentationCommandStore(
            catalog,
            states,
            executor,
            dispatcher,
            diagnostics);
        _menu = new WorkbenchMenuProjection(
            host,
            plugins,
            catalog,
            states,
            availability,
            _commands,
            dispatcher,
            diagnostics);
        _keyBindings = new WorkbenchKeyBindingProjection(
            host,
            plugins,
            availability,
            _commands,
            dispatcher,
            diagnostics);
    }

    public IWorkbenchMenuProjection Menu => _menu;

    public IWorkbenchKeyBindingProjection KeyBindings => _keyBindings;

    /// <summary>按“观察者 → Adapter”顺序解除订阅，防止释放过程中产生新的 View 刷新。</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _menu.Dispose();
        _keyBindings.Dispose();
        _commands.Dispose();
    }
}
