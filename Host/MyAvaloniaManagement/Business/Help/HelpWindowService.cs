using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using MyAvaloniaManagement.Business.Commands.Context;
using MyAvaloniaManagement.Business.Commands.Execution;
using MyAvaloniaManagement.Views.Help;

namespace MyAvaloniaManagement.Business.Help;

/// <summary>只拥有当前 Runtime 的一个帮助窗口；构造期间不创建浏览器或读取文章。</summary>
internal sealed class HelpWindowService(HelpContentCatalog catalog, HelpReadingStateStore store,
    Func<IHelpReader> createReader) : IDisposable
{
    private HelpWindow? _window;
    private Window? _mainWindow;
    private bool _disposed;
    internal HelpWindow? CurrentWindow => _window;

    internal void Attach(Window mainWindow)
    {
        if (_mainWindow is not null) throw new InvalidOperationException("Help desktop already attached.");
        _mainWindow = mainWindow;
        mainWindow.Closed += MainClosed;
    }

    internal void ShowOrActivate()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_disposed) return;
        if (_window is null)
        {
            var reader = createReader();
            try
            {
                _window = new HelpWindow(catalog, store.Load(), reader);
                _window.Closed += HelpClosed;
                _window.Show();
            }
            catch { reader.Dispose(); _window = null; throw; }
        }
        else if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void HelpClosed(object? sender, EventArgs args)
    {
        if (sender is not HelpWindow window) return;
        window.Closed -= HelpClosed;
        store.Save(window.ReadingState);
        if (ReferenceEquals(_window, window)) _window = null;
    }

    private void MainClosed(object? sender, EventArgs args) => Dispose();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_mainWindow is not null) { _mainWindow.Closed -= MainClosed; _mainWindow = null; }
        if (_window is null) return;
        if (Dispatcher.UIThread.CheckAccess()) _window.Close();
        else Dispatcher.UIThread.Post(() => _window?.Close());
    }
}

internal sealed class HostOpenHelpCommandHandler(HelpWindowService windows) : IHostWorkbenchCommandHandler
{
    public bool CanExecute(WorkbenchContextSnapshot context) => true;
    public async ValueTask ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Dispatcher.UIThread.InvokeAsync(windows.ShowOrActivate);
    }
}
