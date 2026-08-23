using System.Collections.Generic;
using System.Linq;
using System;

namespace MyAvaloniaManagement.Business.Workspace;

/// <summary>
/// 提供当前 Workspace 可创建 Document 的分类菜单只读查询。
/// </summary>
internal sealed class DocumentCreationMenuQuery
{
    private readonly WorkspaceSession _workspace;

    public DocumentCreationMenuQuery(WorkspaceSession workspace)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
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
