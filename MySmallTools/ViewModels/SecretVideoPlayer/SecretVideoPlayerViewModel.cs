using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using MySmallTools.Business.SecretVideoPlayer;

namespace MySmallTools.ViewModels.SecretVideoPlayer;

/// <summary>
/// 安全视频页面的协调视图模型，负责公开信息、密码加载和播放器资源状态之间的切换。
/// </summary>
/// <remarks>
/// 选择文件后立即读取明文公开区，不等待密码；真正加载媒体时才验证密码。
/// 编辑信息前会主动释放播放器媒体，确保 LibVLC 不再持有文件句柄，随后才能安全地以读写方式打开同一容器。
/// </remarks>
public partial class SecretVideoPlayerViewModel : Document
{
    private bool _mediaLoaded;
    private string _rawPublicTitle = string.Empty;

    [ObservableProperty] private string _filePath = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private bool _showPassword;
    [ObservableProperty] private string _statusMessage = "请选择 SECVID03 加密视频文件";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private VideoPlayerControlViewModel _playerViewModel;
    [ObservableProperty] private string _publicTitle = string.Empty;
    [ObservableProperty] private string _publicDescription = string.Empty;
    [ObservableProperty] private bool _hasPublicDescription;
    [ObservableProperty] private bool _isEditingPublicInfo;
    [ObservableProperty] private string _editableTitle = string.Empty;
    [ObservableProperty] private string _editableDescription = string.Empty;

    public int EditableTitleCharacterCount => EncryptedVideoContainer.CountRunes(EditableTitle);
    public int EditableDescriptionCharacterCount => EncryptedVideoContainer.CountRunes(EditableDescription);

    public SecretVideoPlayerViewModel(VideoPlayerControlViewModel playerViewModel)
    {
        PlayerViewModel = playerViewModel ?? throw new ArgumentNullException(nameof(playerViewModel));
    }

    partial void OnPasswordChanged(string value) => LoadVideoCommand.NotifyCanExecuteChanged();

    partial void OnFilePathChanged(string value)
    {
        // 文件发生变化时旧媒体的流、缓存和文件句柄都已经失效，必须先完整清理再读取新文件的公开区。
        PlayerViewModel.CleanupMedia();
        _mediaLoaded = false;
        IsEditingPublicInfo = false;
        ReadPublicInfo(value);
        LoadVideoCommand.NotifyCanExecuteChanged();
        EditPublicInfoCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsLoadingChanged(bool value)
    {
        LoadVideoCommand.NotifyCanExecuteChanged();
        EditPublicInfoCommand.NotifyCanExecuteChanged();
    }

    partial void OnEditableTitleChanged(string value)
    {
        OnPropertyChanged(nameof(EditableTitleCharacterCount));
        SavePublicInfoCommand.NotifyCanExecuteChanged();
    }

    partial void OnEditableDescriptionChanged(string value)
    {
        OnPropertyChanged(nameof(EditableDescriptionCharacterCount));
        SavePublicInfoCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsEditingPublicInfoChanged(bool value)
    {
        SavePublicInfoCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanLoadVideo))]
    private async Task LoadVideoAsync()
    {
        if (!File.Exists(FilePath) || string.IsNullOrEmpty(Password))
        {
            StatusMessage = "请选择文件并输入密码";
            return;
        }

        IsLoading = true;
        StatusMessage = "正在验证密码并打开随机读取流...";
        try
        {
            var success = await PlayerViewModel.LoadMediaAsync(FilePath, Password);
            _mediaLoaded = success;
            StatusMessage = success ? "视频已加载，可以开始播放" : "加载失败：密码错误、文件已损坏或不是 SECVID03";
        }
        catch (Exception ex)
        {
            _mediaLoaded = false;
            StatusMessage = $"加载失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            EditPublicInfoCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanLoadVideo() => !IsLoading && File.Exists(FilePath);

    [RelayCommand(CanExecute = nameof(CanEditPublicInfo))]
    private void EditPublicInfo()
    {
        // “编辑信息”是明确的资源切换点：即使视频当前已暂停，也先释放 Media/Input，
        // 避免公开区保存时与 LibVLC 的后台读取发生共享冲突。
        if (_mediaLoaded)
        {
            PlayerViewModel.CleanupMedia();
            _mediaLoaded = false;
        }

        EditableTitle = _rawPublicTitle;
        EditableDescription = PublicDescription;
        IsEditingPublicInfo = true;
    }

    private bool CanEditPublicInfo() => !IsLoading && File.Exists(FilePath);

    [RelayCommand(CanExecute = nameof(CanSavePublicInfo))]
    private void SavePublicInfo()
    {
        try
        {
            EncryptedVideoContainer.UpdatePublicInfo(FilePath, EditableTitle, EditableDescription);
            IsEditingPublicInfo = false;
            ReadPublicInfo(FilePath);
            StatusMessage = "标题和描述已原地保存，视频密文未移动";
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存公开信息失败: {ex.Message}";
        }
    }

    private bool CanSavePublicInfo() => IsEditingPublicInfo &&
        EditableTitleCharacterCount <= EncryptedVideoContainer.MaxTitleRunes &&
        EditableDescriptionCharacterCount <= EncryptedVideoContainer.MaxDescriptionRunes;

    [RelayCommand]
    private void CancelEditPublicInfo() => IsEditingPublicInfo = false;

    [RelayCommand]
    private void TogglePasswordVisibility() => ShowPassword = !ShowPassword;

    private void ReadPublicInfo(string path)
    {
        // 先设置安全的容器文件名回退值。公开区损坏不应阻止用户继续输入密码验证视频主体。
        PublicTitle = string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetFileName(path);
        _rawPublicTitle = string.Empty;
        PublicDescription = string.Empty;
        HasPublicDescription = false;
        if (!File.Exists(path)) return;

        try
        {
            var info = EncryptedVideoContainer.ReadPublicInfo(path);
            _rawPublicTitle = info.Title;
            PublicTitle = string.IsNullOrEmpty(info.Title) ? info.OriginalFileName : info.Title;
            PublicDescription = info.Description;
            HasPublicDescription = !string.IsNullOrEmpty(info.Description);
            StatusMessage = "公开信息已读取，请输入密码播放";
        }
        catch (Exception ex)
        {
            PublicTitle = Path.GetFileName(path);
            PublicDescription = "描述不可读取";
            HasPublicDescription = true;
            StatusMessage = $"公开信息不可读取，文件可能不受支持或已经损坏；仍可尝试输入密码播放: {ex.Message}";
        }
    }
}
