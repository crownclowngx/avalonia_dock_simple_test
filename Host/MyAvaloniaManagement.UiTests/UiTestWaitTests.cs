using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Xunit;

namespace MyAvaloniaManagement.UiTests;

public sealed class UiTestWaitTests
{
    [AvaloniaFact]
    public async Task 已满足条件同步完成且只观察一次()
    {
        var calls = 0;
        var pending = UiTestWait.UntilAsync(() => { calls++; return true; }, "已就绪");
        Assert.True(pending.IsCompletedSuccessfully);
        await pending;
        Assert.Equal(1, calls);
    }

    [AvaloniaFact]
    public async Task 等待队列完成不重复发送业务动作()
    {
        var actions = 0;
        Dispatcher.UIThread.Post(() => actions++);
        await UiTestWait.UntilAsync(() => actions == 1, "一次输入");
        await UiTestWait.DrainAsync();
        Assert.Equal(1, actions);
    }

    [AvaloniaFact]
    public async Task 超时包含场景且观察异常原样传播()
    {
        var error = await Assert.ThrowsAsync<TimeoutException>(() =>
            UiTestWait.UntilAsync(() => false, "缺少关闭按钮", TimeSpan.Zero));
        Assert.Contains("缺少关闭按钮", error.Message);
        var expected = new InvalidOperationException("观察错误");
        Assert.Same(expected, await Assert.ThrowsAsync<InvalidOperationException>(() =>
            UiTestWait.UntilAsync(() => throw expected, "异常不重试")));
    }
}
