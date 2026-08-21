using MyAvaloniaManagement.PluginSdk;

namespace DaTangAccountingHelpPlug.Constants;

/// <summary>集中保存 DaTang 会计辅助插件在 Host V2 中唯一有效的稳定身份。</summary>
/// <remarks>
/// V2 不读取旧布局或旧 Document 信封，因此这里不保留 GUID 或历史别名。清单身份与
/// 两个 Document 类型身份分别拥有独立语义，Host 会在不可变贡献目录发布前做全局冲突校验。
/// </remarks>
public static class DaTangContributionIds
{
    /// <summary>获取插件清单声明的规范身份。</summary>
    public static PluginId Plugin { get; } = new("myavalonia.plugin.datang-accounting-help");

    /// <summary>获取发票信息导入 Document 的稳定身份。</summary>
    public static DocumentTypeId InvoiceInfoImportDocument { get; } =
        new("myavalonia.plugin.datang-accounting-help.document.invoice-info-import");

    /// <summary>获取银行余额调节 Document 的稳定身份。</summary>
    public static DocumentTypeId BankBalanceReconciliationDocument { get; } =
        new("myavalonia.plugin.datang-accounting-help.document.bank-balance-reconciliation");
}
