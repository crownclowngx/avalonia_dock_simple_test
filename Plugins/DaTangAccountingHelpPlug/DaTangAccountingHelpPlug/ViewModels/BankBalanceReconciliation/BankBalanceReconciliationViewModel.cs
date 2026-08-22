using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DaTangAccountingHelpPlug.Business.BankBalanceReconciliation.Profiles;
using MyAvaloniaManagement.PluginSdk;

namespace DaTangAccountingHelpPlug.ViewModels.BankBalanceReconciliation;

/// <summary>组合银行余额调节界面状态并实现 Host V2 可持久化 Document 契约。</summary>
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
    private bool _disposed;
    private bool _isRestoring;
    private bool _isDirty;
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
    public bool IsDirty => _isDirty;

    /// <inheritdoc />
    public event EventHandler? IsDirtyChanged;

    /// <inheritdoc />
    public ValueTask InitializeAsync(
        DocumentActivationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        _documentLifetime.ClosingToken.ThrowIfCancellationRequested();

        // 先把全部 payload 解码到独立临时状态；结构或业务校验失败发生在修改模型之前。
        var restoredState = context.RestoredContent is null
            ? null
            : ReconciliationDocumentContentCodec.Decode(context.RestoredContent, _profileLoader);

        if (restoredState is not null)
        {
            ApplyRestoredState(restoredState);
        }
        else
        {
            SetDirty(false);
        }

        SetPresentationTitle(string.IsNullOrWhiteSpace(context.Title)
            ? "银行余额调节表"
            : context.Title);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<DocumentContent> CaptureContentAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _documentLifetime.ClosingToken.ThrowIfCancellationRequested();

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
        return ValueTask.FromResult(ReconciliationDocumentContentCodec.Encode(state));
    }

    /// <inheritdoc />
    public void AcceptChanges() => SetDirty(false);

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
            SetDirty(false);
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
            SetDirty(true);
        }
    }

    private void SetDirty(bool value)
    {
        if (!SetProperty(ref _isDirty, value, nameof(IsDirty)))
        {
            return;
        }

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
