using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using MyAvaloniaManagement.Business.Workspace;

namespace MyAvaloniaManagement.Models.Tools;

/// <summary>单个视图拥有的树形展示状态。分类快照可共用，展开状态不可在多个界面之间共用。</summary>
internal sealed class NavigationTreeNode : ObservableObject
{
    private bool _isExpanded;

    private NavigationTreeNode(DocumentCreationCategory category, bool includeItems)
    {
        Category = category;
        DisplayName = category.Name;
        Key = category.Path.Key;
        Children = Array.AsReadOnly(category.Children.Select(child => new NavigationTreeNode(child, includeItems))
            .Concat(includeItems ? category.Items.Select(item => new NavigationTreeNode(item)) : []).ToArray());
    }

    private NavigationTreeNode(DocumentCreationItem item)
    {
        Item = item;
        DisplayName = item.DisplayName;
        Key = $"entry:{item.Entry.DocumentTypeId.Value}:{item.Entry.CreationIntentId?.Value}";
        Children = Array.Empty<NavigationTreeNode>();
    }

    public string DisplayName { get; }
    public string Key { get; }
    public DocumentCreationCategory? Category { get; }
    public DocumentCreationItem? Item { get; }
    public bool IsCategory => Category is not null;
    public string CountLabel => Category?.EntryCount.ToString() ?? string.Empty;
    public string IconKey => IsCategory ? "builtin:folder" : Item!.IconKey;
    public Business.Presentation.Icons.HostIconRequest IconRequest =>
        IsCategory ? new(null, IconKey) : Item!.IconRequest;
    public string ToolTip => Category?.Path.DisplayPath ?? $"{Item!.CategoryPath}\n{Item.Description}";
    public IReadOnlyList<NavigationTreeNode> Children { get; }
    public bool IsExpanded { get => _isExpanded; set => SetProperty(ref _isExpanded, value); }

    /// <summary>重新读取目录时按完整分类身份恢复展开，不把同名但不同父级的节点混为一谈。</summary>
    internal static IReadOnlyList<NavigationTreeNode> Build(IReadOnlyList<DocumentCreationCategory> categories,
        bool includeItems, IReadOnlyList<NavigationTreeNode>? previous = null)
    {
        var expanded = Flatten(previous ?? []).Where(node => node.IsCategory && node.IsExpanded)
            .Select(node => node.Key).ToHashSet(StringComparer.Ordinal);
        var roots = Array.AsReadOnly(categories.Select(category => new NavigationTreeNode(category, includeItems)).ToArray());
        foreach (var node in Flatten(roots)) node.IsExpanded = expanded.Contains(node.Key);
        return roots;
    }

    internal static IEnumerable<NavigationTreeNode> Flatten(IEnumerable<NavigationTreeNode> roots)
    {
        var pending = new Stack<NavigationTreeNode>(roots.Reverse());
        while (pending.TryPop(out var node))
        {
            yield return node;
            foreach (var child in node.Children.Reverse()) pending.Push(child);
        }
    }
}
