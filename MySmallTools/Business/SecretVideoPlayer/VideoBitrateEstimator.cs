using System;
using System.IO;

namespace MySmallTools.Business.SecretVideoPlayer
{
    /// <summary>
    /// 视频比特率估算器
    /// 用于估算视频文件的比特率，以便计算合适的缓冲块大小
    /// </summary>
    public static class VideoBitrateEstimator
    {
        /// <summary>
        /// 根据文件大小估算比特率
        /// </summary>
        /// <param name="fileSizeBytes">文件大小（字节）</param>
        /// <param name="estimatedDurationSeconds">估算的视频时长（秒），如果未知则使用默认值</param>
        /// <returns>估算的比特率（bits per second）</returns>
        public static long EstimateBitrate(long fileSizeBytes, double estimatedDurationSeconds = 0)
        {
            // 如果没有提供时长，根据文件大小使用经验值
            if (estimatedDurationSeconds <= 0)
            {
                estimatedDurationSeconds = EstimateDurationFromFileSize(fileSizeBytes);
            }
            
            // 比特率 = (文件大小 * 8) / 时长
            var bitrate = (long)((fileSizeBytes * 8.0) / estimatedDurationSeconds);
            
            // 应用合理的范围限制
            return Math.Max(Math.Min(bitrate, 50_000_000), 500_000); // 500Kbps - 50Mbps
        }
        
        /// <summary>
        /// 根据文件大小估算视频时长
        /// </summary>
        private static double EstimateDurationFromFileSize(long fileSizeBytes)
        {
            // 基于常见视频质量的经验公式
            var fileSizeMB = fileSizeBytes / (1024.0 * 1024.0);
            
            if (fileSizeMB < 10) // 小文件，可能是短视频
                return 60; // 假设1分钟
            else if (fileSizeMB < 100) // 中等文件
                return 300; // 假设5分钟
            else if (fileSizeMB < 500) // 较大文件
                return 1800; // 假设30分钟
            else if (fileSizeMB < 2000) // 大文件
                return 5400; // 假设90分钟
            else // 超大文件
                return 7200; // 假设2小时
        }
        
        /// <summary>
        /// 计算指定秒数对应的字节数
        /// </summary>
        /// <param name="bitrate">比特率（bits per second）</param>
        /// <param name="seconds">秒数</param>
        /// <returns>对应的字节数</returns>
        public static int CalculateBytesForDuration(long bitrate, double seconds)
        {
            var bytes = (long)((bitrate * seconds) / 8.0);
            
            // 确保在合理范围内
            return (int)Math.Max(Math.Min(bytes, 100 * 1024 * 1024), 1024 * 1024); // 1MB - 100MB
        }
        
        /// <summary>
        /// 根据文件扩展名调整比特率估算
        /// </summary>
        /// <param name="baseBitrate">基础比特率</param>
        /// <param name="fileExtension">文件扩展名</param>
        /// <returns>调整后的比特率</returns>
        public static long AdjustBitrateByFormat(long baseBitrate, string fileExtension)
        {
            var extension = fileExtension?.ToLowerInvariant();
            
            return extension switch
            {
                ".mp4" => baseBitrate, // 标准
                ".avi" => (long)(baseBitrate * 1.2), // 通常压缩率较低
                ".mkv" => (long)(baseBitrate * 1.1), // 稍高质量
                ".mov" => (long)(baseBitrate * 1.15), // Apple格式，通常质量较高
                ".wmv" => (long)(baseBitrate * 0.8), // 微软格式，压缩率较高
                ".flv" => (long)(baseBitrate * 0.7), // Flash格式，高压缩
                ".webm" => (long)(baseBitrate * 0.9), // Web优化格式
                _ => baseBitrate
            };
        }
        
        /// <summary>
        /// 智能估算比特率（综合多种因素）
        /// </summary>
        /// <param name="stream">视频流</param>
        /// <param name="fileName">文件名（用于格式判断）</param>
        /// <param name="estimatedDuration">估算时长（可选）</param>
        /// <returns>智能估算的比特率</returns>
        public static long SmartEstimate(Stream stream, string? fileName = null, double estimatedDuration = 0)
        {
            var fileSize = stream.Length;
            var baseBitrate = EstimateBitrate(fileSize, estimatedDuration);
            
            // 根据文件格式调整
            if (!string.IsNullOrEmpty(fileName))
            {
                var extension = Path.GetExtension(fileName);
                baseBitrate = AdjustBitrateByFormat(baseBitrate, extension);
            }
            
            return baseBitrate;
        }
    }
}