using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MyAvaloniaManagement.Business.Workspace;

namespace MyAvaloniaManagement.Models.Tools;

/// <summary>
/// 表示 Document 创建菜单中一个分类的只读内容快照。
/// </summary>
/// <remarks>
/// 分类名与条目在构造期一次冻结，只有纯 UI 状态 <see cref="IsExpanded"/>
/// 可变。这防止外部集合在菜单展示期间悄然改变已发布内容。
/// </remarks>
internal sealed class CategoryNode : INotifyPropertyChanged
{
    private bool _isExpanded;

    internal CategoryNode(
        string categoryName,
        IEnumerable<DocumentCreationMenuEntry> documents)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(categoryName);
        ArgumentNullException.ThrowIfNull(documents);
        CategoryName = categoryName;
        Documents = Array.AsReadOnly<DocumentCreationMenuEntry>([.. documents]);
    }

    /// <summary>取得构造期冻结的分类名。</summary>
    public string CategoryName { get; }

    /// <summary>取得构造期复制的 Document 创建条目快照。</summary>
    public IReadOnlyList<DocumentCreationMenuEntry> Documents { get; }

    /// <summary>取得或设置分类是否在当前视图中展开。</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
            {
                return;
            }

            _isExpanded = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
