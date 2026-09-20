using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using MyAvaloniaManagement.Business.Commands.Catalog;
using MyAvaloniaManagement.Business.Commands.Execution;
using MyAvaloniaManagement.Business.Commands.State;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.Business.Presentation.Commands;

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
