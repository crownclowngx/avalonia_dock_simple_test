using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DaTangAccountingHelpPlug.Business.BankBalanceReconciliation.Profiles;
using MyAvaloniaManagement.PluginSdk;

namespace DaTangAccountingHelpPlug.ViewModels.BankBalanceReconciliation;

/// <summary>组合银行余额调节界面状态并实现带修订确认的可持久化 Document 契约。</summary>
/// <remarks>
/// 本模型不继承 Dock，也不拥有文件路径或原子保存事务。它只聚合当前 Document Scope 的三个子模型，
/// 观察确实会进入内容 payload 的字段，并把严格编解码委托给独立 Codec。Host 在保存主文件成功后
/// 才调用 <see cref="AcceptChanges"/>，因此内容捕获失败不会错误清除脏状态。
/// </remarks>
public sealed class BankBalanceReconciliationViewModel :
    ObservableObject,
    IPersistablePluginDocument,
    IDisposable
{
    private readonly IDocumentLifetime _documentLifetime;
    private readonly ReconciliationProfileLoader _profileLoader;
    private readonly object _revisionLock = new();
    private bool _disposed;
    private bool _isRestoring;
    private long _contentRevision;
    private long _acceptedRevision;
    private string _title = "银行余额调节表";

    /// <summary>创建一个由当前插件 Document Scope 独占的组合模型。</summary>
    public BankBalanceReconciliationViewModel(
        ReconciliationSourceViewModel source,
        ReconciliationOptionsViewModel options,
        ReconciliationRunViewModel run,
        IDocumentLifetime documentLifetime,
        ReconciliationProfileLoader profileLoader)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Options = options ?? throw new ArgumentNullException(nameof(options));
        Run = run ?? throw new ArgumentNullException(nameof(run));
        _documentLifetime = documentLifetime ?? throw new ArgumentNullException(nameof(documentLifetime));
        _profileLoader = profileLoader ?? throw new ArgumentNullException(nameof(profileLoader));

        Source.PropertyChanged += OnSourcePropertyChanged;
        Options.PropertyChanged += OnOptionsPropertyChanged;
        Run.PropertyChanged += OnRunPropertyChanged;
    }

    /// <summary>获取当前 Document 的来源文件和账户选择状态。</summary>
    public ReconciliationSourceViewModel Source { get; }

    /// <summary>获取当前 Document 的配置与匹配选项状态。</summary>
    public ReconciliationOptionsViewModel Options { get; }

    /// <summary>获取当前 Document 的运行、结果与审计状态。</summary>
    public ReconciliationRunViewModel Run { get; }

    /// <inheritdoc />
    public DocumentPresentationState Presentation => new(_title);

    /// <inheritdoc />
    public event EventHandler? PresentationChanged;

    /// <inheritdoc />
    public bool IsDirty
    {
        get
        {
            lock (_revisionLock)
            {
                return _contentRevision != _acceptedRevision;
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler? IsDirtyChanged;

    /// <inheritdoc />
    public ValueTask InitializeAsync(
        DocumentActivation activation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(activation);
        cancellationToken.ThrowIfCancellationRequested();
        _documentLifetime.ClosingToken.ThrowIfCancellationRequested();

        // 先把全部 payload 解码到独立临时状态；结构或业务校验失败发生在修改模型之前。
        var restoredState = activation switch
        {
            NewDocumentActivation => null,
            RestoreDocumentActivation restore =>
                ReconciliationDocumentContentCodec.Decode(restore.RestoredContent, _profileLoader),
            _ => throw new NotSupportedException("银行余额调节表收到未知 Document 激活类型。"),
        };

        if (restoredState is not null)
        {
            ApplyRestoredState(restoredState);
        }
        else
        {
            ResetRevisionState();
        }

        SetPresentationTitle(string.IsNullOrWhiteSpace(activation.Title)
            ? "银行余额调节表"
            : activation.Title);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<DocumentSaveSnapshot> CaptureSaveSnapshotAsync(
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _documentLifetime.ClosingToken.ThrowIfCancellationRequested();
            var revisionBeforeCapture = ReadCurrentRevision();

            var state = new ReconciliationDocumentState(
                Options.Configuration,
                Source.SelectedProfile?.Id ?? string.Empty,
                Source.EnterpriseLedgerPath,
                Source.BankStatementPath,
                Source.ReceiptEnrichmentPath,
                Source.AsOfDate,
                Options.UseLegacyMode,
                Options.EnableLooseAmountAlignment,
                Options.PreviousUnreconciledDifference,
                Run.LastOutputPath);
            var content = ReconciliationDocumentContentCodec.Encode(state);
            var revisionAfterCapture = ReadCurrentRevision();
            if (revisionBeforeCapture == revisionAfterCapture)
            {
                return ValueTask.FromResult(
                    new DocumentSaveSnapshot(revisionAfterCapture, content));
            }

            // 子模型可能从后台完成 Excel 或文件选择操作。只要持久字段在编码期间推进过
            // Revision，就放弃本轮 DTO 并重试，避免把不同观察时刻的字段错误拼成可确认快照。
        }
    }

    /// <inheritdoc />
    public void AcceptChanges(DocumentRevision savedRevision)
    {
        var dirtyChanged = false;
        lock (_revisionLock)
        {
            if (_contentRevision != savedRevision.Value)
            {
                return;
            }

            dirtyChanged = _acceptedRevision != _contentRevision;
            _acceptedRevision = _contentRevision;
        }

        if (dirtyChanged)
        {
            RaiseDirtyChanged();
        }
    }

    private void ApplyRestoredState(ReconciliationDocumentState state)
    {
        _isRestoring = true;
        try
        {
            Options.ApplyConfiguration(state.Configuration, state.SelectedProfileId);
            Source.EnterpriseLedgerPath = state.EnterpriseLedgerPath;
            Source.BankStatementPath = state.BankStatementPath;
            Source.ReceiptEnrichmentPath = state.ReceiptEnrichmentPath;
            Source.AsOfDate = state.AsOfDate;
            Options.UseLegacyMode = state.UseLegacyMode;
            Options.EnableLooseAmountAlignment = state.EnableLooseAmountAlignment;
            Options.PreviousUnreconciledDifference = state.PreviousUnreconciledDifference;
            Run.LastOutputPath = state.LastOutputPath;
            ResetRevisionState();
        }
        finally
        {
            _isRestoring = false;
        }
    }

    private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(Source.SelectedProfile)
            or nameof(Source.EnterpriseLedgerPath)
            or nameof(Source.BankStatementPath)
            or nameof(Source.ReceiptEnrichmentPath)
            or nameof(Source.AsOfDate))
        {
            MarkDirty();
        }
    }

    private void OnOptionsPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(Options.Configuration)
            or nameof(Options.UseLegacyMode)
            or nameof(Options.EnableLooseAmountAlignment)
            or nameof(Options.PreviousUnreconciledDifference))
        {
            MarkDirty();
        }
    }

    private void OnRunPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(Run.LastOutputPath))
        {
            MarkDirty();
        }
    }

    private void MarkDirty()
    {
        if (!_isRestoring && !_disposed && !_documentLifetime.IsClosing)
        {
            var dirtyChanged = false;
            lock (_revisionLock)
            {
                var wasDirty = _contentRevision != _acceptedRevision;
                _contentRevision = checked(_contentRevision + 1);
                dirtyChanged = !wasDirty;
            }

            if (dirtyChanged)
            {
                RaiseDirtyChanged();
            }
        }
    }

    private DocumentRevision ReadCurrentRevision()
    {
        lock (_revisionLock)
        {
            return new DocumentRevision(_contentRevision);
        }
    }

    private void ResetRevisionState()
    {
        var dirtyChanged = false;
        lock (_revisionLock)
        {
            dirtyChanged = _contentRevision != _acceptedRevision;
            _acceptedRevision = _contentRevision;
        }

        if (dirtyChanged)
        {
            RaiseDirtyChanged();
        }
    }

    private void RaiseDirtyChanged()
    {
        OnPropertyChanged(nameof(IsDirty));
        IsDirtyChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetPresentationTitle(string title)
    {
        if (string.Equals(_title, title, StringComparison.Ordinal))
        {
            return;
        }

        _title = title;
        PresentationChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>解除父模型拥有的观察关系并取消当前 Scope 的运行任务；重复释放保持幂等。</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Source.PropertyChanged -= OnSourcePropertyChanged;
        Options.PropertyChanged -= OnOptionsPropertyChanged;
        Run.PropertyChanged -= OnRunPropertyChanged;
        Run.Dispose();
    }
}
