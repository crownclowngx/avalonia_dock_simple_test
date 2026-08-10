namespace MyAvaloniaManagement.Business.Constants;

/// <summary>
/// 集中定义宿主内建工具在策略、Dock 布局和定位器之间共享的稳定 ID。
/// </summary>
/// <remarks>
/// 稳定 ID 会进入布局快照，必须避免在不同调用位置重复硬编码而产生拼写不一致。
/// </remarks>
public class DockNameConstant
{
    /// <summary>
    /// 工具管理面板的稳定 ID。
    /// </summary>
    public const string ToolManagement = "toolManagement";

    /// <summary>
    /// 插件分组菜单的稳定 ID。
    /// </summary>
    public const string PlugGroupMenu = "plugGroupMenu";

    /// <summary>
    /// 插件状态面板的稳定 ID。
    /// </summary>
    public const string PluginStatus = "pluginStatus";
}
