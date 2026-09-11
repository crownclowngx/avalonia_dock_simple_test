using System.Collections.Generic;
using System.Linq;

namespace MyAvaloniaManagement.Models.Plugins;

/// <summary>
/// 插件状态窗口使用的一次查询快照。只保存展示数据，不持有插件实例、容器或控件。
/// </summary>
internal sealed record PluginStatusItem(
    string PluginId,
    string AssemblyName,
    string StatusText,
    string DurationText,
    string AvailabilityText,
    string Detail)
{
    /// <summary>稳定选择键；没有 PluginId 的失败候选使用目录键，刷新时也能保留选择。</summary>
    public string Key { get; init; } = PluginId;
    public bool IsAvailable { get; init; }
    public bool HasProblem { get; init; }
    public string StatusSymbol => HasProblem ? "⚠" : IsAvailable ? "✓" : "○";
    public string StatusLabel => HasProblem ? "异常 / 警告" : IsAvailable ? "可用" : "未就绪 / 已停止";
    public IReadOnlyList<PluginContributionItem> Contributions { get; init; } = [];
    public IReadOnlyList<PluginDiagnosticItem> Diagnostics { get; init; } = [];
    public bool HasContributions => Contributions.Count > 0;
    public bool HasDiagnostics => Diagnostics.Count > 0;
    public string ContributionSummary => $"已声明 {Contributions.Count} 项贡献 · {(IsAvailable ? "插件可用" : "插件当前不可用")}";

    /// <summary>搜索仅扫描快照；输入关键词不会调用任何插件工厂或触发磁盘发现。</summary>
    public bool Matches(string text) => new[] { PluginId, AssemblyName, VersionText, StatusText, Detail }
        .Concat(Diagnostics.Select(item => item.Code + " " + item.Message))
        .Any(value => value.Contains(text, System.StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 当前构建清单声明的插件版本；仅供宿主状态视图使用，不扩大公共插件契约。
    /// </summary>
    public string VersionText { get; init; } = "未提供";

    /// <summary>
    /// 当前清单的 Plugin SDK 区间摘要；仅供宿主诊断 UI 使用。
    /// </summary>
    public string CompatibilityText { get; init; } = "未提供";
}

/// <summary>声明和运行可用性分开展示；列出声明不会创建文档、工具或命令处理器。</summary>
internal sealed record PluginContributionItem(string Kind, string Name, string Id, string AvailabilityText);

/// <summary>仅接收 Host 已脱敏的诊断字段；窗口与剪贴板共享同一份可公开摘要。</summary>
internal sealed record PluginDiagnosticItem(string TimeText, string SeverityText, string PhaseText,
    string Code, string Message, string TechnicalDetail);
