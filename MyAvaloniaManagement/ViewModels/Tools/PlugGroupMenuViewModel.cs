using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Controls;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.Models.Tools;
using MyAvaloniaManagementCommon.DocumentCreation;

namespace MyAvaloniaManagement.ViewModels.Tools;

public partial class PlugGroupMenuViewModel:Tool
{
    private readonly ManagementFactory? _factory;
    private readonly PluginMenuService? _pluginMenuService;

    // 按分类分组的文档元数据，用于绑定菜单（树结构）
    public Dictionary<string, List<DocumentMetadata>> DocumentMetadataByCategory =>
        _pluginMenuService?.GetDocumentMetadataByCategory() ?? new Dictionary<string, List<DocumentMetadata>>();
    public List<CategoryNode> CategoryNodes =>
        DocumentMetadataByCategory.Select(kv => new CategoryNode(kv.Key, kv.Value)).ToList();
    public PlugGroupMenuViewModel()
    {
        Title = "插件分组菜单";
        _factory = AppServices.Instance.ManagementFactory;
        _pluginMenuService = AppServices.Instance.PluginMenuService;
    }

    /// <summary>
    /// 创建文档命令
    /// </summary>
    /// <param name="documentType">文档类型ID</param>
    public void CreateDocument(string documentType)
    {
        var document = _factory?.CreateManagementNewDocument(new DocumentCreationParams(documentType));
        var files = _factory?.GetDockable<IDocumentDock>("Files") as DocumentDock;
        if (document != null)
        {
            files?.AddDocument(document);
        }
    }
    
    /// <summary>
    /// 切换分类展开状态的命令
    /// </summary>
    [RelayCommand]
    public void ToggleCategoryExpand(CategoryNode node)
    {
        if (node != null)
        {
            node.IsExpanded = !node.IsExpanded;
        }
    }
}