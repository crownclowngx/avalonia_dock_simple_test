using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;

namespace MySmallTools.ViewModels.SecretVideoPlayer;

/// <summary>
/// 文件夹视频库 Document：只协调浏览状态、公共密码和现有播放器控件。
/// </summary>
public partial class SecretVideoLibraryViewModel : Document, IDisposable
{
    private bool _disposed;
    private long _playGeneration;

    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private bool _isOpening;
    [ObservableProperty] private bool _isLibraryPaneOpen = true;
    [ObservableProperty] private string _statusMessage = "请选择文件夹并输入公共密码";

    public VideoLibraryBrowserViewModel Browser { get; }
    public VideoPlayerControlViewModel PlayerViewModel { get; }

    public SecretVideoLibraryViewModel(
        VideoLibraryBrowserViewModel browser,
        VideoPlayerControlViewModel playerViewModel)
    {
        Browser = browser ?? throw new ArgumentNullException(nameof(browser));
        PlayerViewModel = playerViewModel ?? throw new ArgumentNullException(nameof(playerViewModel));
        Browser.PropertyChanged += OnBrowserPropertyChanged;
    }

    partial void OnPasswordChanged(string value) => PlaySelectedCommand.NotifyCanExecuteChanged();

    partial void OnIsOpeningChanged(bool value) => PlaySelectedCommand.NotifyCanExecuteChanged();

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
            PlayerViewModel.CleanupMedia();
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
        if (string.IsNullOrEmpty(Password))
        {
            StatusMessage = "请输入公共密码";
            return;
        }
        if (!File.Exists(item.FilePath))
        {
            StatusMessage = "视频文件不存在或已被删除";
            PlaySelectedCommand.NotifyCanExecuteChanged();
            return;
        }

        var generation = Interlocked.Increment(ref _playGeneration);
        IsOpening = true;
        StatusMessage = $"正在验证密码并打开 {item.DisplayName}...";
        try
        {
            var success = await PlayerViewModel.LoadAndPlayMediaAsync(item.FilePath, Password);
            if (_disposed || generation != Volatile.Read(ref _playGeneration))
                return;

            StatusMessage = success
                ? $"正在播放 {item.DisplayName}"
                : PlayerViewModel.StatusMessage;
        }
        catch (Exception ex)
        {
            if (!_disposed && generation == Volatile.Read(ref _playGeneration))
                StatusMessage = $"播放失败: {ex.Message}";
        }
        finally
        {
            if (!_disposed && generation == Volatile.Read(ref _playGeneration))
                IsOpening = false;
        }
    }

    private bool CanPlaySelected() => !_disposed &&
        !IsOpening &&
        Browser.SelectedItem is { FilePath: var path } &&
        File.Exists(path) &&
        !string.IsNullOrEmpty(Password);

    [RelayCommand]
    private void ToggleLibraryPane() => IsLibraryPaneOpen = !IsLibraryPaneOpen;

    private void OnBrowserPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VideoLibraryBrowserViewModel.SelectedItem))
            PlaySelectedCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Interlocked.Increment(ref _playGeneration);
        Browser.PropertyChanged -= OnBrowserPropertyChanged;
        Password = string.Empty;
        GC.SuppressFinalize(this);
    }
}
