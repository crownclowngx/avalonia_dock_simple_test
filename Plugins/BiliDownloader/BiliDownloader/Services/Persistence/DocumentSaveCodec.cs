using BiliDownloader.Models;
using MyAvaloniaManagementCommon.Save;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BiliDownloader.Services.Persistence;

public sealed record DecodedDocument(int MajorVersion, string Content, bool IsKnownVersion);

public static class DocumentSaveCodec
{
    public static DocumentSaveData EncodeV2(
        string documentTypeId,
        string title,
        DocumentSaveDataV2 content) => new()
    {
        DocumentTypeId = documentTypeId,
        Title = title,
        SaveTime = DateTime.Now,
        Content = JsonConvert.SerializeObject(content),
        PluginMetadata = JsonConvert.SerializeObject(new { Version = "2.0" }),
    };

    public static DecodedDocument Decode(DocumentSaveData saveData)
    {
        var versionText = "1.0";
        if (!string.IsNullOrWhiteSpace(saveData.PluginMetadata))
        {
            var metadata = JsonConvert.DeserializeObject<JObject>(saveData.PluginMetadata);
            versionText = metadata?["Version"]?.ToString() ?? "1.0";
        }
        var known = Version.TryParse(versionText, out var version);
        var major = known ? version!.Major : -1;
        return new DecodedDocument(major, saveData.Content ?? "", known && major is 1 or 2);
    }
}
