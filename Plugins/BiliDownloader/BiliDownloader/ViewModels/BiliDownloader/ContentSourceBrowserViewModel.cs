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

/// <summary>
/// 单个来源的层级分页浏览器。
/// 设计意图：每一级保存独立分页与选择状态，返回上级时无需重新联网或重建勾选集合。
/// </summary>
public partial class ContentSourceBrowserViewModel : ObservableObject
{
    private readonly IContentSourceProviderRegistry _registry;
    private readonly VideoParseResultFactory _resultFactory;
    private readonly Action<VideoParseResult> _onResolved;
    private readonly List<BrowserLevelState> _levels = [];
    private ContentSourceDescriptor? _descriptor;

    public ContentSourceBrowserViewModel(
        IContentSourceProviderRegistry registry,
        VideoParseResultFactory resultFactory,
        Action<VideoParseResult> onResolved)
    {
        _registry = registry;
        _resultFactory = resultFactory;
        _onResolved = onResolved;
    }

    public ObservableCollection<ContentSourceItemViewModel> Items { get; } = [];
    public ObservableCollection<ContentSourceBreadcrumb> Breadcrumbs { get; } = [];
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private bool _hasMore;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _canRetry;
    [ObservableProperty] private bool _canGoBack;
    [ObservableProperty] private bool _canResolveCurrentSource;

    public bool IsNotBusy => !IsBusy;
    public bool IsReadOnlySource => _descriptor is not null && !CanResolveCurrentSource;
    public string ReadOnlyMessage => IsReadOnlySource
        ? "该来源在 P1-G2 中仅支持浏览，暂不创建下载任务。"
        : string.Empty;

    public async Task OpenAsync(ContentSourceDescriptor descriptor)
    {
        _descriptor = descriptor;
        _levels.Clear();
        _levels.Add(new BrowserLevelState(null, descriptor.DisplayName));
        RebuildBreadcrumbs();
        ApplyLevel();
        await LoadMoreAsync(CancellationToken.None);

        if (descriptor.PublicParameters.TryGetValue("autoOpen", out var autoOpen) &&
            string.Equals(autoOpen, "true", StringComparison.OrdinalIgnoreCase) &&
            Items.Count == 1 && Items[0].CanOpen)
            await EnterItemAsync(Items[0]);
    }

    [RelayCommand]
    private async Task LoadMoreAsync(CancellationToken cancellationToken)
    {
        if (_descriptor is null || IsBusy || _levels.Count == 0) return;
        var level = _levels[^1];
        try
        {
            IsBusy = true;
            CanRetry = false;
            Status = "正在读取内容…";
            var provider = _registry.GetRequired(_descriptor.Kind);
            var request = new ContentPageRequest(20, level.NextToken, parentKey: level.ParentKey);
            var page = await provider.GetPageAsync(_descriptor, request, cancellationToken);
            var supportsResolution = _registry.TryGetResolutionProvider(_descriptor.Kind, out _);
            foreach (var item in level.Accumulator.Append(provider, request, page))
                level.Items.Add(new ContentSourceItemViewModel(item, supportsResolution, EnterItemAsync));
            level.NextToken = page.NextContinuationToken;
            level.HasMore = page.HasMore;
            ApplyLevel();
            Status = Items.Count == 0
                ? "此来源暂时没有内容。"
                : $"已加载 {Items.Count} 项，请选择需要处理的内容。";
        }
        catch (ContentSourceException ex)
        {
            Status = ex.Message;
            CanRetry = true;
        }
        catch (OperationCanceledException) { Status = "读取已取消。"; }
        catch
        {
            Status = "读取内容失败，请稍后重试。";
            CanRetry = true;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanRetryLoad))]
    private Task RetryAsync(CancellationToken cancellationToken) => LoadMoreAsync(cancellationToken);

    private bool CanRetryLoad() => CanRetry && !IsBusy;

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (_levels.Count == 0 || IsBusy) return;
        _levels[^1].Reset();
        ApplyLevel();
        await LoadMoreAsync(cancellationToken);
    }

    [RelayCommand]
    private Task BackLevelAsync() => NavigateToDepthAsync(Math.Max(0, _levels.Count - 2));

    private Task EnterItemAsync(ContentSourceItemViewModel item)
    {
        if (!item.CanOpen || IsBusy) return Task.CompletedTask;
        _levels.Add(new BrowserLevelState(item.Item.Key, item.Title));
        RebuildBreadcrumbs();
        ApplyLevel();
        return LoadMoreAsync(CancellationToken.None);
    }

    private Task NavigateToDepthAsync(int depth)
    {
        if (depth < 0 || depth >= _levels.Count || IsBusy) return Task.CompletedTask;
        while (_levels.Count > depth + 1) _levels.RemoveAt(_levels.Count - 1);
        RebuildBreadcrumbs();
        ApplyLevel();
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ResolveSelectedAsync(CancellationToken cancellationToken)
    {
        if (_descriptor is null) return;
        if (!_registry.TryGetResolutionProvider(_descriptor.Kind, out var resolver))
        {
            Status = "该来源当前仅支持浏览。";
            return;
        }

        var selected = Items.Where(item => item.IsSelected && item.CanSelect).Select(item => item.Item).ToArray();
        if (selected.Length == 0)
        {
            Status = "请至少选择一个可下载内容项。";
            return;
        }

        try
        {
            IsBusy = true;
            Status = $"正在解析 {selected.Length} 项…";
            var collections = new List<BiliVideoCollection>();
            foreach (var item in selected)
                collections.Add(await resolver!.ResolveItemAsync(_descriptor, item, cancellationToken));
            var result = await _resultFactory.CreateAsync(collections, _descriptor.DisplayName, cancellationToken);
            _onResolved(result);
            Status = $"已解析 {result.VideoItems.Count} 个视频单元。";
        }
        catch (ContentSourceException ex) { Status = ex.Message; }
        catch (OperationCanceledException) { Status = "解析已取消。"; }
        catch { Status = "解析所选内容失败，请稍后重试。"; }
        finally { IsBusy = false; }
    }

    private void ApplyLevel()
    {
        Items.Clear();
        if (_levels.Count == 0) return;
        var level = _levels[^1];
        foreach (var item in level.Items) Items.Add(item);
        Title = level.Title;
        HasMore = level.HasMore;
        CanGoBack = _levels.Count > 1;
        CanResolveCurrentSource = _descriptor is not null &&
            _registry.TryGetResolutionProvider(_descriptor.Kind, out _);
        OnPropertyChanged(nameof(IsReadOnlySource));
        OnPropertyChanged(nameof(ReadOnlyMessage));
    }

    private void RebuildBreadcrumbs()
    {
        Breadcrumbs.Clear();
        for (var index = 0; index < _levels.Count; index++)
            Breadcrumbs.Add(new ContentSourceBreadcrumb(_levels[index].Title, index, NavigateToDepthAsync));
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotBusy));
        RetryCommand.NotifyCanExecuteChanged();
    }

    partial void OnCanRetryChanged(bool value) => RetryCommand.NotifyCanExecuteChanged();

    private sealed class BrowserLevelState(ContentItemKey? parentKey, string title)
    {
        public ContentItemKey? ParentKey { get; } = parentKey;
        public string Title { get; } = title;
        public ObservableCollection<ContentSourceItemViewModel> Items { get; } = [];
        public ContentPageAccumulator Accumulator { get; private set; } = new();
        public string? NextToken { get; set; }
        public bool HasMore { get; set; }

        public void Reset()
        {
            Items.Clear();
            Accumulator = new ContentPageAccumulator();
            NextToken = null;
            HasMore = false;
        }
    }
}
