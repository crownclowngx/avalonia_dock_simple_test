namespace MyAvaloniaManagementCommon.ToolCreation;

/// <summary>
/// Tool 在主工作区中的稳定停靠方向。
/// </summary>
public enum ToolDockSide
{
    /// <summary>停靠在工作区左侧 ToolDock。</summary>
    Left,
    /// <summary>停靠在工作区右侧 ToolDock。</summary>
    Right,
    /// <summary>停靠在工作区顶部 ToolDock。</summary>
    Top,
    /// <summary>停靠在工作区底部 ToolDock。</summary>
    Bottom,
}

/// <summary>
/// 描述一种宿主级单例 Tool 扩展贡献。
/// </summary>
/// <remarks>
/// Tool 的 Dock Id 由宿主根据本元数据统一赋值，策略不再维护第二份字符串 ID。
/// 这使元数据成为身份的唯一事实源，也让布局迁移可以在实例创建之前完成。
/// </remarks>
public sealed class ToolMetadata
{
    /// <summary>创建一种 Tool 贡献的不可变身份和停靠元数据。</summary>
    /// <param name="toolTypeId">插件拥有的规范稳定 ID。</param>
    /// <param name="displayName">展示给用户的名称。</param>
    /// <param name="dockSide">宿主创建 Tool 时使用的初始停靠方向。</param>
    /// <param name="legacyIds">仅用于读取旧布局的历史别名。</param>
    public ToolMetadata(
        ToolTypeId toolTypeId,
        string displayName,
        ToolDockSide dockSide,
        IEnumerable<ToolTypeId>? legacyIds = null)
    {
        ToolTypeId = toolTypeId ?? throw new ArgumentNullException(nameof(toolTypeId));
        DisplayName = displayName ?? string.Empty;
        DockSide = dockSide;
        LegacyIds = Array.AsReadOnly((legacyIds ?? []).ToArray());
    }

    /// <summary>获取注册、布局和诊断使用的主身份。</summary>
    public ToolTypeId ToolTypeId { get; }
    /// <summary>获取展示名称。</summary>
    public string DisplayName { get; }
    /// <summary>获取功能说明。</summary>
    public string Description { get; init; } = string.Empty;
    /// <summary>获取可选图标资源路径。</summary>
    public string IconPath { get; init; } = string.Empty;
    /// <summary>获取初始停靠方向。</summary>
    public ToolDockSide DockSide { get; }
    /// <summary>获取只读历史别名；宿主不会把别名写入新布局。</summary>
    public IReadOnlyList<ToolTypeId> LegacyIds { get; }
}
