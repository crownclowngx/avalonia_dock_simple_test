namespace DaTangAccountingHelpPlug.Models.BankBalanceReconciliation;

public sealed class ReconciliationConfiguration
{
    public int SchemaVersion { get; set; } = 1;
    public List<EnterpriseLedgerLayout> EnterpriseLayouts { get; set; } = [];
    public List<BankReconciliationProfile> BankProfiles { get; set; } = [];
    public List<CounterpartyNormalizationRule> NormalizationRules { get; set; } = [];
    public List<ReferenceAggregationRule> ReferenceAggregationRules { get; set; } = [];
    public List<AggregationRule> AggregationRules { get; set; } = [];
}

/// <summary>企业账第一张工作表的列位契约，列号均为从 1 开始。</summary>
public sealed class EnterpriseLedgerLayout
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int StartRow { get; set; }
    public int DateColumn { get; set; }
    public int ReferenceColumn { get; set; }
    public int SummaryColumn { get; set; }
    public int DebitColumn { get; set; }
    public int CreditColumn { get; set; }
    public int MarkerColumn { get; set; }
    public int BalanceColumn { get; set; }
    public int BalanceDirectionColumn { get; set; }
    public int BalanceTrailingRowOffset { get; set; }
    public string VerifyUnitCell { get; set; } = string.Empty;
    public string VerifyAccountCell { get; set; } = string.Empty;
}

/// <summary>单位与银行账户对应的读取配置。</summary>
public sealed class BankReconciliationProfile
{
    public string Id { get; set; } = string.Empty;
    public string UnitShortName { get; set; } = string.Empty;
    public string UnitName { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string BankShortName { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;
    /// <summary>来源于 DZ Main 的“方向”列：1 按源行升序处理，2 按源行降序处理。</summary>
    public int DirectionMode { get; set; } = 1;
    public string EnterpriseLayoutId { get; set; } = string.Empty;
    public int StartRow { get; set; }
    public int DateColumn { get; set; }
    public int CounterpartyColumn { get; set; }
    public int SummaryColumn { get; set; }
    public int DebitColumn { get; set; }
    public int CreditColumn { get; set; }
    public int MarkerColumn { get; set; }
    public int BalanceColumn { get; set; }
    public bool BalanceFromLastRow { get; set; }
    public int BalanceTrailingRowOffset { get; set; }
    public int CounterpartyAccountColumn { get; set; }
    public int ReceiptEnrichmentColumn { get; set; }
    public int AccountSuffixLength { get; set; }
    public string VerifyUnitCell { get; set; } = string.Empty;
    public string VerifyAccountCell { get; set; } = string.Empty;
    public string NormalizationSelector { get; set; } = string.Empty;
    public string CleanupMode { get; set; } = string.Empty;
}

/// <summary>将银行户名和摘要映射为企业账中可检索的候选名称。</summary>
public sealed class CounterpartyNormalizationRule
{
    public string Id { get; set; } = string.Empty;
    public string BankSummaryContains { get; set; } = string.Empty;
    public string BankCounterpartyContains { get; set; } = string.Empty;
    public List<string> CandidateNames { get; set; } = [];
    public string ReorderPrefix { get; set; } = string.Empty;
    public int ReorderPrefixLength { get; set; }
    public List<string> AggregationKeywords { get; set; } = [];
}

/// <summary>只描述确定的领域汇总规则，不承载通用组合求和。</summary>
public sealed class AggregationRule
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public ReconciliationDirection BankDirection { get; set; }
    public List<string> BankKeywords { get; set; } = [];
    public List<string> EnterpriseKeywords { get; set; } = [];
}

/// <summary>按银行摘要中的业务编号汇总，并关联企业凭证号。</summary>
public sealed class ReferenceAggregationRule
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public List<string> ApplicableProfileIds { get; set; } = [];
    public ReconciliationDirection BankDirection { get; set; }
    public string BankSummaryKeyword { get; set; } = string.Empty;
    public List<string> EnterpriseReferencePrefixes { get; set; } = [];
}
