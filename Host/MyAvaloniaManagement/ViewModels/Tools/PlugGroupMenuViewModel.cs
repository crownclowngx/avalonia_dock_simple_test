using System.Collections.Generic;
using System.Linq;
using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using MyAvaloniaManagement.Business.Documents;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.Business.Workspace;
using MyAvaloniaManagement.Models.Tools;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.ViewModels.Tools;

/// <summary>
/// 将插件文档元数据组织成分类菜单，并负责创建所选类型的文档。
/// </summary>
/// <remarks>
/// 菜单查询与 Document 创建分别委托给 <see cref="PluginMenuService"/>
/// 和 <see cref="DocumentPersistenceCoordinator"/>，ViewModel 不接触 Dock 工作区对象。
/// </remarks>
internal sealed partial class PlugGroupMenuViewModel
{
    private readonly DocumentPersistenceCoordinator? _documents;
    private readonly DocumentOperationState? _operationState;
    private readonly PluginMenuService? _pluginMenuService;

    /// <summary>
    /// 获取按分类分组且允许显示在菜单中的文档元数据。
    /// </summary>
    /// <summary>
    /// 获取供树形菜单绑定的分类节点快照。
    /// </summary>
    public List<CategoryNode> CategoryNodes =>
        (_pluginMenuService?.GetCreationEntriesByCategory() ?? [])
        .Select(kv => new CategoryNode(kv.Key, kv.Value)).ToList();

    /// <summary>
    /// 使用显式工厂和菜单服务创建插件菜单工具。
    /// </summary>
    public PlugGroupMenuViewModel(
        PluginMenuService pluginMenuService,
        DocumentPersistenceCoordinator documents,
        DocumentOperationState operationState)
    {
        _pluginMenuService = pluginMenuService;
        _documents = documents;
        _operationState = operationState;
    }

    /// <summary>
    /// 创建指定插件类型的文档并加入主文档区域。
    /// </summary>
    /// <param name="documentType">文档类型ID</param>
    [RelayCommand]
    public async Task CreateDocumentAsync(string documentType)
    {
        if (_documents is not null && _operationState is not null)
        {
            _operationState.Apply(await _documents.CreateDocumentAsync(
                DocumentTypeId.Parse(documentType)));
        }
    }

    /// <summary>按菜单入口创建文档，并把入口意图作为强类型参数传给策略。</summary>
    [RelayCommand]
    public async Task CreateDocumentEntryAsync(DocumentCreationMenuEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (_documents is not null && _operationState is not null)
        {
            _operationState.Apply(await _documents.CreateDocumentAsync(
                entry.DocumentTypeId,
                entry.CreationIntentId));
        }
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
