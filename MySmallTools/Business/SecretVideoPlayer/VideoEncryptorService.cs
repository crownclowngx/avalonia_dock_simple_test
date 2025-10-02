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

}