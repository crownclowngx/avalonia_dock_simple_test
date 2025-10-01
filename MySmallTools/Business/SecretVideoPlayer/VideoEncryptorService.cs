using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MySmallTools.Models.SecretVideoPlayer;

namespace MySmallTools.Business.SecretVideoPlayer;

/// <summary>
/// 视频文件加密服务
/// </summary>
public class VideoEncryptorService
{
    private readonly SmartVideoEncryptor _encryptor;

    public VideoEncryptorService()
    {
        _encryptor = new SmartVideoEncryptor();
    }

    /// <summary>
    /// 加密视频文件（带进度回调）
    /// </summary>
    /// <param name="task">加密任务</param>
    /// <param name="progressCallback">进度回调</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task EncryptVideoWithProgressAsync(
        EncryptionTask task, 
        IProgress<EncryptionProgress>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        if (task == null) throw new ArgumentNullException(nameof(task));
        if (string.IsNullOrEmpty(task.InputFilePath)) throw new ArgumentException("输入文件路径不能为空");
        if (string.IsNullOrEmpty(task.OutputFilePath)) throw new ArgumentException("输出文件路径不能为空");
        if (string.IsNullOrEmpty(task.Password)) throw new ArgumentException("密码不能为空");

        try
        {
            // 验证输入文件
            if (!File.Exists(task.InputFilePath))
            {
                throw new FileNotFoundException($"输入文件不存在: {task.InputFilePath}");
            }

            // 获取文件信息
            var fileInfo = new FileInfo(task.InputFilePath);
            task.TotalBytes = fileInfo.Length;
            task.StartTime = DateTime.Now;
            task.IsRunning = true;
            task.Status = "开始加密...";

            // 报告初始进度
            progressCallback?.Report(new EncryptionProgress
            {
                ProcessedBytes = 0,
                TotalBytes = task.TotalBytes,
                Percentage = 0,
                Status = "准备加密..."
            });

            // 创建输出目录（如果不存在）
            var outputDir = Path.GetDirectoryName(task.OutputFilePath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            // 执行加密（这里需要修改SmartVideoEncryptor以支持进度回调）
            await EncryptWithProgressAsync(task, progressCallback, cancellationToken);

            // 完成
            task.EndTime = DateTime.Now;
            task.IsCompleted = true;
            task.IsRunning = false;
            task.Progress = 100;
            task.Status = "加密完成";

            progressCallback?.Report(new EncryptionProgress
            {
                ProcessedBytes = task.TotalBytes,
                TotalBytes = task.TotalBytes,
                Percentage = 100,
                Status = "加密完成"
            });
        }
        catch (OperationCanceledException)
        {
            task.IsRunning = false;
            task.Status = "加密已取消";
            task.ErrorMessage = "用户取消了加密操作";
            throw;
        }
        catch (Exception ex)
        {
            task.IsRunning = false;
            task.Status = "加密失败";
            task.ErrorMessage = ex.Message;
            throw;
        }
    }

    /// <summary>
    /// 带进度的加密实现
    /// </summary>
    private async Task EncryptWithProgressAsync(
        EncryptionTask task, 
        IProgress<EncryptionProgress>? progressCallback,
        CancellationToken cancellationToken)
    {
        // 直接调用SmartVideoEncryptor的带进度回调的方法
        await _encryptor.EncryptVideoWithProgressAsync(
            task.InputFilePath, 
            task.OutputFilePath, 
            task.Password, 
            progressCallback, 
            cancellationToken);
    }

    /// <summary>
    /// 验证密码强度
    /// </summary>
    /// <param name="password">密码</param>
    /// <returns>密码强度评分 (0-100)</returns>
    public int ValidatePasswordStrength(string password)
    {
        if (string.IsNullOrEmpty(password)) return 0;
        
        int score = 0;
        
        // 长度评分
        if (password.Length >= 6) score += 20;
        if (password.Length >= 8) score += 10;
        if (password.Length >= 12) score += 10;
        
        // 字符类型评分
        if (password.Any(char.IsLower)) score += 15;
        if (password.Any(char.IsUpper)) score += 15;
        if (password.Any(char.IsDigit)) score += 15;
        if (password.Any(c => !char.IsLetterOrDigit(c))) score += 15;
        
        return Math.Min(score, 100);
    }

    /// <summary>
    /// 检查文件是否为支持的视频格式
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>是否支持</returns>
    public bool IsSupportedVideoFormat(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return false;
        
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var supportedFormats = new[]
        {
            ".mp4", ".avi", ".mkv", ".mov", ".wmv", 
            ".flv", ".webm", ".m4v", ".3gp", ".ogv"
        };
        
        return supportedFormats.Contains(extension);
    }
}

/// <summary>
/// 加密进度信息
/// </summary>
public class EncryptionProgress
{
    /// <summary>
    /// 已处理字节数
    /// </summary>
    public long ProcessedBytes { get; set; }
    
    /// <summary>
    /// 总字节数
    /// </summary>
    public long TotalBytes { get; set; }
    
    /// <summary>
    /// 完成百分比
    /// </summary>
    public double Percentage { get; set; }
    
    /// <summary>
    /// 状态描述
    /// </summary>
    public string Status { get; set; } = string.Empty;
}