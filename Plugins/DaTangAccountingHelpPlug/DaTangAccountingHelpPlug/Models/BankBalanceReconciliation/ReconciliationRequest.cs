namespace DaTangAccountingHelpPlug.Models.BankBalanceReconciliation;

public enum ReconciliationMode
{
    Strict,
    LegacyCompatible
}

/// <summary>一次完整对账运行所需的不可变输入。</summary>
public sealed record ReconciliationRequest
{
    public required BankReconciliationProfile Profile { get; init; }
    public required EnterpriseLedgerLayout EnterpriseLayout { get; init; }
    public required string EnterpriseLedgerPath { get; init; }
    public required string BankStatementPath { get; init; }
    public string ReceiptEnrichmentPath { get; init; } = string.Empty;
    public required string OutputPath { get; init; }
    public DateTime AsOfDate { get; init; } = DateTime.Today;
    public decimal PreviousUnreconciledDifference { get; init; }
    public ReconciliationMode Mode { get; init; } = ReconciliationMode.Strict;
    public bool EnableLooseAmountAlignment { get; init; }
    public required ReconciliationConfiguration Configuration { get; init; }
}

public sealed record ReconciliationInputData
{
    public required IReadOnlyList<ReconciliationEntry> EnterpriseEntries { get; init; }
    public required IReadOnlyList<ReconciliationEntry> BankEntries { get; init; }
    public decimal EnterpriseBalance { get; init; }
    public decimal BankBalance { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
