using DaTangAccountingHelpPlug.Models.BankBalanceReconciliation;

namespace DaTangAccountingHelpPlug.Tests.BankBalanceReconciliation;

internal static class ReconciliationTestData
{
    public static ReconciliationConfiguration Configuration() => new()
    {
        SchemaVersion = 1,
        EnterpriseLayouts = [Layout()],
        BankProfiles = [Profile()]
    };

    public static EnterpriseLedgerLayout Layout() => new()
    {
        Id = "layout",
        DisplayName = "测试布局",
        StartRow = 2,
        DateColumn = 1,
        ReferenceColumn = 2,
        SummaryColumn = 3,
        DebitColumn = 4,
        CreditColumn = 5,
        MarkerColumn = 6,
        BalanceColumn = 7,
        BalanceDirectionColumn = 8
    };

    public static BankReconciliationProfile Profile() => new()
    {
        Id = "bank",
        UnitShortName = "测试单位",
        UnitName = "测试单位有限公司",
        BankName = "测试银行",
        BankShortName = "测试行",
        AccountNumber = "622200001234",
        SourceName = "测试",
        EnterpriseLayoutId = "layout",
        StartRow = 2,
        DateColumn = 1,
        CounterpartyColumn = 2,
        SummaryColumn = 3,
        DebitColumn = 4,
        CreditColumn = 5,
        MarkerColumn = 6,
        BalanceColumn = 7,
        BalanceFromLastRow = false,
        AccountSuffixLength = 4
    };

    public static ReconciliationRequest Request(
        ReconciliationMode mode = ReconciliationMode.Strict,
        bool looseAmount = false,
        string enterprisePath = "enterprise.xlsx",
        string bankPath = "bank.xlsx",
        string outputPath = "output.xlsx") => new()
    {
        Profile = Profile(),
        EnterpriseLayout = Layout(),
        EnterpriseLedgerPath = enterprisePath,
        BankStatementPath = bankPath,
        OutputPath = outputPath,
        AsOfDate = new DateTime(2026, 7, 31),
        Mode = mode,
        EnableLooseAmountAlignment = looseAmount,
        Configuration = Configuration()
    };

    public static ReconciliationEntry Entry(
        string id,
        ReconciliationDirection direction,
        decimal amount,
        string text,
        int row) => new()
    {
        EntryId = id,
        Source = direction is ReconciliationDirection.BankReceived or ReconciliationDirection.BankPaid
            ? ReconciliationEntrySource.BankStatement
            : ReconciliationEntrySource.EnterpriseLedger,
        Direction = direction,
        SourceRow = row,
        TransactionDate = new DateTime(2026, 7, 30),
        ReferenceNumber = row.ToString(),
        Counterparty = text,
        NormalizedCounterparty = text,
        Summary = text,
        Debit = direction is ReconciliationDirection.EnterpriseReceived or ReconciliationDirection.BankPaid
            ? amount
            : 0m,
        Credit = direction is ReconciliationDirection.EnterprisePaid or ReconciliationDirection.BankReceived
            ? amount
            : 0m,
        Amount = amount
    };
}
