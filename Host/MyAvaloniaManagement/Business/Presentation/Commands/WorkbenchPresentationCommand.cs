using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using MyAvaloniaManagement.Business.Commands.Execution;
using MyAvaloniaManagement.Business.Commands.State;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.Business.Presentation.Commands;

/// <summary>定义主窗口 XAML 消费的最小工作台命令展示端口。</summary>
/// <remarks>
/// 该端口只暴露 Avalonia 能绑定的 <see cref="ICommand"/>，不向 ViewModel 或设计数据泄漏
/// Executor、Context、Provider 或 Document。生产展示模型与纯内存设计样例是两个真实实现，
/// 因而这是一条实际替换边界，而不是为单个实现制造的形式抽象。
/// </remarks>
internal interface IWorkbenchCommandPresentationBindings
{
    /// <summary>获取 Host 与插件共享的声明式菜单投影。</summary>
    IWorkbenchMenuProjection Menu { get; }

    /// <summary>获取已经完成 Host 保留项和插件冲突治理的快捷键投影。</summary>
    IWorkbenchKeyBindingProjection KeyBindings { get; }
}

/// <summary>为 XAML 同时提供执行入口和显式 Enabled 投影的窄命令端口。</summary>
/// <remarks>
/// Avalonia <c>MenuItem</c> 会在触发时检查 <see cref="ICommand.CanExecute"/>，但不会在所有
/// 挂载时序下把初始结果写入 <c>IsEnabled</c>。显式只读属性保证菜单视觉状态确定；生产实现仍在
/// 每次读取时查询统一 State Query，属性本身不保存第二份业务状态。
/// </remarks>
internal interface IWorkbenchPresentationCommandBinding : ICommand, INotifyPropertyChanged
{
    /// <summary>获取当前命令是否可由展示控件触发。</summary>
    bool IsEnabled { get; }
}

/// <summary>把稳定 <see cref="CommandId"/> 适配为 Avalonia 可绑定命令。</summary>
/// <remarks>
/// 本类型不缓存业务可用性；每次 <see cref="CanExecute"/> 都查询统一 State Query，执行则始终进入
/// Executor 的最终重查。状态事件只表达“应重新查询”，工作线程通知会被切回构造时显式注入的
/// UI Dispatcher。适配器不增加单飞、重试、队列或第二套业务状态。
/// </remarks>
internal sealed class WorkbenchPresentationCommand :
    IWorkbenchPresentationCommandBinding,
    IDisposable
{
    private readonly object _gate = new();
    private readonly WorkbenchCommandStateQuery _states;
    private readonly WorkbenchCommandExecutor _executor;
    private readonly Dispatcher _dispatcher;
    private readonly IHostDiagnosticSink? _diagnostics;
    private bool _refreshQueued;
    private bool _disposed;

    internal WorkbenchPresentationCommand(
        CommandId commandId,
        WorkbenchCommandStateQuery states,
        WorkbenchCommandExecutor executor,
        Dispatcher dispatcher,
        IHostDiagnosticSink? diagnostics = null)
    {
        CommandId = commandId ?? throw new ArgumentNullException(nameof(commandId));
        _states = states ?? throw new ArgumentNullException(nameof(states));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _diagnostics = diagnostics;
        _states.StateInvalidated += OnStateInvalidated;
    }

    /// <summary>获取此展示命令映射的稳定工作台身份。</summary>
    internal CommandId CommandId { get; }

    /// <summary>当 Avalonia 应重新查询当前可用性时发生。</summary>
    public event EventHandler? CanExecuteChanged;

    /// <summary>当显式 Enabled 展示属性需要重新读取时发生。</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>获取实时工作台状态，不缓存此前查询结果。</summary>
    public bool IsEnabled => CanExecute(null);

    /// <summary>从统一状态查询读取当前可用性，不相信此前 UI 缓存的结果。</summary>
    public bool CanExecute(object? parameter)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }
        }

        try
        {
            return _states.Query(CommandId).Status == WorkbenchCommandStateStatus.Enabled;
        }
        catch (Exception exception)
        {
            // State Query 已隔离正常的插件状态异常；这里仍封闭意外的 Host internal 异常，
            // 避免 Avalonia 在读取 IsEnabled 时把异常传播到 UI Dispatcher。
            ReportUnexpectedFailure(exception);
            return false;
        }
    }

    /// <summary>从 XAML 入口启动一个内部已观察的异步执行。</summary>
    public void Execute(object? parameter)
    {
        _ = ExecuteObservedAsync();
    }

    /// <summary>
    /// 通过统一 Executor 执行当前 CommandId，供 Headless UI 和内部协调代码确定地等待真实结果。
    /// </summary>
    /// <remarks>
    /// 本方法不先相信 <see cref="CanExecute"/> 的展示结果；Executor 会重新捕获 Catalog、Context、
    /// 当前实例和 CanExecute，因而菜单显示与用户真正触发之间发生的目标切换不会误保存旧 Document。
    /// </remarks>
    internal ValueTask<WorkbenchCommandExecutionResult> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return ValueTask.FromResult(
                    WorkbenchCommandExecutionResult.FromStatus(
                        WorkbenchCommandExecutionStatus.CommandDisabled));
            }
        }

        return _executor.ExecuteAsync(CommandId, cancellationToken);
    }

    private async Task ExecuteObservedAsync()
    {
        try
        {
            // ICommand 的同步签名无法返回 Task。这里真实等待 Executor，并在同一方法内观察
            // 所有意外异常，绝不把未观察 Task 或 async void 异常交还 Avalonia。
            _ = await ExecuteAsync();
        }
        catch (Exception exception)
        {
            ReportUnexpectedFailure(exception);
        }
    }

    private void OnStateInvalidated(
        object? sender,
        WorkbenchCommandStateInvalidatedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (!args.IsFullRefresh && args.CommandId != CommandId)
        {
            return;
        }

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
            PublishCanExecuteChanged();
            return;
        }

        // Dispatcher 队列可能晚于 HostRuntime Dispose 执行；回调会再次检查 disposed，
        // 因而不会让已释放的窗口绑定或根容器重新收到状态变化。
        _dispatcher.Post(PublishCanExecuteChanged, DispatcherPriority.Normal);
    }

    private void PublishCanExecuteChanged()
    {
        Delegate[] commandHandlers;
        Delegate[] propertyHandlers;
        lock (_gate)
        {
            _refreshQueued = false;
            if (_disposed)
            {
                return;
            }
            commandHandlers = CanExecuteChanged?.GetInvocationList() ?? [];
            propertyHandlers = PropertyChanged?.GetInvocationList() ?? [];
        }

        foreach (EventHandler handler in commandHandlers)
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch (Exception exception)
            {
                // 一个异常观察者不能阻断同一 Command 的其他 Menu/KeyBinding 投影刷新。
                ReportUnexpectedFailure(exception);
            }
        }
        foreach (PropertyChangedEventHandler handler in propertyHandlers)
        {
            try
            {
                handler(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
            }
            catch (Exception exception)
            {
                ReportUnexpectedFailure(exception);
            }
        }
    }

    private void ReportUnexpectedFailure(Exception exception)
    {
        try
        {
            _diagnostics?.Report(new HostDiagnosticDraft(
                HostDiagnosticCodes.WorkbenchCommandExecutionFailed,
                HostDiagnosticPhase.WorkbenchCommand)
            {
                StableId = CommandId.Value,
                Exception = exception,
            });
        }
        catch
        {
            // 诊断端口本身失败时也不能从 ICommand 的 UI 边界继续抛出。
        }
    }

    /// <summary>退订统一状态源，并使已经排队的迟到刷新失效。</summary>
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
            CanExecuteChanged = null;
            PropertyChanged = null;
        }
        _states.StateInvalidated -= OnStateInvalidated;
    }
}
