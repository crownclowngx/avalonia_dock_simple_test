using MyAvaloniaManagement.Business.Workspace;

namespace MyAvaloniaManagement.Business.Commands.Context;

/// <summary>命令面板显示时捕获的目标值；页面身份与上下文代次共同阻止同名替换和切走再切回的旧提交。</summary>
/// <remarks>不持有 Document 或 Target，不进入 SDK；菜单与普通快捷键无需提供此约束。</remarks>
internal sealed record WorkbenchCommandTargetExpectation(WorkspacePageId PageId, long ContextRevision)
{
    internal bool Matches(WorkbenchContextCapture capture) =>
        capture.Document?.PageId == PageId && capture.Snapshot.Revision == ContextRevision;
}
