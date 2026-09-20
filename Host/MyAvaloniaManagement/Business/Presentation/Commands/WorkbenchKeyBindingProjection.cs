using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;
using Avalonia.Threading;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.Business.Plugins.Registration;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.Business.Presentation.Commands;

/// <summary>定义 Window 消费的只读快捷键投影。</summary>
internal interface IWorkbenchKeyBindingProjection
{
    /// <summary>当插件可用性变化，需要重新安装活动快捷键时发生。</summary>
    event EventHandler? Changed;

    /// <summary>取得已经完成冲突治理的活动快捷键快照。</summary>
    IReadOnlyList<WorkbenchKeyBindingProjectionEntry> Items { get; }
}

/// <summary>表示由 Host Window 最终创建为 KeyBinding 的活动快捷键条目。</summary>
internal sealed record WorkbenchKeyBindingProjectionEntry(
    CommandPlacementId PlacementId,
    CommandId CommandId,
    Key Key,
    KeyModifiers Modifiers,
    IWorkbenchPresentationCommandBinding Command);

/// <summary>应用 Host 优先、跨插件双禁用政策并生成活动快捷键快照。</summary>
/// <remarks>
/// 冲突结果来自冻结声明，当前可用性在读取时重新过滤；本投影不执行命令或拥有插件对象。
/// 与菜单复用 Store 中的同一命令适配器，释放时只退订自己的观察者，适配器由组合对象最后释放。
/// </remarks>
internal sealed class WorkbenchKeyBindingProjection : IWorkbenchKeyBindingProjection, IDisposable
{
    private readonly object _gate = new();
    private readonly HostWorkbenchCommandProjectionCatalog _host;
    private readonly PluginRegistry _plugins;
    private readonly PluginAvailabilityReadModel _availability;
    private readonly WorkbenchPresentationCommandStore _presentationCommands;
    private readonly UiRefreshScheduler _refresh;
    private readonly HashSet<CommandPlacementId> _inactivePluginPlacements;
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
        _refresh = new UiRefreshScheduler(_gate, dispatcher, UiRefreshMode.ImmediateOnUiThread, PublishChanged);
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
            .Concat(_host.ReservedKeyGestures)
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
        PluginAvailabilityChangedEventArgs args) => _refresh.RequestRefresh();

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
            _refresh.Dispose();
            Changed = null;
        }
        _availability.AvailabilityChanged -= OnAvailabilityChanged;
    }

}
