using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using MySmallTools.Business.SecretVideoPlayer;
using MySmallTools.Models.SecretVideoPlayer;

namespace MySmallTools.ViewModels.SecretVideoPlayer;

/// <summary>
/// 视频文件加密器视图模型
/// </summary>
public partial class VideoEncryptorViewModel : Document
{
    #region Events
    
    /// <summary>
    /// 请求文件选择事件
    /// </summary>
    public event EventHandler? FileSelectionRequested;
    
    #endregion
    
    #region Fields
    private readonly VideoEncryptorService _encryptorService;
    private EncryptionTask _currentTask;

    public VideoEncryptorViewModel()
    {
        _encryptorService = new VideoEncryptorService();
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

    /// <summary>
    /// 是否正在加密
    /// </summary>
    [ObservableProperty]
    private bool _isEncrypting = false;
    
    partial void OnIsEncryptingChanged(bool value)
    {
        // 手动触发相关命令状态更新
        SelectFileCommand.NotifyCanExecuteChanged();
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

    [RelayCommand(CanExecute = nameof(CanSelectFile))]
    private async Task SelectFileAsync()
    {
        try
        {
            // 触发文件选择请求事件，让View处理文件选择对话框
            FileSelectionRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            StatusMessage = $"选择文件时出错: {ex.Message}";
        }
    }

    private bool CanSelectFile() => !IsEncrypting;

    [RelayCommand(CanExecute = nameof(CanStartEncryption))]
    private async Task StartEncryptionAsync()
    {
        try
        {
            IsEncrypting = true;
            _currentTask.IsRunning = true;
            _currentTask.StartTime = DateTime.Now;
            _currentTask.Status = "开始加密...";
            
            StatusMessage = "正在加密视频文件，请稍候...";

            // 执行加密
            var progress = new Progress<EncryptionProgress>(OnEncryptionProgress);
            await _encryptorService.EncryptVideoWithProgressAsync(_currentTask, progress);

            _currentTask.IsCompleted = true;
            _currentTask.IsRunning = false;
            _currentTask.EndTime = DateTime.Now;
            _currentTask.Progress = 100;
            _currentTask.Status = "加密完成";
            
            StatusMessage = $"加密完成！输出文件: {OutputFilePath}";
        }
        catch (Exception ex)
        {
            _currentTask.IsRunning = false;
            _currentTask.ErrorMessage = ex.Message;
            _currentTask.Status = "加密失败";
            StatusMessage = $"加密失败: {ex.Message}";
        }
        finally
        {
            IsEncrypting = false;
        }
    }

    private bool CanStartEncryption()
    {
        return !IsEncrypting &&
               !string.IsNullOrEmpty(SelectedFilePath) &&
               !string.IsNullOrEmpty(OutputFilePath) &&
               !string.IsNullOrEmpty(Password) &&
               Password == ConfirmPassword &&
               Password.Length >= 6;
    }
    

    [RelayCommand(CanExecute = nameof(CanClear))]
    private void ClearAll()
    {
        SelectedFilePath = string.Empty;
        OutputFilePath = string.Empty;
        Password = string.Empty;
        ConfirmPassword = string.Empty;
        Progress = 0;
        ProgressText = "0%";
        StatusMessage = "请选择要加密的视频文件";
        EstimatedTimeText = string.Empty;
        ProcessingSpeedText = string.Empty;
        FileSizeText = string.Empty;
        FileFormatText = string.Empty;
        
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
        var extension = Path.GetExtension(SelectedFilePath);
        
        OutputFilePath = Path.Combine(directory!, $"{fileNameWithoutExt}_encrypted{extension}");
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
        Progress = progress.Percentage;
        ProgressText = $"{progress.Percentage:F1}%";
        StatusMessage = progress.Status;
        
        _currentTask.ProcessedBytes = progress.ProcessedBytes;
        _currentTask.Progress = progress.Percentage;
        
        UpdateSpeedAndTimeInfo();
    }

    #endregion
}