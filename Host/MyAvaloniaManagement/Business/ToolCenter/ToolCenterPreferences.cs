using System;
using System.Collections.Generic;
using System.Linq;
using MyAvaloniaManagement.Business.Constants;

namespace MyAvaloniaManagement.Business.ToolCenter;

internal sealed record ToolPreferenceResult(bool Succeeded, string Message = "", string? CategoryId = null);

/// <summary>Runtime 拥有的用户选择；收藏、最近与分类的修改完全独立于布局。</summary>
internal sealed class ToolCenterPreferences
{
    internal const string OtherCategoryId = "builtin:other";
    internal static IReadOnlyList<ToolCategory> BuiltInCategories { get; } = Array.AsReadOnly<ToolCategory>(
        [new("builtin:navigation", "资源导航"), new("builtin:diagnostics", "系统诊断"), new(OtherCategoryId, "其他工具")]);
    private readonly ToolCenterPreferencesStore _store;
    internal ToolCenterSettings Current { get; private set; }
    internal string SaveWarning => _store.Error;
    internal event EventHandler? Changed;
    public ToolCenterPreferences(ToolCenterPreferencesStore store) { _store = store; Current = store.Load(); }
    internal IReadOnlyList<ToolCategory> Categories => BuiltInCategories.Concat(Current.Categories).ToArray();

    internal string CategoryFor(string toolId)
    {
        if (Current.ToolCategoryAssignments.TryGetValue(toolId, out var assigned)) return assigned;
        if (toolId == HostExtensionIds.FileSystemTree.Value || toolId == HostExtensionIds.PluginMenu.Value) return "builtin:navigation";
        return toolId == HostExtensionIds.PluginStatus.Value ? "builtin:diagnostics" : OtherCategoryId;
    }

    internal void ToggleFavorite(string id, string name)
    {
        if (!ToolCenterPreferencesStore.ValidToolId(id)) return;
        var ids = Current.FavoriteToolIds.Contains(id, StringComparer.Ordinal)
            ? Current.FavoriteToolIds.Where(item => item != id).ToArray() : [.. Current.FavoriteToolIds, id];
        Commit(Current with { FavoriteToolIds = ids, LastKnownToolNames = RememberName(id, name) });
    }

    internal void MoveFavorite(string id, int delta)
    {
        var ids = Current.FavoriteToolIds.ToList();
        var index = ids.IndexOf(id);
        if (index < 0) return;
        var target = Math.Clamp(index + delta, 0, ids.Count - 1);
        if (target == index) return;
        ids.RemoveAt(index); ids.Insert(target, id);
        Commit(Current with { FavoriteToolIds = ids.ToArray() });
    }

    internal void RememberAccess(string id, string name)
    {
        if (!ToolCenterPreferencesStore.ValidToolId(id)) return;
        Commit(Current with { RecentToolIds = new[] { id }.Concat(Current.RecentToolIds.Where(item => item != id)).Take(20).ToArray(),
            LastKnownToolNames = RememberName(id, name) });
    }

    internal ToolPreferenceResult AddCategory(string name)
    {
        var error = ValidateName(name, null);
        if (error is not null) return new(false, error);
        var category = new ToolCategory("user:" + Guid.NewGuid().ToString("N"), name.Trim());
        Commit(Current with { Categories = [.. Current.Categories, category] });
        return new(true, CategoryId: category.Id);
    }

    internal ToolPreferenceResult RenameCategory(string id, string name)
    {
        if (!Current.Categories.Any(item => item.Id == id)) return new(false, "只能重命名自定义分类");
        var error = ValidateName(name, id);
        if (error is not null) return new(false, error);
        Commit(Current with { Categories = Current.Categories.Select(item => item.Id == id ? item with { DisplayName = name.Trim() } : item).ToArray() });
        return new(true, CategoryId: id);
    }

    internal ToolPreferenceResult DeleteCategory(string id)
    {
        if (!Current.Categories.Any(item => item.Id == id)) return new(false, "预置分类不能删除");
        Commit(Current with
        {
            Categories = Current.Categories.Where(item => item.Id != id).ToArray(),
            ToolCategoryAssignments = Current.ToolCategoryAssignments.ToDictionary(pair => pair.Key,
                pair => pair.Value == id ? OtherCategoryId : pair.Value, StringComparer.Ordinal)
        });
        return new(true);
    }

    internal void AssignCategory(string toolId, string categoryId)
    {
        if (!ToolCenterPreferencesStore.ValidToolId(toolId) || !Categories.Any(item => item.Id == categoryId)) return;
        var assignments = new Dictionary<string, string>(Current.ToolCategoryAssignments, StringComparer.Ordinal) { [toolId] = categoryId };
        Commit(Current with { ToolCategoryAssignments = assignments });
    }

    private string? ValidateName(string? name, string? exceptId) =>
        string.IsNullOrWhiteSpace(name) || name.Trim().Length > 60 ? "分类名称需要 1–60 个字符" :
        Categories.Any(item => item.Id != exceptId && string.Equals(item.DisplayName, name.Trim(), StringComparison.OrdinalIgnoreCase))
            ? "已有同名分类" : null;

    private Dictionary<string, string> RememberName(string id, string name) =>
        new(Current.LastKnownToolNames, StringComparer.Ordinal) { [id] = name };

    private void Commit(ToolCenterSettings next)
    {
        // 内存选择先成立；磁盘失败不能撤销显隐或让本次收藏看起来未发生。
        Current = ToolCenterPreferencesStore.Normalize(next);
        _store.Save(Current);
        foreach (EventHandler handler in Changed?.GetInvocationList() ?? [])
        {
            try { handler(this, EventArgs.Empty); }
            catch { Console.Error.WriteLine("ToolCenter errorCode=TOOL_PREFERENCE_OBSERVER_FAILED"); }
        }
    }
}
