namespace BiliDownloader.Services;

/// <summary>
/// ffmpeg 服务接口：抽象路径发现、验证和合并操作
/// </summary>
public interface IFfmpegService
{
    /// <summary>ffmpeg 是否就绪</summary>
    bool IsReady { get; }

    /// <summary>当前解析到的 ffmpeg 路径</summary>
    string? ResolvedPath { get; }

    /// <summary>验证指定路径是否为有效的 ffmpeg 可执行文件</summary>
    Task<bool> ValidatePathAsync(string path, CancellationToken ct = default);

    /// <summary>合并视频和音频</summary>
    Task MergeAsync(string videoPath, string audioPath, string outputPath, CancellationToken ct = default);
}
