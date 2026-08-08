using System.Diagnostics;
using System.Text.Json;
using BiliDownloader.Models;

namespace BiliDownloader.Services.Infrastructure;

/// <summary>
/// 发布前媒体验证边界。下载编排只表达“验证 staging 文件”，不负责拼接 ffprobe 参数或解释 JSON，
/// 因而外部进程、协议解析与文件发布三个变化方向彼此隔离。
/// </summary>
public interface IMediaOutputVerifier
{
    /// <summary>
    /// 验证 staging 文件，并返回实际检测到的高规格特征。实现必须在超时、格式损坏或特征冲突时抛出
    /// <see cref="MediaValidationException"/>；调用者据此禁止原子发布。
    /// </summary>
    Task<MediaFeatureFlags> VerifyAsync(
        string mediaPath,
        MediaFeatureFlags expectedFeatures,
        CancellationToken cancellationToken = default);
}

/// <summary>ffprobe 无法证明成品与预期一致时的结构化异常。</summary>
public sealed class MediaValidationException : Exception
{
    public MediaValidationException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// 无可用 ffprobe 依赖时的安全默认实现。标准媒体不会调用它；高规格媒体宁可明确失败，也不能绕过验证。
/// </summary>
public sealed class UnavailableMediaOutputVerifier : IMediaOutputVerifier
{
    public Task<MediaFeatureFlags> VerifyAsync(
        string mediaPath,
        MediaFeatureFlags expectedFeatures,
        CancellationToken cancellationToken = default)
        => expectedFeatures == MediaFeatureFlags.None
            ? Task.FromResult(MediaFeatureFlags.None)
            : Task.FromException<MediaFeatureFlags>(new MediaValidationException(
                "当前运行时没有可用的 ffprobe 验证器，高规格媒体已被安全阻止。"));
}

/// <summary>
/// 基于 ffprobe JSON 的生产验证器。它只接受 ffmpeg 定位器解析出的同目录 ffprobe，避免系统 PATH 中
/// 另一个不受控版本参与验证；15 秒上限防止损坏媒体让下载工作线程无限等待。
/// </summary>
public sealed class FfprobeMediaOutputVerifier(
    IFfmpegRuntimeLocator runtimeLocator,
    IFfmpegProcessFactory processFactory,
    TimeSpan? timeout = null) : IMediaOutputVerifier
{
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(15);

    /// <inheritdoc />
    public async Task<MediaFeatureFlags> VerifyAsync(
        string mediaPath,
        MediaFeatureFlags expectedFeatures,
        CancellationToken cancellationToken = default)
    {
        if (expectedFeatures == MediaFeatureFlags.None) return MediaFeatureFlags.None;
        if (string.IsNullOrWhiteSpace(mediaPath) || !File.Exists(mediaPath))
            throw new MediaValidationException("发布前媒体验证失败：staging 文件不存在。");

        var ffprobePath = ResolveFfprobePath();
        using var timeoutCts = new CancellationTokenSource(_timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        using var process = processFactory.Start(new ProcessStartInfo
        {
            FileName = ffprobePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            ArgumentList =
            {
                "-v", "error",
                "-print_format", "json",
                "-show_streams",
                mediaPath,
            },
        });

        try
        {
            var outputTask = process.ReadStandardOutputAsync(linkedCts.Token);
            var errorTask = process.ReadStandardErrorAsync(linkedCts.Token);
            await process.WaitForExitAsync(linkedCts.Token);
            var output = await outputTask;
            var error = await errorTask;
            if (process.ExitCode != 0)
                throw new MediaValidationException(
                    $"ffprobe 无法读取 staging 媒体（退出码 {process.ExitCode}）：{SanitizeDiagnostic(error)}");

            var actual = ParseFeatures(output);
            if (actual != expectedFeatures)
                throw new MediaValidationException(
                    $"发布前媒体特征冲突：预期 {expectedFeatures}，实际 {actual}。已阻止发布。");
            return actual;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new MediaValidationException($"ffprobe 验证超过 {_timeout.TotalSeconds:0} 秒，已阻止发布。");
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
        catch (JsonException ex)
        {
            throw new MediaValidationException("ffprobe 返回了无法解析的 JSON，已阻止发布。", ex);
        }
    }

    internal static MediaFeatureFlags ParseFeatures(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("streams", out var streams)
            || streams.ValueKind != JsonValueKind.Array)
            throw new JsonException("streams 数组缺失。");

        var result = MediaFeatureFlags.None;
        foreach (var stream in streams.EnumerateArray())
        {
            var codecType = ReadString(stream, "codec_type");
            if (codecType.Equals("video", StringComparison.OrdinalIgnoreCase))
            {
                var isDolbyVision = HasDolbyVisionSideData(stream);
                if (isDolbyVision)
                    result |= MediaFeatureFlags.DolbyVision;
                else if (ReadString(stream, "color_primaries").Equals("bt2020", StringComparison.OrdinalIgnoreCase)
                         && ReadString(stream, "color_transfer").Equals("smpte2084", StringComparison.OrdinalIgnoreCase))
                    result |= MediaFeatureFlags.Hdr;
            }
            else if (codecType.Equals("audio", StringComparison.OrdinalIgnoreCase))
            {
                var codecName = ReadString(stream, "codec_name");
                var profile = ReadString(stream, "profile");
                if (codecName.Equals("flac", StringComparison.OrdinalIgnoreCase))
                    result |= MediaFeatureFlags.HiResAudio;
                if ((codecName.Equals("eac3", StringComparison.OrdinalIgnoreCase)
                     || codecName.Equals("ec-3", StringComparison.OrdinalIgnoreCase))
                    && profile.Contains("Atmos", StringComparison.OrdinalIgnoreCase))
                    result |= MediaFeatureFlags.DolbyAtmos;
            }
        }
        return result;
    }

    private string ResolveFfprobePath()
    {
        var ffmpegPath = runtimeLocator.ResolvedPath ?? runtimeLocator.ResolveFfmpegPath();
        if (string.IsNullOrWhiteSpace(ffmpegPath))
            throw new MediaValidationException("无法定位 ffmpeg，因而不能解析同目录 ffprobe。");
        var fullFfmpegPath = Path.GetFullPath(ffmpegPath);
        var extension = Path.GetExtension(fullFfmpegPath);
        var candidate = Path.Combine(Path.GetDirectoryName(fullFfmpegPath)!, "ffprobe" + extension);
        if (!File.Exists(candidate))
            throw new MediaValidationException("已找到 ffmpeg，但同目录缺少 ffprobe；高规格媒体不能在未验证时发布。");
        return candidate;
    }

    private static bool HasDolbyVisionSideData(JsonElement stream)
    {
        if (!stream.TryGetProperty("side_data_list", out var sideData)
            || sideData.ValueKind != JsonValueKind.Array) return false;
        foreach (var item in sideData.EnumerateArray())
        {
            if (ReadString(item, "side_data_type").Contains("DOVI configuration record", StringComparison.OrdinalIgnoreCase))
                return true;
            if (item.TryGetProperty("dv_profile", out var profile)
                && profile.TryGetInt32(out var number) && number > 0)
                return true;
        }
        return false;
    }

    private static string ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string SanitizeDiagnostic(string value)
    {
        // stderr 只保留单行短摘要，避免临时路径或第三方元数据未经控制地进入任务错误字段。
        var firstLine = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return firstLine.Length <= 240 ? firstLine : firstLine[..240];
    }

    private static void TryKill(IFfmpegProcess process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
    }
}
