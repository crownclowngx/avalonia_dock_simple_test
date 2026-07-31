using DaTangAccountingHelpPlug.Models.BankBalanceReconciliation;

namespace DaTangAccountingHelpPlug.Business.BankBalanceReconciliation.Reading;

public interface IReconciliationWorkbookReader
{
    Task<ReconciliationInputData> ReadAsync(
        ReconciliationRequest request,
        IProgress<ReconciliationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
