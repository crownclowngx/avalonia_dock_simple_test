using System.Globalization;
using System.Text;
using BiliDownloader.Models;
using BiliDownloader.Services.Api;

namespace BiliDownloader.Services.Download.Extras;

/// <summary>字幕格式策略；每个实现只负责一种确定性文本格式，符合开闭原则。</summary>
public interface ISubtitleFormatter
{
    SubtitleOutputFormat FormatType { get; }
    string FileExtension { get; }
    string Format(IReadOnlyList<SubtitleCue> cues);
}

/// <summary>SRT 格式化器；小时使用总小时数，避免超过一天的媒体时间轴回绕。</summary>
public sealed class SrtSubtitleFormatter : ISubtitleFormatter
{
    public SubtitleOutputFormat FormatType => SubtitleOutputFormat.Srt;
    public string FileExtension => ".srt";

    public string Format(IReadOnlyList<SubtitleCue> cues)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < cues.Count; index++)
        {
            var cue = cues[index];
            builder.AppendLine((index + 1).ToString(CultureInfo.InvariantCulture));
            builder.Append(FormatTime(cue.Start)).Append(" --> ").AppendLine(FormatTime(cue.End));
            builder.AppendLine(cue.Text);
            builder.AppendLine();
        }
        return builder.ToString();
    }

    private static string FormatTime(TimeSpan value)
        => $"{(long)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00},{value.Milliseconds:000}";
}

/// <summary>WebVTT 格式化器；正文保留 Unicode 和用户换行，只统一时间分隔符。</summary>
public sealed class VttSubtitleFormatter : ISubtitleFormatter
{
    public SubtitleOutputFormat FormatType => SubtitleOutputFormat.Vtt;
    public string FileExtension => ".vtt";

    public string Format(IReadOnlyList<SubtitleCue> cues)
    {
        var builder = new StringBuilder("WEBVTT\n\n");
        foreach (var cue in cues)
        {
            builder.Append(FormatTime(cue.Start)).Append(" --> ").AppendLine(FormatTime(cue.End));
            builder.AppendLine(cue.Text);
            builder.AppendLine();
        }
        return builder.ToString();
    }

    private static string FormatTime(TimeSpan value)
        => $"{(long)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}.{value.Milliseconds:000}";
}

/// <summary>外置字幕 ASS 格式化器；使用中性默认样式，不依赖或分发第三方字体文件。</summary>
public sealed class AssSubtitleFormatter : ISubtitleFormatter
{
    public SubtitleOutputFormat FormatType => SubtitleOutputFormat.Ass;
    public string FileExtension => ".ass";

    public string Format(IReadOnlyList<SubtitleCue> cues)
    {
        var builder = new StringBuilder();
        builder.AppendLine("[Script Info]");
        builder.AppendLine("ScriptType: v4.00+");
        builder.AppendLine("PlayResX: 1920");
        builder.AppendLine("PlayResY: 1080");
        builder.AppendLine();
        builder.AppendLine("[V4+ Styles]");
        builder.AppendLine("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding");
        builder.AppendLine("Style: Default,Arial,48,&H00FFFFFF,&H000000FF,&H00101010,&H80000000,0,0,0,0,100,100,0,0,1,2,0,2,60,60,45,1");
        builder.AppendLine();
        builder.AppendLine("[Events]");
        builder.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");
        foreach (var cue in cues)
        {
            builder.Append("Dialogue: 0,")
                .Append(FormatTime(cue.Start)).Append(',')
                .Append(FormatTime(cue.End))
                .Append(",Default,,0,0,0,,")
                .AppendLine(EscapeText(cue.Text));
        }
        return builder.ToString();
    }

    internal static string EscapeText(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("{", "\\{", StringComparison.Ordinal)
        .Replace("}", "\\}", StringComparison.Ordinal)
        .Replace("\n", "\\N", StringComparison.Ordinal);

    private static string FormatTime(TimeSpan value)
        => $"{(long)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}.{value.Milliseconds / 10:00}";
}

/// <summary>按枚举解析格式策略，注册缺失时明确失败，不在执行阶段静默回退到 SRT。</summary>
public sealed class SubtitleFormatterRegistry(IEnumerable<ISubtitleFormatter> formatters)
{
    private readonly IReadOnlyDictionary<SubtitleOutputFormat, ISubtitleFormatter> _formatters =
        formatters.ToDictionary(static formatter => formatter.FormatType);

    public ISubtitleFormatter Resolve(SubtitleOutputFormat format)
        => _formatters.TryGetValue(format, out var formatter)
            ? formatter
            : throw new NotSupportedException($"未注册字幕格式 {format}。");
}

/// <summary>字幕目录服务；同语言只保留一轨，使用来源优先级和轨道 ID 保证结果确定。</summary>
public interface ISubtitleCatalogService
{
    Task<IReadOnlyList<SubtitleTrackDescriptor>> GetPreferredTracksAsync(
        long aid, long cid, string cookie, CancellationToken cancellationToken = default);
}

public sealed class SubtitleCatalogService(IBiliSubtitleApi api) : ISubtitleCatalogService
{
    public async Task<IReadOnlyList<SubtitleTrackDescriptor>> GetPreferredTracksAsync(
        long aid, long cid, string cookie, CancellationToken cancellationToken = default)
    {
        var tracks = await api.GetSubtitleTracksAsync(aid, cid, cookie, cancellationToken);
        return tracks
            .Where(static track => !string.IsNullOrWhiteSpace(track.StableLanguageKey))
            .GroupBy(static track => track.StableLanguageKey.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(static group => group
                .OrderBy(static track => track.SourcePriority)
                .ThenBy(static track => track.PlatformTrackId, StringComparer.Ordinal)
                .First())
            .OrderBy(static track => track.StableLanguageKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

/// <summary>字幕正文获取边界；处理器依赖该接口而不是具体 BiliApiService。</summary>
public interface ISubtitleContentProvider
{
    Task<IReadOnlyList<SubtitleCue>> GetCuesAsync(
        SubtitleTrackDescriptor track, string cookie, CancellationToken cancellationToken = default);
}

public sealed class SubtitleContentProvider(IBiliSubtitleApi api) : ISubtitleContentProvider
{
    public Task<IReadOnlyList<SubtitleCue>> GetCuesAsync(
        SubtitleTrackDescriptor track, string cookie, CancellationToken cancellationToken = default)
        => api.GetSubtitleCuesAsync(track.DownloadUrl, cookie, cancellationToken);
}
