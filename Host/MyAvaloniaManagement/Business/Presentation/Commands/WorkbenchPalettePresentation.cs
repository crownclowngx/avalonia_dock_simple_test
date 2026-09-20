using System;
using System.Collections.Generic;
using System.Linq;
using MyAvaloniaManagement.Business.Commands.Context;
using MyAvaloniaManagement.Business.Presentation.Icons;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.Business.Presentation.Commands;

/// <summary>一条真实候选的不可变展示快照；组标题只是该行的装饰，不具有独立执行身份。</summary>
/// <remarks>仅保存值及既有命令适配器，不拥有页面、窗口或插件对象。预期目标属于本次快照，不能写入共享命令缓存。</remarks>
internal sealed record WorkbenchCommandPaletteProjectionEntry(
    WorkbenchPaletteIdentity Identity, string DisplayName, string Description,
    string ShortcutText, bool IsEnabled, IWorkbenchPresentationCommandBinding Command)
{
    public string StableKey => Identity.StableKey;
    public CommandId? CommandId => (Identity as CommandPaletteIdentity)?.Id;
    public ToolTypeId? ToolTypeId => (Identity as ToolPaletteIdentity)?.Id;
    public string ActionText { get; init; } = "执行命令";
    public string SearchName { get; init; } = DisplayName;
    public int MatchRank { get; init; }
    public HostIconRequest IconRequest { get; init; } = new(null, string.Empty);
    public HostIconRenderer? IconRenderer { get; init; }
    public string SourceText { get; init; } = string.Empty;
    public string InstanceText { get; init; } = string.Empty;
    public string TargetText { get; init; } = string.Empty;
    public string UnavailableReason { get; init; } = string.Empty;
    public string ExecuteHint { get; init; } = string.Empty;
    public bool StartsGroup { get; init; }
    internal WorkbenchCommandTargetExpectation? ExpectedTarget { get; init; }

    public string GroupName => Identity switch
    {
        PagePaletteIdentity => "已打开的页面",
        FunctionPaletteIdentity => "新开页面",
        ToolPaletteIdentity => "工具面板",
        CommandPaletteIdentity => "命令",
        _ => throw new InvalidOperationException("未定义的搜索结果类型。")
    };
    public string LeadingAction => Identity switch
    {
        PagePaletteIdentity => "↩",
        FunctionPaletteIdentity => "＋",
        ToolPaletteIdentity => ActionText,
        _ => string.Empty
    };
    public string IdentityText => Join(SourceText, InstanceText);
    public string DisabledText => IsEnabled ? string.Empty :
        UnavailableReason.Length > 0 ? UnavailableReason : "当前状态下不可用";
    public string DetailText => Join(TargetText.Length > 0 ? TargetText : Description, DisabledText);
    public string ContextText => Join(SourceText, DetailText);
    public string EnterHint => IsEnabled
        ? "Enter " + (ExecuteHint.Length > 0 ? ExecuteHint : "执行：" + DisplayName)
        : DisabledText;
    public string AccessibleText => Join(GroupName, LeadingAction, DisplayName, IdentityText, DetailText, ShortcutText);

    private static string Join(params string[] parts) => string.Join(" · ", parts.Where(part => part.Length > 0));
}

/// <summary>纯值排序与会话顺序维护；不查询业务，不创建候选或副作用。</summary>
internal static class WorkbenchPaletteOrdering
{
    /// <summary>先比较各组的最佳匹配，再在组内排序；弱匹配仍留在所属组，避免已有页和创建入口穿插。</summary>
    internal static WorkbenchCommandPaletteProjectionEntry[] Sort(IEnumerable<WorkbenchCommandPaletteProjectionEntry> items) =>
        MarkGroups(items.GroupBy(item => item.Identity.Order)
            .OrderBy(group => group.Min(item => item.MatchRank)).ThenBy(group => group.Key)
            .SelectMany(group => group.OrderBy(item => item.MatchRank).ThenByDescending(item => item.IsEnabled)
                .ThenBy(item => item.SearchName, StringComparer.Ordinal).ThenBy(item => item.StableKey, StringComparer.Ordinal)));

    /// <summary>同一次查询仅状态变化时沿用旧位置；成员、名称或匹配改变才采用新排序，避免用户按键时条目跳动。</summary>
    internal static WorkbenchCommandPaletteProjectionEntry[] PreserveOrder(
        IReadOnlyList<WorkbenchCommandPaletteProjectionEntry> previous,
        WorkbenchCommandPaletteProjectionEntry[] current)
    {
        var byId = current.ToDictionary(item => item.StableKey, StringComparer.Ordinal);
        if (previous.Count != current.Length || previous.Any(old => !byId.TryGetValue(old.StableKey, out var next) ||
            old.SearchName != next.SearchName || old.MatchRank != next.MatchRank)) return current;
        return MarkGroups(previous.Select(old => byId[old.StableKey]));
    }

    /// <summary>重排后重新标记组首；标题永远附着于真实结果，不进入选择和执行集合。</summary>
    private static WorkbenchCommandPaletteProjectionEntry[] MarkGroups(IEnumerable<WorkbenchCommandPaletteProjectionEntry> items)
    {
        int? previous = null;
        return items.Select(item =>
        {
            var starts = previous != item.Identity.Order;
            previous = item.Identity.Order;
            return item with { StartsGroup = starts };
        }).ToArray();
    }
}
