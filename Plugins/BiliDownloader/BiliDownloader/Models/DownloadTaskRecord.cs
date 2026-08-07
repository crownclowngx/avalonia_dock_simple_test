using CommunityToolkit.Mvvm.ComponentModel;

namespace BiliDownloader.Models;

/// <summary>
/// 下载任务 SQLite 持久化记录
/// </summary>
public partial class DownloadTaskRecord : ObservableObject
{
    /// <summary>
    /// 任务唯一标识（对应 BiliVideoItem.ItemId / DownloadItemInfo.ItemId）
    /// </summary>
    public string TaskId { get; set; } = string.Empty;

    /// <summary>
    /// 关联的 Document 实例 ID（用于定向回传进度和按 Document 查询）
    /// </summary>
    public string DocumentId { get; set; } = string.Empty;

    /// <summary>User-facing title of the Document that submitted this task.</summary>
    public string SourceDocumentTitle { get; set; } = string.Empty;

    /// <summary>
    /// 系列标题
    /// </summary>
    public string SeriesTitle { get; set; } = string.Empty;

    /// <summary>
    /// 单个视频标题
    /// </summary>
    public string ItemTitle { get; set; } = string.Empty;

    public long Aid { get; set; }
    public string Bvid { get; set; } = string.Empty;
    public long Cid { get; set; }

    /// <summary>
    /// 版本化媒体身份（mu1:Aid:Cid）。旧任务可能为空；为空只表示身份不完整，
    /// 不能作为阻止新提交的充分依据。
    /// </summary>
    public string MediaUnitKey { get; set; } = string.Empty;

    /// <summary>
    /// 版本化输出身份（rf1:SHA256）。该值不含 Document 和来源，因此可用于跨来源、跨 Document 去重。
    /// </summary>
    public string RenditionFingerprint { get; set; } = string.Empty;

    /// <summary>媒体类型（video/bangumi）</summary>
    public string MediaType { get; set; } = "video";

    /// <summary>番剧 ep_id</summary>
    public long EpId { get; set; }

    /// <summary>番剧 season_id</summary>
    public long SeasonId { get; set; }

    /// <summary>启用的附加资源类型（位枚举整数，0=无）</summary>
    public int ExtrasConfig { get; set; }

    /// <summary>封面图 URL</summary>
    public string CoverUrl { get; set; } = string.Empty;

    /// <summary>附加资源执行结果摘要</summary>
    public string? ExtrasResultSummary { get; set; }

    /// <summary>
    /// 用户选择的清晰度
    /// </summary>
    public int QualityId { get; set; }

    /// <summary>
    /// 用户选择的音频流 ID（0 表示使用最高码率）
    /// </summary>
    public int AudioQualityId { get; set; }

    /// <summary>
    /// 提交快照结构版本。0 表示迁移前的旧任务，只能基于可证明字段执行兼容重建；
    /// 1 表示已经完整保存 <see cref="DownloadProfileSnapshot"/> 中与重新下载有关的意图。
    /// 设计意图：显式版本优于根据空字符串猜测新旧记录，避免把迁移默认值误认为用户真实选择。
    /// </summary>
    public int SubmissionSnapshotVersion { get; set; }

    /// <summary>提交时媒体时长（秒），用于重新构造不可变下载项。</summary>
    public int DurationSeconds { get; set; }

    /// <summary>提交时是否使用系列分组目录。</summary>
    public bool UseGroupFolder { get; set; }

    /// <summary>提交时是否在标题中加入序号。</summary>
    public bool AddIndexToTitle { get; set; }

    /// <summary>提交时固化的命名模板；该字段不进入历史导出白名单。</summary>
    public string NamingTemplate { get; set; } = string.Empty;

    /// <summary>提交时引用的预设 ID；预设后续变化不会改变任务快照。</summary>
    public string? PresetId { get; set; }

    /// <summary>
    /// 用户提交时选择的视频编码。旧任务为 null，历史中心必须显示“未知”，
    /// 不得根据文件扩展名或当前默认设置倒推出一个看似确定的值。
    /// </summary>
    public VideoCodecPreference? SelectedVideoCodec { get; set; }

    /// <summary>P1-G7 写入的实际视频编码；G6 只提供可兼容的未知占位。</summary>
    public string ActualVideoCodec { get; set; } = string.Empty;

    /// <summary>用户提交时选择的输出容器；旧任务为 null。</summary>
    public OutputContainer? SelectedOutputContainer { get; set; }

    /// <summary>用户提交时选择的输出模式；旧任务为 null。</summary>
    public OutputMediaMode? SelectedOutputMediaMode { get; set; }

    /// <summary>
    /// 若任务由历史中心重新下载产生，则记录来源任务 ID。该关联只用于审计，
    /// 不授予旧任务的覆盖确认、路径保留或断点恢复能力。
    /// </summary>
    public string RedownloadedFromTaskId { get; set; } = string.Empty;

    /// <summary>
    /// 输出目录
    /// </summary>
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>
    /// 分组子文件夹名称（UseGroupFolder 时为 SeriesTitle 的合法文件名，否则为空）
    /// </summary>
    public string SubFolder { get; set; } = string.Empty;

    /// <summary>
    /// 最新进度 0~100
    /// </summary>
    [ObservableProperty]
    private double _progress;

    /// <summary>
    /// 视频下载进度 0~100
    /// </summary>
    [ObservableProperty]
    private double _videoProgress;

    /// <summary>
    /// 音频下载进度 0~100
    /// </summary>
    [ObservableProperty]
    private double _audioProgress;

    /// <summary>
    /// 合成进度 0~100
    /// </summary>
    [ObservableProperty]
    private double _mergeProgress;

    /// <summary>
    /// 下载速度文本，如 "2.5 MB/s"
    /// </summary>
    [ObservableProperty]
    private string _speedText = "";

    /// <summary>
    /// 当前状态（存储为字符串，兼容 SQLite）
    /// </summary>
    [ObservableProperty]
    private string _status = DownloadTaskStatusMapper.ToStorageString(DownloadTaskStatus.Ready);

    /// <summary>
    /// 错误信息（仅 failed 时有值）
    /// </summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// 临时文件目录路径（用于断点续传和清理）
    /// </summary>
    public string TempDirectory { get; set; } = string.Empty;

    /// <summary>
    /// 视频流已下载字节数（用于断点续传）
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalDownloadedBytes))]
    private long _videoBytesDownloaded;

    /// <summary>
    /// 音频流已下载字节数（用于断点续传）
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalDownloadedBytes))]
    private long _audioBytesDownloaded;

    /// <summary>Current numeric transfer speed, used for ETA calculations.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstimatedRemainingText))]
    private long _bytesPerSecond;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 预期视频文件大小（字节），用于完整性验证
    /// </summary>
    public long ExpectedVideoBytes { get; set; }

    /// <summary>
    /// 预期音频文件大小（字节），用于完整性验证
    /// </summary>
    public long ExpectedAudioBytes { get; set; }

    /// <summary>
    /// 视频完整性验证是否通过
    /// </summary>
    public bool VideoIntegrityPassed { get; set; }

    /// <summary>
    /// 音频完整性验证是否通过
    /// </summary>
    public bool AudioIntegrityPassed { get; set; }

    /// <summary>
    /// 最终输出文件路径
    /// </summary>
    public string OutputFilePath { get; set; } = string.Empty;

    /// <summary>
    /// 经过平台大小写规则规范化的最终路径键。Coordinator 在提交事务中保留该键，
    /// 从而让多个 Document 即使并发提交也不能把同一目标分配给两个活动任务。
    /// </summary>
    public string OutputPathKey { get; set; } = string.Empty;

    /// <summary>提交时固化的冲突策略；后台恢复不得改用 Document 当前的新设置。</summary>
    public FileConflictPolicy ConflictPolicy { get; set; } = FileConflictPolicy.AutoNumber;

    /// <summary>预检得到的单项峰值空间估算；0 表示无法可靠估算。</summary>
    public long EstimatedRequiredBytes { get; set; }

    /// <summary>覆盖策略是否经过本批用户确认；未确认时执行层即使收到错误数据也拒绝覆盖。</summary>
    public bool OverwriteConfirmed { get; set; }

    /// <summary>
    /// 最后更新时间
    /// </summary>
    public DateTime LastUpdatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 错误分类（network/cdn/ffmpeg/auth/unknown），仅 failed 时有值
    /// </summary>
    public string? ErrorType { get; set; }

    /// <summary>
    /// 是否可重试
    /// </summary>
    public bool IsRetryable { get; set; }

    // ──────────────────────────────────────────────────────────────────────
    // G4: 任务中心产品化 — UI 多选与展示计算属性
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// G4: UI 多选状态（非持久化，不写入 SQLite）。
    /// 设计思考：将选择状态放在模型而非独立 SelectionManager，
    /// 因为 CheckBox 绑定需要 INPC 通知，模型已继承 ObservableObject；
    /// 100 条规模下无需额外间接层，直接绑定最简洁。
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    // ── G4: 只读计算属性（供 UI 展示，不持久化） ──

    /// <summary>预计总大小（视频+音频），用于任务详情展示</summary>
    public long TotalExpectedBytes => ExpectedVideoBytes + ExpectedAudioBytes;

    /// <summary>已下载总大小（视频+音频），用于任务详情展示</summary>
    public long TotalDownloadedBytes => VideoBytesDownloaded + AudioBytesDownloaded;

    public string EstimatedRemainingText
    {
        get
        {
            var remaining = Math.Max(0, TotalExpectedBytes - TotalDownloadedBytes);
            if (BytesPerSecond <= 0 || remaining <= 0) return "";
            var duration = TimeSpan.FromSeconds((double)remaining / BytesPerSecond);
            return duration.TotalHours >= 1
                ? $"约 {duration.Hours}小时{duration.Minutes}分钟"
                : $"约 {Math.Max(1, duration.Minutes)}分钟";
        }
    }

    /// <summary>
    /// 质量显示文本（B站 qn 映射）。
    /// 设计思考：将 QualityId 到用户可读文本的映射放在模型上，
    /// 避免在 View 层使用转换器或重复的 switch 表达式。
    /// </summary>
    public string QualityDisplayText => QualityId switch
    {
        127 => "8K",
        126 => "杜比视界",
        125 => "HDR",
        120 => "4K",
        116 => "1080P60",
        80 => "1080P",
        64 => "720P",
        32 => "480P",
        16 => "360P",
        _ => $"Q{QualityId}"
    };

    /// <summary>
    /// 完整输出路径（含分组子文件夹）。
    /// 设计思考：UI 展示时需要合并 OutputDirectory 和 SubFolder，
    /// 放在模型上避免 View 中写路径拼接逻辑。
    /// </summary>
    public string FullOutputPath => string.IsNullOrEmpty(SubFolder)
        ? OutputDirectory
        : Path.Combine(OutputDirectory, SubFolder);
}
