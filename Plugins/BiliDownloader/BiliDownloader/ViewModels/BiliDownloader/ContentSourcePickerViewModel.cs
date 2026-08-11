using System.Collections.ObjectModel;
using BiliDownloader.Models.ContentSources;
using BiliDownloader.Services.ContentSources;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BiliDownloader.ViewModels.BiliDownloader;

public sealed record ContentSourceOption(
    ContentSourceKind Kind,
    string Name,
    string Description,
    bool SupportsManualInput,
    bool SupportsAccountShortcut,
    string InputPlaceholder,
    string AccountActionText);

/// <summary>只负责来源类型、标识输入和“我的收藏夹”发现，不持有内容分页状态。</summary>
public partial class ContentSourcePickerViewModel : ObservableObject, IDisposable
{
    private readonly IContentSourceProviderRegistry _registry;
    private readonly IFavoriteSourceDiscoveryService _favorites;
    private readonly Func<ContentSourceDescriptor, Task> _onOpened;
    // 来源发现既可能调用远端接口，也可能在返回后触发打开 Browser 的回调。联合父级令牌与
    // 本地释放令牌，可以取消远端等待；回调前的 IsDisposed 检查则负责拦截恰好在关闭竞态中
    // 返回的结果，避免重新创建或激活已经结束的来源浏览流程。
    private readonly CancellationToken _documentToken;
    private readonly CancellationTokenSource _disposeCts = new();
    private int _disposed;

    public ContentSourcePickerViewModel(
        IContentSourceProviderRegistry registry,
        IFavoriteSourceDiscoveryService favorites,
        Func<ContentSourceDescriptor, Task> onOpened,
        CancellationToken documentToken = default)
    {
        _registry = registry;
        _favorites = favorites;
        _onOpened = onOpened;
        _documentToken = documentToken;
        Options =
        [
            new(ContentSourceKind.Uploader, "UP 主投稿", "输入 UP 主空间链接或 UID", true, false, "输入 UID 或空间链接", ""),
            new(ContentSourceKind.Favorite, "收藏夹", "输入公开收藏夹链接，或读取我的收藏夹", true, false, "输入收藏夹链接或 media_id", ""),
            new(ContentSourceKind.WatchLater, "稍后再看", "读取当前账号的稍后再看", false, true, "", "打开稍后再看"),
            new(ContentSourceKind.History, "历史记录", "读取当前账号的观看历史", false, true, "", "打开历史记录"),
            new(ContentSourceKind.FollowingBangumi, "追番", "读取当前账号关注的番剧", false, true, "", "打开我的追番"),
            new(ContentSourceKind.FollowingCinema, "追剧", "读取当前账号关注的影视", false, true, "", "打开我的追剧"),
            new(ContentSourceKind.Collection, "订阅合集", "读取当前账号订阅的视频合集", false, true, "", "打开订阅合集"),
            new(ContentSourceKind.Course, "课程", "打开课程链接，或读取我的课程", true, true, "输入课程 ss/ep 链接或 ID", "读取我的课程"),
        ];
        SelectedOption = Options[0];
    }

    public IReadOnlyList<ContentSourceOption> Options { get; }
    public ObservableCollection<ContentSourceDescriptor> FavoriteFolders { get; } = [];

    [ObservableProperty] private ContentSourceOption _selectedOption;
    [ObservableProperty] private ContentSourceDescriptor? _selectedFavoriteFolder;
    [ObservableProperty] private string _input = string.Empty;
    [ObservableProperty] private string _status = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasFavoriteFolders;

    public bool ShowManualInput => SelectedOption.SupportsManualInput;
    public bool ShowAccountShortcut => SelectedOption.SupportsAccountShortcut;
    public bool IsFavoriteSelected => SelectedOption.Kind == ContentSourceKind.Favorite;

    [RelayCommand]
    private async Task OpenAsync(CancellationToken cancellationToken)
    {
        using var linked = Link(cancellationToken);
        cancellationToken = linked.Token;
        await RunAsync(async () =>
        {
            var provider = _registry.GetRequired(SelectedOption.Kind);
            var descriptor = await provider.NormalizeAsync(Input, cancellationToken);
            await _onOpened(descriptor);
        });
    }

    [RelayCommand]
    private async Task OpenAccountAsync(CancellationToken cancellationToken) =>
        await RunLinkedAsync(cancellationToken, async linkedToken =>
        {
            var provider = _registry.GetRequired(SelectedOption.Kind);
            var descriptor = await provider.NormalizeAsync("self", linkedToken);
            await _onOpened(descriptor);
        });

    [RelayCommand]
    private async Task LoadFavoriteFoldersAsync(CancellationToken cancellationToken) =>
        await RunLinkedAsync(cancellationToken, async linkedToken =>
        {
            FavoriteFolders.Clear();
            foreach (var folder in await _favorites.GetMyFoldersAsync(linkedToken))
                FavoriteFolders.Add(folder);
            HasFavoriteFolders = FavoriteFolders.Count > 0;
            Status = HasFavoriteFolders
                ? $"已读取 {FavoriteFolders.Count} 个收藏夹。"
                : "当前账号没有收藏夹。";
        });

    [RelayCommand]
    private async Task OpenFavoriteAsync(CancellationToken cancellationToken)
    {
        if (SelectedFavoriteFolder is null)
        {
            Status = "请先选择收藏夹。";
            return;
        }
        using var linked = Link(cancellationToken);
        await _onOpened(SelectedFavoriteFolder);
        linked.Token.ThrowIfCancellationRequested();
    }

    private async Task RunLinkedAsync(
        CancellationToken cancellationToken,
        Func<CancellationToken, Task> action)
    {
        using var linked = Link(cancellationToken);
        await RunAsync(() => action(linked.Token));
    }

    private async Task RunAsync(Func<Task> action)
    {
        try { IsBusy = true; Status = string.Empty; await action(); }
        catch (OperationCanceledException) when (IsDisposed) { }
        catch (ContentSourceException ex) { Status = ex.Message; }
        catch (OperationCanceledException) { Status = "操作已取消。"; }
        catch { Status = "读取来源失败，请稍后重试。"; }
        finally { if (!IsDisposed) IsBusy = false; }
    }

    private CancellationTokenSource Link(CancellationToken token) =>
        CancellationTokenSource.CreateLinkedTokenSource(token, _documentToken, _disposeCts.Token);

    private bool IsDisposed => Volatile.Read(ref _disposed) != 0 || _documentToken.IsCancellationRequested;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _disposeCts.Cancel();
        _disposeCts.Dispose();
    }

    partial void OnSelectedOptionChanged(ContentSourceOption value)
    {
        OnPropertyChanged(nameof(ShowManualInput));
        OnPropertyChanged(nameof(ShowAccountShortcut));
        OnPropertyChanged(nameof(IsFavoriteSelected));
        Status = string.Empty;
    }
}
