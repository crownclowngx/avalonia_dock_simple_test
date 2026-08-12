using BiliDownloader.Models;
using MyAvaloniaManagementCommon.Save;
using MyAvaloniaManagementCommon.DocumentCreation;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BiliDownloader.Services.Persistence;

public sealed record DecodedDocument(int MajorVersion, string Content, bool IsKnownVersion);

public static class DocumentSaveCodec
{
    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        TypeNameHandling = TypeNameHandling.None,
        MaxDepth = 64,
    };

    public static DocumentSaveData EncodeV2(
        DocumentTypeId documentTypeId,
        string title,
        DocumentSaveDataV2 content) => new()
    {
        DocumentTypeId = documentTypeId,
        Title = title,
        SaveTime = DateTime.Now,
        Content = JsonConvert.SerializeObject(content),
        PluginMetadata = JsonConvert.SerializeObject(new { Version = "2.0" }),
    };

    /// <summary>
    /// 将已验证的 V3 DTO 放入宿主统一信封。SaveTime 属于本次磁盘快照，
    /// 业务往返测试应比较 Content 而不是要求时间戳相同。
    /// </summary>
    public static DocumentSaveData EncodeV3(
        DocumentTypeId documentTypeId,
        string title,
        DocumentSaveDataV3 content) => new()
    {
        DocumentTypeId = documentTypeId,
        Title = title,
        SaveTime = DateTime.Now,
        Content = JsonConvert.SerializeObject(content, SerializerSettings),
        PluginMetadata = JsonConvert.SerializeObject(new { Version = "3.0" }, SerializerSettings),
    };

    public static DecodedDocument Decode(DocumentSaveData saveData)
    {
        var versionText = "1.0";
        try
        {
            if (!string.IsNullOrWhiteSpace(saveData.PluginMetadata))
            {
                var metadata = JsonConvert.DeserializeObject<JObject>(saveData.PluginMetadata, SerializerSettings);
                versionText = metadata?["Version"]?.ToString() ?? "1.0";
            }
        }
        catch (JsonException ex)
        {
            throw new DocumentLoadException("文档版本元数据已损坏，无法安全打开。", ex);
        }

        var known = Version.TryParse(versionText, out var version);
        var major = known ? version!.Major : -1;
        return new DecodedDocument(major, saveData.Content ?? "", known && major is 1 or 2 or 3);
    }

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
