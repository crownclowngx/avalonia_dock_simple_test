using System.Collections.Generic;
using System.Linq;
using System;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.Business.Workspace;

/// <summary>
/// 提供当前 Workspace 可创建 Document 的分类菜单只读查询。
/// </summary>
internal sealed class DocumentCreationMenuQuery
{
    private readonly WorkspaceCatalog _catalog;
    private readonly PluginAvailabilityReadModel? _availability;
    private readonly HashSet<string> _reportedInvalidPaths = new(StringComparer.Ordinal);

    public DocumentCreationMenuQuery(WorkspaceCatalog catalog, PluginAvailabilityReadModel? availability = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _availability = availability;
    }

    /// <summary>直接转发只读可用性通知，不建立第二个生命周期状态源；订阅者负责成对退订。</summary>
    internal event EventHandler<PluginAvailabilityChangedEventArgs>? Changed
    {
        add { if (_availability is not null) _availability.AvailabilityChanged += value; }
        remove { if (_availability is not null) _availability.AvailabilityChanged -= value; }
    }

    /// <summary>读取当前可用入口并构建独立目录，整个过程不会实例化插件页面。</summary>
    internal DocumentCreationDirectory ReadDirectory() => new(_catalog.GetCreationEntries(), category =>
    {
        lock (_reportedInvalidPaths)
        {
            if (_reportedInvalidPaths.Add(category))
                Console.Error.WriteLine("PluginNavigation errorCode=PLUGIN_CATEGORY_PATH_INVALID");
        }
    });

    /// <summary>轻量判定只读取指定身份；分类路径诊断仍在构建真实目录时执行并按原规则去重。</summary>
    internal bool HasCreationEntry(DocumentTypeId id, CreationIntentId? intentId) => _catalog.HasCreationEntry(id, intentId);

    /// <summary>
    /// 获取按分类分组的创建入口；一个文档类型可以贡献多个入口。
    /// </summary>
    public Dictionary<string, List<DocumentCreationMenuEntry>>
        GetCreationEntriesByCategory() =>
        _catalog.GetCreationEntries()
            .GroupBy(entry => entry.MenuCategory)
            .ToDictionary(group => group.Key, group => group.ToList());
}
