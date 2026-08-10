namespace MyAvaloniaManagement.Models.Plugins;

/// <summary>
/// 插件状态 Tool 使用的只读展示模型。
/// </summary>
public sealed record PluginStatusItem(
    string PluginId,
    string AssemblyName,
    string StatusText,
    string DurationText,
    string DependenciesText,
    string Detail);
