using Avalonia.Threading;

namespace BiliDownloader.Services.Infrastructure;

public interface IUiDispatcher
{
    void Post(Action action);
    Task InvokeAsync(Action action);
    Task InvokeAsync(Func<Task> action);
}

public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public void Post(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    public async Task InvokeAsync(Func<Task> action)
    {
        if (Dispatcher.UIThread.CheckAccess()) await action();
        else await Dispatcher.UIThread.InvokeAsync(action);
    }

    public async Task InvokeAsync(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else await Dispatcher.UIThread.InvokeAsync(action);
    }
}

public sealed class InlineUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
    public Task InvokeAsync(Action action)
    {
        action();
        return Task.CompletedTask;
    }
    public Task InvokeAsync(Func<Task> action) => action();
}
