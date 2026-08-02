namespace BiliDownloader.Models;

/// <summary>
/// 下载预设数据模型：一个预设代表"下载方案的不可变快照"。
/// <para>
/// 设计思考：使用 sealed record 表达值对象语义——两个预设如果字段全部相同则相等。
/// 不使用 ObservableObject，因为预设本身不需要通知 UI；UI 绑定的是 DownloadConfigViewModel
/// 的活属性，预设只在"应用"瞬间将值批量写入那些活属性。
/// IsBuiltIn 标记保护内置预设不被用户删除或覆盖。
/// </para>
/// </summary>
public sealed record DownloadPreset
{
    /// <summary>预设唯一标识（内置为 "builtin_compat" 等，自定义为 GUID）</summary>
    public string Id { get; init; } = "";

    /// <summary>预设显示名称（"兼容"/"质量"/"归档"/用户自定义名称）</summary>
    public string Name { get; init; } = "";

    /// <summary>是否为内置预设（内置预设不可删除、不可覆盖）</summary>
    public bool IsBuiltIn { get; init; }

    /// <summary>
    /// 清晰度偏好策略："highest"（最高可用）/"1080p"/"720p"。
    /// 设计思考：使用字符串而非 int，因为预设应用时清晰度选项列表尚未从 API 加载，
    /// 需要在 PopulateQualities 时根据偏好字符串延迟匹配到实际可用的 QualityId。
    /// </summary>
    public string QualityPreference { get; init; } = "highest";

    /// <summary>音频质量 ID（0 = 最高码率）</summary>
    public int AudioQualityId { get; init; }

    /// <summary>是否使用分组文件夹（多 P / 番剧时按系列名建子目录）</summary>
    public bool UseGroupFolder { get; init; }

    /// <summary>是否在标题前添加序号（V1 兼容字段，V2 中由 NamingTemplate 取代）</summary>
    public bool AddIndexToTitle { get; init; }

    /// <summary>是否下载弹幕</summary>
    public bool DownloadDanmaku { get; init; }

    /// <summary>是否下载字幕</summary>
    public bool DownloadSubtitle { get; init; }

    /// <summary>是否下载封面图</summary>
    public bool DownloadCover { get; init; }

    /// <summary>命名模板（如 "{index}.{title}"），支持变量见 NamingTemplateEngine</summary>
    public string NamingTemplate { get; init; } = "{index}.{title}";

    /// <summary>输出目录（空字符串表示使用全局默认目录）</summary>
    public string OutputDirectory { get; init; } = "";
}

/// <summary>
/// 内置预设定义：提供"兼容""质量""归档"三个开箱即用的预设。
/// <para>
/// 设计思考：内置预设始终从代码获取（硬编码），不写入数据库。
/// 这避免了数据库污染，也保证用户删除自定义预设后内置预设始终可用。
/// 三个预设覆盖最常见场景：兼容性优先、画质优先、完整归档。
/// </para>
/// </summary>
public static class BuiltInPresets
{
    /// <summary>内置预设 ID 常量（持久化到 Document V2 和 last_preset_id）</summary>
    public const string CompatId = "builtin_compat";
    public const string QualityId = "builtin_quality";
    public const string ArchiveId = "builtin_archive";

    /// <summary>
    /// "兼容"预设：720P + 无附加资源 + 序号标题。
    /// 适合网络环境一般、只需快速下载观看的场景。
    /// </summary>
    public static DownloadPreset Compatible() => new()
    {
        Id = CompatId,
        Name = "兼容",
        IsBuiltIn = true,
        QualityPreference = "720p",
        AudioQualityId = 0,
        UseGroupFolder = false,
        AddIndexToTitle = true,
        DownloadDanmaku = false,
        DownloadSubtitle = false,
        DownloadCover = false,
        NamingTemplate = "{index}.{title}",
        OutputDirectory = ""
    };

    /// <summary>
    /// "质量"预设：最高画质 + 字幕 + 纯标题命名。
    /// 适合追求画质、关注字幕的常规下载场景。
    /// </summary>
    public static DownloadPreset Quality() => new()
    {
        Id = QualityId,
        Name = "质量",
        IsBuiltIn = true,
        QualityPreference = "highest",
        AudioQualityId = 0,
        UseGroupFolder = false,
        AddIndexToTitle = false,
        DownloadDanmaku = false,
        DownloadSubtitle = true,
        DownloadCover = false,
        NamingTemplate = "{title}",
        OutputDirectory = ""
    };

    /// <summary>
    /// "归档"预设：最高画质 + 全部附加资源 + 分组文件夹 + BV号标题。
    /// 适合长期保存、建立个人媒体库的归档场景。
    /// </summary>
    public static DownloadPreset Archive() => new()
    {
        Id = ArchiveId,
        Name = "归档",
        IsBuiltIn = true,
        QualityPreference = "highest",
        AudioQualityId = 0,
        UseGroupFolder = true,
        AddIndexToTitle = true,
        DownloadDanmaku = true,
        DownloadSubtitle = true,
        DownloadCover = true,
        NamingTemplate = "{bv}_{title}",
        OutputDirectory = ""
    };

    /// <summary>
    /// 获取所有内置预设列表（顺序：兼容 → 质量 → 归档）。
    /// </summary>
    public static List<DownloadPreset> GetAll() => new()
    {
        Compatible(),
        Quality(),
        Archive()
    };
}
