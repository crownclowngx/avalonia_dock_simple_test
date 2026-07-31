namespace DaTangAccountingHelpPlug.Models.BankBalanceReconciliation;

public sealed record ReconciliationProgress(
    string Stage,
    string Message,
    int Percent);

public sealed record ReconciliationRunSummary(
    bool IsBalanced,
    decimal AdjustedEnterpriseBalance,
    decimal AdjustedBankBalance,
    decimal Difference,
    int MatchedCount,
    int AmbiguousCount,
    string OutputPath);
