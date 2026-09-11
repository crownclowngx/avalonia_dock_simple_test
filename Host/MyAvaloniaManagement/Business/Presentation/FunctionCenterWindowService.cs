using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using MyAvaloniaManagement.Business.Commands.Context;
using MyAvaloniaManagement.Business.Commands.Execution;
using MyAvaloniaManagement.Business.Documents;
using MyAvaloniaManagement.Business.Workspace;
using MyAvaloniaManagement.ViewModels.FunctionCenter;
using MyAvaloniaManagement.Views.FunctionCenter;
using MyAvaloniaManagement.Business.Presentation.Icons;

namespace MyAvaloniaManagement.Business.Presentation;

/// <summary>当前 Runtime 内功能选择窗口的唯一所有者；一次打开建立一次独立会话。</summary>
/// <remarks>
/// 显示命令不等待用户选择，避免把模态窗口存活期算作待排空的工作台命令。
/// 服务显式持有构造会话所需的具体依赖，不在运行时从 Provider 查找服务。
/// </remarks>
internal sealed class FunctionCenterWindowService(
    DocumentCreationMenuQuery query,
    DocumentPersistenceCoordinator documents,
    DocumentOperationState operationState,
    HostIconRenderer icons) : IDisposable
{
    private Window? _owner;
    private FunctionCenterWindow? _window;
    private FunctionCenterViewModel? _viewModel;
    private bool _disposed;
    internal FunctionCenterWindow? CurrentWindow => _window;

    internal void Attach(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (_owner is not null) throw new InvalidOperationException("功能中心已经绑定主窗口。");
        _owner = owner;
        owner.Closing += OnOwnerClosing;
        owner.Closed += OnOwnerClosed;
    }

    internal void ShowOrActivate()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_disposed || _owner is not { IsVisible: true }) return;
        if (_window is not null) { _window.Activate(); return; }
        var viewModel = new FunctionCenterViewModel(query, documents, operationState, icons);
        try
        {
            var window = new FunctionCenterWindow { DataContext = viewModel };
            _viewModel = viewModel;
            _window = window;
            viewModel.Created += OnCreated;
            viewModel.PropertyChanged += OnSessionChanged;
            window.Closed += OnWindowClosed;
            _ = ObserveDialogAsync(window.ShowDialog(_owner));
        }
        catch { viewModel.Dispose(); ReleaseWindow(); throw; }
    }

    private static async Task ObserveDialogAsync(Task completion)
    {
        try { await completion; }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FunctionCenter errorCode=FUNCTION_WINDOW_FAILED type={exception.GetType().Name}");
        }
    }

    private void OnCreated(object? sender, EventArgs args)
    {
        _window?.Close();
        _owner?.Activate();
    }

    private void OnWindowClosed(object? sender, EventArgs args) => ReleaseWindow();
    private void OnOwnerClosing(object? sender, WindowClosingEventArgs args)
    {
        if (_viewModel is { IsBusy: true }) args.Cancel = true;
    }
    private void OnOwnerClosed(object? sender, EventArgs args) => Dispose();

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs args)
    {
        // 释放请求不能让被 Closing 拒绝的窗口变成失去引用的孤儿。
        // 暂存会话直至创建返回，再关闭并退订；Runtime 的文档排空门负责依赖安全。
        if (_disposed && args.PropertyName == nameof(FunctionCenterViewModel.IsBusy) && _viewModel is { IsBusy: false })
            CloseWindow();
    }

    private void ReleaseWindow()
    {
        if (_window is not null) { _window.Closed -= OnWindowClosed; _window.DataContext = null; }
        if (_viewModel is not null)
        {
            _viewModel.Created -= OnCreated;
            _viewModel.PropertyChanged -= OnSessionChanged;
            _viewModel.Dispose();
        }
        _window = null;
        _viewModel = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        void CloseWhenIdle()
        {
            if (_owner is not null)
            {
                _owner.Closing -= OnOwnerClosing;
                _owner.Closed -= OnOwnerClosed;
                _owner = null;
            }
            if (_viewModel is not { IsBusy: true }) CloseWindow();
        }
        if (Dispatcher.UIThread.CheckAccess()) CloseWhenIdle();
        else Dispatcher.UIThread.Post(CloseWhenIdle);
    }

    private void CloseWindow() { _window?.Close(); ReleaseWindow(); }
}

/// <summary>将文件菜单的新建意图适配到 Host 选择窗口，不要求存在活动 Document。</summary>
internal sealed class HostNewDocumentCommandHandler(FunctionCenterWindowService windows) : IHostWorkbenchCommandHandler
{
    public bool CanExecute(WorkbenchContextSnapshot context) => true;
    public async ValueTask ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Dispatcher.UIThread.InvokeAsync(windows.ShowOrActivate);
    }
}
