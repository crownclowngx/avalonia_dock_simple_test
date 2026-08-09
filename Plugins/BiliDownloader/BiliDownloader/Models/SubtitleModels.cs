namespace BiliDownloader.Models;

/// <summary>字幕来源类型。未知值必须保持 Unknown，不能根据显示文案猜测为人工字幕。</summary>
public enum SubtitleSourceType
{
    Unknown,
    Official,
    AiGenerated,
}

/// <summary>
/// 可供用户选择的字幕轨描述。
/// <para>StableLanguageKey、SourceType 与 PlatformTrackId 是稳定事实；DownloadUrl 只允许在内存中短暂存在，
/// 禁止进入 Document、SQLite、日志和历史导出。</para>
/// </summary>
public sealed record SubtitleTrackDescriptor(
    string StableLanguageKey,
    string DisplayName,
    SubtitleSourceType SourceType,
    string PlatformTrackId,
    string DownloadUrl = "")
{
    /// <summary>官方轨优先于来源未知轨，AI 轨只在没有其他轨时作为兜底。</summary>
    public int SourcePriority => SourceType switch
    {
        SubtitleSourceType.Official => 0,
        SubtitleSourceType.Unknown => 1,
        SubtitleSourceType.AiGenerated => 2,
        _ => 3,
    };
}

/// <summary>与平台 JSON 解耦的字幕时间轴单元；时间统一使用 TimeSpan，格式化器不再处理浮点边界。</summary>
public sealed record SubtitleCue(TimeSpan Start, TimeSpan End, string Text, int OriginalIndex);

/// <summary>字幕时间轴规范化器。它是纯函数，供 API 映射和离线 fixture 测试共同使用。</summary>
public static class SubtitleCueNormalizer
{
    public static IReadOnlyList<SubtitleCue> Normalize(
        IEnumerable<(double StartSeconds, double EndSeconds, string? Text)> rawCues)
    {
        ArgumentNullException.ThrowIfNull(rawCues);
        var result = new List<SubtitleCue>();
        var index = 0;
        foreach (var raw in rawCues)
        {
            var currentIndex = index++;
            if (!double.IsFinite(raw.StartSeconds) || !double.IsFinite(raw.EndSeconds)) continue;
            var start = Math.Max(0, raw.StartSeconds);
            if (raw.EndSeconds <= start || string.IsNullOrWhiteSpace(raw.Text)) continue;
            var text = raw.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
            result.Add(new SubtitleCue(
                TimeSpan.FromSeconds(start),
                TimeSpan.FromSeconds(raw.EndSeconds),
                text,
                currentIndex));
        }

        return result
            .OrderBy(static cue => cue.Start)
            .ThenBy(static cue => cue.End)
            .ThenBy(static cue => cue.OriginalIndex)
            .ToArray();
    }
}

/// <summary>批量字幕探测后的 UI 投影；覆盖数量不写入 Document，只反映当前会话所选媒体。</summary>
public sealed record SubtitleLanguageAvailability(
    string StableLanguageKey,
    string DisplayName,
    SubtitleSourceType SourceType,
    int AvailableItemCount,
    int TotalItemCount);
