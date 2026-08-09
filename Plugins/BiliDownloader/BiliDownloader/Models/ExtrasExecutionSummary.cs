using System.Text.Json;
using System.Text.Json.Serialization;

namespace BiliDownloader.Models;

/// <summary>单个附加资源输出的可持久化状态。</summary>
public enum ExtrasItemStatus
{
    Success,
    PartialSuccess,
    Failed,
    Unavailable,
    LegacyUnknown,
}

/// <summary>
/// 单个语言/格式/交付目标的执行事实。ErrorCode 保存稳定分类，Message 仅保存已脱敏短摘要；
/// 该模型刻意不提供正文或 URL 字段，从类型层面降低敏感数据误落库的风险。
/// </summary>
public sealed record ExtrasItemResult(
    string Key,
    ExtrasItemStatus Status,
    string? ErrorCode = null,
    string? Message = null,
    IReadOnlyList<string>? OutputFiles = null,
    IReadOnlyList<int>? FailedSegments = null)
{
    [JsonIgnore]
    public bool IsRetryable => Status is ExtrasItemStatus.Failed or ExtrasItemStatus.PartialSuccess;
}

/// <summary>版本化附加资源结果。相同 Key 后写覆盖前写，使独立重试可以幂等合并结果。</summary>
public sealed record ExtrasExecutionSummary
{
    public const int CurrentVersion = 1;
    public int Version { get; init; } = CurrentVersion;
    public IReadOnlyList<ExtrasItemResult> Items { get; init; } = Array.Empty<ExtrasItemResult>();

    [JsonIgnore]
    public bool HasRetryableFailures => Items.Any(static item => item.IsRetryable);

    public ExtrasExecutionSummary Merge(IEnumerable<ExtrasItemResult> updates)
    {
        var byKey = Items.ToDictionary(static item => item.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var update in updates) byKey[update.Key] = update;
        return this with
        {
            Version = CurrentVersion,
            Items = byKey.Values.OrderBy(static item => item.Key, StringComparer.OrdinalIgnoreCase).ToArray(),
        };
    }

    public static ExtrasExecutionSummary FromLegacy(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? new ExtrasExecutionSummary()
            : new ExtrasExecutionSummary
            {
                Items = [new ExtrasItemResult("legacy", ExtrasItemStatus.LegacyUnknown, Message: value)],
            };
}

/// <summary>集中管理 SQLite JSON，确保所有调用点使用相同大小写、空值和旧摘要兼容规则。</summary>
public static class ExtrasExecutionSummaryCodec
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(ExtrasExecutionSummary summary)
        => JsonSerializer.Serialize(summary, Options);

    public static ExtrasExecutionSummary Deserialize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return new ExtrasExecutionSummary();
        try
        {
            var summary = JsonSerializer.Deserialize<ExtrasExecutionSummary>(value, Options);
            return summary is { Version: > 0 } ? summary : ExtrasExecutionSummary.FromLegacy(value);
        }
        catch (JsonException)
        {
            return ExtrasExecutionSummary.FromLegacy(value);
        }
    }
}
