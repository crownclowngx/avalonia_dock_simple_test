using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using MySmallTools.ViewModels.SecretVideoPlayer.SingleVideo;

namespace MySmallTools.ViewModels.SecretVideoPlayer;

/// <summary>
/// 单文件播放器 Document 的兼容组合外壳。
/// </summary>
/// <remarks>
/// 新界面分别绑定 <see cref="Source"/> 和 <see cref="PublicInfo"/>；旧公开属性与命令继续
/// 转发到唯一状态所有者，保证宿主、既有测试和外部绑定在 G8 前无需破坏式迁移。
/// </remarks>
public sealed class SecretVideoPlayerViewModel : Document, IDisposable
{
    private bool _disposed;

    public VideoPlayerControlViewModel PlayerViewModel { get; }
    public SingleVideoSourceViewModel Source { get; }
    public PublicInfoEditorViewModel PublicInfo { get; }

    public SecretVideoPlayerViewModel(VideoPlayerControlViewModel playerViewModel)
    {
        PlayerViewModel = playerViewModel ?? throw new ArgumentNullException(nameof(playerViewModel));
        Source = new SingleVideoSourceViewModel(PlayerViewModel, OnFileChanged);
        PublicInfo = new PublicInfoEditorViewModel(PlayerViewModel, Source);
        Source.PropertyChanged += OnSourcePropertyChanged;
        PublicInfo.PropertyChanged += OnPublicInfoPropertyChanged;
    }

    public string FilePath { get => Source.FilePath; set => Source.FilePath = value; }
    public string Password { get => Source.Password; set => Source.Password = value; }
    public bool ShowPassword { get => Source.ShowPassword; set => Source.ShowPassword = value; }
    public string StatusMessage { get => Source.StatusMessage; set => Source.StatusMessage = value; }
    public bool IsLoading => Source.IsLoading;
    public string PublicTitle => PublicInfo.PublicTitle;
    public string PublicDescription => PublicInfo.PublicDescription;
    public bool HasPublicDescription => PublicInfo.HasPublicDescription;
    public bool IsEditingPublicInfo => PublicInfo.IsEditingPublicInfo;
    public string EditableTitle { get => PublicInfo.EditableTitle; set => PublicInfo.EditableTitle = value; }
    public string EditableDescription
    {
        get => PublicInfo.EditableDescription;
        set => PublicInfo.EditableDescription = value;
    }
    public int EditableTitleCharacterCount => PublicInfo.EditableTitleCharacterCount;
    public int EditableDescriptionCharacterCount => PublicInfo.EditableDescriptionCharacterCount;

    public IAsyncRelayCommand LoadVideoCommand => Source.LoadVideoCommand;
    public IRelayCommand TogglePasswordVisibilityCommand => Source.TogglePasswordVisibilityCommand;
    public IAsyncRelayCommand EditPublicInfoCommand => PublicInfo.EditPublicInfoCommand;
    public IRelayCommand SavePublicInfoCommand => PublicInfo.SavePublicInfoCommand;
    public IRelayCommand CancelEditPublicInfoCommand => PublicInfo.CancelEditPublicInfoCommand;

    public Task SelectFileAsync(
        string filePath,
        CancellationToken cancellationToken = default) =>
        Source.SelectFileAsync(filePath, cancellationToken);

    private void OnFileChanged(string path)
    {
        PublicInfo?.Read(path);
        PublicInfo?.EditPublicInfoCommand.NotifyCanExecuteChanged();
    }

    private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not null)
            OnPropertyChanged(e.PropertyName);
        if (e.PropertyName is nameof(Source.FilePath) or nameof(Source.IsLoading))
            PublicInfo.EditPublicInfoCommand.NotifyCanExecuteChanged();
    }

    private void OnPublicInfoPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not null)
            OnPropertyChanged(e.PropertyName);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Source.PropertyChanged -= OnSourcePropertyChanged;
        PublicInfo.PropertyChanged -= OnPublicInfoPropertyChanged;
        Source.Dispose();
        GC.SuppressFinalize(this);
    }
}
