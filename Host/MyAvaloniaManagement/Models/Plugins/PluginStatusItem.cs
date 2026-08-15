namespace MyAvaloniaManagement.Models.Plugins;

/// <summary>
/// 插件状态 Tool 使用的只读展示模型。
/// </summary>
internal sealed record PluginStatusItem(
    string PluginId,
    string AssemblyName,
    string StatusText,
    string DurationText,
    string DependenciesText,
    string Detail)
{
    /// <summary>
    /// 当前构建清单声明的插件版本；仅供宿主状态视图使用，不扩大公共插件契约。
    /// </summary>
    internal string VersionText { get; init; } = "未提供";

    /// <summary>
    /// 当前清单的 Host API 与 Common 区间摘要；仅供宿主诊断 UI 使用。
    /// </summary>
    internal string CompatibilityText { get; init; } = "未提供";
}
