namespace MyAvaloniaManagement.Business.Constants;

/// <summary>已经从工作区退出的内置 Tool 身份，只供布局与偏好迁移使用。</summary>
/// <remarks>
/// 使用明确白名单，区分“宿主主动退役”和“用户暂时没有安装某个插件”。
/// 后者的布局、收藏和最近使用仍需保留，不能借清理旧内置项一并丢弃。
/// </remarks>
internal static class RetiredHostToolIds
{
    internal const string ToolManagement = "myavalonia.host.tool.management";
    internal const string PluginStatus = "myavalonia.host.tool.plugin-status";
    internal static bool Contains(string? id) => id is ToolManagement or PluginStatus;
}
