using System.Collections.Generic;
using System.Linq;
using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using MyAvaloniaManagement.Business.Documents;
using MyAvaloniaManagement.Business.Workspace;
using MyAvaloniaManagement.Models.Tools;
using MyAvaloniaManagement.PluginSdk;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.Business.Navigation;
using MyAvaloniaManagement.Business.Presentation.Icons;

namespace MyAvaloniaManagement.ViewModels.Tools;

/// <summary>
/// 将插件文档元数据组织成分类菜单，并负责创建所选类型的文档。
/// </summary>
/// <remarks>
/// 菜单查询与 Document 创建分别委托给 <see cref="DocumentCreationMenuQuery"/>
/// 和 <see cref="DocumentPersistenceCoordinator"/>，ViewModel 不接触 Dock 工作区对象。
/// </remarks>
internal sealed partial class PlugGroupMenuViewModel : ObservableObject, IDisposable
{
    private readonly DocumentPersistenceCoordinator _documents;
    private readonly DocumentOperationState _operationState;
    private readonly DocumentCreationMenuQuery _menu;
    private readonly PluginNavigationSettingsStore _settingsStore;
    private PluginNavigationSettings _settings;
    private PluginMenuModeOption _selectedMode;
    private bool _disposed;

    /// <summary>
    /// 获取旧版完整字符串分组；不要为了新版路径解析而修改此快照的分组语义。
    /// </summary>
    public IReadOnlyList<CategoryNode> CategoryNodes { get; private set; } = [];

    /// <summary>当前 Tool 独享的树形展示状态，功能中心不复用这些可变节点。</summary>
    public IReadOnlyList<NavigationTreeNode> TreeNodes { get; private set; } = [];
    public IReadOnlyList<PluginMenuModeOption> Modes { get; }
    public bool IsLegacy => SelectedMode.Mode == PluginMenuMode.Legacy;
    public bool IsTree => !IsLegacy;
    public HostIconRenderer Icons { get; }

    /// <summary>模式选择只改变展示与用户偏好，不操作 Dock 布局和已经打开的文档。</summary>
    public PluginMenuModeOption SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (_disposed || value is null || !Modes.Contains(value) || !SetProperty(ref _selectedMode, value)) return;
            _settings = _settings with { Mode = value.Mode };
            _settingsStore.Save(_settings);
            OnPropertyChanged(nameof(IsLegacy));
            OnPropertyChanged(nameof(IsTree));
            Refresh();
        }
    }

    /// <summary>
    /// 使用显式工厂和菜单服务创建插件菜单工具。
    /// </summary>
    public PlugGroupMenuViewModel(
        DocumentCreationMenuQuery pluginMenuService,
        DocumentPersistenceCoordinator documents,
        DocumentOperationState operationState,
        PluginNavigationSettingsStore settingsStore,
        HostIconRenderer icons)
    {
        ArgumentNullException.ThrowIfNull(pluginMenuService);
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _operationState = operationState ??
            throw new ArgumentNullException(nameof(operationState));
        _menu = pluginMenuService;
        Icons = icons ?? throw new ArgumentNullException(nameof(icons));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _settings = _settingsStore.Load();
        Modes = Array.AsReadOnly(new[]
        {
            new PluginMenuModeOption(PluginMenuMode.Legacy, _settings.LegacyName),
            new PluginMenuModeOption(PluginMenuMode.Tree, _settings.TreeName),
        });
        _selectedMode = Modes.Single(mode => mode.Mode == _settings.Mode);
        Refresh();
        _menu.Changed += OnDirectoryChanged;
    }

    /// <summary>按菜单入口创建文档，并把入口意图作为强类型参数传给协调器。</summary>
    [RelayCommand]
    public async Task CreateDocumentEntryAsync(DocumentCreationMenuEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _operationState.Apply(await _documents.CreateDocumentAsync(
            entry.DocumentTypeId,
            entry.CreationIntentId));
    }

    /// <summary>
    /// 切换指定菜单分类的展开状态。
    /// </summary>
    /// <param name="node">要切换的分类节点。</param>
    [RelayCommand]
    public void ToggleCategoryExpand(CategoryNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        node.IsExpanded = !node.IsExpanded;
    }

    /// <summary>分类仅切换展开；叶子仍提交同一个强类型创建入口，避免出现第二套激活实现。</summary>
    [RelayCommand]
    private async Task ActivateTreeNodeAsync(NavigationTreeNode node)
    {
        if (node.IsCategory) node.IsExpanded = !node.IsExpanded;
        else if (node.Item is { } item) await CreateDocumentEntryAsync(item.Entry);
    }

    private void OnDirectoryChanged(object? sender, PluginAvailabilityChangedEventArgs args)
    {
        if (Dispatcher.UIThread.CheckAccess()) Refresh();
        else Dispatcher.UIThread.Post(Refresh);
    }

    /// <summary>一次读取同时生成两种投影，保证叶子集合一致，并按分类身份恢复各自展开状态。</summary>
    private void Refresh()
    {
        if (_disposed) return;
        var directory = _menu.ReadDirectory();
        var expanded = CategoryNodes.Where(node => node.IsExpanded).Select(node => node.CategoryName).ToHashSet(StringComparer.Ordinal);
        CategoryNodes = Array.AsReadOnly(directory.Items.Select(item => item.Entry).GroupBy(entry => entry.MenuCategory)
            .Select(group => new CategoryNode(group.Key, group) { IsExpanded = expanded.Contains(group.Key) }).ToArray());
        TreeNodes = NavigationTreeNode.Build(directory.Categories, true, TreeNodes);
        OnPropertyChanged(nameof(CategoryNodes));
        OnPropertyChanged(nameof(TreeNodes));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _menu.Changed -= OnDirectoryChanged;
    }
}

/// <summary>可改显示名与稳定模式值成对保存，界面绑定名称，业务判断只使用模式值。</summary>
internal sealed record PluginMenuModeOption(PluginMenuMode Mode, string DisplayName);
