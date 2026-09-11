using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;
using Avalonia.Threading;
using MyAvaloniaManagement.Business.Commands.Catalog;
using MyAvaloniaManagement.Business.Commands.State;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Plugins.Registration;
using MyAvaloniaManagement.Business.ToolCenter;
using MyAvaloniaManagement.Business.Workspace;
using MyAvaloniaManagement.Business.Search;
using MyAvaloniaManagement.Business.Presentation.Icons;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.Business.Presentation.Commands;

/// <summary>定义 Command Palette View 消费的只读查询投影。</summary>
/// <remarks>
/// 该端口只接受用户查询文本并返回 Host-owned 展示快照，不暴露 Catalog、Context、Target、
/// Provider 或 Executor。生产投影与设计器样例是两个真实实现，因此这是一条实际替换边界。
/// </remarks>
internal interface IWorkbenchCommandPaletteProjection
{
    /// <summary>当命令状态、活动目标或有效快捷键变化，需要重新查询当前结果时发生。</summary>
    event EventHandler? Changed;

    /// <summary>按名称及辅助说明查询功能、已有页面、工具与可发现命令。</summary>
    /// <param name="query">允许为 null 的普通子串查询；首尾空白会被忽略。</param>
    /// <returns>按匹配等级、结果类型、名称与稳定身份确定性排序的快照。</returns>
    IReadOnlyList<WorkbenchCommandPaletteProjectionEntry> GetItems(string? query);
}

/// <summary>只保存展示快照、明确身份与 Host 操作绑定，不引用插件页面实例。</summary>
internal sealed record WorkbenchCommandPaletteProjectionEntry(
    WorkbenchPaletteIdentity Identity, string DisplayName, string Description,
    string ShortcutText, bool IsEnabled, IWorkbenchPresentationCommandBinding Command)
{
    public string StableKey => Identity.StableKey;
    public CommandId? CommandId => (Identity as CommandPaletteIdentity)?.Id;
    public ToolTypeId? ToolTypeId => (Identity as ToolPaletteIdentity)?.Id;
    public string ActionText { get; init; } = "执行命令";
    public string SearchName { get; init; } = DisplayName;
    public int MatchRank { get; init; }
    public HostIconRequest IconRequest { get; init; } = new(null, string.Empty);
    public HostIconRenderer? IconRenderer { get; init; }
}

/// <summary>汇合四类只读候选，普通命令仍以既有菜单声明为发现许可。</summary>
/// <remarks>
/// 候选集合只在不可变 Catalog/Registry 构造后计算一次，但每次查询都会重新读取统一 State Query；
/// 因此本类型既不保存插件业务状态，也不建立第二套执行逻辑。快捷键文本来自冲突治理后的有效投影，
/// 不会把已经被 Host 保留项或跨插件冲突禁用的 Gesture 误导性地显示给用户。
/// </remarks>
internal sealed class WorkbenchCommandPaletteProjection :
    IWorkbenchCommandPaletteProjection,
    IDisposable
{
    private readonly object _gate = new();
    private readonly IReadOnlyList<CommandId> _candidateCommandIds;
    private readonly WorkbenchCommandCatalog _catalog;
    private readonly WorkbenchCommandStateQuery _states;
    private readonly IWorkbenchKeyBindingProjection _keyBindings;
    private readonly WorkbenchPresentationCommandStore _presentationCommands;
    private readonly Dispatcher _dispatcher;
    private readonly IHostDiagnosticSink? _diagnostics;
    private readonly ToolWorkspaceReadModel? _tools;
    private readonly WorkspaceSession? _workspace;
    private readonly DocumentCreationMenuQuery? _functions;
    private readonly WorkspacePaletteActions? _workspaceActions;
    private readonly HostIconRenderer? _icons;
    // 只表示 Dispatcher 队列中已有刷新，不代表任何插件业务状态或结果缓存。
    private bool _refreshQueued;
    // Dispose 后拒绝同步读取，并让已经排队的迟到回调安全退出。
    private bool _disposed;

    internal WorkbenchCommandPaletteProjection(
        HostWorkbenchCommandProjectionCatalog host,
        PluginRegistry plugins,
        WorkbenchCommandCatalog catalog,
        WorkbenchCommandStateQuery states,
        IWorkbenchKeyBindingProjection keyBindings,
        WorkbenchPresentationCommandStore presentationCommands,
        Dispatcher dispatcher,
        IHostDiagnosticSink? diagnostics = null,
        ToolWorkspaceReadModel? tools = null,
        WorkspaceSession? workspace = null,
        DocumentCreationMenuQuery? functions = null,
        WorkspacePaletteActions? workspaceActions = null,
        HostIconRenderer? icons = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(plugins);
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _states = states ?? throw new ArgumentNullException(nameof(states));
        _keyBindings = keyBindings ?? throw new ArgumentNullException(nameof(keyBindings));
        _presentationCommands = presentationCommands ??
            throw new ArgumentNullException(nameof(presentationCommands));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _diagnostics = diagnostics;
        _tools = tools;
        _workspace = workspace;
        _functions = functions;
        _workspaceActions = workspaceActions;
        _icons = icons;

        // Palette v1 没有新增 SDK Contribution。以既有菜单声明作为明确发现许可，既能覆盖
        // G7/G8 的真实命令，又不会把仅供局部或快捷键入口使用的 Catalog 命令自动暴露出来。
        _candidateCommandIds = host.MenuContributions
            .Select(item => item.CommandId)
            .Concat(plugins.MenuCommandContributions.Select(item => item.Descriptor.CommandId))
            .Distinct()
            .OrderBy(item => item.Value, StringComparer.Ordinal)
            .ToArray();

        _states.StateInvalidated += OnStateInvalidated;
        _keyBindings.Changed += OnKeyBindingsChanged;
        if (_tools is not null) _tools.Changed += OnKeyBindingsChanged;
        if (_workspace is not null) _workspace.PagesChanged += OnKeyBindingsChanged;
        if (_functions is not null) _functions.Changed += OnFunctionsChanged;
    }

    public event EventHandler? Changed;

    public IReadOnlyList<WorkbenchCommandPaletteProjectionEntry> GetItems(string? query)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        var normalizedQuery = query?.Trim() ?? string.Empty;
        var shortcuts = BuildShortcutTextByCommand();
        var result = new List<WorkbenchCommandPaletteProjectionEntry>();
        foreach (var commandId in _candidateCommandIds)
        {
            if (!_catalog.TryGet(commandId, out var entry))
            {
                continue;
            }

            var state = _states.Query(commandId).Status;
            if (state is WorkbenchCommandStateStatus.CommandNotFound or
                WorkbenchCommandStateStatus.OwnerUnavailable or
                WorkbenchCommandStateStatus.TargetUnavailable)
            {
                continue;
            }

            var descriptor = entry.Descriptor;
            var rank = WorkbenchTextMatch.Rank(descriptor.DisplayName, normalizedQuery, descriptor.Description, commandId.Value);
            if (rank == int.MaxValue) continue;
            result.Add(new(new CommandPaletteIdentity(commandId), descriptor.DisplayName, descriptor.Description,
                shortcuts.GetValueOrDefault(commandId, string.Empty), state == WorkbenchCommandStateStatus.Enabled,
                _presentationCommands.Get(commandId)) { MatchRank = rank, IconRenderer = _icons });
        }

        if (_tools is not null && _workspaceActions is not null)
        {
            foreach (var tool in _tools.Capture())
            {
                var rank = WorkbenchTextMatch.Rank(tool.DisplayName, normalizedQuery, tool.Description, tool.SourceName, tool.ToolId);
                if (rank == int.MaxValue) continue;
                var identity = new ToolPaletteIdentity(new ToolTypeId(tool.ToolId));
                var binding = new WorkspacePaletteCommand(identity, _workspaceActions);
                result.Add(new(identity, $"工具 · {tool.DisplayName}", $"{tool.SourceName} · {tool.StatusText}", string.Empty,
                    tool.CanOpen, binding) { SearchName = tool.DisplayName, MatchRank = rank,
                    ActionText = tool.IsVisible ? "定位" : "显示", IconRequest = tool.IconRequest, IconRenderer = _icons });
            }
        }
        if (_functions is not null && _workspaceActions is not null)
        {
            foreach (var function in _functions.ReadDirectory().Items)
            {
                var rank = function.MatchRank(normalizedQuery);
                if (rank == int.MaxValue) continue;
                var identity = new FunctionPaletteIdentity(function.Entry.DocumentTypeId, function.Entry.CreationIntentId);
                result.Add(new(identity, function.DisplayName,
                    $"{function.Description} · {function.CategoryPath} · {function.Entry.OwnerId?.Value ?? "内置"}", string.Empty,
                    _workspace?.CanCreateDocuments == true, new WorkspacePaletteCommand(identity, _workspaceActions))
                    { MatchRank = rank, ActionText = "打开新标签", IconRequest = function.IconRequest, IconRenderer = _icons });
            }
        }
        if (_workspace is not null && _workspaceActions is not null)
        {
            foreach (var page in _workspace.GetOpenPages())
            {
                var rank = WorkbenchTextMatch.Rank(page.Title, normalizedQuery, page.FunctionName, page.SourceName);
                if (rank == int.MaxValue) continue;
                var identity = new PagePaletteIdentity(page.Id);
                result.Add(new(identity, page.Title, page.Description, string.Empty, page.CanActivate,
                    new WorkspacePaletteCommand(identity, _workspaceActions)) { MatchRank = rank,
                    ActionText = "切换到页面", IconRequest = page.IconRequest, IconRenderer = _icons });
            }
        }
        return result.OrderBy(item => item.MatchRank).ThenBy(item => item.Identity.Order)
            .ThenBy(item => item.SearchName, StringComparer.Ordinal)
            .ThenBy(item => item.StableKey, StringComparer.Ordinal).ToArray();
    }

    private void OnFunctionsChanged(object? sender, Business.Lifecycle.PluginAvailabilityChangedEventArgs args) => QueueChanged();

    private IReadOnlyDictionary<CommandId, string> BuildShortcutTextByCommand() =>
        _keyBindings.Items
            .GroupBy(item => item.CommandId)
            .ToDictionary(
                group => group.Key,
                group => string.Join(
                    " / ",
                    group.OrderBy(item => item.PlacementId.Value, StringComparer.Ordinal)
                        .Select(item => FormatGesture(item.Key, item.Modifiers))));

    private static string FormatGesture(Key key, KeyModifiers modifiers)
    {
        var parts = new List<string>(5);
        AddModifier(parts, modifiers, KeyModifiers.Control, "Ctrl");
        AddModifier(parts, modifiers, KeyModifiers.Alt, "Alt");
        AddModifier(parts, modifiers, KeyModifiers.Shift, "Shift");
        AddModifier(parts, modifiers, KeyModifiers.Meta, "Meta");
        parts.Add(key.ToString());
        return string.Join('+', parts);
    }

    private static void AddModifier(
        ICollection<string> parts,
        KeyModifiers actual,
        KeyModifiers expected,
        string text)
    {
        if ((actual & expected) != 0)
        {
            parts.Add(text);
        }
    }

    private void OnStateInvalidated(
        object? sender,
        WorkbenchCommandStateInvalidatedEventArgs args) => QueueChanged();

    private void OnKeyBindingsChanged(object? sender, EventArgs args) => QueueChanged();

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

        // 即使通知本来就在 UI 线程，也统一排入 Dispatcher。这样同一输入/状态事务连续发出的
        // 多个失效信号只触发一次 View 刷新；_refreshQueued 是朴素合并标记，不缓存业务状态。
        _dispatcher.Post(PublishChanged, DispatcherPriority.Normal);
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
            // Palette 刷新已经隔离观察者；诊断设施失败不能重新把异常传播到 UI Dispatcher。
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
        _keyBindings.Changed -= OnKeyBindingsChanged;
        if (_tools is not null) _tools.Changed -= OnKeyBindingsChanged;
        if (_workspace is not null) _workspace.PagesChanged -= OnKeyBindingsChanged;
        if (_functions is not null) _functions.Changed -= OnFunctionsChanged;
    }
}
