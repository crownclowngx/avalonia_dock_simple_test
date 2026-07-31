using DaTangAccountingHelpPlug.Business.BankBalanceReconciliation.Profiles;
using DaTangAccountingHelpPlug.Models.BankBalanceReconciliation;
using Xunit;

namespace DaTangAccountingHelpPlug.Tests.BankBalanceReconciliation;

public sealed class ReconciliationProfileTests
{
    [Fact]
    public void 内置配置完整迁移DZ中的布局账户和归一化规则()
    {
        var configuration = new ReconciliationProfileLoader().LoadDefault();

        Assert.Equal(1, configuration.SchemaVersion);
        Assert.Equal(4, configuration.EnterpriseLayouts.Count);
        Assert.Equal(35, configuration.BankProfiles.Count);
        Assert.Equal(61, configuration.NormalizationRules.Count);
        Assert.Contains(configuration.BankProfiles, profile => profile.DirectionMode == 1);
        Assert.Contains(configuration.BankProfiles, profile => profile.DirectionMode == 2);
        Assert.All(configuration.BankProfiles, profile =>
            Assert.True(profile.DirectionMode is 1 or 2));
        Assert.All(configuration.BankProfiles, profile =>
            Assert.Contains(configuration.EnterpriseLayouts, layout => layout.Id == profile.EnterpriseLayoutId));
    }

    [Fact]
    public void 配置整包校验拒绝重复标识()
    {
        var configuration = ReconciliationTestData.Configuration();
        configuration.BankProfiles.Add(ReconciliationTestData.Profile());

        var exception = Assert.Throws<InvalidDataException>(() =>
            new ReconciliationProfileLoader().Validate(configuration));

        Assert.Contains("重复", exception.Message);
    }

    [Fact]
    public void 配置整包校验拒绝空关键字规则()
    {
        var configuration = ReconciliationTestData.Configuration();
        configuration.NormalizationRules.Add(new CounterpartyNormalizationRule
        {
            Id = "empty-rule",
            CandidateNames = ["候选"]
        });

        var exception = Assert.Throws<InvalidDataException>(() =>
            new ReconciliationProfileLoader().Validate(configuration));

        Assert.Contains("匹配条件", exception.Message);
    }
}
