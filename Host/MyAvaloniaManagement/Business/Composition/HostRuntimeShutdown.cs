using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MyAvaloniaManagement.Business.Commands.Execution;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.Business.WorkflowActions;

namespace MyAvaloniaManagement.Business.Composition;

/// <summary>
/// 正常退出与启动回滚共用的所有权释放流程。只消费已创建参与者与资源释放端口，不解析 DI。
/// Command/Workflow 负责证明自己的调用已排空，Lifecycle 负责证明回调与取消通知已结束；
/// 本类只组合这些结论，并独占 Provider 的最终释放或保留选择。
/// </summary>
internal sealed class HostRuntimeShutdown(
    IDisposable pluginProviders, IDisposable hostProvider, Action closeDocumentScopes,
    HostShutdownParticipants participants, HostResourceRetention retention, IHostDiagnosticSink? diagnostics)
{
    private readonly object _gate = new();
    private Task<HostRuntimeShutdownResult>? _completion;

    /// <summary>先发布占位任务再执行关闭；并发调用共享结果，绝不在内部锁中运行插件清理。</summary>
    internal Task<HostRuntimeShutdownResult> RunAsync()
    {
        TaskCompletionSource<HostRuntimeShutdownResult> source;
        lock (_gate)
        {
            if (_completion is not null) return _completion;
            source = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _completion = source.Task;
        }
        _ = CompleteAsync(source);
        return source.Task;
    }

    private async Task CompleteAsync(TaskCompletionSource<HostRuntimeShutdownResult> source)
    {
        var failures = new List<Exception>();
        var snapshot = participants.Freeze();
        var safe = true;
        PluginLifecycleShutdownResult? lifecycleResult = null;
        try
        {
            // 每个入口都要尝试关闭。某个入口失败不能阻止其他入口撤销，但必须阻止 Provider 释放。
            safe &= Try(() => snapshot.Workflow?.BeginShutdown(), failures);
            safe &= Try(() => snapshot.Commands?.BeginShutdown(), failures);
            safe &= Try(() => snapshot.States?.BeginShutdown(), failures);
            safe &= Try(() => snapshot.Workspace?.BeginShutdown(), failures);
            var entrancesClosed = safe;

            var commandsDrained = true;
            if (snapshot.Commands is not null)
            {
                var gate = new WorkbenchCommandShutdownGate(snapshot.Commands, diagnostics);
                commandsDrained = gate.TryDrain(out var failure);
                if (failure is not null) failures.Add(failure);
            }
            if (safe && commandsDrained)
            {
                // 保持现有 UI/View→Scope 顺序，某个 UI 清理失败仍尝试 Scope 兜底。
                safe &= Try(() => snapshot.Workspace?.Dispose(), failures);
                safe &= Try(closeDocumentScopes, failures);
            }

            var workflowDrained = true;
            if (snapshot.Workflow is not null)
            {
                var gate = new WorkflowActionShutdownGate(snapshot.Workflow, diagnostics);
                workflowDrained = gate.TryDrain(out var failure);
                if (failure is not null) failures.Add(failure);
            }
            // 清理异常不妨碍其余可安全生命周期获得关闭机会；仍在执行的业务调用则禁止 Lifecycle 关闭。
            if (entrancesClosed && commandsDrained && workflowDrained && snapshot.Lifecycles is not null)
            {
                lifecycleResult = await snapshot.Lifecycles.ShutdownAllAsync().ConfigureAwait(false);
                if (!lifecycleResult.CanReleaseProviders)
                    failures.Add(new InvalidOperationException("生命周期未完成安全关闭，Provider 已保留。"));
            }
            safe &= commandsDrained && workflowDrained && (lifecycleResult?.CanReleaseProviders ?? true);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
            safe = false;
        }

        // 在释放或交接前关闭迟到诊断出口，保证 Program 随后结束日志会话不会产生释放后访问。
        snapshot.Lifecycles?.CloseDiagnostics();
        if (safe)
        {
            Try(pluginProviders.Dispose, failures);
            Try(hostProvider.Dispose, failures);
        }
        else
        {
            retention.Retain(this);
        }
        source.TrySetResult(new HostRuntimeShutdownResult(!safe, failures.ToArray(), lifecycleResult));
    }

    private static bool Try(Action action, List<Exception> failures)
    {
        try { action(); return true; }
        catch (Exception exception) { failures.Add(exception); return false; }
    }
}

internal sealed record HostRuntimeShutdownResult(
    bool ResourcesRetained, IReadOnlyList<Exception> Failures, PluginLifecycleShutdownResult? Lifecycles);
