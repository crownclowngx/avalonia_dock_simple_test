using System.Collections.ObjectModel;
using BiliDownloader.Models;
using BiliDownloader.Models.ContentSources;
using BiliDownloader.Services.ContentSources;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BiliDownloader.ViewModels.BiliDownloader;

/// <summary>只负责单个来源的分页、显式选择和批量解析。</summary>
public partial class ContentSourceBrowserViewModel : ObservableObject
{
    private readonly IContentSourceProviderRegistry _registry;
    private readonly VideoParseResultFactory _resultFactory;
    private readonly Action<VideoParseResult> _onResolved;
    private ContentSourceDescriptor? _descriptor;
    private string? _nextToken;
    private ContentPageAccumulator _accumulator = new();

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
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private bool _hasMore;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _canRetry;

    public bool IsNotBusy => !IsBusy;

    public async Task OpenAsync(ContentSourceDescriptor descriptor)
    {
        _descriptor = descriptor;
        _nextToken = null;
        _accumulator = new ContentPageAccumulator();
        Items.Clear();
        Title = descriptor.DisplayName;
        await LoadMoreAsync(CancellationToken.None);
    }

    [RelayCommand]
    private async Task LoadMoreAsync(CancellationToken cancellationToken)
    {
        if (_descriptor is null || IsBusy) return;
        try
        {
            IsBusy = true;
            CanRetry = false;
            Status = "正在读取内容…";
            var provider = _registry.GetRequired(_descriptor.Kind);
            var request = new ContentPageRequest(20, _nextToken);
            var page = await provider.GetPageAsync(_descriptor, request, cancellationToken);
            foreach (var item in _accumulator.Append(provider, request, page))
                Items.Add(new ContentSourceItemViewModel(item));
            _nextToken = page.NextContinuationToken;
            HasMore = page.HasMore;
            Status = Items.Count == 0
                ? "此来源暂时没有内容。"
                : $"已加载 {Items.Count} 项，请勾选需要下载的内容。";
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
    private Task RetryAsync(CancellationToken cancellationToken) =>
        LoadMoreAsync(cancellationToken);

    private bool CanRetryLoad() => CanRetry && !IsBusy;

    [RelayCommand]
    private async Task ResolveSelectedAsync(CancellationToken cancellationToken)
    {
        if (_descriptor is null) return;
        var selected = Items.Where(item => item.IsSelected).Select(item => item.Item).ToArray();
        if (selected.Length == 0)
        {
            Status = "请至少选择一个内容项。";
            return;
        }

        try
        {
            IsBusy = true;
            Status = $"正在解析 {selected.Length} 项…";
            var provider = _registry.GetRequired(_descriptor.Kind);
            var collections = new List<BiliVideoCollection>();
            foreach (var item in selected)
                collections.Add(await provider.ResolveItemAsync(_descriptor, item, cancellationToken));
            var result = await _resultFactory.CreateAsync(
                collections, _descriptor.DisplayName, cancellationToken);
            _onResolved(result);
            Status = $"已解析 {result.VideoItems.Count} 个视频单元。";
        }
        catch (ContentSourceException ex) { Status = ex.Message; }
        catch (OperationCanceledException) { Status = "解析已取消。"; }
        catch { Status = "解析所选内容失败，请稍后重试。"; }
        finally { IsBusy = false; }
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotBusy));
        RetryCommand.NotifyCanExecuteChanged();
    }

    partial void OnCanRetryChanged(bool value) => RetryCommand.NotifyCanExecuteChanged();
}
