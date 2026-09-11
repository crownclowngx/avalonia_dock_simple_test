using System;
using System.Collections.Generic;
using System.Linq;
using MyAvaloniaManagement.Business.Presentation.Icons;
using MyAvaloniaManagement.Business.Workspace;
using MyAvaloniaManagement.Models.Tools;

namespace MyAvaloniaManagement.Business.ToolCenter;

internal sealed record ToolCenterItem(ToolWorkspaceState State, string CategoryId, string CategoryName, bool IsFavorite, bool IsMissing = false)
{
    public string ToolId => State.ToolId;
    public string DisplayName => State.DisplayName;
    public string Description => State.Description;
    public string SourceName => State.SourceName;
    public string SourceId => State.OwnerId?.Value ?? "host";
    public string Subtitle => $"{CategoryName} · {SourceName}";
    public HostIconRequest IconRequest => State.IconRequest;
    public string StatusText => State.StatusText;
    public bool CanOpen => State.CanOpen;
    public bool CanHide => State.CanHide;
    public bool IsVisible => State.IsVisible;
    public string OpenText => IsVisible ? "定位" : "显示";
    public string FavoriteText => IsFavorite ? "★" : "☆";
    public string FavoriteTip => IsFavorite ? "移出常用" : "加入常用";
}

internal sealed record ToolSource(string Id, string DisplayName);

/// <summary>只读组合和筛选；同一工具只有一个身份，搜索不调用任何业务工厂。</summary>
internal sealed class ToolCenterQuery(ToolWorkspaceReadModel readModel, ToolCenterPreferences preferences)
{
    internal IReadOnlyList<ToolCenterItem> Capture()
    {
        var categories = preferences.Categories.ToDictionary(item => item.Id, item => item.DisplayName, StringComparer.Ordinal);
        var favorites = preferences.Current.FavoriteToolIds.ToHashSet(StringComparer.Ordinal);
        var items = readModel.Capture().Select(state =>
        {
            var category = preferences.CategoryFor(state.ToolId);
            return new ToolCenterItem(state, category, categories.GetValueOrDefault(category, "其他工具"), favorites.Contains(state.ToolId));
        }).ToList();
        var registered = items.Select(item => item.ToolId).ToHashSet(StringComparer.Ordinal);
        foreach (var id in preferences.Current.FavoriteToolIds.Concat(preferences.Current.RecentToolIds).Distinct(StringComparer.Ordinal).Where(id => !registered.Contains(id)))
        {
            var state = new ToolWorkspaceState(id, preferences.Current.LastKnownToolNames.GetValueOrDefault(id, id), false, false)
                { UnavailableReason = "插件缺失", SourceName = "历史工具" };
            items.Add(new(state, ToolCenterPreferences.OtherCategoryId, "其他工具", favorites.Contains(id), true));
        }
        return items;
    }

    internal IReadOnlyList<ToolCenterItem> Filter(IReadOnlyList<ToolCenterItem> items, string navigation, string search, string source)
    {
        var query = search.Trim();
        IEnumerable<ToolCenterItem> result = query.Length > 0 ? items.Where(item => !item.IsMissing) : navigation switch
        {
            "favorites" => items.Where(item => item.IsFavorite),
            "recent" => items.Where(item => preferences.Current.RecentToolIds.Contains(item.ToolId, StringComparer.Ordinal)),
            "visible" => items.Where(item => !item.IsMissing && item.IsVisible),
            "hidden" => items.Where(item => !item.IsMissing && item.State.LayoutState == ToolLayoutState.Hidden),
            "all" => items.Where(item => !item.IsMissing),
            _ => items.Where(item => !item.IsMissing && item.CategoryId == navigation)
        };
        if (source != "all") result = result.Where(item => item.SourceId == source);
        if (query.Length > 0) result = result.Where(item =>
            new[] { item.DisplayName, item.Description, item.CategoryName, item.SourceName, item.ToolId }
                .Any(text => text.Contains(query, StringComparison.OrdinalIgnoreCase)));
        if (query.Length == 0 && navigation is "favorites" or "recent")
        {
            var order = navigation == "favorites" ? preferences.Current.FavoriteToolIds : preferences.Current.RecentToolIds;
            return result.OrderBy(item => Array.IndexOf(order, item.ToolId)).ToArray();
        }
        return result.OrderBy(item => item.DisplayName, StringComparer.Ordinal).ThenBy(item => item.ToolId, StringComparer.Ordinal).ToArray();
    }
}
