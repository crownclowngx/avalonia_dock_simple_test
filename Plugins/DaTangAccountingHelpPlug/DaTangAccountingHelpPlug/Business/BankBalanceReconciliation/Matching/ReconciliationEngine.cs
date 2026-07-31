using DaTangAccountingHelpPlug.Models.BankBalanceReconciliation;

namespace DaTangAccountingHelpPlug.Business.BankBalanceReconciliation.Matching;

/// <summary>执行可重复、可审计的银行与企业账匹配。</summary>
public sealed class ReconciliationEngine : IReconciliationEngine
{
    private readonly EntryNormalizer _normalizer;
    private readonly AggregationRuleMatcher _aggregationRuleMatcher;

    public ReconciliationEngine(
        EntryNormalizer normalizer,
        AggregationRuleMatcher aggregationRuleMatcher)
    {
        _normalizer = normalizer;
        _aggregationRuleMatcher = aggregationRuleMatcher;
    }

    public ReconciliationResult Reconcile(
        ReconciliationRequest request,
        ReconciliationInputData input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(input);

        var decisions = new List<MatchDecision>();
        var consumedEnterpriseIds = new HashSet<string>(StringComparer.Ordinal);
        var reversedEnterpriseIds = DetectEnterpriseReversals(input.EnterpriseEntries, decisions);
        consumedEnterpriseIds.UnionWith(reversedEnterpriseIds);

        var enterpriseBuckets = input.EnterpriseEntries
            .Where(entry => !consumedEnterpriseIds.Contains(entry.EntryId))
            .GroupBy(entry => (entry.Direction, entry.Amount))
            .ToDictionary(group => group.Key, group => group.OrderBy(entry => entry.SourceRow).ToArray());

        foreach (var bankEntry in input.BankEntries.OrderBy(entry => entry.SourceRow))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var enterpriseDirection = Counterpart(bankEntry.Direction);
            enterpriseBuckets.TryGetValue((enterpriseDirection, bankEntry.Amount), out var amountCandidates);
            amountCandidates ??= [];
            var available = amountCandidates
                .Where(entry => !consumedEnterpriseIds.Contains(entry.EntryId))
                .ToArray();
            var names = _normalizer.ResolveCandidateNames(
                bankEntry,
                request.Configuration.NormalizationRules);
            var namedCandidates = available
                .Where(entry => _normalizer.ContainsCandidate(entry.Summary, names))
                .ToArray();

            IReadOnlyList<ReconciliationEntry> candidates = namedCandidates;
            if (request.Mode == ReconciliationMode.LegacyCompatible &&
                request.EnableLooseAmountAlignment &&
                candidates.Count == 0)
            {
                candidates = available;
            }

            if (candidates.Count == 1 ||
                request.Mode == ReconciliationMode.LegacyCompatible && candidates.Count > 0)
            {
                var matched = candidates[0];
                consumedEnterpriseIds.Add(matched.EntryId);
                decisions.Add(new MatchDecision
                {
                    Status = MatchDecisionStatus.Matched,
                    PrimaryEntry = bankEntry,
                    MatchedEntry = matched,
                    Candidates = candidates,
                    RuleId = request.Mode == ReconciliationMode.Strict ? "strict-name-amount" : "legacy-first-match",
                    Reason = candidates.Count == 1
                        ? "收付方向、金额和候选名称唯一匹配"
                        : "兼容模式按旧宏顺序选择首个候选"
                });
            }
            else if (candidates.Count > 1)
            {
                // 多个候选说明现有信息不足以证明唯一对应关系。
                // 严格模式宁可保留为待复核项，也不通过遍历顺序猜测结果。
                decisions.Add(new MatchDecision
                {
                    Status = MatchDecisionStatus.Ambiguous,
                    PrimaryEntry = bankEntry,
                    Candidates = candidates,
                    RuleId = "strict-ambiguous",
                    Reason = $"存在 {candidates.Count} 条同方向、同金额且名称匹配的企业记录"
                });
            }
            else
            {
                decisions.Add(new MatchDecision
                {
                    Status = MatchDecisionStatus.Unmatched,
                    PrimaryEntry = bankEntry,
                    RuleId = "no-candidate",
                    Reason = available.Length == 0 ? "没有同方向同金额记录" : "金额候选的摘要不包含候选名称"
                });
            }
        }

        foreach (var enterpriseEntry in input.EnterpriseEntries.OrderBy(entry => entry.SourceRow))
        {
            if (consumedEnterpriseIds.Contains(enterpriseEntry.EntryId))
                continue;

            decisions.Add(new MatchDecision
            {
                Status = MatchDecisionStatus.Unmatched,
                PrimaryEntry = enterpriseEntry,
                RuleId = "not-consumed",
                Reason = "未被任何银行流水唯一核销"
            });
        }

        _aggregationRuleMatcher.Apply(
            decisions,
            request.Configuration.AggregationRules,
            cancellationToken);

        return new ReconciliationResult
        {
            Request = request,
            Input = input,
            Decisions = decisions
        };
    }

    private HashSet<string> DetectEnterpriseReversals(
        IReadOnlyList<ReconciliationEntry> entries,
        ICollection<MatchDecision> decisions)
    {
        var consumed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in entries.GroupBy(entry =>
                     (entry.Direction, entry.Amount, Summary: _normalizer.NormalizeText(entry.Summary))))
        {
            var positives = group.Where(entry => entry.Debit > 0m || entry.Credit > 0m).ToList();
            var negatives = group.Where(entry => entry.Debit < 0m || entry.Credit < 0m).ToList();
            while (positives.Count > 0 && negatives.Count > 0)
            {
                var positive = positives[0];
                var negative = negatives[0];
                positives.RemoveAt(0);
                negatives.RemoveAt(0);
                consumed.Add(positive.EntryId);
                consumed.Add(negative.EntryId);
                decisions.Add(new MatchDecision
                {
                    Status = MatchDecisionStatus.Matched,
                    PrimaryEntry = positive,
                    MatchedEntry = negative,
                    Candidates = [negative],
                    RuleId = "enterprise-reversal",
                    Reason = "企业账同摘要同金额正负冲销"
                });
                decisions.Add(new MatchDecision
                {
                    Status = MatchDecisionStatus.Matched,
                    PrimaryEntry = negative,
                    MatchedEntry = positive,
                    Candidates = [positive],
                    RuleId = "enterprise-reversal",
                    Reason = "企业账同摘要同金额正负冲销"
                });
            }
        }

        return consumed;
    }

    private static ReconciliationDirection Counterpart(ReconciliationDirection bankDirection) =>
        bankDirection switch
        {
            ReconciliationDirection.BankReceived => ReconciliationDirection.EnterpriseReceived,
            ReconciliationDirection.BankPaid => ReconciliationDirection.EnterprisePaid,
            _ => throw new InvalidOperationException("银行记录方向无效。")
        };
}
