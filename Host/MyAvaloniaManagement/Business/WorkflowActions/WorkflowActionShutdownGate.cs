using System;
using System.Threading.Tasks;
using MyAvaloniaManagement.Business.Diagnostics;

namespace MyAvaloniaManagement.Business.WorkflowActions;

/// <summary>定义关闭门控实际需要的最小运行端口。</summary>
/// <remarks>
/// 该端口刻意不暴露 Run、目录或 Provider：关闭策略只应知道如何停止入口、宽限多久以及
/// Handler 是否已经排空。这样组合根不依赖执行器内部状态，测试也无需构造完整插件容器。
/// </remarks>
internal interface IWorkflowActionShutdownParticipant
{
    TimeSpan ShutdownGrace { get; }

    void BeginShutdown();

    Task<bool> WaitForDrainAsync(TimeSpan timeout);
}

/// <summary>负责 Workflow Action 关闭的停止入口、排空判断和超时诊断。</summary>
/// <remarks>
/// 本类型不释放 Lifecycle 或 Provider，也不强杀仍在运行的 Handler。调用者只有在
/// <see cref="TryDrain"/> 返回 <see langword="true"/> 后，才可以继续释放插件所有权图。
/// 将这一决定集中在窄门控内，是为了让安全条件只有一个实现位置，而不是把同一判断散落在
/// HostRuntime、ProviderOwner 和 LifecycleCoordinator 中。
/// </remarks>
internal sealed class WorkflowActionShutdownGate(
    IWorkflowActionShutdownParticipant participant,
    IHostDiagnosticSink? diagnostics = null)
{
    private readonly IWorkflowActionShutdownParticipant _participant =
        participant ?? throw new ArgumentNullException(nameof(participant));

    /// <summary>同步关闭新 Run/调用入口并向所有在途 Run 传播取消。</summary>
    internal void BeginShutdown() => _participant.BeginShutdown();

    /// <summary>在冻结宽限内等待 Handler 排空，并把失败转换为可聚合的关闭异常。</summary>
    /// <param name="failure">失败时返回应由 HostRuntime 汇总的异常；成功时为 null。</param>
    /// <returns>只有确认全部 Handler 已退出时才返回 true。</returns>
    internal bool TryDrain(out Exception? failure)
    {
        try
        {
            if (_participant.WaitForDrainAsync(_participant.ShutdownGrace)
                .GetAwaiter().GetResult())
            {
                failure = null;
                return true;
            }

            failure = new TimeoutException(
                "Workflow Action 在关闭宽限内没有退出；为避免释放仍在使用的 Provider，后续释放已跳过。");
            diagnostics?.Report(new HostDiagnosticDraft(
                HostDiagnosticCodes.WorkflowActionShutdownTimeout,
                HostDiagnosticPhase.WorkflowAction)
            {
                Exception = failure,
                Duration = _participant.ShutdownGrace,
            });
            return false;
        }
        catch (Exception exception)
        {
            // 无法证明已经排空与明确超时具有相同安全含义：都必须保留 Provider。
            failure = exception;
            return false;
        }
    }
}
