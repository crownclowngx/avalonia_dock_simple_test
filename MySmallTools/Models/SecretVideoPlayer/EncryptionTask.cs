using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MySmallTools.Models.SecretVideoPlayer;

/// <summary>
/// 加密任务模型
/// </summary>
public class EncryptionTask : INotifyPropertyChanged
{
    private string _inputFilePath = string.Empty;
    private string _outputFilePath = string.Empty;
    private string _password = string.Empty;
    private string _title = string.Empty;
    private string _description = string.Empty;
    private double _progress = 0;
    private string _status = "等待开始";
    private bool _isCompleted = false;
    private bool _isRunning = false;
    private string _errorMessage = string.Empty;
    private DateTime _startTime;
    private DateTime _endTime;
    private long _totalBytes = 0;
    private long _processedBytes = 0;

    /// <summary>
    /// 输入文件路径
    /// </summary>
    public string InputFilePath
    {
        get => _inputFilePath;
        set => SetProperty(ref _inputFilePath, value);
    }

    /// <summary>
    /// 输出文件路径
    /// </summary>
    public string OutputFilePath
    {
        get => _outputFilePath;
        set => SetProperty(ref _outputFilePath, value);
    }

    /// <summary>
    /// 加密密码
    /// </summary>
    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value);
    }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    /// <summary>
    /// 加密进度 (0-100)
    /// </summary>
    public double Progress
    {
        get => _progress;
        set => SetProperty(ref _progress, value);
    }

    /// <summary>
    /// 当前状态描述
    /// </summary>
    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    /// <summary>
    /// 是否已完成
    /// </summary>
    public bool IsCompleted
    {
        get => _isCompleted;
        set => SetProperty(ref _isCompleted, value);
    }

    /// <summary>
    /// 是否正在运行
    /// </summary>
    public bool IsRunning
    {
        get => _isRunning;
        set => SetProperty(ref _isRunning, value);
    }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    /// <summary>
    /// 开始时间
    /// </summary>
    public DateTime StartTime
    {
        get => _startTime;
        set => SetProperty(ref _startTime, value);
    }

    /// <summary>
    /// 结束时间
    /// </summary>
    public DateTime EndTime
    {
        get => _endTime;
        set => SetProperty(ref _endTime, value);
    }

    /// <summary>
    /// 总字节数
    /// </summary>
    public long TotalBytes
    {
        get => _totalBytes;
        set => SetProperty(ref _totalBytes, value);
    }

    /// <summary>
    /// 已处理字节数
    /// </summary>
    public long ProcessedBytes
    {
        get => _processedBytes;
        set => SetProperty(ref _processedBytes, value);
    }

    /// <summary>
    /// 获取处理速度 (字节/秒)
    /// </summary>
    public double ProcessingSpeed
    {
        get
        {
            if (!IsRunning || ProcessedBytes == 0) return 0;
            var elapsed = DateTime.Now - StartTime;
            return elapsed.TotalSeconds > 0 ? ProcessedBytes / elapsed.TotalSeconds : 0;
        }
    }

    /// <summary>
    /// 获取预估剩余时间
    /// </summary>
    public TimeSpan EstimatedTimeRemaining
    {
        get
        {
            var speed = ProcessingSpeed;
            if (speed <= 0 || TotalBytes <= ProcessedBytes) return TimeSpan.Zero;
            
            var remainingBytes = TotalBytes - ProcessedBytes;
            var remainingSeconds = remainingBytes / speed;
            return TimeSpan.FromSeconds(remainingSeconds);
        }
    }

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
}
