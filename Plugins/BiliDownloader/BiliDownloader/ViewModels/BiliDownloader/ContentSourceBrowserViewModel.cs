using System.Collections.ObjectModel;
using BiliDownloader.Models;
using BiliDownloader.Models.ContentSources;
using BiliDownloader.Services.ContentSources;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BiliDownloader.ViewModels.BiliDownloader;

public sealed class ContentSourceBreadcrumb
{
    public ContentSourceBreadcrumb(string title, int depth, Func<int, Task> navigate)
    {
        Title = title;
        Depth = depth;
        NavigateCommand = new AsyncRelayCommand(() => navigate(depth));
    }

    public string Title { get; }
    public int Depth { get; }
    public IAsyncRelayCommand NavigateCommand { get; }
}

/// <summary>来源排序的展示选项，避免 XAML 直接绑定枚举名称。</summary>
public sealed record ContentSourceSortOption(string Label, ContentSourceSortOrder Value);

/// <summary>可组合的内容类型筛选项；变化回调只通知当前浏览会话重建查询。</summary>
public sealed class ContentSourceTypeFilterOption : ObservableObject
{
    private readonly Action _changed;
    private bool _isSelected;

    public ContentSourceTypeFilterOption(string label, ContentSourceItemType value, Action changed)
    {
        Label = label;
        Value = value;
        _changed = changed;
    }

    public string Label { get; }
    public ContentSourceItemType Value { get; }
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!SetProperty(ref _isSelected, value)) return;
            _changed();
        }
    }

    internal void Restore(bool value) => SetProperty(ref _isSelected, value);
}

/// <summary>
/// 单个来源的层级分页浏览器。
/// 设计意图：ViewModel 只组合筛选、缓存、选择和物化服务；稳定业务状态不寄存在可回收的 UI 行中。
/// </summary>
public partial class ContentSourceBrowserViewModel : ObservableObject, IDisposable
{
    private const int UiPageSize = ContentPageRequest.DefaultPageSize;
    private static readonly SourceFilterRules EmptyFilters = SourceFilterRules.Empty;
    private readonly IContentSourceProviderRegistry _registry;
    private readonly VideoParseResultFactory _resultFactory;
    private readonly Action<VideoParseResult> _onResolved;
    private readonly IContentPageCache _pageCache;
    private readonly IContentSelectionMaterializer _materializer;
    private readonly ContentQueryCoordinator _queryCoordinator;
    private readonly List<BrowserLevelState> _levels = [];
    private ContentSourceDescriptor? _descriptor;
    private CancellationTokenSource? _filterDebounceCts;
    private CancellationTokenSource? _resolveCts;
    private bool _synchronizingFilters;
    // 父级 Document 令牌用于统一关闭整棵页面对象树；本地 CTS 则表示当前 Browser 实例已经
    // 被所有者释放。分页加载、筛选防抖和选择解析还会各自创建更短生命周期的 CTS，从而同时
    // 支持“新操作替代旧操作”和“关闭页面终止全部操作”，而不把局部取消反向传播给 Document。
    private readonly CancellationToken _documentToken;
    private readonly CancellationTokenSource _disposeCts = new();
    private int _disposed;

    /// <summary>
    /// 仅在来源身份或可持久筛选发生变化时触发；分页、缓存和选择变化不会触发，
    /// 从而避免把纯会话状态错误计入 Document 的 IsModified。
    /// </summary>
    public event Action? PersistentStateChanged;

    public ContentSourceBrowserViewModel(
        IContentSourceProviderRegistry registry,
        VideoParseResultFactory resultFactory,
        Action<VideoParseResult> onResolved,
        IContentPageCache? pageCache = null,
        IContentSelectionMaterializer? materializer = null,
        ContentQueryCoordinator? queryCoordinator = null,
        CancellationToken documentToken = default)
    {
        _registry = registry;
        _resultFactory = resultFactory;
        _onResolved = onResolved;
        _pageCache = pageCache ?? new MemoryContentPageCache();
        _materializer = materializer ?? new ContentSelectionMaterializer();
        _queryCoordinator = queryCoordinator ?? new ContentQueryCoordinator();
        _documentToken = documentToken;

        SortOptions =
        [
            new ContentSourceSortOption("来源默认", ContentSourceSortOrder.ProviderDefault),
            new ContentSourceSortOption("最新发布", ContentSourceSortOrder.PublishedNewest),
            new ContentSourceSortOption("最早发布", ContentSourceSortOrder.PublishedOldest),
        ];
        _selectedSortOption = SortOptions[0];
        TypeFilterOptions = new ObservableCollection<ContentSourceTypeFilterOption>(
            Enum.GetValues<ContentSourceItemType>()
                .Where(static type => type != ContentSourceItemType.Unknown)
                .Select(type => new ContentSourceTypeFilterOption(TypeLabel(type), type, OnTypeFilterChanged)));
    }

    public ObservableCollection<ContentSourceItemViewModel> Items { get; } = [];
    public ObservableCollection<ContentSourceBreadcrumb> Breadcrumbs { get; } = [];
    public ObservableCollection<ContentSourceTypeFilterOption> TypeFilterOptions { get; }
    public IReadOnlyList<ContentSourceSortOption> SortOptions { get; }

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private bool _hasMore;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isResolvingSelection;
    [ObservableProperty] private bool _canRetry;
    [ObservableProperty] private bool _canGoBack;
    [ObservableProperty] private bool _canResolveCurrentSource;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private DateTimeOffset? _publishedFrom;
    [ObservableProperty] private DateTimeOffset? _publishedTo;
    [ObservableProperty] private ContentSourceSortOption _selectedSortOption;
    [ObservableProperty] private string _filterValidationMessage = string.Empty;
    [ObservableProperty] private string _selectionInvalidatedMessage = string.Empty;
    [ObservableProperty] private string _filterScopeText = string.Empty;
    [ObservableProperty] private int _loadedCount;
    [ObservableProperty] private int _displayedCount;

    public bool IsNotBusy => !IsBusy && !IsResolvingSelection;
    public bool AreFiltersEnabled => IsNotBusy && _levels.Count > 0;
    public bool IsReadOnlySource => _descriptor is not null && !CanResolveCurrentSource;
    public string ReadOnlyMessage => IsReadOnlySource
        ? "该来源当前仅支持浏览，不会创建下载任务。"
        : string.Empty;
    public bool HasFilterValidationMessage => !string.IsNullOrEmpty(FilterValidationMessage);
    public bool HasSelectionInvalidatedMessage => !string.IsNullOrEmpty(SelectionInvalidatedMessage);
    public bool HasFilterScopeNotice => !string.IsNullOrEmpty(FilterScopeText);
    public bool CanShowSelectionBar => CurrentLevel is { } level &&
        CanResolveCurrentSource && level.LoadedItems.Any(IsSelectable);
    public bool HasSelection => CurrentLevel?.Selection.HasSelection == true;
    public bool CanResolveSelection => HasSelection && IsNotBusy;
    public bool IsAllMatchingSelected =>
        CurrentLevel?.Selection.Scope == SelectionScope.AllMatchingResults;
    public bool CanSelectAllMatching => CurrentLevel is { } level &&
        level.Selection.Scope == SelectionScope.ExplicitItems &&
        HasMore && SelectableDisplayedItems().Any() &&
        SelectableDisplayedItems().All(static item => item.IsSelected);
    public bool ShowSelectAllMatchingPrompt => CanSelectAllMatching;
    public bool? LoadedSelectionState
    {
        get
        {
            var selectable = SelectableDisplayedItems().ToArray();
            if (selectable.Length == 0 || selectable.All(static item => !item.IsSelected)) return false;
            return selectable.All(static item => item.IsSelected) ? true : null;
        }
    }
    public string TypeFilterSummary
    {
        get
        {
            var selected = TypeFilterOptions.Where(static option => option.IsSelected).ToArray();
            return selected.Length switch
            {
                0 => "全部类型",
                1 => selected[0].Label,
                _ => $"已选 {selected.Length} 种类型",
            };
        }
    }
    public string SelectionSummaryText
    {
        get
        {
            var level = CurrentLevel;
            if (level is null) return "尚未加载内容";
            if (level.Selection.Scope == SelectionScope.AllMatchingResults)
                return $"已加载 {LoadedCount} · 显示 {DisplayedCount} · 全部匹配（排除 {level.Selection.ExclusionCount}）";

            var visibleSelected = Items.Count(static item => item.IsSelected);
            var hidden = Math.Max(0, level.Selection.ExplicitCount - visibleSelected);
            return hidden > 0
                ? $"已加载 {LoadedCount} · 显示 {DisplayedCount} · 已选 {level.Selection.ExplicitCount}（隐藏 {hidden}）"
                : $"已加载 {LoadedCount} · 显示 {DisplayedCount} · 已选 {level.Selection.ExplicitCount}";
        }
    }

    private BrowserLevelState? CurrentLevel => _levels.Count == 0 ? null : _levels[^1];

    public ContentSourceDescriptor? CurrentDescriptor => _descriptor;

    /// <summary>截取当前活动层级的筛选意图；层级路径和勾选状态有意不进入 V3。</summary>
    public SourceFilterRules CaptureFilters() => CurrentLevel?.Filters ?? SourceFilterRules.Empty;

    public async Task OpenAsync(
        ContentSourceDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _documentToken,
            _disposeCts.Token);
        cancellationToken = linked.Token;
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(descriptor);
        _descriptor = descriptor;
        _levels.Clear();
        var provider = _registry.GetRequired(descriptor.Kind);
        _levels.Add(new BrowserLevelState(null, descriptor.DisplayName, provider.Capabilities));
        AdvanceGeneration();
        RebuildBreadcrumbs();
        ApplyLevel();
        PersistentStateChanged?.Invoke();
        await LoadMoreCoreAsync(cancellationToken, _queryCoordinator.Generation);

        if (descriptor.PublicParameters.TryGetValue("autoOpen", out var autoOpen) &&
            string.Equals(autoOpen, "true", StringComparison.OrdinalIgnoreCase) &&
            Items.Count == 1 && Items[0].CanOpen)
            await EnterItemAsync(Items[0]);
    }

    /// <summary>
    /// 离线挂载已保存来源和筛选。该方法只构造本地浏览状态，绝不读取页面；
    /// 用户随后明确点击刷新时才会进入现有 Provider 查询路径。
    /// </summary>
    public void RestoreOffline(ContentSourceDescriptor descriptor, SourceFilterRules filters)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(filters);
        var provider = _registry.GetRequired(descriptor.Kind);
        _descriptor = descriptor;
        _levels.Clear();
        var level = new BrowserLevelState(null, descriptor.DisplayName, provider.Capabilities)
        {
            Filters = filters,
            Plan = ContentFilterPlanBuilder.Build(filters, provider.Capabilities),
        };
        _levels.Add(level);
        AdvanceGeneration();
        RebuildBreadcrumbs();
        ApplyLevel();
        Status = "来源方案已离线恢复；点击刷新后才会读取远端内容。";
        CanRetry = true;
    }

    [RelayCommand]
    private Task LoadMoreAsync(CancellationToken cancellationToken) =>
        LoadMoreCoreAsync(cancellationToken, _queryCoordinator.Generation);

    private async Task LoadMoreCoreAsync(CancellationToken cancellationToken, long generation)
    {
        if (_descriptor is null || CurrentLevel is null || IsResolvingSelection) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _queryCoordinator.Token);
        try
        {
            using var queryLease = await _queryCoordinator.EnterAsync(linked.Token);
            var level = CurrentLevel;
            if (level is null || !_queryCoordinator.IsCurrent(generation) || level.IsLoading) return;
            level.IsLoading = true;
            IsBusy = true;
            CanRetry = false;
            Status = level.LoadedItems.Count == 0 ? "正在读取内容…" : "正在加载更多内容…";

            var provider = _registry.GetRequired(_descriptor.Kind);
            var request = new ContentPageRequest(
                UiPageSize, level.NextToken, level.Plan.ServerRules, level.ParentKey);
            var cacheKey = new ContentPageCacheKey(
                _descriptor.Kind,
                _descriptor.StableSourceId,
                _descriptor.CapabilityVersion,
                level.ParentKey,
                level.Plan.Fingerprint,
                UiPageSize,
                level.NextToken);
            ContentPage page;
            if (!_pageCache.TryGet(cacheKey, out var cached))
            {
                page = await provider.GetPageAsync(_descriptor, request, linked.Token);
                if (!IsCurrent(generation, level)) return;
                _pageCache.Set(cacheKey, page);
            }
            else
            {
                page = cached!;
            }

            if (!IsCurrent(generation, level)) return;
            foreach (var item in level.Accumulator.Append(provider, request, page))
            {
                level.LoadedItems.Add(item);
                level.KnownItems[item.Key] = item;
            }
            level.NextToken = page.NextContinuationToken;
            level.HasMore = page.HasMore;
            RebuildVisibleItems(level);
            Status = level.LoadedItems.Count == 0
                ? "此来源暂时没有内容。"
                : Items.Count == 0
                    ? "当前已加载内容没有匹配项，可继续加载更多内容。"
                    : $"已加载 {level.LoadedItems.Count} 项，当前显示 {Items.Count} 项。";
        }
        catch (OperationCanceledException)
        {
            if (_queryCoordinator.IsCurrent(generation) && cancellationToken.IsCancellationRequested)
                Status = "读取已取消。";
        }
        catch (ContentSourceException ex)
        {
            if (_queryCoordinator.IsCurrent(generation))
            {
                Status = ex.Message;
                CanRetry = true;
            }
        }
        catch
        {
            if (_queryCoordinator.IsCurrent(generation))
            {
                Status = "读取内容失败，请稍后重试。";
                CanRetry = true;
            }
        }
        finally
        {
            if (_queryCoordinator.IsCurrent(generation))
            {
                if (CurrentLevel is { } current) current.IsLoading = false;
                IsBusy = false;
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanRetryLoad))]
    private Task RetryAsync(CancellationToken cancellationToken) => LoadMoreAsync(cancellationToken);

    private bool CanRetryLoad() => CanRetry && IsNotBusy;

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (_descriptor is null || CurrentLevel is null || IsResolvingSelection) return;
        var level = CurrentLevel;
        _pageCache.Invalidate(_descriptor, level.ParentKey, level.Plan.Fingerprint);
        level.ResetQuery();
        AdvanceGeneration();
        ApplyLevel();
        await LoadMoreCoreAsync(cancellationToken, _queryCoordinator.Generation);
    }

    [RelayCommand]
    private Task BackLevelAsync() => NavigateToDepthAsync(Math.Max(0, _levels.Count - 2));

    private Task EnterItemAsync(ContentSourceItemViewModel item)
    {
        if (!item.CanOpen || !IsNotBusy || _descriptor is null) return Task.CompletedTask;
        var provider = _registry.GetRequired(_descriptor.Kind);
        _levels.Add(new BrowserLevelState(item.Item.Key, item.Title, provider.Capabilities));
        AdvanceGeneration();
        RebuildBreadcrumbs();
        ApplyLevel();
        return LoadMoreCoreAsync(_documentToken, _queryCoordinator.Generation);
    }

    private Task NavigateToDepthAsync(int depth)
    {
        if (depth < 0 || depth >= _levels.Count || !IsNotBusy) return Task.CompletedTask;
        while (_levels.Count > depth + 1) _levels.RemoveAt(_levels.Count - 1);
        AdvanceGeneration();
        RebuildBreadcrumbs();
        ApplyLevel();
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void SelectLoaded()
    {
        if (CurrentLevel is not { } level || !CanResolveCurrentSource || !IsNotBusy) return;
        level.Selection.SelectLoaded(
            Items.Where(static item => item.CanSelect).Select(static item => item.Item.Key),
            level.Plan.Fingerprint);
        SelectionInvalidatedMessage = string.Empty;
        RefreshSelectionPresentation();
    }

    [RelayCommand]
    private void ToggleLoadedSelection()
    {
        if (LoadedSelectionState == true) DeselectLoaded();
        else SelectLoaded();
    }

    [RelayCommand]
    private void DeselectLoaded()
    {
        if (CurrentLevel is not { } level || !IsNotBusy) return;
        level.Selection.DeselectLoaded(
            Items.Where(static item => item.CanSelect).Select(static item => item.Item.Key),
            level.Plan.Fingerprint);
        RefreshSelectionPresentation();
    }

    [RelayCommand]
    private void ClearSelection()
    {
        if (CurrentLevel is not { } level || !IsNotBusy) return;
        level.Selection.Clear();
        SelectionInvalidatedMessage = string.Empty;
        RefreshSelectionPresentation();
    }

    [RelayCommand]
    private void SelectAllMatching()
    {
        if (CurrentLevel is not { } level || !CanSelectAllMatching || !IsNotBusy) return;
        level.Selection.SelectAllMatching(level.Plan.Fingerprint);
        SelectionInvalidatedMessage = string.Empty;
        RefreshSelectionPresentation();
    }

    [RelayCommand]
    private async Task ResolveSelectedAsync(CancellationToken cancellationToken)
    {
        if (_descriptor is null || CurrentLevel is not { } level || IsResolvingSelection) return;
        if (!_registry.TryGetResolutionProvider(_descriptor.Kind, out var resolver))
        {
            Status = "该来源当前仅支持浏览。";
            return;
        }

        IReadOnlyList<ContentSourceItem> selected;
        _resolveCts?.Cancel();
        _resolveCts?.Dispose();
        _resolveCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _documentToken,
            _disposeCts.Token);
        try
        {
            IsResolvingSelection = true;
            IsBusy = true;
            AdvanceGeneration(cancelResolve: false);
            if (level.Selection.Scope == SelectionScope.AllMatchingResults)
            {
                Status = "正在枚举全部匹配内容…";
                var provider = _registry.GetRequired(_descriptor.Kind);
                var progress = new Progress<ContentMaterializationProgress>(value =>
                    Status = $"正在枚举第 {value.PageCount} 页，已匹配 {value.MatchCount} 项…");
                selected = await _materializer.MaterializeAllMatchingAsync(
                    provider, _descriptor, level.ParentKey, level.Filters,
                    level.Selection, progress, _resolveCts.Token);
            }
            else
            {
                selected = level.Selection.SelectedKeys
                    .Select(key => level.KnownItems.TryGetValue(key, out var item) ? item : null)
                    .Where(static item => item is not null && IsSelectable(item))
                    .Cast<ContentSourceItem>()
                    .ToArray();
            }

            if (selected.Count == 0)
            {
                Status = "请至少选择一个可下载内容项。";
                return;
            }

            Status = $"正在解析 {selected.Count} 项…";
            var collections = new List<BiliVideoCollection>(selected.Count);
            foreach (var item in selected)
                collections.Add(await resolver!.ResolveItemAsync(_descriptor, item, _resolveCts.Token));
            var result = await _resultFactory.CreateAsync(collections, _descriptor.DisplayName, _resolveCts.Token);
            _onResolved(result);
            Status = $"已解析 {result.VideoItems.Count} 个视频单元。";
        }
        catch (ContentSourceException ex) { Status = ex.Message; }
        catch (OperationCanceledException) { Status = "解析已取消。"; }
        catch { Status = "解析所选内容失败，请稍后重试。"; }
        finally
        {
            IsResolvingSelection = false;
            IsBusy = false;
            _resolveCts?.Dispose();
            _resolveCts = null;
            RefreshStateProperties();
        }
    }

    [RelayCommand]
    private void CancelResolve() => _resolveCts?.Cancel();

    [RelayCommand]
    private Task ResetFiltersAsync()
    {
        if (!AreFiltersEnabled) return Task.CompletedTask;
        _synchronizingFilters = true;
        SearchText = string.Empty;
        PublishedFrom = null;
        PublishedTo = null;
        SelectedSortOption = SortOptions[0];
        foreach (var option in TypeFilterOptions) option.Restore(false);
        _synchronizingFilters = false;
        OnPropertyChanged(nameof(TypeFilterSummary));
        return ApplyFiltersAsync();
    }

    partial void OnSearchTextChanged(string value)
    {
        if (_synchronizingFilters) return;
        _filterDebounceCts?.Cancel();
        _filterDebounceCts?.Dispose();
        _filterDebounceCts = CancellationTokenSource.CreateLinkedTokenSource(
            _documentToken,
            _disposeCts.Token);
        _ = ApplySearchAfterDebounceAsync(_filterDebounceCts.Token);
    }

    partial void OnPublishedFromChanged(DateTimeOffset? value)
    {
        if (!_synchronizingFilters) _ = ApplyFiltersAsync();
    }

    partial void OnPublishedToChanged(DateTimeOffset? value)
    {
        if (!_synchronizingFilters) _ = ApplyFiltersAsync();
    }

    partial void OnSelectedSortOptionChanged(ContentSourceSortOption value)
    {
        if (!_synchronizingFilters) _ = ApplyFiltersAsync();
    }

    partial void OnFilterValidationMessageChanged(string value) =>
        OnPropertyChanged(nameof(HasFilterValidationMessage));
    partial void OnSelectionInvalidatedMessageChanged(string value) =>
        OnPropertyChanged(nameof(HasSelectionInvalidatedMessage));

    private void OnTypeFilterChanged()
    {
        OnPropertyChanged(nameof(TypeFilterSummary));
        if (!_synchronizingFilters) _ = ApplyFiltersAsync();
    }

    private async Task ApplySearchAfterDebounceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(250, cancellationToken);
            await ApplyFiltersAsync();
        }
        catch (OperationCanceledException) { }
    }

    private async Task ApplyFiltersAsync()
    {
        if (_descriptor is null || CurrentLevel is not { } level || IsResolvingSelection) return;
        if (PublishedFrom.HasValue && PublishedTo.HasValue && PublishedFrom > PublishedTo)
        {
            FilterValidationMessage = "开始日期不能晚于结束日期。";
            return;
        }

        FilterValidationMessage = string.Empty;
        var rules = new SourceFilterRules(
            SearchText,
            PublishedFrom,
            PublishedTo.HasValue ? ToInclusiveDayEnd(PublishedTo.Value) : null,
            TypeFilterOptions.Where(static option => option.IsSelected).Select(static option => option.Value).ToArray(),
            SelectedSortOption.Value);
        var provider = _registry.GetRequired(_descriptor.Kind);
        var plan = ContentFilterPlanBuilder.Build(rules, provider.Capabilities);
        if (plan.Fingerprint == level.Plan.Fingerprint) return;

        if (level.Selection.InvalidateAllMatching(plan.Fingerprint))
            SelectionInvalidatedMessage = "筛选条件已变化，“全部匹配”选择已清除，请重新确认选择范围。";
        level.Filters = rules;
        level.Plan = plan;
        PersistentStateChanged?.Invoke();
        level.ResetQuery();
        AdvanceGeneration();
        ApplyLevel();
        await LoadMoreCoreAsync(_documentToken, _queryCoordinator.Generation);
    }

    private void ApplyLevel()
    {
        Items.Clear();
        if (CurrentLevel is not { } level) return;
        SynchronizeFilterControls(level.Filters);
        RebuildVisibleItems(level);
        Title = level.Title;
        HasMore = level.HasMore;
        CanGoBack = _levels.Count > 1;
        CanResolveCurrentSource = _descriptor is not null &&
            _registry.TryGetResolutionProvider(_descriptor.Kind, out _);
        FilterScopeText = BuildFilterScopeText(level);
        RefreshStateProperties();
    }

    private void RebuildVisibleItems(BrowserLevelState level)
    {
        var supportsResolution = _descriptor is not null &&
            _registry.TryGetResolutionProvider(_descriptor.Kind, out _);
        var filtered = ContentSourceFilterEngine.Apply(level.LoadedItems, level.Plan.ResidualRules);
        Items.Clear();
        foreach (var item in filtered)
            Items.Add(new ContentSourceItemViewModel(
                item,
                supportsResolution,
                EnterItemAsync,
                level.Selection,
                () => level.Plan.Fingerprint,
                RefreshSelectionPresentation));
        LoadedCount = level.LoadedItems.Count;
        DisplayedCount = Items.Count;
        HasMore = level.HasMore;
        RefreshSelectionPresentation();
    }

    private void SynchronizeFilterControls(SourceFilterRules filters)
    {
        _synchronizingFilters = true;
        SearchText = filters.Keyword ?? string.Empty;
        PublishedFrom = filters.PublishedFrom;
        PublishedTo = filters.PublishedTo;
        SelectedSortOption = SortOptions.First(option => option.Value == filters.SortOrder);
        foreach (var option in TypeFilterOptions)
            option.Restore(filters.MediaTypes.Contains(option.Value));
        _synchronizingFilters = false;
        OnPropertyChanged(nameof(TypeFilterSummary));
    }

    private void RefreshSelectionPresentation()
    {
        foreach (var item in Items) item.RefreshSelection();
        RefreshStateProperties();
    }

    private void RefreshStateProperties()
    {
        OnPropertyChanged(nameof(IsNotBusy));
        OnPropertyChanged(nameof(AreFiltersEnabled));
        OnPropertyChanged(nameof(IsReadOnlySource));
        OnPropertyChanged(nameof(ReadOnlyMessage));
        OnPropertyChanged(nameof(CanShowSelectionBar));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanResolveSelection));
        OnPropertyChanged(nameof(IsAllMatchingSelected));
        OnPropertyChanged(nameof(CanSelectAllMatching));
        OnPropertyChanged(nameof(ShowSelectAllMatchingPrompt));
        OnPropertyChanged(nameof(SelectionSummaryText));
        OnPropertyChanged(nameof(LoadedSelectionState));
        OnPropertyChanged(nameof(HasFilterScopeNotice));
        RetryCommand.NotifyCanExecuteChanged();
    }

    private void RebuildBreadcrumbs()
    {
        Breadcrumbs.Clear();
        for (var index = 0; index < _levels.Count; index++)
            Breadcrumbs.Add(new ContentSourceBreadcrumb(_levels[index].Title, index, NavigateToDepthAsync));
    }

    partial void OnIsBusyChanged(bool value) => RefreshStateProperties();
    partial void OnIsResolvingSelectionChanged(bool value) => RefreshStateProperties();
    partial void OnCanRetryChanged(bool value) => RetryCommand.NotifyCanExecuteChanged();

    private void AdvanceGeneration(bool cancelResolve = true)
    {
        _queryCoordinator.Advance();
        if (cancelResolve) _resolveCts?.Cancel();
    }

    private bool IsCurrent(long generation, BrowserLevelState level) =>
        _queryCoordinator.IsCurrent(generation) && ReferenceEquals(CurrentLevel, level);

    private IEnumerable<ContentSourceItemViewModel> SelectableDisplayedItems() =>
        Items.Where(static item => item.CanSelect);

    private string BuildFilterScopeText(BrowserLevelState level)
    {
        if (_descriptor is null) return string.Empty;
        var capabilities = _registry.GetRequired(_descriptor.Kind).Capabilities;
        var supportsAllFields =
            capabilities.HasFlag(ContentSourceCapabilities.SupportsKeyword) &&
            capabilities.HasFlag(ContentSourceCapabilities.SupportsDateRange) &&
            capabilities.HasFlag(ContentSourceCapabilities.SupportsTypeFilter);
        return supportsAllFields && !level.Plan.HasResidualRules
            ? "关键词、日期和类型筛选由来源服务执行。"
            : "当前来源不支持完整服务端筛选；条件先作用于已加载内容，选择全部匹配时会枚举完整来源。";
    }

    private static DateTimeOffset ToInclusiveDayEnd(DateTimeOffset value) =>
        new DateTimeOffset(value.Year, value.Month, value.Day, 0, 0, 0, value.Offset)
            .AddDays(1)
            .AddTicks(-1);

    private static bool IsSelectable(ContentSourceItem item) =>
        item.NodeKind == ContentSourceNodeKind.Media &&
        item.AccessState == ContentAccessState.Available;

    private static string TypeLabel(ContentSourceItemType type) => type switch
    {
        ContentSourceItemType.Video => "视频",
        ContentSourceItemType.Bangumi => "番剧",
        ContentSourceItemType.Cinema => "影视",
        ContentSourceItemType.Collection => "合集",
        ContentSourceItemType.Course => "课程",
        _ => "未知",
    };

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        // 先取消具体操作，再取消实例令牌，并通过 Coordinator generation 使已经越过 await 的
        // 旧查询失效。这里不等待网络或解析任务退出；所有结果应用点依靠令牌、generation 与
        // disposed 三重门禁拒绝迟到写入，保证关闭标签不会卡住 Dock 线程。
        _filterDebounceCts?.Cancel();
        _resolveCts?.Cancel();
        _disposeCts.Cancel();
        _queryCoordinator.Dispose();
        _filterDebounceCts?.Dispose();
        _resolveCts?.Dispose();
        _disposeCts.Dispose();
    }

    private sealed class BrowserLevelState
    {
        public BrowserLevelState(
            ContentItemKey? parentKey,
            string title,
            ContentSourceCapabilities capabilities)
        {
            ParentKey = parentKey;
            Title = title;
            Plan = ContentFilterPlanBuilder.Build(EmptyFilters, capabilities);
        }

        public ContentItemKey? ParentKey { get; }
        public string Title { get; }
        public List<ContentSourceItem> LoadedItems { get; } = [];
        public Dictionary<ContentItemKey, ContentSourceItem> KnownItems { get; } = [];
        public ContentSelectionState Selection { get; } = new();
        public ContentPageAccumulator Accumulator { get; private set; } = new();
        public SourceFilterRules Filters { get; set; } = EmptyFilters;
        public ContentFilterPlan Plan { get; set; }
        public string? NextToken { get; set; }
        public bool HasMore { get; set; }
        public bool IsLoading { get; set; }

        public void ResetQuery()
        {
            LoadedItems.Clear();
            Accumulator = new ContentPageAccumulator();
            NextToken = null;
            HasMore = false;
            IsLoading = false;
        }
    }
}
