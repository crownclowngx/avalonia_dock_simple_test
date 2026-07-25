using System.ComponentModel;
using System.Collections.Specialized;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;

namespace MySmallTools.ViewModels.SecretVideoPlayer;

/// <summary>
/// 文件夹视频库 Document：只协调浏览状态、公共密码和现有播放器控件。
/// </summary>
public partial class SecretVideoLibraryViewModel :
    Document,
    IPlaybackNavigationContext,
    IDisposable
{
    private bool _disposed;
    private long _playGeneration;
    private CancellationTokenSource? _playCancellation;
    private CancellationTokenSource? _autoAdvanceCancellation;
    private long _lastHandledEndedGeneration;

    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private bool _showPassword;
    [ObservableProperty] private bool _isOpening;
    [ObservableProperty] private bool _isLibraryPaneOpen = true;
    [ObservableProperty] private string _statusMessage = "请选择文件夹并输入公共密码";
    [ObservableProperty] private string _currentPlayingPath = string.Empty;
    [ObservableProperty] private bool _isContinuousPlaybackEnabled;

    public VideoLibraryBrowserViewModel Browser { get; }
    public VideoPlayerControlViewModel PlayerViewModel { get; }
    public bool CanNavigatePrevious =>
        !_disposed &&
        Browser.FindVisibleAdjacent(CurrentPlayingPath, -1) is { FilePath: var path } &&
        File.Exists(path);
    public bool CanNavigateNext =>
        !_disposed &&
        Browser.FindVisibleAdjacent(CurrentPlayingPath, 1) is { FilePath: var path } &&
        File.Exists(path);

    // CommunityToolkit 生成的异步命令以更具体的类型公开；显式实现让 UI 端口只依赖
    // BCL 的 ICommand，不把工具包类型扩散到复用播放器控件。
    ICommand IPlaybackNavigationContext.PreviousCommand => PreviousCommand;
    ICommand IPlaybackNavigationContext.NextCommand => NextCommand;

    public SecretVideoLibraryViewModel(
        VideoLibraryBrowserViewModel browser,
        VideoPlayerControlViewModel playerViewModel)
    {
        Browser = browser ?? throw new ArgumentNullException(nameof(browser));
        PlayerViewModel = playerViewModel ?? throw new ArgumentNullException(nameof(playerViewModel));
        Browser.PropertyChanged += OnBrowserPropertyChanged;
        ((INotifyCollectionChanged)Browser.VisibleItems).CollectionChanged += OnVisibleItemsChanged;
        PlayerViewModel.MediaEnded += OnMediaEnded;
    }

    partial void OnPasswordChanged(string value) => PlaySelectedCommand.NotifyCanExecuteChanged();

    partial void OnIsOpeningChanged(bool value) => PlaySelectedCommand.NotifyCanExecuteChanged();

    partial void OnCurrentPlayingPathChanged(string value) => NotifyNavigationState();

    partial void OnIsContinuousPlaybackEnabledChanged(bool value)
    {
        if (!value)
        {
            // 只取消自动推进，不打断用户手动发起的媒体切换。
            TryCancel(Interlocked.Exchange(ref _autoAdvanceCancellation, null));
        }
    }

    /// <summary>
    /// 切换到一个新文件夹。刷新当前文件夹应直接调用 Browser.RefreshCommand，避免停止当前视频。
    /// </summary>
    public async Task OpenFolderAsync(string folderPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsOpening || string.IsNullOrWhiteSpace(folderPath))
            return;

        var fullPath = Path.GetFullPath(folderPath);
        if (!string.Equals(Browser.FolderPath, fullPath, StringComparison.OrdinalIgnoreCase))
        {
            await PlayerViewModel.CleanupMediaAsync();
            CurrentPlayingPath = string.Empty;
            StatusMessage = "已切换视频文件夹";
        }

        await Browser.LoadFolderAsync(fullPath);
    }

    [RelayCommand(CanExecute = nameof(CanPlaySelected))]
    private async Task PlaySelectedAsync()
    {
        var item = Browser.SelectedItem;
        if (item is null)
        {
            StatusMessage = "请选择要播放的视频";
            return;
        }
        await PlayItemAsync(item, PlaybackRequestOrigin.UserSelection);
    }

    private bool CanPlaySelected() => !_disposed &&
        Browser.SelectedItem is { FilePath: var path } &&
        File.Exists(path) &&
        !string.IsNullOrEmpty(Password);

    [RelayCommand(CanExecute = nameof(CanNavigatePrevious))]
    private async Task PreviousAsync()
    {
        var item = Browser.FindVisibleAdjacent(CurrentPlayingPath, -1);
        if (item is not null)
        {
            await PlayItemAsync(item, PlaybackRequestOrigin.Previous);
        }
    }

    [RelayCommand(CanExecute = nameof(CanNavigateNext))]
    private async Task NextAsync()
    {
        var item = Browser.FindVisibleAdjacent(CurrentPlayingPath, 1);
        if (item is not null)
        {
            await PlayItemAsync(item, PlaybackRequestOrigin.Next);
        }
    }

    [RelayCommand]
    private void ToggleLibraryPane() => IsLibraryPaneOpen = !IsLibraryPaneOpen;

    [RelayCommand]
    private void TogglePasswordVisibility() => ShowPassword = !ShowPassword;

    private void OnBrowserPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VideoLibraryBrowserViewModel.SelectedItem))
        {
            PlaySelectedCommand.NotifyCanExecuteChanged();
        }

        if (e.PropertyName is nameof(VideoLibraryBrowserViewModel.SearchText) or
            nameof(VideoLibraryBrowserViewModel.VisibleItemCount))
        {
            // 自动推进必须服从用户此刻看到的筛选列表。筛选变化使未提交的自动目标失效，
            // 但不会停止已经在播放的媒体。
            TryCancel(Interlocked.Exchange(ref _autoAdvanceCancellation, null));
            NotifyNavigationState();
        }
    }

    private void OnVisibleItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        NotifyNavigationState();

    private async void OnMediaEnded(object? sender, PlaybackMediaEndedEventArgs e)
    {
        if (_disposed ||
            !IsContinuousPlaybackEnabled ||
            e.MediaGeneration == Interlocked.Read(ref _lastHandledEndedGeneration) ||
            e.MediaGeneration != PlayerViewModel.PlaybackSnapshot.MediaGeneration)
        {
            return;
        }

        Interlocked.Exchange(ref _lastHandledEndedGeneration, e.MediaGeneration);
        var next = Browser.FindVisibleAdjacent(CurrentPlayingPath, 1);
        if (next is null)
        {
            StatusMessage = "播放完成，已到当前列表末尾";
            return;
        }

        try
        {
            await PlayItemAsync(next, PlaybackRequestOrigin.AutoAdvance);
        }
        catch (OperationCanceledException)
        {
            // 用户关闭连续播放、修改筛选或发起新播放时，自动推进被正常取消。
        }
    }

    private async Task PlayItemAsync(
        Models.SecretVideoPlayer.VideoLibraryItemViewModel item,
        PlaybackRequestOrigin origin)
    {
        if (string.IsNullOrEmpty(Password))
        {
            StatusMessage = "请输入公共密码";
            return;
        }
        if (!File.Exists(item.FilePath))
        {
            StatusMessage = "视频文件不存在或已被删除";
            PlaySelectedCommand.NotifyCanExecuteChanged();
            NotifyNavigationState();
            return;
        }

        Browser.SelectedItem = item;
        var generation = Interlocked.Increment(ref _playGeneration);
        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _playCancellation, cancellation);
        TryCancel(previous);
        if (origin == PlaybackRequestOrigin.AutoAdvance)
        {
            Interlocked.Exchange(ref _autoAdvanceCancellation, cancellation);
        }
        else
        {
            TryCancel(Interlocked.Exchange(ref _autoAdvanceCancellation, null));
        }

        IsOpening = true;
        StatusMessage = $"正在验证密码并打开 {item.DisplayName}...";
        try
        {
            var success = await PlayerViewModel.LoadAndPlayMediaAsync(
                item.FilePath,
                Password,
                cancellation.Token);
            if (_disposed || generation != Volatile.Read(ref _playGeneration))
            {
                return;
            }

            if (success)
            {
                // 路径是当前播放身份；密码和 Item 引用都不进入导航状态。
                CurrentPlayingPath = Path.GetFullPath(item.FilePath);
                StatusMessage = $"正在播放 {item.DisplayName}";
            }
            else
            {
                StatusMessage = PlayerViewModel.StatusMessage;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // 更新的手动请求或用户操作已经接管状态。
        }
        catch
        {
            if (!_disposed && generation == Volatile.Read(ref _playGeneration))
            {
                // 未知异常可能携带绝对路径或 LibVLC 原生文本。媒体库只显示稳定的
                // 可行动提示，详细原生异常不进入 UI、导航状态或日志。
                StatusMessage = "播放失败，请检查文件、密码和播放器状态";
            }
        }
        finally
        {
            Interlocked.CompareExchange(ref _playCancellation, null, cancellation);
            Interlocked.CompareExchange(ref _autoAdvanceCancellation, null, cancellation);
            cancellation.Dispose();
            if (!_disposed && generation == Volatile.Read(ref _playGeneration))
            {
                IsOpening = false;
            }
        }
    }

    private void NotifyNavigationState()
    {
        OnPropertyChanged(nameof(CanNavigatePrevious));
        OnPropertyChanged(nameof(CanNavigateNext));
        PreviousCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Interlocked.Increment(ref _playGeneration);
        TryCancel(Interlocked.Exchange(ref _playCancellation, null));
        TryCancel(Interlocked.Exchange(ref _autoAdvanceCancellation, null));
        Browser.PropertyChanged -= OnBrowserPropertyChanged;
        ((INotifyCollectionChanged)Browser.VisibleItems).CollectionChanged -= OnVisibleItemsChanged;
        PlayerViewModel.MediaEnded -= OnMediaEnded;
        Password = string.Empty;
        GC.SuppressFinalize(this);
    }

    private static void TryCancel(CancellationTokenSource? cancellation)
    {
        try
        {
            cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 请求可能恰好已在完成路径释放。
        }
    }

    private enum PlaybackRequestOrigin
    {
        UserSelection,
        Previous,
        Next,
        AutoAdvance
    }
}
