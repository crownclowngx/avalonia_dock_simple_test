namespace DaTangAccountingHelpPlug.Models.BankBalanceReconciliation;

/// <summary>对账记录来自哪一类原始账簿。</summary>
public enum ReconciliationEntrySource
{
    EnterpriseLedger,
    BankStatement
}

/// <summary>按余额调节表口径统一后的业务方向。</summary>
public enum ReconciliationDirection
{
    BankReceived,
    BankPaid,
    EnterpriseReceived,
    EnterprisePaid
}

/// <summary>
/// 从原始工作表读取的一条不可变业务记录。
/// </summary>
/// <remarks>
/// 原始行号和文本必须一直保留到审计输出；清洗仅生成 <see cref="NormalizedCounterparty"/>，
/// 不能覆盖会计原始凭据中的内容。
/// </remarks>
public sealed record ReconciliationEntry
{
    public required string EntryId { get; init; }
    public required ReconciliationEntrySource Source { get; init; }
    public required ReconciliationDirection Direction { get; init; }
    public required int SourceRow { get; init; }
    public DateTime? TransactionDate { get; init; }
    public string ReferenceNumber { get; init; } = string.Empty;
    public string Counterparty { get; init; } = string.Empty;
    public string NormalizedCounterparty { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string CounterpartyAccount { get; init; } = string.Empty;
    public decimal Debit { get; init; }
    public decimal Credit { get; init; }
    public required decimal Amount { get; init; }
    public string ExistingMarker { get; init; } = string.Empty;
}
