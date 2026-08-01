using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DaTangAccountingHelpPlug.Models.BankBalanceReconciliation;

namespace DaTangAccountingHelpPlug.Business.BankBalanceReconciliation.Profiles;

/// <summary>加载并验证内置或用户导入的银行对账配置。</summary>
public sealed partial class ReconciliationProfileLoader
{
    private const string DefaultResourceName =
        "DaTangAccountingHelpPlug.Resources.BankBalanceReconciliation.reconciliation-profiles.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ReconciliationConfiguration _defaults;

    public ReconciliationProfileLoader()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(DefaultResourceName)
            ?? throw new InvalidOperationException($"找不到内置对账配置资源：{DefaultResourceName}");
        _defaults = Deserialize(stream);
        Validate(_defaults);
    }

    /// <summary>返回副本，避免一个 Document 修改其他 Document 的默认配置。</summary>
    public ReconciliationConfiguration LoadDefault() => Clone(_defaults);

    public async Task<ReconciliationConfiguration> ImportAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        var configuration = await JsonSerializer.DeserializeAsync<ReconciliationConfiguration>(
                stream,
                JsonOptions,
                cancellationToken)
            ?? throw new InvalidDataException("配置文件没有可读取的内容。");
        Validate(configuration);
        return configuration;
    }

    public async Task ExportAsync(
        ReconciliationConfiguration configuration,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        Validate(configuration);
        await using var stream = new FileStream(
            filePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None);
        await JsonSerializer.SerializeAsync(stream, configuration, JsonOptions, cancellationToken);
    }

    public void Validate(ReconciliationConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.SchemaVersion != 1)
            throw new InvalidDataException($"不支持配置版本 {configuration.SchemaVersion}，当前仅支持版本 1。");
        EnsureUnique(configuration.EnterpriseLayouts.Select(item => item.Id), "企业账布局");
        EnsureUnique(configuration.BankProfiles.Select(item => item.Id), "银行配置");
        EnsureUnique(configuration.NormalizationRules.Select(item => item.Id), "名称归一化规则");
        EnsureUnique(configuration.ReferenceAggregationRules.Select(item => item.Id), "凭证汇总规则");
        EnsureUnique(configuration.AggregationRules.Select(item => item.Id), "汇总规则");

        var layoutIds = configuration.EnterpriseLayouts
            .Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var layout in configuration.EnterpriseLayouts)
        {
            ValidateColumns(
                layout.DisplayName,
                layout.StartRow,
                layout.DateColumn,
                layout.ReferenceColumn,
                layout.SummaryColumn,
                layout.DebitColumn,
                layout.CreditColumn,
                layout.MarkerColumn,
                layout.BalanceColumn,
                layout.BalanceDirectionColumn);
            ValidateCell(layout.VerifyUnitCell, $"{layout.DisplayName} 校验单位");
            ValidateCell(layout.VerifyAccountCell, $"{layout.DisplayName} 校验账号");
        }

        foreach (var profile in configuration.BankProfiles)
        {
            if (!layoutIds.Contains(profile.EnterpriseLayoutId))
                throw new InvalidDataException($"银行配置 {profile.Id} 引用了不存在的企业账布局 {profile.EnterpriseLayoutId}。");
            if (profile.DirectionMode is not (1 or 2))
                throw new InvalidDataException($"银行配置 {profile.Id} 的方向模式必须为 1 或 2。");
            ValidateColumns(
                profile.Id,
                profile.StartRow,
                profile.DateColumn,
                profile.CounterpartyColumn,
                profile.SummaryColumn,
                profile.DebitColumn,
                profile.CreditColumn,
                profile.MarkerColumn,
                profile.BalanceColumn);
            ValidateCell(profile.VerifyUnitCell, $"{profile.Id} 校验单位");
            ValidateCell(profile.VerifyAccountCell, $"{profile.Id} 校验账号");
        }

        foreach (var rule in configuration.NormalizationRules)
        {
            if (rule.ReorderPrefixLength < 0)
                throw new InvalidDataException($"名称归一化规则 {rule.Id} 的冲销前缀长度不能为负数。");
            if (rule.CandidateNames.Count == 0 || rule.CandidateNames.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException($"名称归一化规则 {rule.Id} 必须包含非空候选名称。");
            if (string.IsNullOrWhiteSpace(rule.BankSummaryContains) &&
                string.IsNullOrWhiteSpace(rule.BankCounterpartyContains))
                throw new InvalidDataException($"名称归一化规则 {rule.Id} 至少需要一个匹配条件。");
        }

        foreach (var rule in configuration.ReferenceAggregationRules)
        {
            if (string.IsNullOrWhiteSpace(rule.DisplayName) ||
                string.IsNullOrWhiteSpace(rule.BankSummaryKeyword))
                throw new InvalidDataException($"凭证汇总规则 {rule.Id} 必须包含显示名称和银行摘要关键字。");
            if (rule.BankDirection is not (ReconciliationDirection.BankReceived or ReconciliationDirection.BankPaid))
                throw new InvalidDataException($"凭证汇总规则 {rule.Id} 的银行方向无效。");
            if (rule.ApplicableProfileIds.Count == 0 ||
                rule.ApplicableProfileIds.Any(id => !configuration.BankProfiles.Any(profile =>
                    profile.Id.Equals(id, StringComparison.OrdinalIgnoreCase))))
                throw new InvalidDataException($"凭证汇总规则 {rule.Id} 引用了不存在的银行配置。");
            if (rule.EnterpriseReferencePrefixes.Count == 0 ||
                rule.EnterpriseReferencePrefixes.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException($"凭证汇总规则 {rule.Id} 必须包含非空企业凭证前缀。");
        }
    }

    private static ReconciliationConfiguration Deserialize(Stream stream) =>
        JsonSerializer.Deserialize<ReconciliationConfiguration>(stream, JsonOptions)
        ?? throw new InvalidDataException("内置配置没有可读取的内容。");

    private static ReconciliationConfiguration Clone(ReconciliationConfiguration configuration)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(configuration, JsonOptions);
        return JsonSerializer.Deserialize<ReconciliationConfiguration>(bytes, JsonOptions)!;
    }

    private static void EnsureUnique(IEnumerable<string> ids, string kind)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids)
        {
            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id))
                throw new InvalidDataException($"{kind}存在空 ID 或重复 ID：{id}");
        }
    }

    private static void ValidateColumns(string owner, int startRow, params int[] columns)
    {
        if (startRow <= 0 || columns.Any(column => column <= 0))
            throw new InvalidDataException($"{owner} 的起始行和列号必须大于零。");
    }

    private static void ValidateCell(string address, string label)
    {
        if (!string.IsNullOrWhiteSpace(address) && !CellAddressRegex().IsMatch(address))
            throw new InvalidDataException($"{label}的单元格地址无效：{address}");
    }

    [GeneratedRegex("^[A-Za-z]{1,3}[1-9][0-9]*$", RegexOptions.CultureInvariant)]
    private static partial Regex CellAddressRegex();
}
