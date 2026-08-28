namespace MyAvaloniaManagement.Business.Commands.Execution;

/// <summary>描述一次工作台命令调用在 Host 边界得到的稳定结果。</summary>
internal enum WorkbenchCommandExecutionStatus
{
    Succeeded,
    CommandNotFound,
    OwnerUnavailable,
    TargetUnavailable,
    RejectedDuringShutdown,
    Canceled,
    Failed,
}

/// <summary>工作台命令的不可变执行结果。</summary>
/// <remarks>
/// 结果不携带 Exception、路径或插件正文。当前 Host 打开/保存的业务错误继续写入
/// DocumentOperationState；这里的用户说明只处理 Executor 自身的稳定失败语义。
/// </remarks>
internal readonly record struct WorkbenchCommandExecutionResult(
    WorkbenchCommandExecutionStatus Status,
    string UserMessage)
{
    internal static WorkbenchCommandExecutionResult FromStatus(
        WorkbenchCommandExecutionStatus status) => new(status, string.Empty);

    internal static WorkbenchCommandExecutionResult Failure => new(
        WorkbenchCommandExecutionStatus.Failed,
        "工作台命令执行失败；异常正文未写入诊断。");
}
