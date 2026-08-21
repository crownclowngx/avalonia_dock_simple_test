using System.Diagnostics;

namespace MyAvaloniaManagementCommon.Plugin;

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

/// <summary>
/// 执行一个有宿主期限的生命周期操作，并隔离插件异常。
/// </summary>
internal sealed class PluginLifecycleOperationRunner
{
    internal async Task<PluginLifecycleOperationResult> RunAsync(
        Func<CancellationToken, Task> operation,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var timeoutCancellation = new CancellationTokenSource();
        var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellation.Token);
        var stopwatch = Stopwatch.StartNew();

        Task operationTask;
        try
        {
            operationTask = operation(linkedCancellation.Token)
                ?? Task.FromException(new InvalidOperationException(
                    "插件生命周期操作返回了 null Task。"));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
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
            await operationTask.WaitAsync(timeout, cancellationToken)
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
            RequestCancellationWithoutBlocking(
                timeoutCancellation,
                linkedCancellation);
            ObserveLateFault(operationTask);
            return new PluginLifecycleOperationResult(
                PluginLifecycleOperationOutcome.TimedOut,
                stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
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
        CancellationTokenSource linkedCancellation)
    {
        _ = Task.Run(() =>
        {
            try
            {
                timeoutCancellation.Cancel(throwOnFirstException: false);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(
                    $"PluginLifecycle errorCode=LIFECYCLE_CANCELLATION_CALLBACK_FAILED " +
                    $"type={exception.GetType().Name}");
                PluginSensitiveDiagnosticDebugOutput.Write(
                    "LIFECYCLE_CANCELLATION_CALLBACK_FAILED",
                    exception);
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
