using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Dock.Model.Mvvm.Controls;
using MySmallTools.Business.SecretVideoPlayer;
using MySmallTools.Models.SecretVideoPlayer;

namespace MySmallTools.ViewModels.SecretVideoPlayer;

/// <summary>
/// 视频文件加密器视图模型
/// </summary>
public class VideoEncryptorViewModel : Document, INotifyPropertyChanged
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
    private string _selectedFilePath = string.Empty;
    private string _outputFilePath = string.Empty;
    private string _password = string.Empty;
    private string _confirmPassword = string.Empty;
    private bool _isEncrypting = false;
    private string _statusMessage = "请选择要加密的视频文件";
    private double _progress = 0;
    private string _progressText = "0%";
    private bool _showPassword = false;
    private string _estimatedTimeText = string.Empty;
    private string _processingSpeedText = string.Empty;

    public VideoEncryptorViewModel()
    {
        _encryptorService = new VideoEncryptorService();
        _currentTask = new EncryptionTask();
        
        // 订阅任务属性变化
        _currentTask.PropertyChanged += OnTaskPropertyChanged;
        
        // 初始化命令
        SelectFileCommand = new RelayCommand(async () => await SelectFileAsync(), () => !_isEncrypting);
        StartEncryptionCommand = new RelayCommand(async () => await StartEncryptionAsync(), CanStartEncryption);
        ClearCommand = new RelayCommand(ClearAll, () => !_isEncrypting);
        TogglePasswordVisibilityCommand = new RelayCommand(TogglePasswordVisibility);
        GenerateOutputPathCommand = new RelayCommand(GenerateOutputPath, () => !string.IsNullOrEmpty(_selectedFilePath));
    }

    #endregion

    #region Properties

    /// <summary>
    /// 选中的文件路径
    /// </summary>
    public string SelectedFilePath
    {
        get => _selectedFilePath;
        set
        {
            if (SetProperty(ref _selectedFilePath, value))
            {
                _currentTask.InputFilePath = value;
                UpdateFileInfo();
                ((RelayCommand)GenerateOutputPathCommand).RaiseCanExecuteChanged();
                ((RelayCommand)StartEncryptionCommand).RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// 输出文件路径
    /// </summary>
    public string OutputFilePath
    {
        get => _outputFilePath;
        set
        {
            if (SetProperty(ref _outputFilePath, value))
            {
                _currentTask.OutputFilePath = value;
                ((RelayCommand)StartEncryptionCommand).RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// 加密密码
    /// </summary>
    public string Password
    {
        get => _password;
        set
        {
            if (SetProperty(ref _password, value))
            {
                _currentTask.Password = value;
                ((RelayCommand)StartEncryptionCommand).RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// 确认密码
    /// </summary>
    public string ConfirmPassword
    {
        get => _confirmPassword;
        set
        {
            if (SetProperty(ref _confirmPassword, value))
            {
                ((RelayCommand)StartEncryptionCommand).RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// 是否正在加密
    /// </summary>
    public bool IsEncrypting
    {
        get => _isEncrypting;
        set
        {
            if (SetProperty(ref _isEncrypting, value))
            {
                ((RelayCommand)SelectFileCommand).RaiseCanExecuteChanged();
                ((RelayCommand)StartEncryptionCommand).RaiseCanExecuteChanged();
                ((RelayCommand)ClearCommand).RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// 状态消息
    /// </summary>
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>
    /// 进度值 (0-100)
    /// </summary>
    public double Progress
    {
        get => _progress;
        set => SetProperty(ref _progress, value);
    }

    /// <summary>
    /// 进度文本
    /// </summary>
    public string ProgressText
    {
        get => _progressText;
        set => SetProperty(ref _progressText, value);
    }

    /// <summary>
    /// 是否显示密码
    /// </summary>
    public bool ShowPassword
    {
        get => _showPassword;
        set => SetProperty(ref _showPassword, value);
    }

    /// <summary>
    /// 预估剩余时间文本
    /// </summary>
    public string EstimatedTimeText
    {
        get => _estimatedTimeText;
        set => SetProperty(ref _estimatedTimeText, value);
    }

    /// <summary>
    /// 处理速度文本
    /// </summary>
    public string ProcessingSpeedText
    {
        get => _processingSpeedText;
        set => SetProperty(ref _processingSpeedText, value);
    }

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

    public ICommand SelectFileCommand { get; }
    public ICommand StartEncryptionCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand TogglePasswordVisibilityCommand { get; }
    public ICommand GenerateOutputPathCommand { get; }

    #endregion

    #region Private Methods

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

    private bool CanStartEncryption()
    {
        return !_isEncrypting &&
               !string.IsNullOrEmpty(_selectedFilePath) &&
               !string.IsNullOrEmpty(_outputFilePath) &&
               !string.IsNullOrEmpty(_password) &&
               _password == _confirmPassword &&
               _password.Length >= 6;
    }

    private async Task StartEncryptionAsync()
    {
        if (!CanStartEncryption()) return;

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
            
            StatusMessage = $"加密完成！输出文件: {_outputFilePath}";
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

    private void TogglePasswordVisibility()
    {
        ShowPassword = !ShowPassword;
    }

    private void GenerateOutputPath()
    {
        if (string.IsNullOrEmpty(_selectedFilePath)) return;

        var directory = Path.GetDirectoryName(_selectedFilePath);
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(_selectedFilePath);
        var extension = Path.GetExtension(_selectedFilePath);
        
        OutputFilePath = Path.Combine(directory!, $"{fileNameWithoutExt}_encrypted{extension}");
    }

    private void UpdateFileInfo()
    {
        if (string.IsNullOrEmpty(_selectedFilePath) || !File.Exists(_selectedFilePath))
        {
            FileSizeText = string.Empty;
            FileFormatText = string.Empty;
            return;
        }

        try
        {
            var fileInfo = new FileInfo(_selectedFilePath);
            var sizeInMB = fileInfo.Length / (1024.0 * 1024.0);
            FileSizeText = $"文件大小: {sizeInMB:F2} MB";
            
            var extension = Path.GetExtension(_selectedFilePath).ToLowerInvariant();
            FileFormatText = $"文件格式: {extension}";
            
            _currentTask.TotalBytes = fileInfo.Length;
            
            // 自动生成输出路径
            GenerateOutputPath();
            
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

    #region INotifyPropertyChanged

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    #endregion
}