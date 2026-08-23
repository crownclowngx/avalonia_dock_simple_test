namespace MyAvaloniaManagement.Models.Tools;

/// <summary>
/// 表示 Tool 管理界面可以读取的一项不可变工作区状态。
/// </summary>
/// <remarks>
/// 该记录刻意不包含 Root Dock、Dock Tool、Owner 或可变字典。ViewModel 只消费用户真正需要的
/// 展示事实，Dock 树遍历和 Tool 实例所有权始终留在 Host internal Workspace 边界。
/// </remarks>
internal sealed record ToolWorkspaceState(
    string ToolId,
    string DisplayName,
    bool IsVisible,
    bool CanHide);
