namespace BiliDownloader.Services.Download;

public enum DownloadFailureKind
{
    Network,
    Protocol,
    Authentication,
    Ffmpeg,
    Disk,
    ResourceUnavailable,
    Unknown,
    Conflict,
}

public sealed record DownloadFailure(
    DownloadFailureKind Kind,
    bool IsRetryable,
    string ActionHint)
{
    public string StorageValue => Kind switch
    {
        DownloadFailureKind.Network => "network",
        DownloadFailureKind.Protocol => "cdn",
        DownloadFailureKind.Authentication => "auth",
        DownloadFailureKind.Ffmpeg => "ffmpeg",
        DownloadFailureKind.Disk => "disk",
        DownloadFailureKind.ResourceUnavailable => "resource",
        DownloadFailureKind.Conflict => "conflict",
        _ => "unknown",
    };
}

public sealed class MediaAuthorizationException : Exception
{
    public MediaAuthorizationException(string message, Exception? inner = null) : base(message, inner) { }
}

public sealed class FfmpegExecutionException : Exception
{
    public FfmpegExecutionException(string message, Exception? inner = null) : base(message, inner) { }
}

public sealed class ResourceUnavailableException : Exception
{
    public ResourceUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>写入前硬检查发现磁盘空间不足；协调器应暂停任务并保留断点。</summary>
public sealed class InsufficientDiskSpaceException : IOException
{
    public InsufficientDiskSpaceException(long required, long available)
        : base($"磁盘空间不足，需要 {required} 字节，可用 {available} 字节") { }
}

/// <summary>提交后目标被外部程序占用；该异常绝不能回退为覆盖写入。</summary>
public sealed class OutputConflictException : IOException
{
    public OutputConflictException(string path) : base($"输出文件已被其他程序占用：{path}") { }
}

internal static class DownloadErrorClassifier
{
    public static DownloadFailure ClassifyFailure(Exception exception)
    {
        var ex = exception is AggregateException aggregate
            ? aggregate.GetBaseException()
            : exception;

        return ex switch
        {
            DownloadProtocolException => new(DownloadFailureKind.Protocol, true, "切换下载节点后重试"),
            MediaAuthorizationException => new(DownloadFailureKind.Authentication, false, "重新登录后恢复任务"),
            FfmpegExecutionException => new(DownloadFailureKind.Ffmpeg, false, "检查 ffmpeg 设置后重试合并"),
            ResourceUnavailableException => new(DownloadFailureKind.ResourceUnavailable, false, "重新解析内容确认资源状态"),
            OutputConflictException => new(DownloadFailureKind.Conflict, false, "重新执行文件冲突预检"),
            InsufficientDiskSpaceException => new(DownloadFailureKind.Disk, false, "释放空间或更换输出目录后继续"),
            HttpRequestException => new(DownloadFailureKind.Network, true, "检查网络后重试"),
            TaskCanceledException => new(DownloadFailureKind.Network, true, "检查网络后重试"),
            UnauthorizedAccessException => new(DownloadFailureKind.Disk, false, "更换有写入权限的目录"),
            IOException => new(DownloadFailureKind.Disk, false, "检查磁盘空间和目录权限"),
            _ when ex.Message.Contains("ffmpeg", StringComparison.OrdinalIgnoreCase)
                => new(DownloadFailureKind.Ffmpeg, false, "检查 ffmpeg 设置后重试合并"),
            _ => new(DownloadFailureKind.Unknown, false, "查看详细日志"),
        };
    }

    public static (string ErrorType, bool IsRetryable) Classify(Exception ex)
    {
        var result = ClassifyFailure(ex);
        return (result.StorageValue, result.IsRetryable);
    }
}
