using System.Text.Json;
using System.Text.Json.Serialization;
using DaTangAccountingHelpPlug.Business.BankBalanceReconciliation.Profiles;
using DaTangAccountingHelpPlug.Models.BankBalanceReconciliation;
using MyAvaloniaManagement.PluginSdk;

namespace DaTangAccountingHelpPlug.ViewModels.BankBalanceReconciliation;

/// <summary>表示完成结构与领域校验后、尚未提交到 ViewModel 的银行对账内容状态。</summary>
internal sealed record ReconciliationDocumentState(
    ReconciliationConfiguration Configuration,
    string SelectedProfileId,
    string EnterpriseLedgerPath,
    string BankStatementPath,
    string ReceiptEnrichmentPath,
    DateTimeOffset? AsOfDate,
    bool UseLegacyMode,
    bool EnableLooseAmountAlignment,
    decimal PreviousUnreconciledDifference,
    string LastOutputPath);

/// <summary>严格读写银行余额调节 Document 的插件自有 schema 1 payload。</summary>
/// <remarks>
/// Codec 只负责线格式与临时状态，不修改 ViewModel，也不拥有 Host 信封、保存路径或文件事务。
/// 根字段集合固定，所有对象递归拒绝重复字段，配置 DTO 再通过未知字段拒绝和既有领域验证器校验。
/// </remarks>
internal static class ReconciliationDocumentContentCodec
{
    private const int SchemaVersion = 1;
    private static readonly string[] RequiredProperties =
    [
        "configuration", "selectedProfileId", "enterpriseLedgerPath", "bankStatementPath",
        "receiptEnrichmentPath", "asOfDate", "useLegacyMode", "enableLooseAmountAlignment",
        "previousUnreconciledDifference", "lastOutputPath",
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        // 内容线格式把枚举固定为可读字符串；拒绝整数可避免未知枚举值绕过字段类型检查。
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    internal static DocumentContent Encode(ReconciliationDocumentState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var payload = JsonSerializer.SerializeToElement(new
        {
            configuration = state.Configuration,
            selectedProfileId = state.SelectedProfileId,
            enterpriseLedgerPath = state.EnterpriseLedgerPath,
            bankStatementPath = state.BankStatementPath,
            receiptEnrichmentPath = state.ReceiptEnrichmentPath,
            asOfDate = state.AsOfDate,
            useLegacyMode = state.UseLegacyMode,
            enableLooseAmountAlignment = state.EnableLooseAmountAlignment,
            previousUnreconciledDifference = state.PreviousUnreconciledDifference,
            lastOutputPath = state.LastOutputPath,
        }, JsonOptions);
        return new DocumentContent(SchemaVersion, payload);
    }

    internal static ReconciliationDocumentState Decode(
        DocumentContent content,
        ReconciliationProfileLoader profileLoader)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(profileLoader);
        if (content.SchemaVersion != SchemaVersion)
        {
            throw InvalidContent("银行余额调节文档内容版本不受支持。");
        }

        var root = content.Payload;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw InvalidContent("银行余额调节文档正文必须是对象。");
        }

        EnsureNoDuplicateProperties(root);
        var properties = root.EnumerateObject().ToDictionary(
            property => property.Name,
            property => property.Value,
            StringComparer.Ordinal);
        if (properties.Count != RequiredProperties.Length ||
            RequiredProperties.Any(name => !properties.ContainsKey(name)))
        {
            throw InvalidContent("银行余额调节文档字段集合不完整或包含未知字段。");
        }

        try
        {
            var configuration = properties["configuration"]
                .Deserialize<ReconciliationConfiguration>(JsonOptions)
                ?? throw InvalidContent("银行余额调节文档缺少配置数据。");
            profileLoader.Validate(configuration);

            return new ReconciliationDocumentState(
                configuration,
                ReadString(properties, "selectedProfileId"),
                ReadString(properties, "enterpriseLedgerPath"),
                ReadString(properties, "bankStatementPath"),
                ReadString(properties, "receiptEnrichmentPath"),
                ReadNullableDateTimeOffset(properties, "asOfDate"),
                ReadBoolean(properties, "useLegacyMode"),
                ReadBoolean(properties, "enableLooseAmountAlignment"),
                ReadDecimal(properties, "previousUnreconciledDifference"),
                ReadString(properties, "lastOutputPath"));
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or FormatException or InvalidOperationException)
        {
            throw InvalidContent("银行余额调节文档结构损坏或字段类型无效。", exception);
        }
    }

    private static string ReadString(IReadOnlyDictionary<string, JsonElement> values, string name)
    {
        var value = values[name];
        return value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw InvalidContent($"银行余额调节文档字段 {name} 必须是字符串。");
    }

    private static bool ReadBoolean(IReadOnlyDictionary<string, JsonElement> values, string name)
    {
        var value = values[name];
        return value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : throw InvalidContent($"银行余额调节文档字段 {name} 必须是布尔值。");
    }

    private static decimal ReadDecimal(IReadOnlyDictionary<string, JsonElement> values, string name)
    {
        var value = values[name];
        return value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var result)
            ? result
            : throw InvalidContent($"银行余额调节文档字段 {name} 必须是十进制数字。");
    }

    private static DateTimeOffset? ReadNullableDateTimeOffset(
        IReadOnlyDictionary<string, JsonElement> values,
        string name)
    {
        var value = values[name];
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }
        return value.ValueKind == JsonValueKind.String && value.TryGetDateTimeOffset(out var result)
            ? result
            : throw InvalidContent($"银行余额调节文档字段 {name} 必须是带时区日期或 null。");
    }

    private static void EnsureNoDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw InvalidContent("银行余额调节文档包含重复字段。");
                }
                EnsureNoDuplicateProperties(property.Value);
            }
            return;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                EnsureNoDuplicateProperties(item);
            }
        }
    }

    private static InvalidDataException InvalidContent(string message, Exception? inner = null) =>
        new(message, inner);
}
