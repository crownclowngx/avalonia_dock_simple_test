using DaTangAccountingHelpPlug.Models.BankBalanceReconciliation;

namespace DaTangAccountingHelpPlug.ViewModels.BankBalanceReconciliation;

/// <summary>将同一业务组压缩为一个复核标题，同时保留可展开的逐行审计明细。</summary>
public sealed class ReconciliationIssueViewModel
{
    public required string Title { get; init; }
    public required string Summary { get; init; }
    public required string Reason { get; init; }
    public required IReadOnlyList<ReconciliationIssueEntryViewModel> Entries { get; init; }
    public int EntryCount => Entries.Count;

    public static IReadOnlyList<ReconciliationIssueViewModel> Create(
        IEnumerable<MatchDecision> decisions) => decisions
        .Where(decision => decision.Status is MatchDecisionStatus.Unmatched or MatchDecisionStatus.Ambiguous)
        .GroupBy(decision => string.IsNullOrWhiteSpace(decision.GroupKey)
            ? $"entry:{decision.PrimaryEntry.EntryId}"
            : decision.GroupKey,
            StringComparer.Ordinal)
        .Select(CreateGroup)
        .OrderBy(issue => issue.Entries.Min(entry => entry.SourceOrder))
        .ThenBy(issue => issue.Entries.Min(entry => entry.SourceRow))
        .ToArray();

    private static ReconciliationIssueViewModel CreateGroup(IGrouping<string, MatchDecision> group)
    {
        var decisions = group.ToArray();
        var first = decisions[0];
        var entries = decisions
            .Select(decision => new ReconciliationIssueEntryViewModel(decision))
            .OrderBy(entry => entry.SourceOrder)
            .ThenBy(entry => entry.SourceRow)
            .ToArray();
        if (string.IsNullOrWhiteSpace(first.GroupKey))
        {
            return new ReconciliationIssueViewModel
            {
                Title = $"{entries[0].SourceLabel}第 {entries[0].SourceRow} 行",
                Summary = $"{entries[0].Amount:N2}｜{first.Reason}",
                Reason = first.Reason,
                Entries = entries
            };
        }

        var bank = decisions
            .Where(decision => decision.PrimaryEntry.Source == ReconciliationEntrySource.BankStatement)
            .ToArray();
        var enterprise = decisions
            .Where(decision => decision.PrimaryEntry.Source == ReconciliationEntrySource.EnterpriseLedger)
            .ToArray();
        var bankTotal = ReconciliationResult.Money(bank.Sum(decision => decision.PrimaryEntry.Amount));
        var enterpriseTotal = ReconciliationResult.Money(enterprise.Sum(decision => decision.PrimaryEntry.Amount));
        var difference = ReconciliationResult.Money(bankTotal - enterpriseTotal);
        var summary = enterprise.Length == 0
            ? $"银行 {bank.Length} 笔 {bankTotal:N2}｜未找到企业凭证"
            : $"银行 {bank.Length} 笔 {bankTotal:N2}｜企业 {enterpriseTotal:N2}｜差额 {difference:N2}";

        return new ReconciliationIssueViewModel
        {
            Title = first.GroupTitle,
            Summary = summary,
            Reason = first.Reason,
            Entries = entries
        };
    }
}

/// <summary>复核组展开后展示的一条原始账簿记录。</summary>
public sealed class ReconciliationIssueEntryViewModel
{
    public ReconciliationIssueEntryViewModel(MatchDecision decision)
    {
        var entry = decision.PrimaryEntry;
        SourceOrder = entry.Source == ReconciliationEntrySource.BankStatement ? 0 : 1;
        SourceLabel = entry.Source == ReconciliationEntrySource.BankStatement ? "银行账" : "企业账";
        SourceRow = entry.SourceRow;
        Amount = entry.Amount;
        ReferenceNumber = entry.ReferenceNumber;
        Description = string.Join("─", new[] { entry.Counterparty, entry.Summary }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        Status = decision.Status == MatchDecisionStatus.Ambiguous ? "有歧义" : "未匹配";
    }

    public int SourceOrder { get; }
    public string SourceLabel { get; }
    public int SourceRow { get; }
    public decimal Amount { get; }
    public string ReferenceNumber { get; }
    public string Description { get; }
    public string Status { get; }
}
