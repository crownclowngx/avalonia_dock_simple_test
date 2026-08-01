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

    [Theory]
    [InlineData("冲销记账-00344(202607)测试客户")]
    [InlineData("冲销记帐-00344（202607）测试客户")]
    public void 冲销凭证兼容记账字形和中英文括号并唯一定位原记录(string reversalSummary)
    {
        var original = OriginalEntry("E-original", 104580m, "记账-00344", "原始摘要", 57);
        var sameAmountDecoy = OriginalEntry("E-decoy", 104580m, "记账-00999", "原始摘要", 58);
        var reversal = ReversalEntry("E-reversal", 104580m, reversalSummary, 186);

        var result = _engine.Reconcile(
            ReversalRequest(),
            Input([original, sameAmountDecoy, reversal], []));

        var exclusions = result.Decisions
            .Where(item => item.Status == MatchDecisionStatus.Excluded)
            .ToArray();
        Assert.Equal(2, exclusions.Length);
        Assert.All(exclusions, item => Assert.Equal("enterprise-reversal-reference", item.RuleId));
        Assert.Equal("E-reversal", exclusions.Single(item => item.PrimaryEntry.EntryId == "E-original").MatchedEntry?.EntryId);
        Assert.Equal("E-original", exclusions.Single(item => item.PrimaryEntry.EntryId == "E-reversal").MatchedEntry?.EntryId);
        Assert.Contains("原凭证号 记账-00344", exclusions[0].Reason);
        Assert.Equal(MatchDecisionStatus.Unmatched,
            result.Decisions.Single(item => item.PrimaryEntry.EntryId == "E-decoy").Status);
    }

    [Fact]
    public void 冲销摘要没有凭证号时按去除配置前缀后的摘要唯一配对()
    {
        var request = ReversalRequest(reorderPrefixLength: 13);
        var original = OriginalEntry("E-original", 100m, "记账-00001", "测试摘要", 2);
        var reversal = ReversalEntry("E-reversal", 100m, "冲销记账-（202607）测试摘要", 3);

        var result = _engine.Reconcile(request, Input([original, reversal], []));

        Assert.Equal(2, result.Decisions.Count(item => item.Status == MatchDecisionStatus.Excluded));
        Assert.All(result.Decisions, item => Assert.Equal("enterprise-reversal-summary", item.RuleId));
    }

    [Fact]
    public void 冲销找不到内部候选时按修正后的真实方向继续匹配银行流水()
    {
        var reversal = ReversalEntry("E-reversal", 100m, "冲销记账-99999(202607)客户甲", 3);
        var bank = ReconciliationTestData.Entry(
            "B1", ReconciliationDirection.BankReceived, 100m, "客户甲", 2);

        var result = _engine.Reconcile(
            ReversalRequest(),
            Input([reversal], [bank]));

        var bankDecision = Assert.Single(result.Decisions);
        Assert.Equal(MatchDecisionStatus.Matched, bankDecision.Status);
        Assert.Equal("E-reversal", bankDecision.MatchedEntry?.EntryId);
    }

    [Fact]
    public void 同凭证同金额存在多个原记录时冲销进入待复核且不按顺序猜测()
    {
        var first = OriginalEntry("E-first", 100m, "记账-00344", "候选一", 2);
        var second = OriginalEntry("E-second", 100m, "记帐-00344", "候选二", 3);
        var reversal = ReversalEntry("E-reversal", 100m, "冲销记账-00344(202607)测试", 4);

        var result = _engine.Reconcile(
            ReversalRequest(),
            Input([first, second, reversal], []));

        var ambiguous = result.Decisions.Single(item => item.PrimaryEntry.EntryId == "E-reversal");
        Assert.Equal(MatchDecisionStatus.Ambiguous, ambiguous.Status);
        Assert.Equal("enterprise-reversal-ambiguous", ambiguous.RuleId);
        Assert.Equal(2, ambiguous.CandidateCount);
        Assert.DoesNotContain(result.Decisions, item => item.Status == MatchDecisionStatus.Excluded);
        Assert.Equal(MatchDecisionStatus.Unmatched,
            result.Decisions.Single(item => item.PrimaryEntry.EntryId == "E-first").Status);
        Assert.Equal(MatchDecisionStatus.Unmatched,
            result.Decisions.Single(item => item.PrimaryEntry.EntryId == "E-second").Status);
    }

    [Theory]
    [InlineData(ReconciliationMode.Strict)]
    [InlineData(ReconciliationMode.LegacyCompatible)]
    public void 八组冲销在严格和兼容模式均不参与余额调节(ReconciliationMode mode)
    {
        var cases = new (string Reference, decimal Amount)[]
        {
            ("00344", 104580m),
            ("06227", 60623m),
            ("01528", 140423m),
            ("06460", 360111m),
            ("08130", 1300000m),
            ("08182", 40666m),
            ("09558", 80323m),
            ("09524", 23323m)
        };
        var enterprise = cases.SelectMany((item, index) => new[]
        {
            OriginalEntry($"E-original-{item.Reference}", item.Amount, $"记账-{item.Reference}", $"脱敏摘要-{index}", index + 2),
            ReversalEntry($"E-reversal-{item.Reference}", item.Amount, $"冲销记帐-{item.Reference}（202607）脱敏摘要-{index}", index + 102)
        }).ToArray();

        var result = _engine.Reconcile(
            ReversalRequest(mode),
            Input(enterprise, [], 124138731.26m, 124138731.26m));

        Assert.Equal(2110049m, enterprise.Where(item => item.Credit < 0m).Sum(item => Math.Abs(item.Credit)));
        Assert.Equal(16, result.Decisions.Count(item => item.Status == MatchDecisionStatus.Excluded));
        Assert.Equal(0, result.MatchedCount);
        Assert.Equal(0m, result.Difference);
        Assert.True(result.IsBalanced);
    }

    private static ReconciliationRequest ReversalRequest(
        ReconciliationMode mode = ReconciliationMode.Strict,
        int reorderPrefixLength = 18)
    {
        var request = ReconciliationTestData.Request(mode);
        request.Configuration.NormalizationRules.Add(new CounterpartyNormalizationRule
        {
            Id = "reversal",
            BankSummaryContains = "不触发普通名称规则",
            CandidateNames = ["测试候选"],
            ReorderPrefix = "冲销记帐-",
            ReorderPrefixLength = reorderPrefixLength
        });
        return request;
    }

    private static ReconciliationEntry OriginalEntry(
        string id,
        decimal amount,
        string referenceNumber,
        string summary,
        int row) => ReconciliationTestData.Entry(
            id,
            ReconciliationDirection.EnterprisePaid,
            amount,
            summary,
            row) with
        {
            ReferenceNumber = referenceNumber,
            Debit = 0m,
            Credit = amount
        };

    private static ReconciliationEntry ReversalEntry(
        string id,
        decimal amount,
        string summary,
        int row) => ReconciliationTestData.Entry(
            id,
            ReconciliationDirection.EnterpriseReceived,
            amount,
            summary,
            row) with
        {
            Debit = 0m,
            Credit = -amount
        };

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
