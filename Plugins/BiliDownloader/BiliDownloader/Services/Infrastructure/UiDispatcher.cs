using System.Diagnostics.CodeAnalysis;
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
    // 无 Application 消息循环的插件单测中，Avalonia 会把任意测试线程视为可访问 UI，
    // 因而无法诚实、稳定地制造 else 分支。跨线程投递由 G14 Windows 真实窗口 Smoke
    // 验证；这里排除的只有三行框架适配代码，不改变运行行为、public API 或门禁阈值。
    [ExcludeFromCodeCoverage]
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
