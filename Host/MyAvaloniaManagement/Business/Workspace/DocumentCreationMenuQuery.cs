using System.Collections.Generic;
using System.Linq;
using System;
using MyAvaloniaManagement.Business.Lifecycle;

namespace MyAvaloniaManagement.Business.Workspace;

/// <summary>
/// 提供当前 Workspace 可创建 Document 的分类菜单只读查询。
/// </summary>
internal sealed class DocumentCreationMenuQuery
{
    private readonly WorkspaceSession _workspace;
    private readonly PluginAvailabilityReadModel? _availability;
    private readonly HashSet<string> _reportedInvalidPaths = new(StringComparer.Ordinal);

    public DocumentCreationMenuQuery(WorkspaceSession workspace, PluginAvailabilityReadModel? availability = null)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _availability = availability;
    }

    /// <summary>直接转发只读可用性通知，不建立第二个生命周期状态源；订阅者负责成对退订。</summary>
    internal event EventHandler<PluginAvailabilityChangedEventArgs>? Changed
    {
        add { if (_availability is not null) _availability.AvailabilityChanged += value; }
        remove { if (_availability is not null) _availability.AvailabilityChanged -= value; }
    }

    /// <summary>读取当前可用入口并构建独立目录，整个过程不会实例化插件页面。</summary>
    internal DocumentCreationDirectory ReadDirectory() => new(_workspace.GetAllDocumentCreationEntries(), category =>
    {
        lock (_reportedInvalidPaths)
        {
            if (_reportedInvalidPaths.Add(category))
                Console.Error.WriteLine("PluginNavigation errorCode=PLUGIN_CATEGORY_PATH_INVALID");
        }
    });

    /// <summary>
    /// 获取按分类分组的创建入口；一个文档类型可以贡献多个入口。
    /// </summary>
    public Dictionary<string, List<DocumentCreationMenuEntry>>
        GetCreationEntriesByCategory() =>
        _workspace.GetAllDocumentCreationEntries()
            .GroupBy(entry => entry.MenuCategory)
            .ToDictionary(group => group.Key, group => group.ToList());
}
