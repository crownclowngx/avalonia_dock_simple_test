using System.Text;
using DaTangAccountingHelpPlug.Models.BankBalanceReconciliation;

namespace DaTangAccountingHelpPlug.Business.BankBalanceReconciliation.Matching;

/// <summary>负责文本清洗和候选名称规则，不决定两条流水是否匹配。</summary>
public sealed class EntryNormalizer
{
    public string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim())
        {
            builder.Append(character switch
            {
                '(' => '（',
                ')' => '）',
                '\t' or '\r' or '\n' => ' ',
                _ => character
            });
        }

        return string.Join(' ', builder.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    public IReadOnlyList<string> ResolveCandidateNames(
        ReconciliationEntry bankEntry,
        IReadOnlyList<CounterpartyNormalizationRule> rules)
    {
        foreach (var rule in rules)
        {
            var summaryMatches = Matches(rule.BankSummaryContains, bankEntry.Summary);
            var counterpartyMatches = Matches(rule.BankCounterpartyContains, bankEntry.Counterparty);
            if (summaryMatches && counterpartyMatches && rule.CandidateNames.Count > 0)
            {
                return rule.CandidateNames
                    .Select(NormalizeText)
                    .Where(name => name.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }

        var fallback = NormalizeText(bankEntry.Counterparty);
        return fallback.Length == 0 ? [] : [fallback];
    }

    public bool ContainsCandidate(string enterpriseSummary, IReadOnlyList<string> candidates)
    {
        var normalizedSummary = NormalizeText(enterpriseSummary);
        return candidates.Any(candidate =>
            candidate.Length > 0 &&
            normalizedSummary.Contains(candidate, StringComparison.OrdinalIgnoreCase));
    }

    public bool TryResolveReversal(
        string summary,
        IReadOnlyList<CounterpartyNormalizationRule> rules,
        out EnterpriseReversalDescriptor descriptor)
    {
        var normalizedSummary = NormalizeReversalText(summary);
        foreach (var rule in rules)
        {
            if (!HasConfiguredReorderPrefix(rule.ReorderPrefix))
                continue;

            var normalizedPrefix = NormalizeReversalText(rule.ReorderPrefix);
            if (!normalizedSummary.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var openParenthesis = normalizedSummary.IndexOf('（', normalizedPrefix.Length);
            var referenceSuffix = (openParenthesis < 0
                    ? normalizedSummary[normalizedPrefix.Length..]
                    : normalizedSummary[normalizedPrefix.Length..openParenthesis])
                .Trim();
            var originalReferencePrefix = normalizedPrefix.StartsWith("冲销", StringComparison.OrdinalIgnoreCase)
                ? normalizedPrefix[2..]
                : string.Empty;
            var originalReference = referenceSuffix.Length == 0
                ? string.Empty
                : $"{originalReferencePrefix}{referenceSuffix}";

            var originalSummary = ResolveOriginalSummary(
                normalizedSummary,
                rule.ReorderPrefixLength,
                openParenthesis);
            descriptor = new EnterpriseReversalDescriptor(originalReference, originalSummary);
            return true;
        }

        descriptor = default;
        return false;
    }

    public string NormalizeReference(string? value) => NormalizeReversalText(value);

    public string NormalizeReversalSummary(string? value) => NormalizeReversalText(value);

    private string NormalizeReversalText(string? value) =>
        // “记账/记帐”只是字形差异，冲销凭证关联时必须归一，否则同一凭证会被误判为无候选。
        NormalizeText(value).Replace('帐', '账');

    private static string ResolveOriginalSummary(
        string normalizedSummary,
        int reorderPrefixLength,
        int openParenthesis)
    {
        if (reorderPrefixLength > 0 && reorderPrefixLength <= normalizedSummary.Length)
            return normalizedSummary[reorderPrefixLength..].Trim();

        if (openParenthesis < 0)
            return string.Empty;

        var closeParenthesis = normalizedSummary.IndexOf('）', openParenthesis + 1);
        return closeParenthesis >= 0 && closeParenthesis + 1 < normalizedSummary.Length
            ? normalizedSummary[(closeParenthesis + 1)..].Trim()
            : string.Empty;
    }

    private static bool HasConfiguredReorderPrefix(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !string.Equals(value, "_DZ_Null_", StringComparison.OrdinalIgnoreCase);

    private static bool Matches(string configuredValue, string actualValue) =>
        string.IsNullOrWhiteSpace(configuredValue) ||
        string.Equals(configuredValue, "_DZ_Null_", StringComparison.Ordinal) ||
        actualValue.Contains(configuredValue, StringComparison.OrdinalIgnoreCase);
}

/// <summary>冲销摘要中可用于定位原企业凭证的审计线索。</summary>
public readonly record struct EnterpriseReversalDescriptor(
    string OriginalReferenceNumber,
    string OriginalSummary);
