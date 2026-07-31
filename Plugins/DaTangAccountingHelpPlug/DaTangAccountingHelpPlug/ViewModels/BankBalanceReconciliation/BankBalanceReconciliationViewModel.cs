using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using DaTangAccountingHelpPlug.Constants;
using DaTangAccountingHelpPlug.Models.BankBalanceReconciliation;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagementCommon.Save;

namespace DaTangAccountingHelpPlug.ViewModels.BankBalanceReconciliation;

/// <summary>银行余额调节 Document 的组合外壳。</summary>
public sealed class BankBalanceReconciliationViewModel : Document, ISavableDocument, IDisposable
{
    private bool _disposed;

    public ReconciliationSourceViewModel Source { get; }
    public ReconciliationOptionsViewModel Options { get; }
    public ReconciliationRunViewModel Run { get; }

    public string FilePath { get; set; } = string.Empty;
    public string SaveDocumentTypeId => SaveDocumentTypeIdConstant.BankBalanceReconciliationDocument;

    public BankBalanceReconciliationViewModel(
        ReconciliationSourceViewModel source,
        ReconciliationOptionsViewModel options,
        ReconciliationRunViewModel run)
    {
        Source = source;
        Options = options;
        Run = run;
        Title = "银行余额调节表";
    }

    public DocumentSaveData CreateSaveDocumentMetaData(string filePath)
    {
        var state = new SavedState
        {
            Configuration = Options.Configuration,
            SelectedProfileId = Source.SelectedProfile?.Id ?? string.Empty,
            EnterpriseLedgerPath = Source.EnterpriseLedgerPath,
            BankStatementPath = Source.BankStatementPath,
            ReceiptEnrichmentPath = Source.ReceiptEnrichmentPath,
            AsOfDate = Source.AsOfDate,
            UseLegacyMode = Options.UseLegacyMode,
            EnableLooseAmountAlignment = Options.EnableLooseAmountAlignment,
            PreviousUnreconciledDifference = Options.PreviousUnreconciledDifference,
            LastOutputPath = Run.LastOutputPath
        };
        FilePath = filePath;
        IsModified = false;
        return new DocumentSaveData
        {
            DocumentTypeId = SaveDocumentTypeId,
            Title = Title,
            SaveTime = DateTime.Now,
            Content = JsonSerializer.Serialize(state),
            PluginMetadata = JsonSerializer.Serialize(new { Version = 1 })
        };
    }

    public void LoadDocumentByMetaData(DocumentSaveData saveData)
    {
        var state = JsonSerializer.Deserialize<SavedState>(saveData.Content);
        if (state is null)
            return;
        if (state.Configuration is not null)
            Options.ApplyConfiguration(state.Configuration, state.SelectedProfileId);
        Source.EnterpriseLedgerPath = state.EnterpriseLedgerPath;
        Source.BankStatementPath = state.BankStatementPath;
        Source.ReceiptEnrichmentPath = state.ReceiptEnrichmentPath;
        Source.AsOfDate = state.AsOfDate;
        Options.UseLegacyMode = state.UseLegacyMode;
        Options.EnableLooseAmountAlignment = state.EnableLooseAmountAlignment;
        Options.PreviousUnreconciledDifference = state.PreviousUnreconciledDifference;
        Run.LastOutputPath = state.LastOutputPath;
        Title = string.IsNullOrWhiteSpace(saveData.Title) ? "银行余额调节表" : saveData.Title;
        IsModified = false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Run.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class SavedState
    {
        public ReconciliationConfiguration? Configuration { get; set; }
        public string SelectedProfileId { get; set; } = string.Empty;
        public string EnterpriseLedgerPath { get; set; } = string.Empty;
        public string BankStatementPath { get; set; } = string.Empty;
        public string ReceiptEnrichmentPath { get; set; } = string.Empty;
        public DateTimeOffset? AsOfDate { get; set; }
        public bool UseLegacyMode { get; set; }
        public bool EnableLooseAmountAlignment { get; set; }
        public decimal PreviousUnreconciledDifference { get; set; }
        public string LastOutputPath { get; set; } = string.Empty;
    }
}
