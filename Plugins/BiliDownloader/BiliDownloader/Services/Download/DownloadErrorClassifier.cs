using BiliDownloader.Services.Infrastructure;

namespace BiliDownloader.Services.Download;

/// <summary>稳定的失败类型；存储值由 <see cref="DownloadFailure.StorageValue"/> 统一维护。</summary>
public enum DownloadFailureKind
{
    Network,
    Protocol,
    Authentication,
    Ffmpeg,
    Directory,
    Disk,
    ResourceUnavailable,
    Merge,
    Unknown,
    Conflict,
    MediaValidation,
}

/// <summary>
/// UI 可执行的有限行动集合。持久化层只保存错误类型，行动由策略按当前版本生成，
/// 因此未来可以改进按钮文案或处理流程而不迁移历史任务。
/// </summary>
public enum DownloadFailureActionKind
{
    None,
    LoginAndContinue,
    InstallOrRepairFfmpeg,
    SelectCustomFfmpeg,
    ChangeOutputDirectory,
    Continue,
    Retry,
    RetryMerge,
    OpenLogs,
    Restart,
}

/// <summary>一个可绑定的错误行动；Kind 用于路由，Label 只用于展示。</summary>
public sealed record DownloadFailureAction(DownloadFailureActionKind Kind, string Label);

/// <summary>任务卡片所需的完整错误展示，不包含技术异常文本或敏感数据。</summary>
public sealed record DownloadFailurePresentation(
    string UserMessage,
    DownloadFailureAction PrimaryAction,
    DownloadFailureAction? SecondaryAction = null);

/// <summary>
/// 分类结果同时包含持久化事实和用户可读信息。Coordinator 记录技术异常到脱敏日志，
/// 只把 <see cref="UserMessage"/> 写入任务记录，避免 UI 暴露冗长 stderr。
/// </summary>
public sealed record DownloadFailure(
    DownloadFailureKind Kind,
    bool IsRetryable,
    string UserMessage,
    DownloadFailureAction PrimaryAction,
    DownloadFailureAction? SecondaryAction = null)
{
    public string StorageValue => Kind switch
    {
        DownloadFailureKind.Network => "network",
        DownloadFailureKind.Protocol => "cdn",
        DownloadFailureKind.Authentication => "auth",
        DownloadFailureKind.Ffmpeg => "ffmpeg",
        DownloadFailureKind.Directory => "directory",
        DownloadFailureKind.Disk => "disk",
        DownloadFailureKind.ResourceUnavailable => "resource",
        DownloadFailureKind.Merge => "merge",
        DownloadFailureKind.Conflict => "conflict",
        DownloadFailureKind.MediaValidation => "media_validation",
        _ => "unknown",
    };
}

public sealed class MediaAuthorizationException : Exception
{
    public MediaAuthorizationException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>G0-G6 兼容异常；新 ffmpeg 合并代码应抛出 <see cref="MediaMergeException"/>。</summary>
public sealed class FfmpegExecutionException : Exception
{
    public FfmpegExecutionException(string message, Exception? inner = null) : base(message, inner) { }
}

public sealed class ResourceUnavailableException : Exception
{
    public ResourceUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>输出目录无法创建或写入；与容量不足分开，才能给出“更换目录”行动。</summary>
public sealed class OutputDirectoryException : IOException
{
    public OutputDirectoryException(string message, Exception? inner = null) : base(message, inner) { }
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

/// <summary>把异常类型转换为稳定失败事实；新代码不得依赖本地化异常消息进行主分类。</summary>
internal static class DownloadErrorClassifier
{
    public static DownloadFailure ClassifyFailure(Exception exception)
    {
        var ex = exception is AggregateException aggregate ? aggregate.GetBaseException() : exception;
        return ex switch
        {
            DownloadProtocolException => Failure(DownloadFailureKind.Protocol, true,
                "下载节点响应异常，请更换节点后重试。", DownloadFailureActionKind.Retry, "更换节点重试"),
            MediaAuthorizationException => Failure(DownloadFailureKind.Authentication, false,
                "登录状态已失效，重新登录后可以继续任务。", DownloadFailureActionKind.LoginAndContinue, "重新登录并继续"),
            FfmpegUnavailableException => Failure(DownloadFailureKind.Ffmpeg, false,
                "ffmpeg 缺失或损坏，修复后可以继续合并。", DownloadFailureActionKind.InstallOrRepairFfmpeg, "安装/修复并继续合并",
                DownloadFailureActionKind.SelectCustomFfmpeg, "选择自定义路径"),
            MediaMergeException or FfmpegExecutionException => Failure(DownloadFailureKind.Merge, true,
                "音视频合并失败，已保留下载完成的临时媒体。", DownloadFailureActionKind.RetryMerge, "仅重试合并",
                DownloadFailureActionKind.OpenLogs, "查看日志"),
            MediaValidationException => Failure(DownloadFailureKind.MediaValidation, false,
                "成品媒体特征与预期不一致，已阻止发布并保留可信输入。", DownloadFailureActionKind.Restart, "完整重试",
                DownloadFailureActionKind.OpenLogs, "查看日志"),
            ResourceUnavailableException => Failure(DownloadFailureKind.ResourceUnavailable, true,
                "媒体资源已失效或暂不可用，请重新解析后重试。", DownloadFailureActionKind.Retry, "重新解析并重试",
                DownloadFailureActionKind.OpenLogs, "查看日志"),
            OutputConflictException => Failure(DownloadFailureKind.Conflict, false,
                "输出位置出现新的文件冲突，请更换输出位置。", DownloadFailureActionKind.ChangeOutputDirectory, "更换输出位置",
                DownloadFailureActionKind.OpenLogs, "查看日志"),
            InsufficientDiskSpaceException => Failure(DownloadFailureKind.Disk, false,
                "磁盘空间不足，请释放空间后继续或更换目录。", DownloadFailureActionKind.Continue, "重新检查并继续",
                DownloadFailureActionKind.ChangeOutputDirectory, "更换目录"),
            OutputDirectoryException or UnauthorizedAccessException => Failure(DownloadFailureKind.Directory, false,
                "输出目录无法写入，请选择新的输出目录。", DownloadFailureActionKind.ChangeOutputDirectory, "更换目录并继续",
                DownloadFailureActionKind.OpenLogs, "查看日志"),
            HttpRequestException or TaskCanceledException => Failure(DownloadFailureKind.Network, true,
                "网络连接失败，请检查网络后重试。", DownloadFailureActionKind.Retry, "重试",
                DownloadFailureActionKind.OpenLogs, "查看日志"),
            IOException => Failure(DownloadFailureKind.Disk, false,
                "磁盘读写失败，请检查空间和文件系统状态。", DownloadFailureActionKind.Continue, "重新检查并继续",
                DownloadFailureActionKind.ChangeOutputDirectory, "更换目录"),
            // 只为历史第三方执行器保留兼容兜底；本仓库新代码均使用上面的明确异常类型。
            _ when ex.Message.Contains("ffmpeg", StringComparison.OrdinalIgnoreCase)
                => Failure(DownloadFailureKind.Ffmpeg, false,
                    "ffmpeg 缺失或损坏，修复后可以继续合并。", DownloadFailureActionKind.InstallOrRepairFfmpeg, "安装/修复并继续合并",
                    DownloadFailureActionKind.OpenLogs, "查看日志"),
            _ => Failure(DownloadFailureKind.Unknown, false,
                "任务发生未识别错误，请查看日志定位问题。", DownloadFailureActionKind.OpenLogs, "查看日志",
                DownloadFailureActionKind.Restart, "完整重试"),
        };
    }

    public static (string ErrorType, bool IsRetryable) Classify(Exception ex)
    {
        var result = ClassifyFailure(ex);
        return (result.StorageValue, result.IsRetryable);
    }

    private static DownloadFailure Failure(
        DownloadFailureKind kind,
        bool retryable,
        string message,
        DownloadFailureActionKind primaryKind,
        string primaryLabel,
        DownloadFailureActionKind? secondaryKind = null,
        string? secondaryLabel = null)
        => new(kind, retryable, message, new(primaryKind, primaryLabel),
            secondaryKind is null ? null : new(secondaryKind.Value, secondaryLabel!));
}

/// <summary>把历史持久化错误值映射为当前版本的用户行动，不解析 ErrorMessage 文本。</summary>
public interface IDownloadFailurePresentationPolicy
{
    DownloadFailurePresentation Resolve(string? errorType);
}

public sealed class DownloadFailurePresentationPolicy : IDownloadFailurePresentationPolicy
{
    public DownloadFailurePresentation Resolve(string? errorType)
    {
        var failure = errorType?.ToLowerInvariant() switch
        {
            "auth" => DownloadErrorClassifier.ClassifyFailure(new MediaAuthorizationException("")),
            "ffmpeg" => DownloadErrorClassifier.ClassifyFailure(new FfmpegUnavailableException("")),
            "directory" => DownloadErrorClassifier.ClassifyFailure(new OutputDirectoryException("")),
            "disk" => DownloadErrorClassifier.ClassifyFailure(new InsufficientDiskSpaceException(0, 0)),
            "network" => DownloadErrorClassifier.ClassifyFailure(new HttpRequestException()),
            "cdn" => DownloadErrorClassifier.ClassifyFailure(new DownloadProtocolException("")),
            "resource" => DownloadErrorClassifier.ClassifyFailure(new ResourceUnavailableException("")),
            "merge" => DownloadErrorClassifier.ClassifyFailure(new MediaMergeException("")),
            "conflict" => DownloadErrorClassifier.ClassifyFailure(new OutputConflictException("")),
            "media_validation" => DownloadErrorClassifier.ClassifyFailure(new MediaValidationException("")),
            _ => DownloadErrorClassifier.ClassifyFailure(new Exception()),
        };
        return new(failure.UserMessage, failure.PrimaryAction, failure.SecondaryAction);
    }
}
