using System.Diagnostics;
using System.Text.Json;
using BiliDownloader.Models;

namespace BiliDownloader.Services.Infrastructure;

/// <summary>
/// 基于 ffprobe JSON 的字幕轨验证器。验证发生在原子替换主媒体之前；任何无法证明的轨道
/// 都会阻止候选文件发布，但不会删除或修改已经可播放的无字幕主文件。
/// </summary>
public sealed class FfprobeSubtitleTrackVerifier(
    IFfmpegRuntimeLocator runtimeLocator,
    IFfmpegProcessFactory processFactory,
    TimeSpan? timeout = null) : ISubtitleTrackVerifier
{
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(15);

    public async Task VerifyAsync(
        string mediaPath,
        IReadOnlyList<SubtitleMuxTrack> expectedTracks,
        OutputContainer outputContainer,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(mediaPath)) throw new MediaValidationException("软字幕候选文件不存在。");
        var ffprobePath = ResolveFfprobePath();
        using var timeoutSource = new CancellationTokenSource(_timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeoutSource.Token);
        var startInfo = new ProcessStartInfo
        {
            FileName = ffprobePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in new[]
        {
            "-v", "error", "-print_format", "json", "-show_streams", mediaPath,
        }) startInfo.ArgumentList.Add(argument);

        using var process = processFactory.Start(startInfo);
        using var registration = linked.Token.Register(() => TryKill(process));
        try
        {
            var stdout = process.ReadStandardOutputAsync(linked.Token);
            var stderr = process.ReadStandardErrorAsync(linked.Token);
            await process.WaitForExitAsync(linked.Token);
            var json = await stdout;
            var error = await stderr;
            if (process.ExitCode != 0)
                throw new MediaValidationException(
                    $"ffprobe 无法读取软字幕候选文件（退出码 {process.ExitCode}）：{Sanitize(error)}");
            ValidateJson(json, expectedTracks, outputContainer);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new MediaValidationException($"软字幕轨验证超过 {_timeout.TotalSeconds:0} 秒。");
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
        catch (JsonException ex)
        {
            throw new MediaValidationException("ffprobe 返回了无法解析的字幕轨 JSON。", ex);
        }
    }

    internal static void ValidateJson(
        string json,
        IReadOnlyList<SubtitleMuxTrack> expectedTracks,
        OutputContainer outputContainer)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("streams", out var streams)
            || streams.ValueKind != JsonValueKind.Array)
            throw new JsonException("streams 数组缺失。");
        var actual = streams.EnumerateArray()
            .Where(stream => ReadString(stream, "codec_type").Equals("subtitle", StringComparison.OrdinalIgnoreCase))
            .Select(stream => new ActualSubtitleTrack(
                ReadString(stream, "codec_name"),
                ReadTag(stream, "language"),
                ReadTag(stream, "title")))
            .ToList();
        // 轨数必须精确相等。只验证“至少存在期望轨”会放过重试时累积的重复字幕，
        // 而这种候选一旦替换主文件就很难由用户察觉和恢复。
        if (actual.Count != expectedTracks.Count)
            throw new MediaValidationException(
                $"软字幕轨验证失败：期望 {expectedTracks.Count} 轨，实际 {actual.Count} 轨。" );
        var used = new HashSet<int>();
        foreach (var expected in expectedTracks)
        {
            var codec = outputContainer == OutputContainer.Mp4
                ? "mov_text"
                : expected.SourceFormat == SubtitleOutputFormat.Ass ? "ass" : "subrip";
            // 使用可空索引表达“没有匹配”，不能用 FirstOrDefault(int)；后者在失败时返回 0，
            // 会把第一条真实轨误当作匹配，掩盖缺失的语言或标题元数据。
            var match = Enumerable.Range(0, actual.Count)
                .Where(index => !used.Contains(index)
                    && actual[index].Codec.Equals(codec, StringComparison.OrdinalIgnoreCase)
                    && actual[index].Language.Equals(expected.LanguageKey, StringComparison.OrdinalIgnoreCase)
                    && actual[index].Title.Equals(expected.Title, StringComparison.Ordinal))
                .Select(static index => (int?)index)
                .FirstOrDefault();
            if (match is null)
                throw new MediaValidationException(
                    $"软字幕轨验证失败：缺少 {expected.LanguageKey}/{codec}/{expected.Title}。" );
            used.Add(match.Value);
        }
    }

    private string ResolveFfprobePath()
    {
        var ffmpegPath = runtimeLocator.ResolvedPath ?? runtimeLocator.ResolveFfmpegPath();
        if (string.IsNullOrWhiteSpace(ffmpegPath))
            throw new MediaValidationException("无法定位 ffmpeg，因而不能验证软字幕轨。");
        var full = Path.GetFullPath(ffmpegPath);
        var candidate = Path.Combine(Path.GetDirectoryName(full)!, "ffprobe" + Path.GetExtension(full));
        if (!File.Exists(candidate)) throw new MediaValidationException("ffmpeg 同目录缺少 ffprobe。");
        return candidate;
    }

    private static string ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty : string.Empty;

    private static string ReadTag(JsonElement element, string name)
        => element.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Object
            ? ReadString(tags, name) : string.Empty;

    private static string Sanitize(string value)
    {
        var line = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return line.Length <= 240 ? line : line[..240];
    }

    private static void TryKill(IFfmpegProcess process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
    }

    private sealed record ActualSubtitleTrack(string Codec, string Language, string Title);
}
