using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Plugin;

namespace DaTangAccountingHelpPlug.Constants;

public static class SaveDocumentTypeIdConstant
{
    public static readonly PluginId PluginId = new("myavalonia.plugin.datang-accounting-help");
    public static readonly DocumentTypeId InvoiceInfoImportDocument =
        new("myavalonia.plugin.datang-accounting-help.document.invoice-info-import");
    public static readonly DocumentTypeId LegacyInvoiceInfoImportDocument =
        new("D8525F12-F58B-F95D-1B4B-62EE33CF128D");
    public static readonly DocumentTypeId BankBalanceReconciliationDocument =
        new("myavalonia.plugin.datang-accounting-help.document.bank-balance-reconciliation");
    public static readonly DocumentTypeId LegacyBankBalanceReconciliationDocument =
        new("9D0ACD63-6C35-4CC8-87B1-E9B3C91E1C18");
}
