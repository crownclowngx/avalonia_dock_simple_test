using DaTangAccountingHelpPlug.Models.BankBalanceReconciliation;

namespace DaTangAccountingHelpPlug.Business.BankBalanceReconciliation.Matching;

/// <summary>执行可重复、可审计的银行与企业账匹配。</summary>
public sealed class ReconciliationEngine : IReconciliationEngine
{
    private readonly EntryNormalizer _normalizer;
    private readonly AggregationRuleMatcher _aggregationRuleMatcher;
    private readonly ReferenceAggregationMatcher _referenceAggregationMatcher;

    public ReconciliationEngine(
        EntryNormalizer normalizer,
        AggregationRuleMatcher aggregationRuleMatcher,
        ReferenceAggregationMatcher referenceAggregationMatcher)
    {
        _normalizer = normalizer;
        _aggregationRuleMatcher = aggregationRuleMatcher;
        _referenceAggregationMatcher = referenceAggregationMatcher;
    }

    public ReconciliationResult Reconcile(
        ReconciliationRequest request,
        ReconciliationInputData input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(input);

        var decisions = new List<MatchDecision>();
        var consumedBankIds = new HashSet<string>(StringComparer.Ordinal);
        var consumedEnterpriseIds = new HashSet<string>(StringComparer.Ordinal);
        var reversedEnterpriseIds = DetectEnterpriseReversals(
            input.EnterpriseEntries,
            request.Configuration.NormalizationRules,
            decisions,
            cancellationToken);
        consumedEnterpriseIds.UnionWith(reversedEnterpriseIds);

        // 带业务编号的多笔付款必须先按凭证汇总；否则兼容模式会把单笔金额
        // 错配给其他同金额凭证，随后真正的汇总凭证反而无法被核销。
        _referenceAggregationMatcher.Apply(
            request,
            input,
            decisions,
            consumedBankIds,
            consumedEnterpriseIds,
            cancellationToken);

        var enterpriseBuckets = input.EnterpriseEntries
            .Where(entry => !consumedEnterpriseIds.Contains(entry.EntryId))
            .GroupBy(entry => (entry.Direction, entry.Amount))
            .ToDictionary(group => group.Key, group => group.OrderBy(entry => entry.SourceRow).ToArray());

        // 银行流水顺序已由 Reader 按银行配置确定；这里必须原样消费，
        // 否则倒序银行账会被再次改成升序，兼容模式的首个候选关系也会随之改变。
        foreach (var bankEntry in input.BankEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (consumedBankIds.Contains(bankEntry.EntryId))
                continue;
            var enterpriseDirection = Counterpart(bankEntry.Direction);
            enterpriseBuckets.TryGetValue((enterpriseDirection, bankEntry.Amount), out var amountCandidates);
            amountCandidates ??= [];
            var available = amountCandidates
                .Where(entry => !consumedEnterpriseIds.Contains(entry.EntryId))
                .ToArray();
            var names = _normalizer.ResolveCandidateNames(
                bankEntry,
                request.Configuration.NormalizationRules,
                request.Profile.Id);
            var namedCandidates = available
                .Where(entry => _normalizer.ContainsCandidate(entry.Summary, names))
                .ToArray();

            IReadOnlyList<ReconciliationEntry> candidates = namedCandidates;
            var usedAmountOnlyFallback = false;
            if (request.Mode == ReconciliationMode.LegacyCompatible &&
                request.EnableLooseAmountAlignment &&
                candidates.Count == 0)
            {
                candidates = available;
                usedAmountOnlyFallback = candidates.Count > 0;
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
                    RuleId = request.Mode == ReconciliationMode.Strict
                        ? "strict-name-amount"
                        : usedAmountOnlyFallback
                            ? "legacy-amount-only"
                            : "legacy-first-name-match",
                    Reason = usedAmountOnlyFallback
                        ? "兼容宽松金额整理未通过单位名称校验，按稳定顺序选择首个同方向同金额候选"
                        : request.Mode == ReconciliationMode.LegacyCompatible
                            ? "兼容模式在单位名称校验通过后，按稳定顺序选择首个候选"
                            : "收付方向、金额和候选名称唯一匹配"
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
        IReadOnlyList<CounterpartyNormalizationRule> normalizationRules,
        ICollection<MatchDecision> decisions,
        CancellationToken cancellationToken)
    {
        var consumed = new HashSet<string>(StringComparer.Ordinal);
        var positiveEntries = entries
            .Where(IsPositiveEntry)
            .OrderBy(entry => entry.SourceRow)
            .ToArray();

        foreach (var reversal in entries
                     .Where(IsNegativeEntry)
                     .OrderBy(entry => entry.SourceRow))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (consumed.Contains(reversal.EntryId) ||
                !_normalizer.TryResolveReversal(reversal.Summary, normalizationRules, out var descriptor))
            {
                continue;
            }

            var candidates = positiveEntries
                .Where(candidate =>
                    !consumed.Contains(candidate.EntryId) &&
                    candidate.Amount == reversal.Amount &&
                    candidate.Direction == OppositeEnterpriseDirection(reversal.Direction))
                .Where(candidate => descriptor.OriginalReferenceNumber.Length > 0
                    ? string.Equals(
                        _normalizer.NormalizeReference(candidate.ReferenceNumber),
                        _normalizer.NormalizeReference(descriptor.OriginalReferenceNumber),
                        StringComparison.OrdinalIgnoreCase)
                    : descriptor.OriginalSummary.Length > 0 &&
                      string.Equals(
                          _normalizer.NormalizeReversalSummary(candidate.Summary),
                          descriptor.OriginalSummary,
                          StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (candidates.Length == 1)
            {
                var original = candidates[0];
                consumed.Add(original.EntryId);
                consumed.Add(reversal.EntryId);
                var ruleId = descriptor.OriginalReferenceNumber.Length > 0
                    ? "enterprise-reversal-reference"
                    : "enterprise-reversal-summary";
                var evidence = descriptor.OriginalReferenceNumber.Length > 0
                    ? $"原凭证号 {descriptor.OriginalReferenceNumber}"
                    : $"原摘要 {descriptor.OriginalSummary}";

                // 凭证号比摘要更能证明会计记录的唯一关系；只有唯一候选时才排除冲销双方。
                // 两条决定互相引用，使输出审计表可以从任一行追溯到对应的原记录或冲销记录。
                decisions.Add(CreateReversalExclusion(original, reversal, ruleId, evidence));
                decisions.Add(CreateReversalExclusion(reversal, original, ruleId, evidence));
                continue;
            }

            if (candidates.Length == 0)
                continue;

            // 多个同凭证候选仍不能证明唯一关系。冲销记录停止自动匹配，正数候选保留给银行流水。
            consumed.Add(reversal.EntryId);
            var ambiguousEvidence = descriptor.OriginalReferenceNumber.Length > 0
                ? $"原凭证号 {descriptor.OriginalReferenceNumber}"
                : $"原摘要 {descriptor.OriginalSummary}";
            decisions.Add(new MatchDecision
            {
                Status = MatchDecisionStatus.Ambiguous,
                PrimaryEntry = reversal,
                Candidates = candidates,
                RuleId = "enterprise-reversal-ambiguous",
                Reason = $"{ambiguousEvidence} 存在 {candidates.Length} 条同金额候选，无法唯一识别企业账内部冲销"
            });
        }

        return consumed;
    }

    private static MatchDecision CreateReversalExclusion(
        ReconciliationEntry primary,
        ReconciliationEntry matched,
        string ruleId,
        string evidence) => new()
    {
        Status = MatchDecisionStatus.Excluded,
        PrimaryEntry = primary,
        MatchedEntry = matched,
        Candidates = [matched],
        RuleId = ruleId,
        Reason = $"{evidence}；企业账内部冲销，不参与银企匹配"
    };

    private static bool IsPositiveEntry(ReconciliationEntry entry) =>
        entry.Debit > 0m || entry.Credit > 0m;

    private static bool IsNegativeEntry(ReconciliationEntry entry) =>
        entry.Debit < 0m || entry.Credit < 0m;

    private static ReconciliationDirection OppositeEnterpriseDirection(
        ReconciliationDirection direction) => direction switch
    {
        ReconciliationDirection.EnterpriseReceived => ReconciliationDirection.EnterprisePaid,
        ReconciliationDirection.EnterprisePaid => ReconciliationDirection.EnterpriseReceived,
        _ => throw new InvalidOperationException("冲销记录必须来自企业账。")
    };

    private static ReconciliationDirection Counterpart(ReconciliationDirection bankDirection) =>
        bankDirection switch
        {
            ReconciliationDirection.BankReceived => ReconciliationDirection.EnterpriseReceived,
            ReconciliationDirection.BankPaid => ReconciliationDirection.EnterprisePaid,
            _ => throw new InvalidOperationException("银行记录方向无效。")
        };
}
