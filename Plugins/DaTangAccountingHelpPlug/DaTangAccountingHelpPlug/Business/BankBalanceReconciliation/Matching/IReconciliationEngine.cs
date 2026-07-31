using DaTangAccountingHelpPlug.Models.BankBalanceReconciliation;

namespace DaTangAccountingHelpPlug.Business.BankBalanceReconciliation.Matching;

public interface IReconciliationEngine
{
    ReconciliationResult Reconcile(
        ReconciliationRequest request,
        ReconciliationInputData input,
        CancellationToken cancellationToken = default);
}
