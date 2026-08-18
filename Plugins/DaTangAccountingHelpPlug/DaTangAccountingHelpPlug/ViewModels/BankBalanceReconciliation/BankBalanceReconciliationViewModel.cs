using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using DaTangAccountingHelpPlug.Constants;
using DaTangAccountingHelpPlug.Models.BankBalanceReconciliation;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagementCommon.Save;
using MyAvaloniaManagementCommon.DocumentCreation;

namespace DaTangAccountingHelpPlug.ViewModels.BankBalanceReconciliation;

/// <summary>银行余额调节 Document 的组合外壳。</summary>
public sealed class BankBalanceReconciliationViewModel : Document, ISavableDocument, IDocumentSaveState, IDisposable
{
    private const int CurrentContentSchemaVersion = 1;
    private bool _disposed;
    private bool _isRestoring;

    public ReconciliationSourceViewModel Source { get; }
    public ReconciliationOptionsViewModel Options { get; }
    public ReconciliationRunViewModel Run { get; }

    public string FilePath { get; set; } = string.Empty;
    public DocumentTypeId SaveDocumentTypeId => SaveDocumentTypeIdConstant.BankBalanceReconciliationDocument;
    public bool IsDirty => IsModified;

    public BankBalanceReconciliationViewModel(
        ReconciliationSourceViewModel source,
        ReconciliationOptionsViewModel options,
        ReconciliationRunViewModel run)
    {
        Source = source;
        Options = options;
        Run = run;
        Title = "银行余额调节表";

        // 只观察会进入 SavedState 的字段。运行进度、提示和审计投影不属于 Document
        // 持久状态，不能因为一次执行过程就制造无法解释的关闭提示。
        Source.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(Source.SelectedProfile)
                or nameof(Source.EnterpriseLedgerPath)
                or nameof(Source.BankStatementPath)
                or nameof(Source.ReceiptEnrichmentPath)
                or nameof(Source.AsOfDate))
            {
                MarkDirty();
            }
        };
        Options.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(Options.Configuration)
                or nameof(Options.UseLegacyMode)
                or nameof(Options.EnableLooseAmountAlignment)
                or nameof(Options.PreviousUnreconciledDifference))
            {
                MarkDirty();
            }
        };
        Run.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(Run.LastOutputPath))
            {
                MarkDirty();
            }
        };
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
        // 内容 schema 属于插件；信封身份、标题和 UTC 时间由宿主从 Registry 与目标路径生成。
        return new DocumentSaveData(
            CurrentContentSchemaVersion,
            JsonSerializer.Serialize(state));
    }

    public void LoadDocumentByMetaData(DocumentSaveData saveData)
    {
        ArgumentNullException.ThrowIfNull(saveData);
        if (saveData.ContentSchemaVersion != CurrentContentSchemaVersion)
        {
            throw new DocumentLoadException("银行余额调节文档内容版本不受支持。");
        }

        _isRestoring = true;
        try
        {
            SavedState state;
            try
            {
                state = JsonSerializer.Deserialize<SavedState>(saveData.Payload)
                    ?? throw new DocumentLoadException("银行余额调节文档内容为空。");
            }
            catch (JsonException exception)
            {
                throw new DocumentLoadException(
                    "银行余额调节文档结构损坏或包含无效字段。",
                    exception);
            }

            if (state.Configuration is null)
            {
                throw new DocumentLoadException("银行余额调节文档缺少配置数据。");
            }

            Options.ApplyConfiguration(state.Configuration, state.SelectedProfileId);
            Source.EnterpriseLedgerPath = state.EnterpriseLedgerPath;
            Source.BankStatementPath = state.BankStatementPath;
            Source.ReceiptEnrichmentPath = state.ReceiptEnrichmentPath;
            Source.AsOfDate = state.AsOfDate;
            Options.UseLegacyMode = state.UseLegacyMode;
            Options.EnableLooseAmountAlignment = state.EnableLooseAmountAlignment;
            Options.PreviousUnreconciledDifference = state.PreviousUnreconciledDifference;
            Run.LastOutputPath = state.LastOutputPath;
            IsModified = false;
        }
        finally
        {
            _isRestoring = false;
        }
    }

    public void AcceptChanges() => IsModified = false;

    private void MarkDirty()
    {
        if (!_isRestoring && !_disposed)
        {
            IsModified = true;
        }
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
