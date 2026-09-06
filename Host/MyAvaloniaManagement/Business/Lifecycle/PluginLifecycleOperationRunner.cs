using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace MyAvaloniaManagement.Business.Lifecycle;

internal enum PluginLifecycleOperationOutcome { Succeeded, Failed, TimedOut }

internal sealed record PluginLifecycleOperationResult(
    PluginLifecycleOperationOutcome Outcome, TimeSpan Duration, Exception? Exception = null);

/// <summary>拥有真实回调 Task 和取消资源；停止等待不等价于执行结束。</summary>
/// <remarks>
/// Host 令牌只触发快速请求，插件取消回调在被跟踪的任务中执行。两种取消来源使用同一条路径，
/// 因而不会遗漏仍在运行的取消通知。本类不调度生命周期委托，不改变插件开始执行的线程。
/// </remarks>
internal sealed class PluginLifecycleOperation
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly TimeProvider _time;
    private readonly TimeSpan _grace;
    private readonly Action<Exception>? _cancellationFailure;
    private readonly CancellationTokenRegistration _hostRegistration;
    private Task? _cancellationTask;
    private long _cancelledAt;
    private bool _finishing;

    internal PluginLifecycleOperation(CancellationToken hostCancellation, TimeSpan grace,
        TimeProvider time, Action<Exception>? cancellationFailure)
    {
        _time = time;
        _grace = grace;
        _cancellationFailure = cancellationFailure;
        _hostRegistration = hostCancellation.Register(
            static state => ((PluginLifecycleOperation)state!).RequestCancellation(), this);
    }

    internal CancellationToken Token => _cancellation.Token;
    internal Task Execution { get; private set; } = Task.CompletedTask;
    internal Task Completion { get; private set; } = Task.CompletedTask;

    /// <summary>放弃等待前发布真实任务，并安装迟到异常观察和取消资源收尾。</summary>
    internal void Attach(Task execution)
    {
        Execution = execution;
        Completion = FinishAsync();
    }

    internal void RequestCancellation()
    {
        lock (_gate)
        {
            if (_finishing || _cancellationTask is not null) return;
            // 第一次请求固定宽限起点，回滚和重复关闭不得为同一操作续期。
            _cancelledAt = _time.GetTimestamp();
            _cancellationTask = Task.Run(() =>
            {
                try { _cancellation.Cancel(throwOnFirstException: false); }
                catch (Exception exception)
                {
                    // 报告也属于通知任务；报告失败不得形成新的未观察异常。
                    try { _cancellationFailure?.Invoke(exception); }
                    catch { /* 诊断失败不能中断所有权收尾。 */ }
                }
            });
        }
    }

    /// <summary>只等待原预算剩余部分；成功同时证明执行和取消通知均已结束。</summary>
    internal async Task<bool> WaitForCompletionAsync()
    {
        if (Completion.IsCompleted) return true;
        RequestCancellation();
        TimeSpan remaining;
        lock (_gate)
        {
            remaining = _cancellationTask is null ? _grace : _grace - _time.GetElapsedTime(_cancelledAt);
        }
        if (remaining <= TimeSpan.Zero) return Completion.IsCompleted;
        try
        {
            await Completion.WaitAsync(remaining, _time).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException) { return Completion.IsCompleted; }
    }

    private async Task FinishAsync()
    {
        try { await Execution.ConfigureAwait(false); }
        catch { /* 终态由协调器读取，此处观察迟到异常。 */ }
        // 先断开 Host 入口再冻结通知任务。锁外 Dispose，避免与令牌回调互锁。
        _hostRegistration.Dispose();
        Task? notification;
        lock (_gate)
        {
            _finishing = true;
            notification = _cancellationTask;
        }
        if (notification is not null) await notification.ConfigureAwait(false);
        _cancellation.Dispose();
    }
}

/// <summary>执行带期限的回调，不决定插件顺序、可用性和 Provider 释放。</summary>
internal sealed class PluginLifecycleOperationRunner
{
    private readonly TimeSpan _grace;
    private readonly TimeProvider _time;

    internal PluginLifecycleOperationRunner(TimeSpan? grace = null, TimeProvider? time = null)
    {
        _grace = grace ?? TimeSpan.FromSeconds(2);
        _time = time ?? TimeProvider.System;
    }

    internal async Task<PluginLifecycleOperationResult> RunAsync(
        Func<CancellationToken, Task> operation, TimeSpan timeout, CancellationToken hostCancellationToken,
        Action<Exception>? cancellationFailure = null, Action<PluginLifecycleOperation>? track = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        var owned = new PluginLifecycleOperation(hostCancellationToken, _grace, _time, cancellationFailure);
        var stopwatch = Stopwatch.StartNew();
        Task task;
        try
        {
            task = operation(owned.Token) ?? Task.FromException(
                new InvalidOperationException("插件生命周期操作返回了 null Task。"));
        }
        catch (Exception exception) { task = Task.FromException(exception); }
        owned.Attach(task);
        track?.Invoke(owned);
        try
        {
            await task.WaitAsync(timeout, _time, hostCancellationToken).ConfigureAwait(false);
            return new(PluginLifecycleOperationOutcome.Succeeded, stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (hostCancellationToken.IsCancellationRequested)
        {
            owned.RequestCancellation();
            throw;
        }
        catch (TimeoutException exception)
        {
            // 等待期限与插件自身抛出的 TimeoutException 必须区分；不能在定时器触发后
            // 因原任务恰好完成，就把真实超时改写为普通失败。
            if (ReferenceEquals(task.Exception?.InnerException, exception))
                return new(PluginLifecycleOperationOutcome.Failed, stopwatch.Elapsed, exception);
            owned.RequestCancellation();
            return new(PluginLifecycleOperationOutcome.TimedOut, stopwatch.Elapsed);
        }
        catch (Exception exception)
        {
            return new(PluginLifecycleOperationOutcome.Failed, stopwatch.Elapsed, exception);
        }
    }
}
