using DaTangAccountingHelpPlug.Models.BankBalanceReconciliation;

namespace DaTangAccountingHelpPlug.Business.BankBalanceReconciliation.Reporting;

public interface IReconciliationReportWriter
{
    Task WriteAsync(
        ReconciliationResult result,
        IProgress<ReconciliationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
