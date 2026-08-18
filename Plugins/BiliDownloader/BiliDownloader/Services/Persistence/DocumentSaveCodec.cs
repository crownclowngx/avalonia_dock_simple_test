using BiliDownloader.Models;
using MyAvaloniaManagementCommon.Save;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BiliDownloader.Services.Persistence;

public static class DocumentSaveCodec
{
    public const int CurrentContentSchemaVersion = 3;

    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        TypeNameHandling = TypeNameHandling.None,
        MaxDepth = 64,
    };

    /// <summary>
    /// 将已验证的 V3 DTO 转换为插件内容快照。宿主会在保存事务中独立补充
    /// PluginId、DocumentTypeId、标题和 UTC 时间，插件不能维护这些字段的副本。
    /// </summary>
    public static DocumentContentSnapshot EncodeV3(DocumentSaveDataV3 content) =>
        new(
            CurrentContentSchemaVersion,
            JsonConvert.SerializeObject(content, SerializerSettings));

    internal static T? Deserialize<T>(string content)
    {
        try
        {
            return JsonConvert.DeserializeObject<T>(content, SerializerSettings);
        }
        catch (JsonException ex)
        {
            throw new DocumentLoadException("文档内容已损坏或不符合当前保存格式。", ex);
        }
    }

    internal static JObject DeserializeObject(string content) =>
        Deserialize<JObject>(content) ?? new JObject();
}
