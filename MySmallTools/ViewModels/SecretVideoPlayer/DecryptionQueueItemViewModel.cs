using CommunityToolkit.Mvvm.ComponentModel;
using MySmallTools.Business.SecretVideoPlayer;

namespace MySmallTools.ViewModels.SecretVideoPlayer;

/// <summary>
/// 单个解密候选在页面中的可观察状态；不持有密码，也不执行文件操作。
/// </summary>
public partial class DecryptionQueueItemViewModel : ObservableObject
{
    public DecryptionQueueItemViewModel(DecryptionCandidate candidate)
    {
        Candidate = candidate ?? throw new ArgumentNullException(nameof(candidate));
        State = candidate.IsValid ? DecryptionItemState.Pending : DecryptionItemState.Failed;
        Message = candidate.ValidationMessage;
    }

    public DecryptionCandidate Candidate { get; }
    public string InputPath => Candidate.InputPath;
    public string EncryptedFileName => Candidate.EncryptedFileName;
    public string PublicTitle => Candidate.PublicTitle;
    public bool HasPublicTitle => !string.IsNullOrWhiteSpace(PublicTitle);

    [ObservableProperty] private DecryptionItemState _state;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private string _outputPath = string.Empty;

    public string StateText => State switch
    {
        DecryptionItemState.Pending => "等待",
        DecryptionItemState.Running => "解密中",
        DecryptionItemState.Succeeded => "完成",
        DecryptionItemState.Failed => "失败",
        DecryptionItemState.Cancelled => "已取消",
        _ => string.Empty
    };

    public bool HasMessage => State != DecryptionItemState.Succeeded && !string.IsNullOrWhiteSpace(Message);
    public bool HasOutputPath => State == DecryptionItemState.Succeeded && !string.IsNullOrWhiteSpace(OutputPath);
    public bool IsRunning => State == DecryptionItemState.Running;

    partial void OnStateChanged(DecryptionItemState value)
    {
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(HasMessage));
        OnPropertyChanged(nameof(HasOutputPath));
    }

    partial void OnMessageChanged(string value) => OnPropertyChanged(nameof(HasMessage));
    partial void OnOutputPathChanged(string value) => OnPropertyChanged(nameof(HasOutputPath));

    public void ResetForRetry()
    {
        if (!Candidate.IsValid || State == DecryptionItemState.Succeeded)
            return;

        State = DecryptionItemState.Pending;
        Progress = 0;
        Message = string.Empty;
        OutputPath = string.Empty;
    }
}
