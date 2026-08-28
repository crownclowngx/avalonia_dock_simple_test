using System;
using MyAvaloniaManagement.Business.Diagnostics;

namespace MyAvaloniaManagement.Business.Commands.Execution;

/// <summary>负责工作台命令关闭时的停止入口、排空判断和超时诊断。</summary>
/// <remarks>
/// 本类型不释放 Workspace、Scope 或 Provider。调用者只有在 <see cref="TryDrain"/> 返回 true 后
/// 才能继续释放可能被命令使用的对象图；超时只保留资源并报告，不强杀同进程代码。
/// </remarks>
internal sealed class WorkbenchCommandShutdownGate(
    IWorkbenchCommandShutdownParticipant participant,
    IHostDiagnosticSink? diagnostics = null)
{
    private readonly IWorkbenchCommandShutdownParticipant _participant =
        participant ?? throw new ArgumentNullException(nameof(participant));

    internal void BeginShutdown() => _participant.BeginShutdown();

    /// <summary>在冻结宽限内等待排空，并把不安全状态转换为可聚合关闭异常。</summary>
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
                "工作台命令在关闭宽限内没有退出；为避免悬空访问，Workspace、Scope 和 Provider 释放已跳过。");
            diagnostics?.Report(new HostDiagnosticDraft(
                HostDiagnosticCodes.WorkbenchCommandShutdownTimeout,
                HostDiagnosticPhase.WorkbenchCommand)
            {
                Exception = failure,
                Duration = _participant.ShutdownGrace,
            });
            return false;
        }
        catch (Exception exception)
        {
            // 无法证明已经排空与明确超时具有相同安全含义，都不能继续释放所有权图。
            failure = exception;
            return false;
        }
    }
}
