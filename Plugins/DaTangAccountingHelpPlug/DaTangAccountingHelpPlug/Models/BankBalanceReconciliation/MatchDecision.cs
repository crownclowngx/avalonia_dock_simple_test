namespace DaTangAccountingHelpPlug.Models.BankBalanceReconciliation;

public enum MatchDecisionStatus
{
    Matched,
    Unmatched,
    Ambiguous,
    Excluded,
    Aggregated
}

/// <summary>匹配引擎对一条源记录作出的可审计决定。</summary>
public sealed record MatchDecision
{
    public required MatchDecisionStatus Status { get; init; }
    public required ReconciliationEntry PrimaryEntry { get; init; }
    public ReconciliationEntry? MatchedEntry { get; init; }
    public IReadOnlyList<ReconciliationEntry> Candidates { get; init; } = [];
    public string RuleId { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    /// <summary>同一业务组使用相同键，界面据此压缩展示，审计表仍保留每条来源记录。</summary>
    public string GroupKey { get; init; } = string.Empty;
    public string GroupTitle { get; init; } = string.Empty;
    public int GroupEntryCount { get; init; }
    public int CandidateCount => Candidates.Count;
}
