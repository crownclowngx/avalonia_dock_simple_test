using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using MySmallTools.Business.SecretVideoPlayer.Container;
using MySmallTools.Business.SecretVideoPlayer.Encryption;
using MySmallTools.Business.SecretVideoPlayer.Operations;

namespace MySmallTools.ViewModels.SecretVideoPlayer;

/// <summary>
/// 单文件加密 Document：持有当前表单密码，协调预检、执行、取消和界面状态。
/// </summary>
public partial class VideoEncryptorViewModel : Document, IDisposable
{
    private readonly IVideoEncryptionService _encryptorService;
    private readonly ObservableCollection<VideoPreflightIssue> _preflightIssues = [];
    private CancellationTokenSource? _operationCancellation;
    private DateTime _startedAt;
    private long _processedBytes;
    private long _totalBytes;
    private int _operationGeneration;
    private bool _disposed;

    public VideoEncryptorViewModel(IVideoEncryptionService encryptorService)
    {
        _encryptorService = encryptorService ?? throw new ArgumentNullException(nameof(encryptorService));
        PreflightIssues = new ReadOnlyObservableCollection<VideoPreflightIssue>(_preflightIssues);
    }

    public ReadOnlyObservableCollection<VideoPreflightIssue> PreflightIssues { get; }
    public bool HasPreflightIssues => _preflightIssues.Count > 0;
    public bool IsBusy => IsPreflighting || IsEncrypting;
    public int VideoTitleCharacterCount => EncryptedVideoContainer.CountRunes(VideoTitle);
    public int DescriptionCharacterCount => EncryptedVideoContainer.CountRunes(Description);

    [ObservableProperty] private string _selectedFilePath = string.Empty;
    [ObservableProperty] private string _outputFilePath = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _confirmPassword = string.Empty;
    [ObservableProperty] private string _videoTitle = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private bool _isPreflighting;
    [ObservableProperty] private bool _isEncrypting;
    [ObservableProperty] private string _statusMessage = "请选择要加密的视频文件";
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _progressText = "0%";
    [ObservableProperty] private bool _showPassword;
    [ObservableProperty] private string _estimatedTimeText = string.Empty;
    [ObservableProperty] private string _processingSpeedText = string.Empty;
    [ObservableProperty] private string _fileSizeText = string.Empty;
    [ObservableProperty] private string _fileFormatText = string.Empty;
    [ObservableProperty] private VideoTaskState _taskState = VideoTaskState.Pending;
    [ObservableProperty] private string _failureCodeText = string.Empty;

    partial void OnSelectedFilePathChanged(string value)
    {
        UpdateFileInfo();
        GenerateOutputPathCommand.NotifyCanExecuteChanged();
        NotifyCommandStates();
    }

    partial void OnOutputFilePathChanged(string value) => NotifyCommandStates();
    partial void OnPasswordChanged(string value) => NotifyCommandStates();
    partial void OnConfirmPasswordChanged(string value) => NotifyCommandStates();

    partial void OnVideoTitleChanged(string value)
    {
        OnPropertyChanged(nameof(VideoTitleCharacterCount));
        NotifyCommandStates();
    }

    partial void OnDescriptionChanged(string value)
    {
        OnPropertyChanged(nameof(DescriptionCharacterCount));
        NotifyCommandStates();
    }

    partial void OnIsPreflightingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsBusy));
        NotifyCommandStates();
    }

    partial void OnIsEncryptingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsBusy));
        NotifyCommandStates();
    }

    [RelayCommand(CanExecute = nameof(CanStartEncryption))]
    private async Task StartEncryptionAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var generation = Interlocked.Increment(ref _operationGeneration);
        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _operationCancellation, cancellation);
        previous?.Cancel();
        previous?.Dispose();

        var request = new VideoEncryptionRequest(
            SelectedFilePath,
            OutputFilePath,
            VideoTitle,
            Description);

        ResetRunPresentation();
        try
        {
            TaskState = VideoTaskState.Preflighting;
            IsPreflighting = true;
            StatusMessage = "正在检查输入、输出目录和磁盘空间...";
            var preflight = await _encryptorService
                .PreflightAsync(request, cancellation.Token);

            if (!IsCurrent(generation))
                return;

            ApplyPreflight(preflight);
            var blocker = preflight.Issues.FirstOrDefault(issue =>
                issue.Severity == PreflightSeverity.Blocking);
            if (blocker is not null)
            {
                TaskState = VideoTaskState.Failed;
                FailureCodeText = blocker.Code.ToString();
                StatusMessage = $"{blocker.Message} {blocker.SuggestedAction}";
                return;
            }

            TaskState = VideoTaskState.Ready;
            IsPreflighting = false;
            IsEncrypting = true;
            _startedAt = DateTime.Now;
            _totalBytes = 0;
            StatusMessage = preflight.Issues.Count > 0
                ? "预检通过（存在警告），开始加密..."
                : "预检通过，开始加密...";

            var progress = new Progress<VideoTaskProgress>(value =>
            {
                if (IsCurrent(generation))
                    ApplyProgress(value);
            });
            await _encryptorService.EncryptAsync(
                request,
                Password,
                progress,
                cancellation.Token);

            if (!IsCurrent(generation))
                return;

            TaskState = VideoTaskState.Succeeded;
            Progress = 100;
            ProgressText = "100%";
            StatusMessage = $"加密完成！输出文件: {OutputFilePath}";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (IsCurrent(generation))
            {
                TaskState = VideoTaskState.Cancelled;
                FailureCodeText = VideoTaskFailureCode.Cancelled.ToString();
                StatusMessage = "加密已取消，未完成的临时文件已进入清理流程。";
            }
        }
        catch (VideoTaskException ex)
        {
            if (IsCurrent(generation))
            {
                TaskState = VideoTaskState.Failed;
                FailureCodeText = ex.FailureCode.ToString();
                StatusMessage = $"{ex.Message}（错误代码：{ex.FailureCode}）";
            }
        }
        catch
        {
            if (IsCurrent(generation))
            {
                TaskState = VideoTaskState.Failed;
                FailureCodeText = VideoTaskFailureCode.Unknown.ToString();
                StatusMessage = "加密失败：发生未预期错误，请检查输入和输出位置后重试。";
            }
        }
        finally
        {
            Interlocked.CompareExchange(ref _operationCancellation, null, cancellation);
            cancellation.Dispose();
            if (IsCurrent(generation))
            {
                IsPreflighting = false;
                IsEncrypting = false;
            }
        }
    }

    private bool CanStartEncryption() =>
        !_disposed &&
        !IsBusy &&
        !string.IsNullOrWhiteSpace(SelectedFilePath) &&
        !string.IsNullOrWhiteSpace(OutputFilePath) &&
        !string.IsNullOrEmpty(Password) &&
        Password == ConfirmPassword &&
        Password.Length >= 6 &&
        VideoTitleCharacterCount <= EncryptedVideoContainer.MaxTitleRunes &&
        DescriptionCharacterCount <= EncryptedVideoContainer.MaxDescriptionRunes;

    [RelayCommand(CanExecute = nameof(CanClear))]
    private void ClearAll()
    {
        SelectedFilePath = string.Empty;
        OutputFilePath = string.Empty;
        Password = string.Empty;
        ConfirmPassword = string.Empty;
        VideoTitle = string.Empty;
        Description = string.Empty;
        FileSizeText = string.Empty;
        FileFormatText = string.Empty;
        ResetRunPresentation();
        TaskState = VideoTaskState.Pending;
        StatusMessage = "请选择要加密的视频文件";
    }

    private bool CanClear() => !_disposed && !IsBusy;

    [RelayCommand]
    private void TogglePasswordVisibility() => ShowPassword = !ShowPassword;

    [RelayCommand(CanExecute = nameof(CanCancelEncryption))]
    private void CancelEncryption()
    {
        try
        {
            Volatile.Read(ref _operationCancellation)?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 任务恰好结束时无需再次处理。
        }
    }

    private bool CanCancelEncryption() => !_disposed && IsBusy;

    [RelayCommand(CanExecute = nameof(CanGenerateOutputPath))]
    private void GenerateOutputPath()
    {
        if (string.IsNullOrEmpty(SelectedFilePath))
            return;

        var directory = Path.GetDirectoryName(SelectedFilePath);
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(SelectedFilePath);
        if (!string.IsNullOrEmpty(directory))
            OutputFilePath = Path.Combine(directory, $"{fileNameWithoutExtension}_encrypted.secvid");
    }

    private bool CanGenerateOutputPath() => !_disposed && !IsBusy && !string.IsNullOrEmpty(SelectedFilePath);

    private void UpdateFileInfo()
    {
        if (string.IsNullOrEmpty(SelectedFilePath) || !File.Exists(SelectedFilePath))
        {
            FileSizeText = string.Empty;
            FileFormatText = string.Empty;
            return;
        }

        try
        {
            var fileInfo = new FileInfo(SelectedFilePath);
            FileSizeText = $"文件大小: {fileInfo.Length / (1024d * 1024):F2} MB";
            FileFormatText = $"文件格式: {Path.GetExtension(SelectedFilePath).ToLowerInvariant()}";
            GenerateOutputPath();
            StatusMessage = "文件已选择，请输入加密密码";
        }
        catch
        {
            StatusMessage = "读取文件信息失败，请检查文件是否仍然可用。";
        }
    }

    private void ApplyPreflight(VideoPreflightResult result)
    {
        _preflightIssues.Clear();
        foreach (var issue in result.Issues)
            _preflightIssues.Add(issue);
        OnPropertyChanged(nameof(HasPreflightIssues));
    }

    private void ApplyProgress(VideoTaskProgress value)
    {
        TaskState = value.State;
        _processedBytes = value.ProcessedBytes;
        _totalBytes = value.TotalBytes;
        Progress = value.Percentage;
        ProgressText = $"{value.Percentage:F1}%";
        StatusMessage = value.Message;
        FailureCodeText = value.FailureCode?.ToString() ?? string.Empty;
        UpdateSpeedAndTimeInfo();
    }

    private void UpdateSpeedAndTimeInfo()
    {
        var elapsed = DateTime.Now - _startedAt;
        if (elapsed.TotalSeconds <= 0 || _processedBytes <= 0)
            return;

        var speed = _processedBytes / elapsed.TotalSeconds;
        ProcessingSpeedText = $"处理速度: {speed / (1024 * 1024):F2} MB/s";
        if (_totalBytes > _processedBytes && speed > 0)
        {
            var remaining = TimeSpan.FromSeconds((_totalBytes - _processedBytes) / speed);
            EstimatedTimeText = $"预计剩余: {remaining:mm\\:ss}";
        }
    }

    private void ResetRunPresentation()
    {
        Progress = 0;
        ProgressText = "0%";
        ProcessingSpeedText = string.Empty;
        EstimatedTimeText = string.Empty;
        FailureCodeText = string.Empty;
        _preflightIssues.Clear();
        OnPropertyChanged(nameof(HasPreflightIssues));
    }

    private bool IsCurrent(int generation) =>
        !_disposed && generation == Volatile.Read(ref _operationGeneration);

    private void NotifyCommandStates()
    {
        StartEncryptionCommand.NotifyCanExecuteChanged();
        ClearAllCommand.NotifyCanExecuteChanged();
        CancelEncryptionCommand.NotifyCanExecuteChanged();
        GenerateOutputPathCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Interlocked.Increment(ref _operationGeneration);
        Password = string.Empty;
        ConfirmPassword = string.Empty;
        Interlocked.Exchange(ref _operationCancellation, null)?.Cancel();
        GC.SuppressFinalize(this);
    }
}
