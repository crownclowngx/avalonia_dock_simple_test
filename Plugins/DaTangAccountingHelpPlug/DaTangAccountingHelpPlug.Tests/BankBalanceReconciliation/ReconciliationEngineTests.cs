using DaTangAccountingHelpPlug.Business.BankBalanceReconciliation.Matching;
using DaTangAccountingHelpPlug.Models.BankBalanceReconciliation;
using Xunit;

namespace DaTangAccountingHelpPlug.Tests.BankBalanceReconciliation;

public sealed class ReconciliationEngineTests
{
    private readonly ReconciliationEngine _engine = new(new EntryNormalizer(), new AggregationRuleMatcher());

    [Fact]
    public void 严格模式仅自动核销唯一候选()
    {
        var enterprise = ReconciliationTestData.Entry(
            "E1", ReconciliationDirection.EnterpriseReceived, 100m, "北京客户", 2);
        var bank = ReconciliationTestData.Entry(
            "B1", ReconciliationDirection.BankReceived, 100m, "北京客户", 2);

        var result = _engine.Reconcile(
            ReconciliationTestData.Request(),
            Input([enterprise], [bank]));

        var decision = Assert.Single(result.Decisions);
        Assert.Equal(MatchDecisionStatus.Matched, decision.Status);
        Assert.Equal("E1", decision.MatchedEntry?.EntryId);
        Assert.Equal("strict-name-amount", decision.RuleId);
    }

    [Fact]
    public void 严格模式把重复候选保留为待复核()
    {
        var enterprise = new[]
        {
            ReconciliationTestData.Entry("E1", ReconciliationDirection.EnterpriseReceived, 100m, "同一客户", 2),
            ReconciliationTestData.Entry("E2", ReconciliationDirection.EnterpriseReceived, 100m, "同一客户", 3)
        };
        var bank = ReconciliationTestData.Entry(
            "B1", ReconciliationDirection.BankReceived, 100m, "同一客户", 2);

        var result = _engine.Reconcile(
            ReconciliationTestData.Request(),
            Input(enterprise, [bank]));

        var decision = Assert.Single(result.Decisions, item => item.PrimaryEntry.EntryId == "B1");
        Assert.Equal(MatchDecisionStatus.Ambiguous, decision.Status);
        Assert.Equal(2, decision.CandidateCount);
        Assert.Equal(1, result.AmbiguousCount);
    }

    [Fact]
    public void 兼容模式按来源行选择首个候选且每条记录只消费一次()
    {
        var enterprise = new[]
        {
            ReconciliationTestData.Entry("E-later", ReconciliationDirection.EnterpriseReceived, 100m, "同一客户", 8),
            ReconciliationTestData.Entry("E-first", ReconciliationDirection.EnterpriseReceived, 100m, "同一客户", 3)
        };
        var banks = new[]
        {
            ReconciliationTestData.Entry("B1", ReconciliationDirection.BankReceived, 100m, "同一客户", 2),
            ReconciliationTestData.Entry("B2", ReconciliationDirection.BankReceived, 100m, "同一客户", 3)
        };

        var result = _engine.Reconcile(
            ReconciliationTestData.Request(ReconciliationMode.LegacyCompatible),
            Input(enterprise, banks));
        var matches = result.Decisions.Where(item => item.Status == MatchDecisionStatus.Matched).ToArray();

        Assert.Equal(2, matches.Length);
        Assert.Equal("E-first", matches[0].MatchedEntry?.EntryId);
        Assert.Equal("E-later", matches[1].MatchedEntry?.EntryId);
        Assert.Equal(2, matches.Select(item => item.MatchedEntry?.EntryId).Distinct().Count());
    }

    [Fact]
    public void 四类未达按标准公式计算调节余额()
    {
        var enterprise = new[]
        {
            ReconciliationTestData.Entry("E1", ReconciliationDirection.EnterpriseReceived, 30m, "企收", 2),
            ReconciliationTestData.Entry("E2", ReconciliationDirection.EnterprisePaid, 50m, "企付", 3)
        };
        var banks = new[]
        {
            ReconciliationTestData.Entry("B1", ReconciliationDirection.BankReceived, 100m, "银收", 2),
            ReconciliationTestData.Entry("B2", ReconciliationDirection.BankPaid, 20m, "银付", 3)
        };

        var result = _engine.Reconcile(
            ReconciliationTestData.Request(),
            Input(enterprise, banks, 1000m, 1000m));

        Assert.Equal(1080m, result.AdjustedEnterpriseBalance);
        Assert.Equal(980m, result.AdjustedBankBalance);
        Assert.Equal(100m, result.Difference);
    }

    private static ReconciliationInputData Input(
        IReadOnlyList<ReconciliationEntry> enterprise,
        IReadOnlyList<ReconciliationEntry> bank,
        decimal enterpriseBalance = 0m,
        decimal bankBalance = 0m) => new()
    {
        EnterpriseEntries = enterprise,
        BankEntries = bank,
        EnterpriseBalance = enterpriseBalance,
        BankBalance = bankBalance
    };
}
