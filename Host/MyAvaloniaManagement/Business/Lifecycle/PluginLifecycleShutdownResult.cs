using System.Collections.Generic;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.Business.Lifecycle;

/// <summary>分别表达释放硬约束与保守政策，避免把回调已失败误报为仍在运行。</summary>
internal enum PluginLifecycleRetentionReason
{
    OperationRunning,
    ShutdownFailed,
    ShutdownNotExecuted,
    CheckFailed,
}

internal sealed record PluginLifecycleRetention(
    PluginId PluginId, PluginLifecycleStage Stage, PluginLifecycleRetentionReason Reason);

/// <summary>冻结一次关闭判定；Runtime 只消费结论，不推测插件实现内部的后台任务。</summary>
internal sealed record PluginLifecycleShutdownResult(IReadOnlyList<PluginLifecycleRetention> Retentions)
{
    internal bool CanReleaseProviders => Retentions.Count == 0;
}
