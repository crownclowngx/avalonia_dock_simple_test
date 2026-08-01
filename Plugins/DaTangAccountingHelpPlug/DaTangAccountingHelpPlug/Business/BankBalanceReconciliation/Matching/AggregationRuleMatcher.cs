using DaTangAccountingHelpPlug.Models.BankBalanceReconciliation;

namespace DaTangAccountingHelpPlug.Business.BankBalanceReconciliation.Matching;

/// <summary>执行配置中明确声明的整组等额抵消。</summary>
public sealed class AggregationRuleMatcher
{
    public void Apply(
        IList<MatchDecision> decisions,
        IReadOnlyList<AggregationRule> rules,
        CancellationToken cancellationToken)
    {
        foreach (var rule in rules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var enterpriseDirection = rule.BankDirection switch
            {
                ReconciliationDirection.BankReceived => ReconciliationDirection.EnterpriseReceived,
                ReconciliationDirection.BankPaid => ReconciliationDirection.EnterprisePaid,
                _ => throw new InvalidOperationException($"汇总规则 {rule.Id} 的银行方向无效。")
            };

            var bank = decisions.Where(decision =>
                    decision.Status == MatchDecisionStatus.Unmatched &&
                    string.IsNullOrWhiteSpace(decision.GroupKey) &&
                    decision.PrimaryEntry.Direction == rule.BankDirection &&
                    ContainsAny(decision.PrimaryEntry, rule.BankKeywords))
                .ToArray();
            var enterprise = decisions.Where(decision =>
                    decision.Status == MatchDecisionStatus.Unmatched &&
                    string.IsNullOrWhiteSpace(decision.GroupKey) &&
                    decision.PrimaryEntry.Direction == enterpriseDirection &&
                    ContainsAny(decision.PrimaryEntry, rule.EnterpriseKeywords))
                .ToArray();

            // 凭证编号汇总阶段形成的组必须保持完整。
            // 金额不等或凭证不唯一时，后续通用汇总不能拆散业务组再尝试“凑平”。

            if (bank.Length == 0 || enterprise.Length == 0)
                continue;

            var bankTotal = ReconciliationResult.Money(bank.Sum(item => item.PrimaryEntry.Amount));
            var enterpriseTotal = ReconciliationResult.Money(enterprise.Sum(item => item.PrimaryEntry.Amount));
            if (bankTotal != enterpriseTotal)
                continue;

            var bankEntries = bank.Select(item => item.PrimaryEntry).ToArray();
            var enterpriseEntries = enterprise.Select(item => item.PrimaryEntry).ToArray();
            Replace(decisions, bank, enterpriseEntries, rule);
            Replace(decisions, enterprise, bankEntries, rule);
        }
    }

    private static void Replace(
        IList<MatchDecision> decisions,
        IReadOnlyList<MatchDecision> source,
        IReadOnlyList<ReconciliationEntry> oppositeEntries,
        AggregationRule rule)
    {
        foreach (var decision in source)
        {
            var index = decisions.IndexOf(decision);
            decisions[index] = decision with
            {
                Status = MatchDecisionStatus.Aggregated,
                Candidates = oppositeEntries,
                RuleId = rule.Id,
                Reason = $"按“{rule.DisplayName}”规则整组等额抵消"
            };
        }
    }

    private static bool ContainsAny(ReconciliationEntry entry, IReadOnlyList<string> keywords) =>
        keywords.Count == 0 || keywords.Any(keyword =>
            entry.Summary.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
            entry.Counterparty.Contains(keyword, StringComparison.OrdinalIgnoreCase));
}
