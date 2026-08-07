using System.Collections.Generic;
using System.Linq;
using MyAvaloniaManagement.ViewModels;
using MyAvaloniaManagementCommon.DocumentCreation;

namespace MyAvaloniaManagement.Business.Helpers;

/// <summary>
/// 插件菜单服务，用于处理插件菜单的生成和管理
/// </summary>
public class PluginMenuService
{
    private readonly ManagementFactory _factory;

    public PluginMenuService(ManagementFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// 获取按MenuCategory分组的文档元数据
    /// </summary>
    /// <returns>按MenuCategory分组的文档元数据字典</returns>
    public Dictionary<string, List<DocumentMetadata>> GetDocumentMetadataByCategory()
    {
        // 获取所有ShowInMenu为true的文档元数据，并按MenuCategory分组
        return _factory.GetAllDocumentMetadata()
            .Where(m => m.ShowInMenu)
            .GroupBy(m => m.MenuCategory)
            .ToDictionary(
                group => group.Key, 
                group => group.ToList()
            );
    }

    /// <summary>
    /// 获取按分类分组的创建入口；一个文档类型可以贡献多个入口。
    /// </summary>
    public Dictionary<string, List<DocumentCreationMenuEntry>> GetCreationEntriesByCategory() =>
        _factory.GetAllDocumentCreationEntries()
            .GroupBy(entry => entry.MenuCategory)
            .ToDictionary(group => group.Key, group => group.ToList());
}
