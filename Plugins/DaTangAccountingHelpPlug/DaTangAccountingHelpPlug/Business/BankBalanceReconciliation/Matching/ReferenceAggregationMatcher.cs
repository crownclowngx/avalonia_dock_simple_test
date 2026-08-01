using System.Text;
using DaTangAccountingHelpPlug.Models.BankBalanceReconciliation;

namespace DaTangAccountingHelpPlug.Business.BankBalanceReconciliation.Matching;

/// <summary>按银行摘要业务编号汇总流水，并关联唯一企业凭证。</summary>
public sealed class ReferenceAggregationMatcher
{
    public void Apply(
        ReconciliationRequest request,
        ReconciliationInputData input,
        ICollection<MatchDecision> decisions,
        ISet<string> consumedBankIds,
        ISet<string> consumedEnterpriseIds,
        CancellationToken cancellationToken)
    {
        foreach (var rule in request.Configuration.ReferenceAggregationRules.Where(rule =>
                     rule.ApplicableProfileIds.Contains(request.Profile.Id, StringComparer.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var enterpriseDirection = Counterpart(rule.BankDirection);
            var bankGroups = input.BankEntries
                .Where(entry =>
                    !consumedBankIds.Contains(entry.EntryId) &&
                    entry.Direction == rule.BankDirection)
                .Select(entry => new
                {
                    Entry = entry,
                    Reference = TryExtractBankReference(
                        entry.Summary,
                        rule.BankSummaryKeyword,
                        out var reference)
                        ? reference
                        : string.Empty
                })
                .Where(item => item.Reference.Length > 0)
                .GroupBy(item => item.Reference, StringComparer.Ordinal)
                .Select(group => new
                {
                    Reference = group.Key,
                    Entries = group.Select(item => item.Entry).ToArray()
                })
                .ToArray();

            foreach (var group in bankGroups)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bankEntries = group.Entries
                    .Where(entry => !consumedBankIds.Contains(entry.EntryId))
                    .ToArray();
                if (bankEntries.Length == 0)
                    continue;

                var enterpriseEntries = input.EnterpriseEntries
                    .Where(entry =>
                        !consumedEnterpriseIds.Contains(entry.EntryId) &&
                        entry.Direction == enterpriseDirection &&
                        TryExtractEnterpriseReference(
                            entry.ReferenceNumber,
                            rule.EnterpriseReferencePrefixes,
                            out var reference) &&
                        reference == group.Reference)
                    .ToArray();
                var groupKey = $"reference:{rule.Id}:{group.Reference}";
                var groupTitle = $"{rule.BankSummaryKeyword}{group.Reference}";
                var bankTotal = ReconciliationResult.Money(bankEntries.Sum(entry => entry.Amount));

                if (enterpriseEntries.Length == 1)
                {
                    var enterpriseEntry = enterpriseEntries[0];
                    consumedEnterpriseIds.Add(enterpriseEntry.EntryId);
                    foreach (var bankEntry in bankEntries)
                        consumedBankIds.Add(bankEntry.EntryId);

                    if (bankTotal == enterpriseEntry.Amount)
                    {
                        AddMatchedGroup(
                            decisions,
                            bankEntries,
                            enterpriseEntry,
                            rule,
                            groupKey,
                            groupTitle,
                            bankTotal);
                    }
                    else
                    {
                        AddAmountMismatchGroup(
                            decisions,
                            bankEntries,
                            enterpriseEntry,
                            groupKey,
                            groupTitle,
                            bankTotal);
                    }

                    continue;
                }

                foreach (var bankEntry in bankEntries)
                    consumedBankIds.Add(bankEntry.EntryId);
                if (enterpriseEntries.Length == 0)
                {
                    AddBankGroupDecisions(
                        decisions,
                        bankEntries,
                        MatchDecisionStatus.Unmatched,
                        [],
                        "reference-group-no-enterprise",
                        $"{groupTitle} 的 {bankEntries.Length} 笔银行流水合计 {bankTotal:N2}，没有找到对应企业凭证",
                        groupKey,
                        groupTitle);
                    continue;
                }

                foreach (var enterpriseEntry in enterpriseEntries)
                    consumedEnterpriseIds.Add(enterpriseEntry.EntryId);
                var ambiguousReason =
                    $"{groupTitle} 对应 {enterpriseEntries.Length} 条企业凭证，无法证明唯一关系";
                AddBankGroupDecisions(
                    decisions,
                    bankEntries,
                    MatchDecisionStatus.Ambiguous,
                    enterpriseEntries,
                    "reference-group-enterprise-ambiguous",
                    ambiguousReason,
                    groupKey,
                    groupTitle);
                foreach (var enterpriseEntry in enterpriseEntries)
                {
                    decisions.Add(CreateGroupDecision(
                        MatchDecisionStatus.Ambiguous,
                        enterpriseEntry,
                        bankEntries,
                        "reference-group-enterprise-ambiguous",
                        ambiguousReason,
                        groupKey,
                        groupTitle,
                        bankEntries.Length));
                }
            }
        }
    }

    private static void AddMatchedGroup(
        ICollection<MatchDecision> decisions,
        IReadOnlyList<ReconciliationEntry> bankEntries,
        ReconciliationEntry enterpriseEntry,
        ReferenceAggregationRule rule,
        string groupKey,
        string groupTitle,
        decimal bankTotal)
    {
        var reason =
            $"{groupTitle} 的 {bankEntries.Count} 笔银行流水合计 {bankTotal:N2}，与企业凭证 {enterpriseEntry.ReferenceNumber} 等额";
        foreach (var bankEntry in bankEntries)
        {
            decisions.Add(new MatchDecision
            {
                Status = MatchDecisionStatus.Aggregated,
                PrimaryEntry = bankEntry,
                MatchedEntry = enterpriseEntry,
                Candidates = [enterpriseEntry],
                RuleId = rule.Id,
                Reason = reason,
                GroupKey = groupKey,
                GroupTitle = groupTitle,
                GroupEntryCount = bankEntries.Count
            });
        }
    }

    private static void AddAmountMismatchGroup(
        ICollection<MatchDecision> decisions,
        IReadOnlyList<ReconciliationEntry> bankEntries,
        ReconciliationEntry enterpriseEntry,
        string groupKey,
        string groupTitle,
        decimal bankTotal)
    {
        var difference = ReconciliationResult.Money(bankTotal - enterpriseEntry.Amount);
        var reason =
            $"{groupTitle} 的 {bankEntries.Count} 笔银行流水合计 {bankTotal:N2}，企业凭证 {enterpriseEntry.ReferenceNumber} 为 {enterpriseEntry.Amount:N2}，差额 {difference:N2}";
        AddBankGroupDecisions(
            decisions,
            bankEntries,
            MatchDecisionStatus.Unmatched,
            [enterpriseEntry],
            "reference-group-amount-mismatch",
            reason,
            groupKey,
            groupTitle);
        decisions.Add(CreateGroupDecision(
            MatchDecisionStatus.Unmatched,
            enterpriseEntry,
            bankEntries,
            "reference-group-amount-mismatch",
            reason,
            groupKey,
            groupTitle,
            bankEntries.Count));
    }

    private static void AddBankGroupDecisions(
        ICollection<MatchDecision> decisions,
        IReadOnlyList<ReconciliationEntry> bankEntries,
        MatchDecisionStatus status,
        IReadOnlyList<ReconciliationEntry> candidates,
        string ruleId,
        string reason,
        string groupKey,
        string groupTitle)
    {
        foreach (var bankEntry in bankEntries)
        {
            decisions.Add(CreateGroupDecision(
                status,
                bankEntry,
                candidates,
                ruleId,
                reason,
                groupKey,
                groupTitle,
                bankEntries.Count));
        }
    }

    private static MatchDecision CreateGroupDecision(
        MatchDecisionStatus status,
        ReconciliationEntry primaryEntry,
        IReadOnlyList<ReconciliationEntry> candidates,
        string ruleId,
        string reason,
        string groupKey,
        string groupTitle,
        int groupEntryCount) => new()
    {
        Status = status,
        PrimaryEntry = primaryEntry,
        Candidates = candidates,
        RuleId = ruleId,
        Reason = reason,
        GroupKey = groupKey,
        GroupTitle = groupTitle,
        GroupEntryCount = groupEntryCount
    };

    private static bool TryExtractBankReference(
        string summary,
        string keyword,
        out string reference)
    {
        var normalizedSummary = NormalizeAccountingText(summary);
        var normalizedKeyword = NormalizeAccountingText(keyword);
        var keywordIndex = normalizedSummary.IndexOf(normalizedKeyword, StringComparison.OrdinalIgnoreCase);
        if (keywordIndex < 0)
        {
            reference = string.Empty;
            return false;
        }

        var index = keywordIndex + normalizedKeyword.Length;
        while (index < normalizedSummary.Length &&
               (char.IsWhiteSpace(normalizedSummary[index]) || normalizedSummary[index] is '-' or '_' or ':' or '：'))
            index++;
        var start = index;
        while (index < normalizedSummary.Length && char.IsAsciiDigit(normalizedSummary[index]))
            index++;
        reference = NormalizeReferenceKey(normalizedSummary[start..index]);
        return reference.Length > 0;
    }

    private static bool TryExtractEnterpriseReference(
        string value,
        IReadOnlyList<string> prefixes,
        out string reference)
    {
        var normalizedValue = NormalizeAccountingText(value);
        foreach (var prefix in prefixes)
        {
            var normalizedPrefix = NormalizeAccountingText(prefix);
            if (!normalizedValue.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
                continue;
            var suffix = normalizedValue[normalizedPrefix.Length..].Trim();
            if (suffix.Length == 0 || suffix.Any(character => !char.IsAsciiDigit(character)))
                continue;
            reference = NormalizeReferenceKey(suffix);
            return reference.Length > 0;
        }

        reference = string.Empty;
        return false;
    }

    private static string NormalizeAccountingText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim())
        {
            builder.Append(character is >= '０' and <= '９'
                ? (char)('0' + character - '０')
                : character);
        }

        return builder.ToString().Replace('帐', '账');
    }

    private static string NormalizeReferenceKey(string value)
    {
        if (value.Length == 0)
            return string.Empty;
        var normalized = value.TrimStart('0');
        return normalized.Length == 0 ? "0" : normalized;
    }

    private static ReconciliationDirection Counterpart(ReconciliationDirection bankDirection) =>
        bankDirection switch
        {
            ReconciliationDirection.BankReceived => ReconciliationDirection.EnterpriseReceived,
            ReconciliationDirection.BankPaid => ReconciliationDirection.EnterprisePaid,
            _ => throw new InvalidOperationException("凭证汇总规则的银行方向无效。")
        };
}
