using System.Collections.Generic;
using System.Linq;
using System;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.Models.Tools;
using MyAvaloniaManagementCommon.DocumentCreation;

namespace MyAvaloniaManagement.ViewModels.Tools;

/// <summary>
/// 将插件文档元数据组织成分类菜单，并负责创建所选类型的文档。
/// </summary>
/// <remarks>
/// 菜单查询与 Dock 文档创建分别委托给 <see cref="PluginMenuService"/>
/// 和 <see cref="ManagementFactory"/>，从而保持插件契约不变。
/// </remarks>
internal sealed partial class PlugGroupMenuViewModel : Tool
{
    private readonly ManagementFactory? _factory;
    private readonly PluginMenuService? _pluginMenuService;

    /// <summary>
    /// 获取按分类分组且允许显示在菜单中的文档元数据。
    /// </summary>
    public Dictionary<string, List<DocumentMetadata>> DocumentMetadataByCategory =>
        _pluginMenuService?.GetDocumentMetadataByCategory() ?? new Dictionary<string, List<DocumentMetadata>>();

    /// <summary>
    /// 获取供树形菜单绑定的分类节点快照。
    /// </summary>
    public List<CategoryNode> CategoryNodes =>
        (_pluginMenuService?.GetCreationEntriesByCategory() ?? [])
        .Select(kv => new CategoryNode(kv.Key, kv.Value)).ToList();

    /// <summary>
    /// 使用显式工厂和菜单服务创建插件菜单工具。
    /// </summary>
    internal PlugGroupMenuViewModel(
        ManagementFactory factory,
        PluginMenuService pluginMenuService)
    {
        Title = "插件分组菜单";
        _factory = factory;
        _pluginMenuService = pluginMenuService;
    }

    /// <summary>
    /// 创建指定插件类型的文档并加入主文档区域。
    /// </summary>
    /// <param name="documentType">文档类型ID</param>
    [RelayCommand]
    public void CreateDocument(string documentType)
    {
        _factory?.CreateAndPublishDocument(
            new DocumentCreationParams(DocumentTypeId.Parse(documentType)));
    }

    /// <summary>按菜单入口创建文档，并把入口意图作为强类型参数传给策略。</summary>
    [RelayCommand]
    public void CreateDocumentEntry(DocumentCreationMenuEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _factory?.CreateAndPublishDocument(new DocumentCreationParams(entry.DocumentTypeId)
        {
            CreationIntentId = entry.CreationIntentId,
        });
    }
    
    /// <summary>
    /// 切换指定菜单分类的展开状态。
    /// </summary>
    /// <param name="node">要切换的分类节点。</param>
    [RelayCommand]
    public void ToggleCategoryExpand(CategoryNode node)
    {
        if (node != null)
        {
            node.IsExpanded = !node.IsExpanded;
        }
    }
}
