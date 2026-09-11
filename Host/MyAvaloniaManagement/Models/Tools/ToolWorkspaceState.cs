using MyAvaloniaManagement.Business.Presentation.Icons;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.Models.Tools;

internal enum ToolLayoutState { Hidden, Docked, AutoHidden }

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
    bool CanHide)
{
    public string Description { get; init; } = string.Empty;
    public PluginId? OwnerId { get; init; }
    public string SourceName { get; init; } = "内置";
    public HostIconRequest IconRequest { get; init; } = new(null, string.Empty);
    public ToolLayoutState? LayoutState { get; init; }
    public bool IsActive { get; init; }
    public bool CanOpen { get; init; }
    public string UnavailableReason { get; init; } = string.Empty;
    public string StatusText => UnavailableReason.Length > 0 ? UnavailableReason : LayoutState switch
    {
        ToolLayoutState.Docked => "已停靠",
        ToolLayoutState.AutoHidden => "自动收起",
        ToolLayoutState.Hidden => "已隐藏",
        _ => "尚未就绪"
    };
}
