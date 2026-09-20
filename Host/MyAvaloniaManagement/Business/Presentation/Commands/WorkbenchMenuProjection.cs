using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using MyAvaloniaManagement.Business.Commands.Catalog;
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

/// <summary>从 Host/Plugin 不可变声明确定性生成当前菜单快照。</summary>
/// <remarks>
/// 菜单只拥有自己的订阅和刷新状态，命令适配器由共享 Store 提供；每次查询仍读取当前目标。
/// 分组和分隔符属于菜单规则，不能与快捷键冲突规则合并。UI 线程上的同步通知保持原范围，
/// Palette 的始终排队政策由其自身实现负责。
/// </remarks>
internal sealed class WorkbenchMenuProjection : IWorkbenchMenuProjection, IDisposable
{
    private readonly object _gate = new();
    private readonly HostWorkbenchCommandProjectionCatalog _host;
    private readonly PluginRegistry _plugins;
    private readonly WorkbenchCommandCatalog _commands;
    private readonly WorkbenchCommandStateQuery _states;
    private readonly PluginAvailabilityReadModel _availability;
    private readonly WorkbenchPresentationCommandStore _presentationCommands;
    private readonly UiRefreshScheduler _refresh;
    private readonly IHostDiagnosticSink? _diagnostics;
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
        _refresh = new UiRefreshScheduler(_gate, dispatcher, UiRefreshMode.ImmediateOnUiThread, PublishChanged);
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

    private void QueueChanged() => _refresh.RequestRefresh();

    private void PublishChanged()
    {
        Delegate[] handlers;
        lock (_gate)
        {
            if (!_refresh.TryBeginRefresh() || _disposed)
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
            _refresh.Dispose();
            Changed = null;
        }
        _states.StateInvalidated -= OnStateInvalidated;
        _availability.AvailabilityChanged -= OnAvailabilityChanged;
    }
}
