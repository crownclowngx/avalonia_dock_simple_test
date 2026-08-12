namespace MyAvaloniaManagementCommon.ToolCreation;

/// <summary>
/// Tool 在主工作区中的稳定停靠方向。
/// </summary>
public enum ToolDockSide
{
    Left,
    Right,
    Top,
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

    public ToolTypeId ToolTypeId { get; }
    public string DisplayName { get; }
    public string Description { get; init; } = string.Empty;
    public string IconPath { get; init; } = string.Empty;
    public ToolDockSide DockSide { get; }
    public IReadOnlyList<ToolTypeId> LegacyIds { get; }
}
