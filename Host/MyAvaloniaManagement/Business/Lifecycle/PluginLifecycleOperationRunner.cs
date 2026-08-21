using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace MyAvaloniaManagement.Business.Lifecycle;

internal enum PluginLifecycleOperationOutcome
{
    Succeeded,
    Failed,
    TimedOut,
}

internal sealed record PluginLifecycleOperationResult(
    PluginLifecycleOperationOutcome Outcome,
    TimeSpan Duration,
    Exception? Exception = null);

/// <summary>只负责执行一个带期限的插件回调，不决定插件顺序、状态或可用性。</summary>
internal sealed class PluginLifecycleOperationRunner
{
    internal async Task<PluginLifecycleOperationResult> RunAsync(
        Func<CancellationToken, Task> operation,
        TimeSpan timeout,
        CancellationToken hostCancellationToken,
        Action<Exception>? cancellationFailure = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        var timeoutCancellation = new CancellationTokenSource();
        var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            hostCancellationToken,
            timeoutCancellation.Token);
        var stopwatch = Stopwatch.StartNew();
        Task operationTask;
        try
        {
            operationTask = operation(linkedCancellation.Token)
                ?? Task.FromException(new InvalidOperationException(
                    "插件生命周期操作返回了 null Task。"));
        }
        catch (OperationCanceledException) when (hostCancellationToken.IsCancellationRequested)
        {
            linkedCancellation.Dispose();
            timeoutCancellation.Dispose();
            throw;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            linkedCancellation.Dispose();
            timeoutCancellation.Dispose();
            return new PluginLifecycleOperationResult(
                PluginLifecycleOperationOutcome.Failed,
                stopwatch.Elapsed,
                exception);
        }

        try
        {
            await operationTask.WaitAsync(timeout, hostCancellationToken)
                .ConfigureAwait(false);
            stopwatch.Stop();
            linkedCancellation.Dispose();
            timeoutCancellation.Dispose();
            return new PluginLifecycleOperationResult(
                PluginLifecycleOperationOutcome.Succeeded,
                stopwatch.Elapsed);
        }
        catch (TimeoutException)
        {
            stopwatch.Stop();
            ObserveLateFault(operationTask);
            RequestCancellationWithoutBlocking(
                timeoutCancellation,
                linkedCancellation,
                cancellationFailure);
            return new PluginLifecycleOperationResult(
                PluginLifecycleOperationOutcome.TimedOut,
                stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (hostCancellationToken.IsCancellationRequested)
        {
            ObserveLateFault(operationTask);
            linkedCancellation.Dispose();
            timeoutCancellation.Dispose();
            throw;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            linkedCancellation.Dispose();
            timeoutCancellation.Dispose();
            return new PluginLifecycleOperationResult(
                PluginLifecycleOperationOutcome.Failed,
                stopwatch.Elapsed,
                exception);
        }
    }

    private static void RequestCancellationWithoutBlocking(
        CancellationTokenSource timeoutCancellation,
        CancellationTokenSource linkedCancellation,
        Action<Exception>? cancellationFailure)
    {
        _ = Task.Run(() =>
        {
            try
            {
                timeoutCancellation.Cancel(throwOnFirstException: false);
            }
            catch (Exception exception)
            {
                cancellationFailure?.Invoke(exception);
            }
            finally
            {
                linkedCancellation.Dispose();
                timeoutCancellation.Dispose();
            }
        });
    }

    private static void ObserveLateFault(Task operationTask)
    {
        _ = operationTask.ContinueWith(
            static task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously |
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }
}
