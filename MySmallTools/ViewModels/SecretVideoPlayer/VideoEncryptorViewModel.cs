using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using MySmallTools.Business.SecretVideoPlayer;
using MySmallTools.Models.SecretVideoPlayer;

namespace MySmallTools.ViewModels.SecretVideoPlayer;

/// <summary>
/// 视频文件加密器视图模型
/// </summary>
public partial class VideoEncryptorViewModel : Document, IDisposable
{
    #region Fields
    private readonly VideoEncryptorService _encryptorService;
    private EncryptionTask _currentTask;
    private CancellationTokenSource? _encryptionCancellation;
    private bool _disposed;

    public VideoEncryptorViewModel(VideoEncryptorService encryptorService)
    {
        _encryptorService = encryptorService ?? throw new ArgumentNullException(nameof(encryptorService));
        _currentTask = new EncryptionTask();
        
        // 订阅任务属性变化
        _currentTask.PropertyChanged += OnTaskPropertyChanged;
    }

    #endregion

    #region Properties

    /// <summary>
    /// 选中的文件路径
    /// </summary>
    [ObservableProperty]
    private string _selectedFilePath = string.Empty;

    partial void OnSelectedFilePathChanged(string value)
    {
        _currentTask.InputFilePath = value;
        UpdateFileInfo();
        GenerateOutputPathCommand.NotifyCanExecuteChanged();
        StartEncryptionCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// 输出文件路径
    /// </summary>
    [ObservableProperty]
    private string _outputFilePath = string.Empty;

    partial void OnOutputFilePathChanged(string value)
    {
        _currentTask.OutputFilePath = value;
        StartEncryptionCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// 加密密码
    /// </summary>
    [ObservableProperty]
    private string _password = string.Empty;

    partial void OnPasswordChanged(string value)
    {
        _currentTask.Password = value;
        StartEncryptionCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// 确认密码
    /// </summary>
    [ObservableProperty]
    private string _confirmPassword = string.Empty;
    partial void OnConfirmPasswordChanged(string value)
    {
        StartEncryptionCommand.NotifyCanExecuteChanged();
    }

    [ObservableProperty]
    private string _videoTitle = string.Empty;

    partial void OnVideoTitleChanged(string value)
    {
        // 字数按 Unicode Rune 实时重算，确保 emoji 不会因为 UTF-16 占两个 char 而被错误计为两个字符。
        _currentTask.Title = value;
        OnPropertyChanged(nameof(VideoTitleCharacterCount));
        StartEncryptionCommand.NotifyCanExecuteChanged();
    }

    [ObservableProperty]
    private string _description = string.Empty;

    partial void OnDescriptionChanged(string value)
    {
        _currentTask.Description = value;
        OnPropertyChanged(nameof(DescriptionCharacterCount));
        StartEncryptionCommand.NotifyCanExecuteChanged();
    }

    public int VideoTitleCharacterCount => EncryptedVideoContainer.CountRunes(VideoTitle);
    public int DescriptionCharacterCount => EncryptedVideoContainer.CountRunes(Description);

    /// <summary>
    /// 是否正在加密
    /// </summary>
    [ObservableProperty]
    private bool _isEncrypting = false;
    
    partial void OnIsEncryptingChanged(bool value)
    {
        // 手动触发相关命令状态更新
        StartEncryptionCommand.NotifyCanExecuteChanged();
        ClearAllCommand.NotifyCanExecuteChanged();
    }
    
    /// <summary>
    /// 状态消息
    /// </summary>
    [ObservableProperty]
    private string _statusMessage = "请选择要加密的视频文件";

    /// <summary>
    /// 进度值 (0-100)
    /// </summary>
    [ObservableProperty]
    private double _progress = 0;

    /// <summary>
    /// 进度文本
    /// </summary>
    [ObservableProperty]
    private string _progressText = "0%";

    /// <summary>
    /// 是否显示密码
    /// </summary>
    [ObservableProperty]
    private bool _showPassword = false;

    /// <summary>
    /// 预估剩余时间文本
    /// </summary>
    [ObservableProperty]
    private string _estimatedTimeText = string.Empty;

    /// <summary>
    /// 处理速度文本
    /// </summary>
    [ObservableProperty]
    private string _processingSpeedText = string.Empty;

    /// <summary>
    /// 文件大小文本
    /// </summary>
    public string FileSizeText { get; private set; } = string.Empty;

    /// <summary>
    /// 文件格式文本
    /// </summary>
    public string FileFormatText { get; private set; } = string.Empty;

    #endregion

    #region Commands

    [RelayCommand(CanExecute = nameof(CanStartEncryption))]
    private async Task StartEncryptionAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // 每次执行使用独立取消源。Document Scope 释放时只需要取消当前引用，
        // 实际 FileStream 和 partial 文件仍由正在运行的加密调用按原有事务顺序清理。
        var cancellation = new CancellationTokenSource();
        var previousCancellation = Interlocked.Exchange(ref _encryptionCancellation, cancellation);
        previousCancellation?.Cancel();
        previousCancellation?.Dispose();

        try
        {
            IsEncrypting = true;
            _currentTask.IsRunning = true;
            _currentTask.StartTime = DateTime.Now;
            _currentTask.Status = "开始加密...";
            
            StatusMessage = "正在加密视频文件，请稍候...";

            // 执行加密
            var progress = new Progress<EncryptionProgress>(OnEncryptionProgress);
            await _encryptorService.EncryptVideoWithProgressAsync(
                _currentTask,
                progress,
                cancellation.Token);

            if (_disposed)
            {
                return;
            }

            _currentTask.IsCompleted = true;
            _currentTask.IsRunning = false;
            _currentTask.EndTime = DateTime.Now;
            _currentTask.Progress = 100;
            _currentTask.Status = "加密完成";
            
            StatusMessage = $"加密完成！输出文件: {OutputFilePath}";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // 关闭 Document 是正常取消路径。Scope 已经释放时不再更新任何绑定属性，
            // 避免已从视觉树移除的对象继续触发命令和界面通知。
            if (!_disposed)
            {
                StatusMessage = "加密已取消";
            }
        }
        catch (Exception ex)
        {
            if (_disposed)
            {
                return;
            }

            _currentTask.IsRunning = false;
            _currentTask.ErrorMessage = ex.Message;
            _currentTask.Status = "加密失败";
            StatusMessage = $"加密失败: {ex.Message}";
        }
        finally
        {
            Interlocked.CompareExchange(ref _encryptionCancellation, null, cancellation);
            cancellation.Dispose();
            if (!_disposed)
            {
                IsEncrypting = false;
            }
        }
    }

    private bool CanStartEncryption()
    {
        // UI 不通过截断输入来“修正”超限文本，避免用户无感丢失描述；只有全部约束满足时才允许开始。
        return !_disposed &&
               !IsEncrypting &&
               !string.IsNullOrEmpty(SelectedFilePath) &&
               !string.IsNullOrEmpty(OutputFilePath) &&
               !string.IsNullOrEmpty(Password) &&
               Password == ConfirmPassword &&
               Password.Length >= 6 &&
               VideoTitleCharacterCount <= EncryptedVideoContainer.MaxTitleRunes &&
               DescriptionCharacterCount <= EncryptedVideoContainer.MaxDescriptionRunes;
    }
    

    [RelayCommand(CanExecute = nameof(CanClear))]
    private void ClearAll()
    {
        SelectedFilePath = string.Empty;
        OutputFilePath = string.Empty;
        Password = string.Empty;
        ConfirmPassword = string.Empty;
        VideoTitle = string.Empty;
        Description = string.Empty;
        Progress = 0;
        ProgressText = "0%";
        StatusMessage = "请选择要加密的视频文件";
        EstimatedTimeText = string.Empty;
        ProcessingSpeedText = string.Empty;
        FileSizeText = string.Empty;
        FileFormatText = string.Empty;
        
        // 旧任务在替换前必须解除订阅；否则它被异步进度对象短暂持有时仍会更新新 Document。
        _currentTask.PropertyChanged -= OnTaskPropertyChanged;
        _currentTask = new EncryptionTask();
        _currentTask.PropertyChanged += OnTaskPropertyChanged;
    }

    private bool CanClear() => !IsEncrypting;

    [RelayCommand]
    private void TogglePasswordVisibility()
    {
        ShowPassword = !ShowPassword;
    }

    [RelayCommand(CanExecute = nameof(CanGenerateOutputPath))]
    private void GenerateOutputPath()
    {
        if (string.IsNullOrEmpty(SelectedFilePath)) return;

        var directory = Path.GetDirectoryName(SelectedFilePath);
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(SelectedFilePath);
        // 新文件统一使用 .secvid，原始扩展名已经写入不可变固定头，不再依赖外部文件扩展名判断格式。
        OutputFilePath = Path.Combine(directory!, $"{fileNameWithoutExt}_encrypted.secvid");
    }

    private bool CanGenerateOutputPath() => !string.IsNullOrEmpty(SelectedFilePath);

    #endregion

    #region Private Methods

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
            var sizeInMB = fileInfo.Length / (1024.0 * 1024.0);
            FileSizeText = $"文件大小: {sizeInMB:F2} MB";
            
            var extension = Path.GetExtension(SelectedFilePath).ToLowerInvariant();
            FileFormatText = $"文件格式: {extension}";
            
            _currentTask.TotalBytes = fileInfo.Length;
            
            // 自动生成输出路径
            GenerateOutputPathCommand.Execute(null);
            
            StatusMessage = "文件已选择，请输入加密密码";
        }
        catch (Exception ex)
        {
            StatusMessage = $"读取文件信息失败: {ex.Message}";
        }
    }

    private void OnTaskPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed || !ReferenceEquals(sender, _currentTask))
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(EncryptionTask.Progress):
                Progress = _currentTask.Progress;
                ProgressText = $"{_currentTask.Progress:F1}%";
                break;
            case nameof(EncryptionTask.Status):
                StatusMessage = _currentTask.Status;
                break;
            case nameof(EncryptionTask.ProcessedBytes):
                UpdateSpeedAndTimeInfo();
                break;
        }
    }

    private void UpdateSpeedAndTimeInfo()
    {
        var speed = _currentTask.ProcessingSpeed;
        if (speed > 0)
        {
            var speedMB = speed / (1024 * 1024);
            ProcessingSpeedText = $"处理速度: {speedMB:F2} MB/s";
            
            var remaining = _currentTask.EstimatedTimeRemaining;
            if (remaining.TotalSeconds > 0)
            {
                EstimatedTimeText = $"预计剩余: {remaining:mm\\:ss}";
            }
        }
    }

    private void OnEncryptionProgress(EncryptionProgress progress)
    {
        if (_disposed)
        {
            return;
        }

        Progress = progress.Percentage;
        ProgressText = $"{progress.Percentage:F1}%";
        StatusMessage = progress.Status;
        
        _currentTask.ProcessedBytes = progress.ProcessedBytes;
        _currentTask.Progress = progress.Percentage;
        
        UpdateSpeedAndTimeInfo();
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _currentTask.PropertyChanged -= OnTaskPropertyChanged;

        // 不在 UI 线程同步等待加密 Task，否则异步清理若需要返回 UI 上下文可能造成死锁。
        // Cancel 会使 Secvid03Encryptor 退出循环并删除 partial 文件，取消源由任务 finally 负责释放。
        Interlocked.Exchange(ref _encryptionCancellation, null)?.Cancel();
        GC.SuppressFinalize(this);
    }

    #endregion
}
