using BiliDownloader.Constants;
using BiliDownloader.Models;
using MyAvaloniaManagementCommon.Save;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BiliDownloader.Services.Persistence;

internal sealed record BiliDownloaderRestoredState(
    int MajorVersion,
    bool IsKnownVersion,
    DocumentSaveDataV2 Data,
    bool RestoreFullConfiguration);

/// <summary>
/// 下载 Document 的纯状态映射器，不访问网络、SQLite 或 UI。
/// 设计意图：版本识别和 V1→V2 迁移只有一个变化原因，Document ViewModel 只负责应用映射结果。
/// </summary>
internal sealed class BiliDownloaderDocumentStateMapper
{
    public DocumentSaveData Create(
        string title,
        string documentId,
        string url,
        string downloadInfo,
        DownloadConfigViewModelSnapshot configuration,
        string namingTemplate)
    {
        var data = new DocumentSaveDataV2
        {
            DocumentId = documentId,
            Url = url,
            DownloadInfo = downloadInfo,
            OutputDirectory = configuration.OutputDirectory,
            UseGroupFolder = configuration.UseGroupFolder,
            AddIndexToTitle = configuration.AddIndexToTitle,
            PresetId = configuration.PresetId,
            NamingTemplate = namingTemplate,
            QualityId = configuration.QualityId,
            AudioQualityId = configuration.AudioQualityId,
            DownloadDanmaku = configuration.DownloadDanmaku,
            DownloadSubtitle = configuration.DownloadSubtitle,
            DownloadCover = configuration.DownloadCover,
            ConflictPolicy = configuration.ConflictPolicy,
        };
        return DocumentSaveCodec.EncodeV2(SaveDocumentTypeIdConstant.BiliDownloaderDocumentId, title, data);
    }

    public BiliDownloaderRestoredState Restore(DocumentSaveData saveData, string defaultOutputDirectory)
    {
        var decoded = DocumentSaveCodec.Decode(saveData);
        return decoded.MajorVersion switch
        {
            2 => new(2, decoded.IsKnownVersion, RestoreV2(decoded.Content), true),
            1 => new(1, decoded.IsKnownVersion, RestoreV1(decoded.Content, defaultOutputDirectory), true),
            _ => new(decoded.MajorVersion, false, RestoreSafeFields(decoded.Content, defaultOutputDirectory), false),
        };
    }

    private static DocumentSaveDataV2 RestoreV2(string content) =>
        JsonConvert.DeserializeObject<DocumentSaveDataV2>(content) ?? new DocumentSaveDataV2();

    private static DocumentSaveDataV2 RestoreV1(string content, string defaultOutputDirectory)
    {
        var source = JsonConvert.DeserializeObject<JObject>(content) ?? new JObject();
        var addIndex = source["AddIndexToTitle"]?.Type is not null and not JTokenType.Null
            ? source["AddIndexToTitle"]!.Value<bool>()
            : true;
        return new DocumentSaveDataV2
        {
            DocumentId = source["DocumentId"]?.ToString() ?? "",
            Url = source["Url"]?.ToString() ?? "",
            DownloadInfo = source["DownloadInfo"]?.ToString() ?? "",
            OutputDirectory = source["OutputDirectory"]?.ToString() ?? defaultOutputDirectory,
            UseGroupFolder = source["UseGroupFolder"]?.Value<bool>() == true,
            AddIndexToTitle = addIndex,
            NamingTemplate = addIndex ? "{index}.{title}" : "{title}",
        };
    }

    private static DocumentSaveDataV2 RestoreSafeFields(string content, string defaultOutputDirectory)
    {
        var source = JsonConvert.DeserializeObject<JObject>(content) ?? new JObject();
        return new DocumentSaveDataV2
        {
            DocumentId = source["DocumentId"]?.ToString() ?? "",
            Url = source["Url"]?.ToString() ?? "",
            OutputDirectory = source["OutputDirectory"]?.ToString() ?? defaultOutputDirectory,
        };
    }
}

/// <summary>保存时从可变 ViewModel 截取的不可变配置快照。</summary>
internal sealed record DownloadConfigViewModelSnapshot(
    string OutputDirectory,
    bool UseGroupFolder,
    bool AddIndexToTitle,
    string PresetId,
    int QualityId,
    int AudioQualityId,
    bool DownloadDanmaku,
    bool DownloadSubtitle,
    bool DownloadCover,
    FileConflictPolicy ConflictPolicy);
