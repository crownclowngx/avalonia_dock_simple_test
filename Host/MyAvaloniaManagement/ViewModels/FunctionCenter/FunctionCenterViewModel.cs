using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyAvaloniaManagement.Business.Documents;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.Business.Workspace;
using MyAvaloniaManagement.Models.Tools;

namespace MyAvaloniaManagement.ViewModels.FunctionCenter;

/// <summary>拥有一次功能选择会话的搜索、选择与提交状态；不拥有 Document 的生命周期。</summary>
/// <remarks>
/// 目录只读查询与实际创建用例分别注入。窗口关闭即释放本会话的订阅；插件页面只会在用户提交后
/// 由已有协调器创建。成功判断使用本次返回值，避免全局历史错误导致误关闭或阻止正常创建。
/// </remarks>
internal sealed partial class FunctionCenterViewModel : ObservableObject, IDisposable
{
    private readonly DocumentCreationMenuQuery _query;
    private readonly DocumentPersistenceCoordinator _documents;
    private readonly DocumentOperationState _operationState;
    private DocumentCreationDirectory _directory;
    private string _searchText = string.Empty;
    private NavigationTreeNode? _selectedCategory;
    private DocumentCreationItem? _selectedItem;
    private bool _isBusy;
    private bool _completed;
    private bool _disposed;
    private bool _updatingProjection;
    private string _error = string.Empty;

    public FunctionCenterViewModel(DocumentCreationMenuQuery query, DocumentPersistenceCoordinator documents,
        DocumentOperationState operationState)
    {
        _query = query ?? throw new ArgumentNullException(nameof(query));
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _operationState = operationState ?? throw new ArgumentNullException(nameof(operationState));
        _directory = _query.ReadDirectory();
        Categories = NavigationTreeNode.Build(_directory.Categories, false);
        VisibleItems = _directory.Items;
        _query.Changed += OnDirectoryChanged;
    }

    /// <summary>只通知窗口“本次文档已经创建”，窗口关闭不反过来释放已发布 Document。</summary>
    internal event EventHandler? Created;
    public IReadOnlyList<NavigationTreeNode> Categories { get; private set; }
    public IReadOnlyList<DocumentCreationItem> VisibleItems { get; private set; }
    public bool HasNoResults => VisibleItems.Count == 0;
    public bool IsIdle => !IsBusy;
    public bool HasError => Error.Length > 0;
    public string ResultsTitle => string.IsNullOrWhiteSpace(SearchText)
        ? SelectedCategory?.Category?.Path.DisplayPath ?? "全部功能"
        : "全部分类中的搜索结果";
    public string ResultCount => $"{VisibleItems.Count} 个功能";
    public bool CanCreate => !_disposed && !_completed && !IsBusy && SelectedItem is not null;

    public string SearchText
    {
        get => _searchText;
        set { if (!IsBusy && SetProperty(ref _searchText, value ?? string.Empty)) Filter(); }
    }

    public NavigationTreeNode? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (IsBusy || _updatingProjection) return;
            SetProperty(ref _selectedCategory, value);
            // 点击同一个分类也应退出全局搜索，不能仅在选择值变化时清空搜索。
            if (_searchText.Length > 0) { _searchText = string.Empty; OnPropertyChanged(nameof(SearchText)); }
            Filter();
        }
    }

    public DocumentCreationItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (IsBusy || _updatingProjection || (value is not null && !VisibleItems.Contains(value))) return;
            if (SetProperty(ref _selectedItem, value)) UpdateCreateState();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(IsIdle));
            UpdateCreateState();
        }
    }

    public string Error
    {
        get => _error;
        private set { if (SetProperty(ref _error, value)) OnPropertyChanged(nameof(HasError)); }
    }

    [RelayCommand]
    private void ShowAll() => SelectedCategory = null;

    /// <summary>显式忙碌检查同时保护直接调用和双击事件，不能只依赖按钮禁用来防止重复提交。</summary>
    [RelayCommand(CanExecute = nameof(CanCreate))]
    public async Task CreateAsync()
    {
        if (!CanCreate) return;
        var entry = SelectedItem!.Entry;
        IsBusy = true;
        Error = string.Empty;
        var succeeded = false;
        try
        {
            var result = await _documents.CreateDocumentAsync(entry.DocumentTypeId, entry.CreationIntentId);
            _operationState.Apply(result);
            succeeded = result.ShouldUpdateError && string.IsNullOrEmpty(result.Error);
            _completed = succeeded;
            Error = succeeded ? string.Empty : result.Error;
        }
        catch (Exception exception)
        {
            // 正常插件失败已由协调器映射；这里兜住关闭入口等 Host 异常，不把异常正文展示给用户。
            Console.Error.WriteLine($"FunctionCenter errorCode=DOCUMENT_CREATE_FAILED type={exception.GetType().Name}");
            Error = "当前无法新建文档，请确认工作区仍然可用后重试。";
            _operationState.Apply(DocumentOperationResult.Failure(Error));
        }
        finally { IsBusy = false; }
        if (succeeded && !_disposed) Created?.Invoke(this, EventArgs.Empty);
    }

    private void Filter()
    {
        // ItemsSource 替换会同步回写控件的临时空选择。它属于投影更新，不能覆盖已按稳定 ID
        // 恢复的选择，也不能当作用户点击分类而清空搜索；这里只保护 UI 线程内的绑定重入。
        var updating = _updatingProjection;
        _updatingProjection = true;
        try
        {
            VisibleItems = _directory.Filter(SelectedCategory?.Category?.Path, SearchText);
            var previous = _selectedItem;
            _selectedItem = previous is null ? null : VisibleItems.FirstOrDefault(item =>
                item.Entry.DocumentTypeId == previous.Entry.DocumentTypeId &&
                item.Entry.CreationIntentId == previous.Entry.CreationIntentId);
            OnPropertyChanged(nameof(VisibleItems));
            OnPropertyChanged(nameof(SelectedItem));
            OnPropertyChanged(nameof(HasNoResults));
            OnPropertyChanged(nameof(ResultsTitle));
            OnPropertyChanged(nameof(ResultCount));
            UpdateCreateState();
        }
        finally { _updatingProjection = updating; }
    }

    private void UpdateCreateState()
    {
        OnPropertyChanged(nameof(CanCreate));
        CreateCommand.NotifyCanExecuteChanged();
    }

    private void OnDirectoryChanged(object? sender, PluginAvailabilityChangedEventArgs args)
    {
        if (Dispatcher.UIThread.CheckAccess()) Refresh();
        else Dispatcher.UIThread.Post(Refresh);
    }

    private void Refresh()
    {
        if (_disposed) return;
        _updatingProjection = true;
        try
        {
            var selectedKey = SelectedCategory?.Key;
            _directory = _query.ReadDirectory();
            Categories = NavigationTreeNode.Build(_directory.Categories, false, Categories);
            _selectedCategory = NavigationTreeNode.Flatten(Categories).FirstOrDefault(node => node.Key == selectedKey);
            OnPropertyChanged(nameof(Categories));
            OnPropertyChanged(nameof(SelectedCategory));
            Filter();
        }
        finally { _updatingProjection = false; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _query.Changed -= OnDirectoryChanged;
        Created = null;
        UpdateCreateState();
    }
}
