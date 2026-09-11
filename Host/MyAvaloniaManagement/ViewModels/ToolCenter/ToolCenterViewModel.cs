using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.Business.Presentation.Icons;
using MyAvaloniaManagement.Business.ToolCenter;
using MyAvaloniaManagement.Business.Workspace;

namespace MyAvaloniaManagement.ViewModels.ToolCenter;

internal sealed record ToolNavigationItem(string Id, string DisplayName);

/// <summary>窗口级交互；只消费纯数据快照和用例，关闭时解除 Runtime 事件订阅。</summary>
internal sealed partial class ToolCenterViewModel : ObservableObject, IDisposable
{
    private readonly ToolCenterQuery _query;
    private readonly ToolCenterPreferences _preferences;
    private readonly ToolCenterActions _actions;
    private readonly WorkspaceSession _workspace;
    private readonly PluginAvailabilityReadModel _availability;
    private IReadOnlyList<ToolCenterItem> _snapshot = [];
    private bool _refreshing;
    private volatile bool _disposed;
    private int _refreshQueued;
    private string _navigation = "all";

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private IReadOnlyList<ToolCenterItem> _visibleItems = [];
    [ObservableProperty] private ToolCenterItem? _selectedItem;
    [ObservableProperty] private IReadOnlyList<ToolNavigationItem> _navigationItems = [];
    [ObservableProperty] private ToolNavigationItem? _selectedNavigation;
    [ObservableProperty] private IReadOnlyList<ToolSource> _sources = [];
    [ObservableProperty] private ToolSource? _selectedSource;
    [ObservableProperty] private IReadOnlyList<ToolCategory> _categories = [];
    [ObservableProperty] private ToolCategory? _editingCategory;
    [ObservableProperty] private string _categoryName = string.Empty;
    [ObservableProperty] private string _error = string.Empty;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private string _resultsTitle = "全部工具";
    [ObservableProperty] private string _saveWarning = string.Empty;
    public HostIconRenderer Icons { get; }
    public bool HasSourceFilter => SelectedSource is { Id: not "all" };
    public bool HasError => Error.Length > 0;
    public bool HasSaveWarning => SaveWarning.Length > 0;
    public bool HasNoResults => VisibleItems.Count == 0;
    public bool CanHideAll => _workspace.CanOperateTools && _snapshot.Any(item => item.CanHide);
    public string EmptyText => SearchText.Trim().Length > 0 ? "没有匹配的工具，试试其他关键词。" :
        _navigation == "favorites" ? "还没有常用工具，点击工具旁的星标即可收藏。" :
        _navigation == "recent" ? "显示或定位工具后，会在这里留下最近访问记录。" : "这里暂时没有工具。";

    public ToolCenterViewModel(ToolCenterQuery query, ToolCenterPreferences preferences, ToolCenterActions actions,
        WorkspaceSession workspace, PluginAvailabilityReadModel availability, HostIconRenderer icons)
    {
        _query = query; _preferences = preferences; _actions = actions;
        _workspace = workspace; _availability = availability; Icons = icons;
        _workspace.LayoutChanged += Changed;
        _preferences.Changed += Changed;
        _availability.AvailabilityChanged += AvailabilityChanged;
        Refresh();
    }

    partial void OnSearchTextChanged(string value) { if (!_refreshing) ApplyFilter(); }
    partial void OnSelectedSourceChanged(ToolSource? value) { OnPropertyChanged(nameof(HasSourceFilter)); if (!_refreshing) ApplyFilter(); }
    partial void OnSelectedNavigationChanged(ToolNavigationItem? value)
    {
        if (_refreshing || value is null) return;
        _navigation = value.Id;
        SearchText = string.Empty;
        ApplyFilter();
    }
    partial void OnEditingCategoryChanged(ToolCategory? value) => CategoryName = value?.DisplayName ?? string.Empty;
    partial void OnErrorChanged(string value) => OnPropertyChanged(nameof(HasError));
    partial void OnSaveWarningChanged(string value) => OnPropertyChanged(nameof(HasSaveWarning));

    [RelayCommand]
    private void OpenTool(ToolCenterItem? item)
    {
        if (_disposed || item is null) return;
        var result = _actions.Open(item.ToolId, item.IsVisible);
        Error = result.Message;
        Refresh();
    }

    [RelayCommand]
    private void HideTool(ToolCenterItem? item)
    {
        if (_disposed || item is null) return;
        Error = _actions.Hide(item.ToolId).Message;
        Refresh();
    }

    [RelayCommand]
    private void ToggleFavorite(ToolCenterItem? item)
    {
        if (!_disposed && item is not null) _preferences.ToggleFavorite(item.ToolId, item.DisplayName);
    }
    [RelayCommand]
    private void HideAll()
    {
        if (_disposed) return;
        var result = _actions.HideAll();
        Error = result.FailureCount == 0 ? string.Empty :
            $"{result.FailureCount} 个工具未能隐藏：" + string.Join("；", result.Results.Where(pair => !pair.Value.Succeeded)
                .Select(pair => $"{_snapshot.FirstOrDefault(item => item.ToolId == pair.Key)?.DisplayName ?? pair.Key}：{pair.Value.Message}"));
        Refresh();
    }

    /// <summary>弹出菜单携带打开时的工具身份，不能用后来变化的 SelectedItem 代替目标。</summary>
    internal void AssignToolCategory(ToolCenterItem item, ToolCategory category)
    {
        if (!_disposed && !item.IsMissing) _preferences.AssignCategory(item.ToolId, category.Id);
    }

    internal void MoveToolFavorite(ToolCenterItem item, int direction)
    {
        if (!_disposed && item.IsFavorite) _preferences.MoveFavorite(item.ToolId, direction);
    }

    [RelayCommand]
    private void ClearSource() => SelectedSource = Sources.FirstOrDefault(item => item.Id == "all");

    [RelayCommand] private void AddCategory()
    {
        if (_disposed) return;
        var result = _preferences.AddCategory(CategoryName);
        Error = result.Message;
        if (result.Succeeded) { Refresh(); EditingCategory = Categories.FirstOrDefault(item => item.Id == result.CategoryId); }
    }
    [RelayCommand] private void RenameCategory()
    {
        if (!_disposed && EditingCategory is { } category) Error = _preferences.RenameCategory(category.Id, CategoryName).Message;
    }
    [RelayCommand] private void DeleteCategory()
    {
        if (!_disposed && EditingCategory is { } category) Error = _preferences.DeleteCategory(category.Id).Message;
    }

    private void Changed(object? sender, EventArgs args) => QueueRefresh();
    private void AvailabilityChanged(object? sender, PluginAvailabilityChangedEventArgs args) => QueueRefresh();
    private void QueueRefresh()
    {
        if (_disposed || Interlocked.Exchange(ref _refreshQueued, 1) != 0) return;
        Dispatcher.UIThread.Post(() => { Interlocked.Exchange(ref _refreshQueued, 0); if (!_disposed) Refresh(); });
    }

    internal void Refresh()
    {
        if (_disposed) return;
        _refreshing = true;
        try
        {
            _snapshot = _query.Capture();
            var registered = _snapshot.Where(item => !item.IsMissing).ToArray();
            var source = SelectedSource?.Id ?? "all";
            Sources = new[] { new ToolSource("all", "全部来源") }.Concat(registered
                .GroupBy(item => item.SourceId).Select(group => new ToolSource(group.Key, group.First().SourceName))
                .OrderBy(item => item.DisplayName, StringComparer.Ordinal)).ToArray();
            SelectedSource = Sources.FirstOrDefault(item => item.Id == source) ?? Sources[0];
            var editing = EditingCategory?.Id;
            Categories = _preferences.Categories;
            EditingCategory = Categories.FirstOrDefault(item => item.Id == editing);
            NavigationItems = new[]
            {
                new ToolNavigationItem("all", $"全部工具  {registered.Length}"),
                new ToolNavigationItem("favorites", $"常用工具  {_preferences.Current.FavoriteToolIds.Length}"),
                new ToolNavigationItem("recent", "最近使用"),
                new ToolNavigationItem("visible", $"已显示  {registered.Count(item => item.IsVisible)}"),
                new ToolNavigationItem("hidden", "已隐藏")
            }.Concat(Categories.Select(category => new ToolNavigationItem(category.Id,
                $"{category.DisplayName}  {registered.Count(item => item.CategoryId == category.Id)}"))).ToArray();
            SelectedNavigation = NavigationItems.FirstOrDefault(item => item.Id == _navigation) ?? NavigationItems[0];
            _navigation = SelectedNavigation.Id;
            Summary = $"共 {registered.Length} 个工具 · {registered.Count(item => item.IsVisible)} 个已显示";
            SaveWarning = _preferences.SaveWarning;
            OnPropertyChanged(nameof(CanHideAll));
        }
        finally { _refreshing = false; }
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (_disposed) return;
        var selectedId = SelectedItem?.ToolId;
        VisibleItems = _query.Filter(_snapshot, _navigation, SearchText, SelectedSource?.Id ?? "all");
        SelectedItem = VisibleItems.FirstOrDefault(item => item.ToolId == selectedId) ?? VisibleItems.FirstOrDefault();
        ResultsTitle = SearchText.Trim().Length > 0 ? "全部工具中的搜索结果" : SelectedNavigation?.DisplayName ?? "全部工具";
        if (HasSourceFilter) ResultsTitle += $" · 来源：{SelectedSource!.DisplayName}";
        OnPropertyChanged(nameof(HasNoResults)); OnPropertyChanged(nameof(EmptyText));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _workspace.LayoutChanged -= Changed;
        _preferences.Changed -= Changed;
        _availability.AvailabilityChanged -= AvailabilityChanged;
    }
}
