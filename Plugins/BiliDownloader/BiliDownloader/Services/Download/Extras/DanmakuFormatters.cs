using System.Globalization;
using System.Security;
using System.Text;
using System.Text.Json;
using BiliDownloader.Models;

namespace BiliDownloader.Services.Download.Extras;

/// <summary>弹幕输出策略；抓取、Protobuf 解码和文本格式完全分离，便于离线验证。</summary>
public interface IDanmakuFormatter
{
    DanmakuOutputFormat FormatType { get; }
    string FileExtension { get; }
    string Format(IReadOnlyList<DanmakuElem> elements);
}

/// <summary>弹幕规范化器：跨分段按稳定 ID 去重并建立与网络返回顺序无关的确定性时间轴。</summary>
public static class DanmakuNormalizer
{
    public static IReadOnlyList<DanmakuElem> Normalize(IEnumerable<DanmakuElem> elements)
    {
        ArgumentNullException.ThrowIfNull(elements);
        return elements
            .Where(static element => element is not null && !string.IsNullOrWhiteSpace(element.Content))
            .GroupBy(static element => !string.IsNullOrWhiteSpace(element.IdStr)
                ? "s:" + element.IdStr
                : element.Id != 0 ? "n:" + element.Id.ToString(CultureInfo.InvariantCulture)
                    : $"c:{element.Progress}:{element.Mode}:{element.Content}", StringComparer.Ordinal)
            .Select(static group => group.First())
            .OrderBy(static element => element.Progress)
            .ThenBy(static element => element.Id)
            .ThenBy(static element => element.IdStr, StringComparer.Ordinal)
            .ToArray();
    }
}

/// <summary>B 站兼容 XML；使用标准实体转义而不是删除字符，保证正文语义不丢失。</summary>
public sealed class XmlDanmakuFormatter : IDanmakuFormatter
{
    public DanmakuOutputFormat FormatType => DanmakuOutputFormat.Xml;
    public string FileExtension => ".xml";

    public string Format(IReadOnlyList<DanmakuElem> elements)
    {
        var builder = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<i>\n");
        foreach (var element in DanmakuNormalizer.Normalize(elements))
        {
            var progress = (element.Progress / 1000d).ToString("F3", CultureInfo.InvariantCulture);
            var p = string.Join(',', progress, element.Mode, element.Fontsize, element.Color,
                element.Ctime, element.Pool, element.MidHash, element.IdStr);
            builder.Append("  <d p=\"").Append(SecurityElement.Escape(p)).Append("\">")
                .Append(SecurityElement.Escape(element.Content) ?? string.Empty)
                .AppendLine("</d>");
        }
        return builder.AppendLine("</i>").ToString();
    }
}

/// <summary>安全、稳定的 JSON 投影；不序列化 Protobuf 未来可能增加的未知字段。</summary>
public sealed class JsonDanmakuFormatter : IDanmakuFormatter
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public DanmakuOutputFormat FormatType => DanmakuOutputFormat.Json;
    public string FileExtension => ".json";

    public string Format(IReadOnlyList<DanmakuElem> elements)
        => JsonSerializer.Serialize(DanmakuNormalizer.Normalize(elements).Select(static element => new
        {
            id = element.Id,
            idStr = element.IdStr,
            progressMilliseconds = element.Progress,
            mode = element.Mode,
            fontSize = element.Fontsize,
            color = element.Color,
            senderHash = element.MidHash,
            content = element.Content,
            createdAtUnixSeconds = element.Ctime,
            pool = element.Pool,
        }), Options);
}

/// <summary>
/// 默认弹幕 ASS 样式。分辨率、字体、字号、描边和持续时间均为常量；
/// 轨道只依赖已经排序的时间轴，不使用随机延迟，因此相同输入必然生成相同字节。
/// </summary>
public sealed class AssDanmakuFormatter : IDanmakuFormatter
{
    private const int PlayResX = 1920;
    private const int PlayResY = 1080;
    private const int FontSize = 36;
    private const int Outline = 2;
    private const double RollingSeconds = 8;
    private const double FixedSeconds = 5;
    private const int LaneHeight = 44;
    private const int LaneCount = 20;

    public DanmakuOutputFormat FormatType => DanmakuOutputFormat.Ass;
    public string FileExtension => ".ass";

    public string Format(IReadOnlyList<DanmakuElem> elements)
    {
        var builder = BuildHeader();
        var rollingFreeAt = new double[LaneCount];
        var topFreeAt = new double[LaneCount];
        var bottomFreeAt = new double[LaneCount];
        foreach (var element in DanmakuNormalizer.Normalize(elements))
        {
            var start = Math.Max(0, element.Progress / 1000d);
            var fixedPosition = element.Mode is 4 or 5;
            var duration = fixedPosition ? FixedSeconds : RollingSeconds;
            var end = start + duration;
            var lanes = element.Mode == 5 ? topFreeAt : element.Mode == 4 ? bottomFreeAt : rollingFreeAt;
            var lane = FindLane(lanes, start);
            lanes[lane] = end;
            var y = element.Mode == 4
                ? PlayResY - 60 - lane * LaneHeight
                : 40 + lane * LaneHeight;
            var position = element.Mode switch
            {
                4 or 5 => $"{{\\an8\\pos({PlayResX / 2},{y})}}",
                _ => $"{{\\move({PlayResX + 20},{y},-{EstimateTextWidth(element.Content)},{y})}}",
            };
            builder.Append("Dialogue: 0,").Append(FormatTime(start)).Append(',').Append(FormatTime(end))
                .Append(",Default,,0,0,0,,").Append(position)
                .Append("{\\c&H").Append(ToAssColor(element.Color)).Append("&}")
                .AppendLine(AssSubtitleFormatter.EscapeText(element.Content));
        }
        return builder.ToString();
    }

    private static StringBuilder BuildHeader()
    {
        var builder = new StringBuilder();
        builder.AppendLine("[Script Info]");
        builder.AppendLine("ScriptType: v4.00+");
        builder.AppendLine($"PlayResX: {PlayResX}");
        builder.AppendLine($"PlayResY: {PlayResY}");
        builder.AppendLine("WrapStyle: 2");
        builder.AppendLine();
        builder.AppendLine("[V4+ Styles]");
        builder.AppendLine("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding");
        builder.AppendLine($"Style: Default,Arial,{FontSize},&H00FFFFFF,&H000000FF,&H00101010,&H60000000,0,0,0,0,100,100,0,0,1,{Outline},0,7,0,0,0,1");
        builder.AppendLine();
        builder.AppendLine("[Events]");
        builder.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");
        return builder;
    }

    private static int FindLane(IReadOnlyList<double> freeAt, double start)
    {
        for (var index = 0; index < freeAt.Count; index++)
            if (freeAt[index] <= start) return index;
        var earliest = 0;
        for (var index = 1; index < freeAt.Count; index++)
            if (freeAt[index] < freeAt[earliest]) earliest = index;
        return earliest;
    }

    private static int EstimateTextWidth(string text) => Math.Max(1, text.EnumerateRunes().Count()) * FontSize;

    private static string ToAssColor(uint rgb)
    {
        var red = (rgb >> 16) & 0xff;
        var green = (rgb >> 8) & 0xff;
        var blue = rgb & 0xff;
        return $"{blue:X2}{green:X2}{red:X2}";
    }

    private static string FormatTime(double seconds)
    {
        var value = TimeSpan.FromSeconds(seconds);
        return $"{(long)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}.{value.Milliseconds / 10:00}";
    }
}

/// <summary>弹幕格式注册表；新增格式只需注册新策略，不修改处理器分支。</summary>
public sealed class DanmakuFormatterRegistry(IEnumerable<IDanmakuFormatter> formatters)
{
    private readonly IReadOnlyDictionary<DanmakuOutputFormat, IDanmakuFormatter> _formatters =
        formatters.ToDictionary(static formatter => formatter.FormatType);

    public IDanmakuFormatter Resolve(DanmakuOutputFormat format)
        => _formatters.TryGetValue(format, out var formatter)
            ? formatter
            : throw new NotSupportedException($"未注册弹幕格式 {format}。");
}
