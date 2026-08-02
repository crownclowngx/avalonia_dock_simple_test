namespace BiliDownloader.Models;

/// <summary>
/// Document V2 保存格式的强类型 DTO。
/// <para>
/// 设计思考：替代 V1 的匿名对象，提高可读性和版本演进能力。
/// 所有字段提供默认值，确保 V1 文件（缺少新字段）反序列化时自动补齐。
/// V1 仅保存 6 个字段（DocumentId/Url/DownloadInfo/OutputDirectory/UseGroupFolder/AddIndexToTitle），
/// V2 新增预设、命名模板、清晰度和附加资源配置。
/// </para>
/// </summary>
public sealed class DocumentSaveDataV2
{
    // === V1 兼容字段（保持原有语义） ===

    /// <summary>Document 唯一标识</summary>
    public string DocumentId { get; set; } = "";

    /// <summary>解析用的原始 URL</summary>
    public string Url { get; set; } = "";

    /// <summary>日志/状态信息</summary>
    public string DownloadInfo { get; set; } = "";

    /// <summary>输出目录</summary>
    public string OutputDirectory { get; set; } = "";

    /// <summary>是否使用分组文件夹</summary>
    public bool UseGroupFolder { get; set; }

    /// <summary>是否添加序号前缀（V1 兼容字段）</summary>
    public bool AddIndexToTitle { get; set; } = true;

    // === V2 新增字段 ===

    /// <summary>当前使用的预设 ID</summary>
    public string PresetId { get; set; } = BuiltInPresets.CompatId;

    /// <summary>命名模板</summary>
    public string NamingTemplate { get; set; } = "{index}.{title}";

    /// <summary>清晰度 ID</summary>
    public int QualityId { get; set; }

    /// <summary>音频质量 ID</summary>
    public int AudioQualityId { get; set; }

    /// <summary>是否下载弹幕</summary>
    public bool DownloadDanmaku { get; set; }

    /// <summary>是否下载字幕</summary>
    public bool DownloadSubtitle { get; set; }

    /// <summary>是否下载封面</summary>
    public bool DownloadCover { get; set; }
}
