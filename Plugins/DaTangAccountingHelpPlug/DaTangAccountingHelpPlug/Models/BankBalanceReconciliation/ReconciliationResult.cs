namespace DaTangAccountingHelpPlug.Models.BankBalanceReconciliation;

/// <summary>匹配结果和余额控制数。</summary>
public sealed class ReconciliationResult
{
    public required ReconciliationRequest Request { get; init; }
    public required ReconciliationInputData Input { get; init; }
    public List<MatchDecision> Decisions { get; init; } = [];

    public IReadOnlyList<ReconciliationEntry> BankReceivedUnrecorded => Unmatched(ReconciliationDirection.BankReceived);
    public IReadOnlyList<ReconciliationEntry> BankPaidUnrecorded => Unmatched(ReconciliationDirection.BankPaid);
    public IReadOnlyList<ReconciliationEntry> EnterpriseReceivedUnrecorded => Unmatched(ReconciliationDirection.EnterpriseReceived);
    public IReadOnlyList<ReconciliationEntry> EnterprisePaidUnrecorded => Unmatched(ReconciliationDirection.EnterprisePaid);

    public decimal AdjustedEnterpriseBalance => Money(
        Input.EnterpriseBalance + Math.Max(Request.PreviousUnreconciledDifference, 0m)
        + BankReceivedUnrecorded.Sum(item => item.Amount)
        - BankPaidUnrecorded.Sum(item => item.Amount));

    public decimal AdjustedBankBalance => Money(
        Input.BankBalance + Math.Max(-Request.PreviousUnreconciledDifference, 0m)
        + EnterpriseReceivedUnrecorded.Sum(item => item.Amount)
        - EnterprisePaidUnrecorded.Sum(item => item.Amount));

    public decimal Difference => Money(AdjustedEnterpriseBalance - AdjustedBankBalance);
    public bool IsBalanced => Difference == 0m;
    public int AmbiguousCount => Decisions.Count(item => item.Status == MatchDecisionStatus.Ambiguous);
    public int MatchedCount => Decisions.Count(item => item.Status is MatchDecisionStatus.Matched or MatchDecisionStatus.Aggregated);

    private IReadOnlyList<ReconciliationEntry> Unmatched(ReconciliationDirection direction) =>
        Decisions
            .Where(decision =>
                decision.PrimaryEntry.Direction == direction &&
                decision.Status is MatchDecisionStatus.Unmatched or MatchDecisionStatus.Ambiguous)
            .Select(decision => decision.PrimaryEntry)
            .ToArray();

    public static decimal Money(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
