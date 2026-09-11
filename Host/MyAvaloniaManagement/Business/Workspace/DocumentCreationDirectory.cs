using System;
using System.Collections.Generic;
using System.Linq;

namespace MyAvaloniaManagement.Business.Workspace;

/// <summary>一次创建入口的只读展示数据；入口身份继续由原有强类型 ID 组合决定。</summary>
internal sealed record DocumentCreationItem(DocumentCreationMenuEntry Entry, DocumentCategoryPath Path)
{
    public string DisplayName => Entry.DisplayName;
    public string Description => Entry.Description;
    public string CategoryPath => Path.DisplayPath;
    public string IconKey => Entry.IconPath;

    internal bool Matches(string text) =>
        DisplayName.Contains(text, StringComparison.OrdinalIgnoreCase) ||
        Description.Contains(text, StringComparison.OrdinalIgnoreCase) ||
        Path.CanonicalPath.Contains(text, StringComparison.OrdinalIgnoreCase) ||
        CategoryPath.Contains(text, StringComparison.OrdinalIgnoreCase);
}

/// <summary>不可变分类节点；与展开状态分开，允许 Tool 和功能中心独立浏览同一目录。</summary>
internal sealed record DocumentCreationCategory(
    string Name,
    DocumentCategoryPath Path,
    IReadOnlyList<DocumentCreationCategory> Children,
    IReadOnlyList<DocumentCreationItem> Items,
    int EntryCount);

/// <summary>把已经排序的创建入口组织成分类树，同时保留原始入口顺序供搜索和旧版使用。</summary>
/// <remarks>
/// 构造器只处理描述数据，不持有 Provider、工厂或控件。树的可变字典只存在于构造期间，
/// 对外交付数组的只读包装，从而不会因为某个视图展开节点而修改其他视图的数据。
/// </remarks>
internal sealed class DocumentCreationDirectory
{
    internal DocumentCreationDirectory(IEnumerable<DocumentCreationMenuEntry> entries, Action<string>? invalidPath = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var roots = new Dictionary<string, Builder>(StringComparer.Ordinal);
        var items = new List<DocumentCreationItem>();
        foreach (var entry in entries)
        {
            var path = DocumentCategoryPath.Parse(entry.MenuCategory);
            if (!path.IsValid) invalidPath?.Invoke(entry.MenuCategory);
            var item = new DocumentCreationItem(entry, path);
            items.Add(item);
            var siblings = roots;
            Builder? current = null;
            var prefix = new List<string>();
            foreach (var segment in path.Segments)
            {
                prefix.Add(segment);
                if (!siblings.TryGetValue(segment, out current))
                {
                    current = new Builder(segment, DocumentCategoryPath.FromSegments(prefix));
                    siblings.Add(segment, current);
                }
                siblings = current.Children;
            }
            current!.Items.Add(item);
        }
        Items = items.AsReadOnly();
        Categories = Freeze(roots);
    }

    internal IReadOnlyList<DocumentCreationItem> Items { get; }
    internal IReadOnlyList<DocumentCreationCategory> Categories { get; }

    /// <summary>按分段比较后代关系，避免“业务/A”错误匹配“业务/ABC”或非法路径回退节点。</summary>
    internal IReadOnlyList<DocumentCreationItem> Filter(DocumentCategoryPath? category, string searchText)
    {
        var search = searchText.Trim();
        return Array.AsReadOnly(Items.Where(item => search.Length > 0
                ? item.Matches(search)
                : category is null ||
                  (item.Path.Segments.Count >= category.Segments.Count &&
                   item.Path.Segments.Take(category.Segments.Count).SequenceEqual(category.Segments, StringComparer.Ordinal)))
            .ToArray());
    }

    private static IReadOnlyList<DocumentCreationCategory> Freeze(Dictionary<string, Builder> siblings) =>
        Array.AsReadOnly(siblings.Values.OrderBy(node => node.Name, StringComparer.Ordinal).Select(node =>
        {
            var children = Freeze(node.Children);
            return new DocumentCreationCategory(node.Name, node.Path, children, node.Items.AsReadOnly(),
                node.Items.Count + children.Sum(child => child.EntryCount));
        }).ToArray());

    private sealed class Builder(string name, DocumentCategoryPath path)
    {
        internal string Name { get; } = name;
        internal DocumentCategoryPath Path { get; } = path;
        internal Dictionary<string, Builder> Children { get; } = new(StringComparer.Ordinal);
        internal List<DocumentCreationItem> Items { get; } = [];
    }
}
