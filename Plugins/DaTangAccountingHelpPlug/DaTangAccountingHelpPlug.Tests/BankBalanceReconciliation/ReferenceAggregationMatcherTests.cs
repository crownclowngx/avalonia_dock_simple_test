using DaTangAccountingHelpPlug.Business.BankBalanceReconciliation.Matching;
using DaTangAccountingHelpPlug.Models.BankBalanceReconciliation;
using Xunit;

namespace DaTangAccountingHelpPlug.Tests.BankBalanceReconciliation;

public sealed class ReferenceAggregationMatcherTests
{
    private readonly ReconciliationEngine _engine = new(
        new EntryNormalizer(),
        new AggregationRuleMatcher(),
        new ReferenceAggregationMatcher());

    [Theory]
    [InlineData(ReconciliationMode.Strict)]
    [InlineData(ReconciliationMode.LegacyCompatible)]
    public void 咨询费426的32笔付款汇总后唯一匹配企业凭证(ReconciliationMode mode)
    {
        var request = Request(mode, looseAmount: true);
        var enterprise = Enterprise("E329", 96000m, "记帐-00426", 329);
        var bank = Enumerable.Range(0, 32)
            .Select(index => Bank($"B{index}", 3000m, "孟祥江", "咨询费426", 3831 + index))
            .ToArray();

        var result = _engine.Reconcile(request, Input([enterprise], bank));

        Assert.Equal(32, result.MatchedCount);
        Assert.All(result.Decisions, decision =>
        {
            Assert.Equal(MatchDecisionStatus.Aggregated, decision.Status);
            Assert.Equal("E329", decision.MatchedEntry?.EntryId);
            Assert.Equal("咨询费426", decision.GroupTitle);
            Assert.Equal(32, decision.GroupEntryCount);
        });
        Assert.Equal(0, result.ReviewIssueCount);
        Assert.DoesNotContain(result.Decisions, decision => decision.RuleId == "legacy-amount-only");
    }

    [Theory]
    [InlineData("咨询费４２６", "记账-000426")]
    [InlineData("咨询费: 000426", "记帐-426")]
    public void 凭证编号兼容全半角数字记账写法和前导零(string bankSummary, string reference)
    {
        var result = _engine.Reconcile(
            Request(),
            Input(
                [Enterprise("E", 3000m, reference, 10)],
                [Bank("B", 3000m, "测试人员", bankSummary, 20)]));

        var decision = Assert.Single(result.Decisions);
        Assert.Equal(MatchDecisionStatus.Aggregated, decision.Status);
        Assert.Equal("E", decision.MatchedEntry?.EntryId);
    }

    [Fact]
    public void 金额不等时整组待复核且不进入宽松金额匹配()
    {
        var result = _engine.Reconcile(
            Request(ReconciliationMode.LegacyCompatible, looseAmount: true),
            Input(
                [Enterprise("E", 7000m, "记帐-00683", 30)],
                [
                    Bank("B1", 3000m, "甲", "咨询费683", 40),
                    Bank("B2", 3000m, "乙", "咨询费683", 41)
                ]));

        Assert.Equal(3, result.Decisions.Count);
        Assert.All(result.Decisions, decision =>
        {
            Assert.Equal(MatchDecisionStatus.Unmatched, decision.Status);
            Assert.Equal("reference-group-amount-mismatch", decision.RuleId);
            Assert.Equal("咨询费683", decision.GroupTitle);
        });
        Assert.Equal(1, result.ReviewIssueCount);
        Assert.DoesNotContain(result.Decisions, decision => decision.RuleId == "legacy-amount-only");
    }

    [Fact]
    public void 同编号存在多个企业凭证时整组歧义且不按顺序猜测()
    {
        var result = _engine.Reconcile(
            Request(ReconciliationMode.LegacyCompatible, looseAmount: true),
            Input(
                [
                    Enterprise("E1", 3000m, "记帐-00426", 10),
                    Enterprise("E2", 3000m, "记账-000426", 11)
                ],
                [Bank("B", 3000m, "人员", "咨询费426", 20)]));

        Assert.Equal(3, result.Decisions.Count);
        Assert.All(result.Decisions, decision =>
            Assert.Equal(MatchDecisionStatus.Ambiguous, decision.Status));
        Assert.Equal(1, result.ReviewIssueCount);
        Assert.DoesNotContain(result.Decisions, decision => decision.MatchedEntry is not null);
    }

    [Fact]
    public void 缺少企业凭证时银行组被保护并作为一个复核组()
    {
        var result = _engine.Reconcile(
            Request(ReconciliationMode.LegacyCompatible, looseAmount: true),
            Input([], [
                Bank("B1", 3000m, "甲", "咨询费999", 20),
                Bank("B2", 3000m, "乙", "咨询费999", 21)
            ]));

        Assert.Equal(2, result.Decisions.Count);
        Assert.All(result.Decisions, decision =>
            Assert.Equal("reference-group-no-enterprise", decision.RuleId));
        Assert.Equal(1, result.ReviewIssueCount);
    }

    [Fact]
    public void 银行收款退款不进入付款凭证汇总()
    {
        var bank = Bank("B", 3000m, "客户公司", "咨询费426", 20) with
        {
            Direction = ReconciliationDirection.BankReceived,
            Debit = 0m,
            Credit = 3000m
        };
        var enterprise = ReconciliationTestData.Entry(
            "E",
            ReconciliationDirection.EnterpriseReceived,
            3000m,
            "客户公司退款",
            10);

        var result = _engine.Reconcile(Request(), Input([enterprise], [bank]));

        var decision = Assert.Single(result.Decisions);
        Assert.Equal(MatchDecisionStatus.Matched, decision.Status);
        Assert.Equal("strict-name-amount", decision.RuleId);
    }

    [Fact]
    public void 规则未包含当前Profile时不影响普通匹配流程()
    {
        var request = Request(ReconciliationMode.LegacyCompatible, looseAmount: true);
        request.Profile.Id = "other-bank";
        var result = _engine.Reconcile(
            request,
            Input(
                [Enterprise("E", 3000m, "记帐-00426", 10)],
                [Bank("B", 3000m, "无关名称", "咨询费426", 20)]));

        var decision = Assert.Single(result.Decisions);
        Assert.Equal("legacy-amount-only", decision.RuleId);
        Assert.True(string.IsNullOrEmpty(decision.GroupKey));
    }

    private static ReconciliationRequest Request(
        ReconciliationMode mode = ReconciliationMode.Strict,
        bool looseAmount = false)
    {
        var request = ReconciliationTestData.Request(mode, looseAmount);
        request.Configuration.ReferenceAggregationRules.Add(new ReferenceAggregationRule
        {
            Id = "consulting-reference",
            DisplayName = "咨询费凭证汇总",
            ApplicableProfileIds = ["bank"],
            BankDirection = ReconciliationDirection.BankPaid,
            BankSummaryKeyword = "咨询费",
            EnterpriseReferencePrefixes = ["记帐-", "记账-"]
        });
        return request;
    }

    private static ReconciliationEntry Bank(
        string id,
        decimal amount,
        string counterparty,
        string summary,
        int row) => ReconciliationTestData.Entry(
        id,
        ReconciliationDirection.BankPaid,
        amount,
        counterparty,
        row) with
    {
        Summary = summary,
        Counterparty = counterparty,
        NormalizedCounterparty = counterparty,
        Debit = amount,
        Credit = 0m
    };

    private static ReconciliationEntry Enterprise(
        string id,
        decimal amount,
        string reference,
        int row) => ReconciliationTestData.Entry(
        id,
        ReconciliationDirection.EnterprisePaid,
        amount,
        "咨询费",
        row) with
    {
        ReferenceNumber = reference,
        Debit = 0m,
        Credit = amount
    };

    private static ReconciliationInputData Input(
        IReadOnlyList<ReconciliationEntry> enterprise,
        IReadOnlyList<ReconciliationEntry> bank) => new()
    {
        EnterpriseEntries = enterprise,
        BankEntries = bank
    };
}
