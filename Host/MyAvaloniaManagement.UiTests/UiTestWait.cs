using System.Diagnostics;
using Avalonia.Headless;
using Avalonia.Threading;

namespace MyAvaloniaManagement.UiTests;

/// <summary>
/// Headless 测试的三种明确等待：处理已排队通知、推进渲染帧、观察业务/布局条件。
/// 排队通知完成不代表后台业务结束；业务完成优先直接等待其 Task，条件轮询仅用于没有完成信号的框架状态。
/// </summary>
internal static class UiTestWait
{
    internal static Task DrainAsync() => Dispatcher.UIThread.InvokeAsync(
        () => { }, DispatcherPriority.Background).GetTask();

    /// <summary>推进真实 Headless 渲染时钟，不用固定休眠猜测模板或一帧是否已经完成。</summary>
    internal static async Task RenderAsync()
    {
        await DrainAsync();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        await DrainAsync();
    }

    /// <summary>
    /// 只观察条件，调用者必须在外部发送一次动作。异常直接传播，不重试断言或业务操作。
    /// 短轮询让出 Dispatcher；截止时间只界定失败上限，不作为成功依据。
    /// </summary>
    internal static async Task UntilAsync(Func<bool> ready, string description, TimeSpan? timeout = null)
    {
        var watch = Stopwatch.StartNew();
        var limit = timeout ?? TimeSpan.FromSeconds(2);
        while (!ready())
        {
            if (watch.Elapsed >= limit) throw new TimeoutException($"Headless 等待超时：{description}；最后一次条件仍为 false。");
            await Task.Delay(10);
            await DrainAsync();
        }
    }
}
