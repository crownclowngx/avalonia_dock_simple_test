using CommunityToolkit.Mvvm.ComponentModel;
using MySmallTools.Business.SecretVideoPlayer.Decryption;
using MySmallTools.Business.SecretVideoPlayer.Operations;

namespace MySmallTools.ViewModels.SecretVideoPlayer;

/// <summary>
/// 单个解密候选的可观察状态；不持有密码，也不执行文件操作。
/// </summary>
public partial class DecryptionQueueItemViewModel : ObservableObject
{
    public DecryptionQueueItemViewModel(DecryptionCandidate candidate)
    {
        _candidate = candidate ?? throw new ArgumentNullException(nameof(candidate));
        State = candidate.IsValid ? VideoTaskState.Pending : VideoTaskState.Failed;
        Message = candidate.ValidationMessage;
        FailureCode = candidate.FailureCode;
    }

    [ObservableProperty] private DecryptionCandidate _candidate;
    [ObservableProperty] private VideoTaskState _state;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private string _outputPath = string.Empty;
    [ObservableProperty] private VideoTaskFailureCode? _failureCode;

    public string InputPath => Candidate.InputPath;
    public string EncryptedFileName => Candidate.EncryptedFileName;
    public string PublicTitle => Candidate.PublicTitle;
    public bool HasPublicTitle => !string.IsNullOrWhiteSpace(PublicTitle);
    public string FailureCodeText => FailureCode?.ToString() ?? string.Empty;
    public bool HasFailureCode => FailureCode.HasValue;

    public string StateText => State switch
    {
        VideoTaskState.Pending => "等待",
        VideoTaskState.Preflighting => "预检中",
        VideoTaskState.Ready => "就绪",
        VideoTaskState.Running => "解密中",
        VideoTaskState.Succeeded => "完成",
        VideoTaskState.Failed => "失败",
        VideoTaskState.Cancelled => "已取消",
        _ => string.Empty
    };

    public bool HasMessage => State != VideoTaskState.Succeeded && !string.IsNullOrWhiteSpace(Message);
    public bool HasOutputPath => State == VideoTaskState.Succeeded && !string.IsNullOrWhiteSpace(OutputPath);
    public bool IsRunning => State == VideoTaskState.Running;

    partial void OnStateChanged(VideoTaskState value)
    {
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(HasMessage));
        OnPropertyChanged(nameof(HasOutputPath));
    }

    partial void OnMessageChanged(string value) => OnPropertyChanged(nameof(HasMessage));
    partial void OnOutputPathChanged(string value) => OnPropertyChanged(nameof(HasOutputPath));
    partial void OnFailureCodeChanged(VideoTaskFailureCode? value)
    {
        OnPropertyChanged(nameof(FailureCodeText));
        OnPropertyChanged(nameof(HasFailureCode));
    }

    partial void OnCandidateChanged(DecryptionCandidate value)
    {
        OnPropertyChanged(nameof(InputPath));
        OnPropertyChanged(nameof(EncryptedFileName));
        OnPropertyChanged(nameof(PublicTitle));
        OnPropertyChanged(nameof(HasPublicTitle));
    }

    public void ApplyInspection(DecryptionCandidate candidate)
    {
        Candidate = candidate;
        Progress = 0;
        OutputPath = string.Empty;
        FailureCode = candidate.FailureCode;
        Message = candidate.ValidationMessage;
        State = candidate.IsValid ? VideoTaskState.Pending : VideoTaskState.Failed;
    }

    public void ApplyPreflight(CandidateDecryptionPreflight preflight)
    {
        OutputPath = preflight.OutputPath;
        var blocker = preflight.Result.Issues.FirstOrDefault(issue =>
            issue.Severity == PreflightSeverity.Blocking);
        if (blocker is not null)
        {
            State = VideoTaskState.Failed;
            FailureCode = blocker.Code;
            Message = $"{blocker.Message} {blocker.SuggestedAction}";
            return;
        }

        State = VideoTaskState.Ready;
        FailureCode = null;
        Message = preflight.Result.Issues.Count == 0
            ? "预检通过"
            : string.Join(" ", preflight.Result.Issues.Select(issue => $"{issue.Message} {issue.SuggestedAction}"));
    }

    public void ResetForRetry()
    {
        if (State == VideoTaskState.Succeeded)
            return;

        State = VideoTaskState.Pending;
        Progress = 0;
        Message = string.Empty;
        OutputPath = string.Empty;
        FailureCode = null;
    }
}
