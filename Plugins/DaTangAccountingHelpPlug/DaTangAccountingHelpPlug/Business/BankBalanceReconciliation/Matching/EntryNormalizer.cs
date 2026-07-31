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

    private static bool Matches(string configuredValue, string actualValue) =>
        string.IsNullOrWhiteSpace(configuredValue) ||
        string.Equals(configuredValue, "_DZ_Null_", StringComparison.Ordinal) ||
        actualValue.Contains(configuredValue, StringComparison.OrdinalIgnoreCase);
}
