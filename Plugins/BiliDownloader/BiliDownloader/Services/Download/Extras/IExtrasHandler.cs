using BiliDownloader.Services.Api;
using BiliDownloader.Models;
using BiliDownloader.Services.Download;

namespace BiliDownloader.Services.Download.Extras;

/// <summary>
/// 附加资源类型（位枚举，支持组合选择）
/// </summary>
[Flags]
public enum ExtrasType
{
    None = 0,

    /// <summary>弹幕（XML 格式，可选转 ASS）</summary>
    Danmaku = 1 << 0,

    /// <summary>字幕（SRT 格式）</summary>
    Subtitle = 1 << 1,

    /// <summary>封面图</summary>
    Cover = 1 << 2,

    // 未来扩展:
    // Nfo       = 1 << 3,
    // AiSummary = 1 << 4,
}

/// <summary>
/// 策略接口：附加资源处理器。
/// 每种 extras 类型实现此接口，协调器通过接口调用，不依赖具体实现（DIP）。
/// </summary>
public interface IExtrasHandler
{
    /// <summary>处理器标识（如 "danmaku", "subtitle", "cover"）</summary>
    string Type { get; }

    /// <summary>人类可读名称（如 "弹幕", "字幕", "封面"）</summary>
    string DisplayName { get; }

    /// <summary>
    /// 执行附加资源下载
    /// </summary>
    /// <param name="context">执行上下文，包含任务信息、输出路径、API 依赖等</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>执行结果</returns>
    Task<ExtrasResult> ExecuteAsync(ExtrasContext context, CancellationToken ct);
}

/// <summary>
/// 处理器执行上下文（参数对象模式，避免接口参数膨胀）
/// </summary>
public class ExtrasContext
{
    // === 任务标识 ===
    public string TaskId { get; init; } = "";
    public long Aid { get; init; }
    public string Bvid { get; init; } = "";
    public long Cid { get; init; }
    public long EpId { get; init; }
    public long SeasonId { get; init; }
    public string MediaType { get; init; } = "video";

    /// <summary>视频时长（秒），弹幕分段计算需要</summary>
    public int Duration { get; init; }

    // === 路径 ===
    public string OutputDirectory { get; init; } = "";
    public string SubFolder { get; init; } = "";

    /// <summary>不含扩展名的基础文件名（已做文件名合法化处理）</summary>
    public string BaseFileName { get; init; } = "";

    /// <summary>附加资源必须服从与主视频相同的冲突策略，不能自行选择覆盖。</summary>
    public FileConflictPolicy ConflictPolicy { get; init; } = FileConflictPolicy.AutoNumber;

    /// <summary>覆盖策略是否已在本批提交中由用户明确确认。</summary>
    public bool OverwriteConfirmed { get; init; }

    // === 凭据 ===
    public string Cookie { get; init; } = "";

    // === 资源特定参数 ===
    /// <summary>封面图 URL（从 BiliVideoCollection.Cover 传递）</summary>
    public string CoverUrl { get; init; } = "";

    // === 外部依赖（由协调器注入） ===
    /// <summary>API 服务引用（用于弹幕/字幕 API 调用）</summary>
    public BiliApiService ApiService { get; init; } = null!;

    /// <summary>进度报告器（可选，用于反馈 extras 下载进度）</summary>
    public IProgress<string>? ProgressReporter { get; init; }
}

/// <summary>
/// 附加资源安全发布器：先写同目录暂存文件，再按已确认策略原子移动。
/// 这样自动序号任务不会因为字幕或封面晚到而静默覆盖外部文件，写入失败也不会留下半个成品。
/// </summary>
internal static class ExtrasOutputWriter
{
    public static async Task WriteTextAsync(string path, string content, ExtrasContext context, CancellationToken ct)
    {
        var staging = path + $".staging-{Guid.NewGuid():N}";
        try
        {
            await File.WriteAllTextAsync(staging, content, ct);
            Publish(staging, path, context);
        }
        finally
        {
            if (File.Exists(staging)) File.Delete(staging);
        }
    }

    public static async Task WriteBytesAsync(string path, byte[] content, ExtrasContext context, CancellationToken ct)
    {
        var staging = path + $".staging-{Guid.NewGuid():N}";
        try
        {
            await File.WriteAllBytesAsync(staging, content, ct);
            Publish(staging, path, context);
        }
        finally
        {
            if (File.Exists(staging)) File.Delete(staging);
        }
    }

    private static void Publish(string staging, string path, ExtrasContext context)
    {
        var mayOverwrite = context.ConflictPolicy == FileConflictPolicy.Overwrite
            && context.OverwriteConfirmed;
        if (File.Exists(path) && !mayOverwrite) throw new OutputConflictException(path);
        File.Move(staging, path, overwrite: mayOverwrite);
    }
}

/// <summary>
/// 处理器执行结果
/// </summary>
public class ExtrasResult
{
    /// <summary>处理器类型标识</summary>
    public string Type { get; init; } = "";

    /// <summary>是否执行成功</summary>
    public bool Success { get; init; }

    /// <summary>输出文件路径列表</summary>
    public List<string> OutputFiles { get; init; } = new();

    /// <summary>错误信息（仅失败时有值）</summary>
    public string? ErrorMessage { get; init; }

    public static ExtrasResult Succeeded(string type, params string[] files)
        => new() { Type = type, Success = true, OutputFiles = files.ToList() };

    public static ExtrasResult Failed(string type, string error)
        => new() { Type = type, Success = false, ErrorMessage = error };
}
