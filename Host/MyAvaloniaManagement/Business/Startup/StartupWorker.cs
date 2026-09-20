using System;
using System.Threading;
using System.Threading.Tasks;

namespace MyAvaloniaManagement.Business.Startup;

/// <summary>
/// 在专属后台线程执行非 UI 启动。Windows 上保留旧入口最初的 STA 语义，避免简单 Task.Run
/// 将模块构造和 Configure 悄悄改为 MTA；此线程不创建 Avalonia 控件，也不提供 COM 消息泵。
/// 异步回调首次挂起后的线程选择仍遵守原有 ConfigureAwait(false) 契约。
/// </summary>
internal static class StartupWorker
{
    internal static Task<T> RunAsync<T>(Func<Task<T>> work)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.TrySetResult(work().GetAwaiter().GetResult()); }
            catch (OperationCanceledException exception) { completion.TrySetCanceled(exception.CancellationToken); }
            catch (Exception exception) { completion.TrySetException(exception); }
        }) { IsBackground = true, Name = "Host startup" };
        if (OperatingSystem.IsWindows()) thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}
