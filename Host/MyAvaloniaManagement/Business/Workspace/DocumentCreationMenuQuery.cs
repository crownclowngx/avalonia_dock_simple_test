using System.Collections.Generic;
using System.Linq;
using MyAvaloniaManagement.Business.Workspace;

namespace MyAvaloniaManagement.Business.Workspace;

/// <summary>
/// 插件菜单服务，用于处理插件菜单的生成和管理
/// </summary>
internal sealed class DocumentCreationMenuQuery
{
    private readonly WorkspaceSession _workspace;

    public DocumentCreationMenuQuery(WorkspaceSession workspace)
    {
        _workspace = workspace;
    }

    /// <summary>
    /// 获取按分类分组的创建入口；一个文档类型可以贡献多个入口。
    /// </summary>
    public Dictionary<string, List<DocumentCreationMenuEntry>>
        GetCreationEntriesByCategory() =>
        _workspace.GetAllDocumentCreationEntries()
            .GroupBy(entry => entry.MenuCategory)
            .ToDictionary(group => group.Key, group => group.ToList());
}
