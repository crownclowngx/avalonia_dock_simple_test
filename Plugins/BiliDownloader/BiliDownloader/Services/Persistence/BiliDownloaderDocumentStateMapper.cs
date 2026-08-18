using BiliDownloader.Constants;
using BiliDownloader.Models;
using BiliDownloader.Models.ContentSources;
using BiliDownloader.Services.Download;
using BiliDownloader.Services.Infrastructure;
using MyAvaloniaManagementCommon.Save;

namespace BiliDownloader.Services.Persistence;

/// <summary>Document 来源、筛选和轻量基线的不可变持久状态。</summary>
public sealed record BiliDownloaderDocumentSourceState(
    SourceDescriptorSaveData? Source,
    SourceFilterRulesSaveData Filters,
    IncrementalBaselineSaveData Baseline);

/// <summary>完成当前格式安全检查后的恢复结果。</summary>
public sealed record BiliDownloaderRestoredState(DocumentSaveDataV3 Data);

/// <summary>
/// BiliDownloader Document 状态映射边界。
/// </summary>
/// <remarks>
/// ViewModel 只负责采集和应用状态；版本判断、安全约束与 JSON 信封均由本接口负责，
/// 从而可以在不创建 Avalonia 控件或访问网络的条件下完整测试保存行为。
/// </remarks>
public interface IBiliDownloaderDocumentStateMapper
{
    DocumentSaveData Create(
        string title,
        string documentId,
        string url,
        string downloadInfo,
        DownloadConfigViewModelSnapshot configuration,
        string namingTemplate,
        BiliDownloaderDocumentSourceState sourceState);

    BiliDownloaderRestoredState Restore(DocumentSaveData saveData);
}

/// <summary>
/// Document V3 的纯状态映射器，不访问 Provider、SQLite、文件系统或 UI。
/// </summary>
public sealed class BiliDownloaderDocumentStateMapper : IBiliDownloaderDocumentStateMapper
{
    public DocumentSaveData Create(
        string title,
        string documentId,
        string url,
        string downloadInfo,
        DownloadConfigViewModelSnapshot configuration,
        string namingTemplate,
        BiliDownloaderDocumentSourceState sourceState)
    {
        var subtitle = NormalizeSubtitle(configuration.SubtitleOptions, configuration.DownloadSubtitle);
        var danmaku = NormalizeDanmaku(configuration.DanmakuOptions, configuration.DownloadDanmaku);
        var data = new DocumentSaveDataV3
        {
            DocumentId = documentId,
            Url = SensitiveDataSanitizer.SanitizeUrlForStorage(url),
            DownloadInfo = SensitiveDataSanitizer.Sanitize(downloadInfo),
            OutputDirectory = configuration.OutputDirectory,
            UseGroupFolder = configuration.UseGroupFolder,
            AddIndexToTitle = configuration.AddIndexToTitle,
            PresetId = configuration.PresetId,
            NamingTemplate = namingTemplate,
            QualityId = configuration.QualityId,
            AudioQualityId = configuration.AudioQualityId,
            DownloadDanmaku = danmaku.Formats.Count > 0,
            DownloadSubtitle = subtitle.SelectionMode != SubtitleSelectionMode.None,
            DownloadCover = configuration.DownloadCover,
            ConflictPolicy = configuration.ConflictPolicy,
            Source = CloneSource(sourceState.Source),
            Filters = CloneFilters(sourceState.Filters),
            Baseline = CloneBaseline(sourceState.Baseline),
            VideoCodecPreference = configuration.VideoCodecPreference,
            OutputContainer = configuration.OutputContainer,
            OutputMediaMode = configuration.OutputMediaMode,
            VideoDynamicRangePreference = configuration.VideoDynamicRangePreference,
            AudioFeaturePreference = configuration.AudioFeaturePreference,
            SubtitleOptions = subtitle,
            DanmakuOptions = danmaku,
            PerTaskRateLimitBytesPerSecond = configuration.PerTaskRateLimitBytesPerSecond,
        };

        DocumentSaveSecurityPolicy.Validate(data);
        return DocumentSaveCodec.EncodeV3(data);
    }

    public BiliDownloaderRestoredState Restore(DocumentSaveData saveData)
    {
        ArgumentNullException.ThrowIfNull(saveData);
        var decoded = DocumentSaveCodec.Decode(saveData);
        if (!decoded.IsKnownVersion)
        {
            throw new DocumentLoadException(
                "该 BiliDownloader Document 不是当前支持的 V3 格式。");
        }

        var data = RestoreV3(decoded.Payload);
        DocumentSaveSecurityPolicy.Validate(data);
        return new BiliDownloaderRestoredState(data);
    }

    private static DocumentSaveDataV3 RestoreV3(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new DocumentLoadException("Document V3 内容为空，无法安全打开。");

        var data = DocumentSaveCodec.Deserialize<DocumentSaveDataV3>(content)
            ?? throw new DocumentLoadException("Document V3 内容为空，无法安全打开。");
        NormalizeV3(data);
        return data;
    }


    private static void NormalizeV3(DocumentSaveDataV3 data)
    {
        data.Source = CloneSource(data.Source);
        data.Filters = CloneFilters(data.Filters);
        data.Baseline = CloneBaseline(data.Baseline);
        data.SubtitleOptions = NormalizeSubtitle(data.SubtitleOptions, data.DownloadSubtitle);
        data.DanmakuOptions = NormalizeDanmaku(data.DanmakuOptions, data.DownloadDanmaku);
        data.DownloadSubtitle = data.SubtitleOptions.SelectionMode != SubtitleSelectionMode.None;
        data.DownloadDanmaku = data.DanmakuOptions.Formats.Count > 0;
        data.Url = SensitiveDataSanitizer.SanitizeUrlForStorage(data.Url);
        data.DownloadInfo = SensitiveDataSanitizer.Sanitize(data.DownloadInfo);
    }

    private static SubtitleOptions NormalizeSubtitle(SubtitleOptions? value, bool legacyEnabled) =>
        value is not null && value.SelectionMode != SubtitleSelectionMode.None
            ? value with { LanguageKeys = value.LanguageKeys?.Where(static key => !string.IsNullOrWhiteSpace(key)).Select(static key => key.Trim()).Distinct(StringComparer.Ordinal).ToArray() ?? Array.Empty<string>() }
            : legacyEnabled ? SubtitleOptions.LegacyEnabled : SubtitleOptions.None;

    private static DanmakuOptions NormalizeDanmaku(DanmakuOptions? value, bool legacyEnabled) =>
        value is not null && value.Formats is { Count: > 0 }
            ? value with { Formats = value.Formats.Distinct().OrderBy(static format => format).ToArray(), AssStyleId = string.IsNullOrWhiteSpace(value.AssStyleId) ? "default" : value.AssStyleId.Trim() }
            : legacyEnabled ? DanmakuOptions.LegacyEnabled : DanmakuOptions.None;

    internal static SourceDescriptorSaveData? CloneSource(SourceDescriptorSaveData? source) => source is null
        ? null
        : new SourceDescriptorSaveData
        {
            Kind = source.Kind,
            StableSourceId = source.StableSourceId,
            DisplayName = SensitiveDataSanitizer.Sanitize(source.DisplayName),
            CapabilityVersion = source.CapabilityVersion,
            AutoOpen = source.AutoOpen,
        };

    internal static SourceFilterRulesSaveData CloneFilters(SourceFilterRulesSaveData? filters) => new()
    {
        Keyword = filters?.Keyword,
        PublishedFrom = filters?.PublishedFrom,
        PublishedTo = filters?.PublishedTo,
        MediaTypes = filters?.MediaTypes?.Distinct().OrderBy(static value => value).ToList() ?? [],
        SortOrder = filters?.SortOrder ?? ContentSourceSortOrder.ProviderDefault,
    };

    internal static IncrementalBaselineSaveData CloneBaseline(IncrementalBaselineSaveData? baseline) => new()
    {
        BaselineVersion = baseline?.BaselineVersion ?? IncrementalBaselineSaveData.CurrentVersion,
        LastCompletedCheckAtUtc = baseline?.LastCompletedCheckAtUtc?.ToUniversalTime(),
        SnapshotToken = baseline?.SnapshotToken,
        BoundaryItemKeys = baseline?.BoundaryItemKeys?.Select(static key => new ContentItemKeySaveData
        {
            SourceKind = key.SourceKind,
            NativeId = key.NativeId,
        }).ToList() ?? [],
    };
}

/// <summary>保存时从可变配置 ViewModel 截取的不可变快照。</summary>
public sealed record DownloadConfigViewModelSnapshot(
    string OutputDirectory,
    bool UseGroupFolder,
    bool AddIndexToTitle,
    string PresetId,
    int QualityId,
    int AudioQualityId,
    bool DownloadDanmaku,
    bool DownloadSubtitle,
    bool DownloadCover,
    FileConflictPolicy ConflictPolicy,
    VideoCodecPreference VideoCodecPreference,
    OutputContainer OutputContainer,
    OutputMediaMode OutputMediaMode,
    VideoDynamicRangePreference VideoDynamicRangePreference,
    AudioFeaturePreference AudioFeaturePreference,
    SubtitleOptions SubtitleOptions,
    DanmakuOptions DanmakuOptions,
    long PerTaskRateLimitBytesPerSecond);

/// <summary>运行时描述符与白名单保存 DTO 之间的唯一转换入口。</summary>
internal static class ContentSourceSaveDataMapper
{
    public static SourceDescriptorSaveData FromRuntime(ContentSourceDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var unknownParameter = descriptor.PublicParameters.Keys
            .FirstOrDefault(static key => !string.Equals(key, "autoOpen", StringComparison.Ordinal));
        if (unknownParameter is not null)
            throw new InvalidOperationException("内容源包含尚未纳入 Document 白名单的公开参数，无法安全保存。");

        var autoOpen = descriptor.PublicParameters.TryGetValue("autoOpen", out var value)
            && bool.TryParse(value, out var parsed)
            && parsed;
        var data = new SourceDescriptorSaveData
        {
            Kind = descriptor.Kind.ToString(),
            StableSourceId = descriptor.StableSourceId,
            DisplayName = SensitiveDataSanitizer.Sanitize(descriptor.DisplayName),
            CapabilityVersion = descriptor.CapabilityVersion,
            AutoOpen = autoOpen,
        };
        DocumentSaveSecurityPolicy.ValidateSource(data);
        return data;
    }

    public static bool TryToRuntime(SourceDescriptorSaveData data, out ContentSourceDescriptor? descriptor)
    {
        descriptor = null;
        if (!Enum.TryParse<ContentSourceKind>(data.Kind, ignoreCase: false, out var kind)
            || !Enum.IsDefined(kind))
            return false;

        var parameters = data.AutoOpen
            ? new Dictionary<string, string>(StringComparer.Ordinal) { ["autoOpen"] = "true" }
            : null;
        descriptor = new ContentSourceDescriptor(
            kind,
            data.StableSourceId,
            data.DisplayName,
            parameters,
            data.CapabilityVersion);
        return true;
    }

    public static SourceFilterRulesSaveData FromRuntime(SourceFilterRules rules) => new()
    {
        Keyword = rules.Keyword,
        PublishedFrom = rules.PublishedFrom,
        PublishedTo = rules.PublishedTo,
        MediaTypes = rules.MediaTypes.ToList(),
        SortOrder = rules.SortOrder,
    };

    public static SourceFilterRules ToRuntime(SourceFilterRulesSaveData data) => new(
        data.Keyword,
        data.PublishedFrom,
        data.PublishedTo,
        data.MediaTypes,
        data.SortOrder);
}

/// <summary>Document V3 的集中安全和结构约束。</summary>
internal static class DocumentSaveSecurityPolicy
{
    private static readonly string[] ForbiddenFragments =
    [
        "cookie", "authorization", "header", "accesskey", "access_key",
        "sessdata", "bili_jct", "w_rid", "signature", "signedurl", "signed_url",
    ];

    public static void Validate(DocumentSaveDataV3 data)
    {
        try
        {
            if (data.Source is not null) ValidateSource(data.Source);
            _ = ContentSourceSaveDataMapper.ToRuntime(data.Filters ?? new SourceFilterRulesSaveData());
            ValidateBaseline(data.Baseline ?? new IncrementalBaselineSaveData());
            ValidateEnum(data.ConflictPolicy);
            ValidateEnum(data.VideoCodecPreference);
            ValidateEnum(data.OutputContainer);
            ValidateEnum(data.OutputMediaMode);
            ValidateEnum(data.VideoDynamicRangePreference);
            ValidateEnum(data.AudioFeaturePreference);
            ValidateEnum(data.SubtitleOptions.SelectionMode);
            ValidateEnum(data.SubtitleOptions.OutputFormat);
            ValidateEnum(data.SubtitleOptions.DeliveryMode);
            foreach (var languageKey in data.SubtitleOptions.LanguageKeys)
                RejectSensitiveOrTemporaryValue(languageKey, "字幕语言键");
            foreach (var format in data.DanmakuOptions.Formats) ValidateEnum(format);
            RejectSensitiveOrTemporaryValue(data.DanmakuOptions.AssStyleId, "弹幕 ASS 样式 ID");
            BandwidthLimitPolicy.Validate(
                data.PerTaskRateLimitBytesPerSecond,
                nameof(data.PerTaskRateLimitBytesPerSecond));
        }
        catch (DocumentLoadException)
        {
            throw;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            throw new DocumentLoadException("Document V3 包含无效或不安全的持久化字段。", ex);
        }
    }

    public static void ValidateSource(SourceDescriptorSaveData source)
    {
        if (string.IsNullOrWhiteSpace(source.Kind)
            || string.IsNullOrWhiteSpace(source.StableSourceId)
            || string.IsNullOrWhiteSpace(source.DisplayName)
            || source.CapabilityVersion <= 0)
            throw new InvalidOperationException("内容源描述不完整。");
        RejectSensitiveOrTemporaryValue(source.StableSourceId, "稳定来源 ID");
    }

    private static void ValidateBaseline(IncrementalBaselineSaveData baseline)
    {
        if (baseline.BaselineVersion <= 0)
            throw new InvalidOperationException("增量基线版本必须为正数。");
        if (baseline.BoundaryItemKeys.Count > IncrementalBaselineSaveData.MaximumBoundaryItemCount)
            throw new InvalidOperationException("增量边界项目超过单页上限。");
        if (baseline.SnapshotToken is { Length: > 2048 })
            throw new InvalidOperationException("增量快照 token 过长。");
        if (!string.IsNullOrWhiteSpace(baseline.SnapshotToken))
            RejectSensitiveOrTemporaryValue(baseline.SnapshotToken, "增量快照 token");
        foreach (var key in baseline.BoundaryItemKeys)
        {
            if (string.IsNullOrWhiteSpace(key.SourceKind) || string.IsNullOrWhiteSpace(key.NativeId))
                throw new InvalidOperationException("增量边界项目键不完整。");
            RejectSensitiveOrTemporaryValue(key.NativeId, "边界项目 ID");
        }
    }

    private static void RejectSensitiveOrTemporaryValue(string value, string fieldName)
    {
        var normalized = value.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
        if (value.Contains("://", StringComparison.Ordinal)
            || value.Contains('?', StringComparison.Ordinal)
            || value.Contains('#', StringComparison.Ordinal)
            || ForbiddenFragments.Any(fragment => normalized.Contains(
                fragment.Replace("_", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal)))
            throw new InvalidOperationException($"{fieldName} 包含临时地址或敏感凭据特征。");
    }

    private static void ValidateEnum<T>(T value) where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
            throw new InvalidOperationException($"{typeof(T).Name} 包含未知枚举值。");
    }
}
