namespace BiliDownloader.Services.Download;

/// <summary>
/// 一次媒体传输完成后的可持久化事实。
/// ExpectedBytes 为 0 表示服务端没有提供可验证的总长度。
/// </summary>
public sealed record DownloadTransferResult(
    long ExpectedBytes,
    long ActualBytes,
    bool IntegrityPassed);

/// <summary>
/// 单个视频任务的媒体下载与合并结果。
/// </summary>
public sealed record BiliDownloadItemResult(
    string OutputFilePath,
    DownloadTransferResult VideoTransfer,
    DownloadTransferResult AudioTransfer);

/// <summary>
/// 服务端 Range 响应与请求或声明长度不一致。
/// </summary>
public sealed class DownloadProtocolException : IOException
{
    public DownloadProtocolException(string message)
        : base(message)
    {
    }

    public DownloadProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
