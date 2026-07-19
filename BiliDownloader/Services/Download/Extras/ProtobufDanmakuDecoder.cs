using System.Text;
using System.Text.RegularExpressions;
using ProtoBuf;

namespace BiliDownloader.Services.Download.Extras;

/// <summary>
/// B站弹幕 Protobuf 解码器。
/// 将 /x/v2/dm/wbi/web/seg.so 返回的 Protobuf 二进制数据解码为弹幕元素列表，
/// 并可生成标准 B站弹幕 XML 格式（与 BiliTools dm.ts 一致）。
/// </summary>
public static partial class ProtobufDanmakuDecoder
{
    /// <summary>
    /// 解码 Protobuf 二进制数据为弹幕元素列表
    /// </summary>
    public static List<DanmakuElem> Decode(byte[] protobufData)
    {
        if (protobufData == null || protobufData.Length == 0)
            return new List<DanmakuElem>();

        try
        {
            using var stream = new MemoryStream(protobufData);
            var danmakuEvent = Serializer.Deserialize<DanmakuEvent>(stream);
            return danmakuEvent.Elems ?? new List<DanmakuElem>();
        }
        catch
        {
            // Protobuf 解码失败（可能是空段或结构变更），返回空列表
            return new List<DanmakuElem>();
        }
    }

    /// <summary>
    /// 将弹幕元素列表转换为标准 B站弹幕 XML 格式
    /// </summary>
    /// <remarks>
    /// 输出格式（与 BiliTools dm.ts 一致）：
    /// <![CDATA[
    /// <?xml version="1.0" encoding="UTF-8"?>
    /// <i>
    ///   <d p="1.234,1,25,16777215,1625000000,0,abc123,12345678">弹幕内容</d>
    /// </i>
    /// ]]>
    /// </remarks>
    public static string ToXml(List<DanmakuElem> elems)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<i>");

        foreach (var elem in elems)
        {
            // p 属性格式：出现时间(秒),模式,字号,颜色,创建时间,弹幕池,发送者hash,弹幕ID
            var progressSec = elem.Progress / 1000.0;
            var p = string.Join(",",
                progressSec.ToString("F3"),
                elem.Mode,
                elem.Fontsize,
                elem.Color,
                elem.Ctime,
                elem.Pool,
                elem.MidHash,
                elem.IdStr);

            // 清理弹幕内容中的 XML 特殊字符
            var content = StripXmlChars(elem.Content ?? "");

            sb.AppendLine($"  <d p=\"{p}\">{content}</d>");
        }

        sb.AppendLine("</i>");
        return sb.ToString();
    }

    /// <summary>
    /// 清理弹幕文本中的 XML 特殊字符
    /// </summary>
    private static string StripXmlChars(string text)
    {
        return XmlCharsRegex().Replace(text, "");
    }

    [GeneratedRegex(@"[<>&]")]
    private static partial Regex XmlCharsRegex();
}

#region Protobuf 数据模型

/// <summary>
/// B站弹幕事件（Protobuf 顶层结构，对应 dm.proto 中的 DmSegMobileReply）
/// </summary>
[ProtoContract]
public class DanmakuEvent
{
    /// <summary>弹幕元素列表</summary>
    [ProtoMember(1)]
    public List<DanmakuElem> Elems { get; set; } = new();
}

/// <summary>
/// 单条弹幕元素（对应 dm.proto 中的 DanmakuElem）
/// </summary>
[ProtoContract]
public class DanmakuElem
{
    /// <summary>弹幕 ID</summary>
    [ProtoMember(1)]
    public long Id { get; set; }

    /// <summary>出现时间（毫秒）</summary>
    [ProtoMember(2)]
    public int Progress { get; set; }

    /// <summary>弹幕模式（1=滚动, 4=底部, 5=顶部, 6=逆向）</summary>
    [ProtoMember(3)]
    public int Mode { get; set; }

    /// <summary>字号</summary>
    [ProtoMember(4)]
    public int Fontsize { get; set; }

    /// <summary>颜色（十进制 RGB）</summary>
    [ProtoMember(5)]
    public uint Color { get; set; }

    /// <summary>发送者 mid 的哈希</summary>
    [ProtoMember(6)]
    public string MidHash { get; set; } = "";

    /// <summary>弹幕文本内容</summary>
    [ProtoMember(7)]
    public string Content { get; set; } = "";

    /// <summary>发送时间（Unix 时间戳）</summary>
    [ProtoMember(8)]
    public long Ctime { get; set; }

    /// <summary>弹幕动作</summary>
    [ProtoMember(9)]
    public string Action { get; set; } = "";

    /// <summary>弹幕池（0=普通, 1=字幕, 2=特殊）</summary>
    [ProtoMember(10)]
    public int Pool { get; set; }

    /// <summary>弹幕 ID 字符串</summary>
    [ProtoMember(11)]
    public string IdStr { get; set; } = "";
}

#endregion
