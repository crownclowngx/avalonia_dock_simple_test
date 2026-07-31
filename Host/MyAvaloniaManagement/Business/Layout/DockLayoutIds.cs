namespace MyAvaloniaManagement.Business.Layout;

/// <summary>
/// 布局文件依赖这些稳定 ID；修改它们等同于升级快照架构。
/// </summary>
internal static class DockLayoutIds
{
    public const string Root = "Root";
    public const string Workspace = "Workspace";
    public const string WorkspaceColumns = "WorkspaceColumns";
    public const string WorkspaceRows = "WorkspaceCenterRows";
    public const string WorkspaceCenterRows = WorkspaceRows;
    public const string LeftPane = "LeftPane";
    public const string LeftTools = "LeftTools";
    public const string TopPane = "TopPane";
    public const string TopTools = "TopTools";
    public const string Documents = "Documents";
    public const string BottomPane = "BottomPane";
    public const string BottomTools = "BottomTools";
    public const string RightPane = "RightPane";
    public const string RightTools = "RightTools";

    public static readonly string[] PersistedPaneIds =
    [
        LeftPane,
        TopPane,
        BottomPane,
        RightPane
    ];

    public static readonly string[] ToolDockIds =
    [
        LeftTools,
        TopTools,
        BottomTools,
        RightTools
    ];

    public static bool IsToolDockId(string? id) =>
        id is LeftTools or TopTools or BottomTools or RightTools;
}
