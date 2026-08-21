using BiliDownloader.Models;
using MyAvaloniaManagement.PluginSdk;
using System.Text.Json;

namespace BiliDownloader.Services.Persistence;

public static class DocumentSaveCodec
{
    public const int CurrentContentSchemaVersion = 3;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        MaxDepth = 64,
        PropertyNameCaseInsensitive = false,
    };

    /// <summary>
    /// 将已验证的 V3 DTO 转换为插件内容快照。宿主会在保存事务中独立补充
    /// PluginId、DocumentTypeId、标题和 UTC 时间，插件不能维护这些字段的副本。
    /// </summary>
    public static DocumentContent EncodeV3(DocumentSaveDataV3 content) =>
        new(
            CurrentContentSchemaVersion,
            JsonSerializer.SerializeToElement(content, SerializerOptions));

    internal static T? Deserialize<T>(JsonElement content)
    {
        try
        {
            return content.Deserialize<T>(SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("文档内容已损坏或不符合当前保存格式。", ex);
        }
    }
}
