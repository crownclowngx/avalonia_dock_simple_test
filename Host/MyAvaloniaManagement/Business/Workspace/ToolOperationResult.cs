using System.Collections.Generic;
using System.Linq;

namespace MyAvaloniaManagement.Business.Workspace;

internal enum ToolOperationStatus { Changed, AlreadySatisfied, Unavailable, NotFound, NotReady, Failed }

/// <summary>区分没有变化与失败，避免 UI 根据 bool 取反而显示虚假的工作区状态。</summary>
internal sealed record ToolOperationResult(ToolOperationStatus Status, string Message = "")
{
    public bool Succeeded => Status is ToolOperationStatus.Changed or ToolOperationStatus.AlreadySatisfied;
}

internal sealed record ToolBatchResult(IReadOnlyDictionary<string, ToolOperationResult> Results)
{
    public int FailureCount => Results.Values.Count(result => !result.Succeeded);
}
