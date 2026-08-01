using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DaTangAccountingHelpPlug.Business.BankBalanceReconciliation;
using DaTangAccountingHelpPlug.Models.BankBalanceReconciliation;

namespace DaTangAccountingHelpPlug.ViewModels.BankBalanceReconciliation;

/// <summary>拥有单个 Document 的运行、取消、日志和结果状态。</summary>
public partial class ReconciliationRunViewModel : ObservableObject, IDisposable
{
    private readonly BankBalanceReconciliationService _service;
    private readonly ReconciliationSourceViewModel _source;
    private readonly ReconciliationOptionsViewModel _options;
    private CancellationTokenSource? _cancellation;
    private bool _disposed;

    public ObservableCollection<string> LogEntries { get; } = [];
    public ObservableCollection<ReconciliationIssueViewModel> AuditIssues { get; } = [];
    public bool HasAuditIssues => AuditIssues.Count > 0;

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private int _progressPercent;
    [ObservableProperty] private string _statusMessage = "请选择配置和输入文件";
    [ObservableProperty] private string _resultMessage = string.Empty;
    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private bool _isBalanced;
    [ObservableProperty] private decimal _difference;
    [ObservableProperty] private int _matchedCount;
    [ObservableProperty] private int _reviewIssueCount;
    [ObservableProperty] private int _ambiguousCount;
    [ObservableProperty] private string _lastOutputPath = string.Empty;

    public ReconciliationRunViewModel(
        BankBalanceReconciliationService service,
        ReconciliationSourceViewModel source,
        ReconciliationOptionsViewModel options)
    {
        _service = service;
        _source = source;
        _options = options;
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        var outputPath = await PickOutputPathAsync();
        if (outputPath is not null)
            await RunAsync(outputPath);
    }

    public async Task<ReconciliationRunSummary?> RunAsync(
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsRunning)
            return null;
        var profile = _source.SelectedProfile
                      ?? throw new InvalidOperationException("请选择银行账户配置。");
        var layout = _options.Configuration.EnterpriseLayouts.FirstOrDefault(item =>
                         item.Id.Equals(profile.EnterpriseLayoutId, StringComparison.OrdinalIgnoreCase))
                     ?? throw new InvalidOperationException("银行配置引用的企业账布局不存在。");

        var request = new ReconciliationRequest
        {
            Profile = profile,
            EnterpriseLayout = layout,
            EnterpriseLedgerPath = _source.EnterpriseLedgerPath,
            BankStatementPath = _source.BankStatementPath,
            ReceiptEnrichmentPath = _source.ReceiptEnrichmentPath,
            OutputPath = outputPath,
            AsOfDate = _source.AsOfDate?.Date ?? DateTime.Today,
            PreviousUnreconciledDifference = _options.PreviousUnreconciledDifference,
            Mode = _options.UseLegacyMode ? ReconciliationMode.LegacyCompatible : ReconciliationMode.Strict,
            EnableLooseAmountAlignment = _options.UseLegacyMode && _options.EnableLooseAmountAlignment,
            Configuration = _options.Configuration
        };

        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var previous = Interlocked.Exchange(ref _cancellation, linked);
        previous?.Cancel();
        previous?.Dispose();
        IsRunning = true;
        HasResult = false;
        ResultMessage = string.Empty;
        LogEntries.Clear();
        AuditIssues.Clear();
        OnPropertyChanged(nameof(HasAuditIssues));
        StartCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        try
        {
            var progress = new Progress<ReconciliationProgress>(item =>
            {
                ProgressPercent = Math.Clamp(item.Percent, 0, 100);
                StatusMessage = item.Message;
                LogEntries.Add($"[{DateTime.Now:HH:mm:ss}] {item.Stage}：{item.Message}");
            });
            var result = await _service.ExecuteAsync(request, progress, linked.Token);
            IsBalanced = result.IsBalanced;
            Difference = result.Difference;
            MatchedCount = result.MatchedCount;
            ReviewIssueCount = result.ReviewIssueCount;
            AmbiguousCount = result.AmbiguousCount;
            foreach (var issue in ReconciliationIssueViewModel.Create(result.Decisions))
                AuditIssues.Add(issue);
            OnPropertyChanged(nameof(HasAuditIssues));
            LastOutputPath = outputPath;
            HasResult = true;
            ResultMessage = result.IsBalanced
                ? $"对账已平，复核 {result.ReviewIssueCount} 组，歧义 {result.AmbiguousCount} 条"
                : $"对账不平，差额 {result.Difference:N2}，复核 {result.ReviewIssueCount} 组，歧义 {result.AmbiguousCount} 条";
            foreach (var warning in result.Input.Warnings)
                LogEntries.Add($"[警告] {warning}");
            return new ReconciliationRunSummary(
                result.IsBalanced,
                result.AdjustedEnterpriseBalance,
                result.AdjustedBankBalance,
                result.Difference,
                result.MatchedCount,
                result.AmbiguousCount,
                outputPath);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            StatusMessage = "已取消，本次没有替换输出文件";
            LogEntries.Add($"[{DateTime.Now:HH:mm:ss}] 用户取消对账");
            return null;
        }
        catch (Exception exception)
        {
            StatusMessage = $"处理失败：{exception.Message}";
            LogEntries.Add($"[{DateTime.Now:HH:mm:ss}] 错误：{exception.Message}");
            return null;
        }
        finally
        {
            Interlocked.CompareExchange(ref _cancellation, null, linked);
            linked.Dispose();
            IsRunning = false;
            StartCommand.NotifyCanExecuteChanged();
            CancelCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _cancellation?.Cancel();

    private bool CanStart() => !_disposed && !IsRunning;
    private bool CanCancel() => !_disposed && IsRunning;

    private async Task<string?> PickOutputPathAsync()
    {
        var window = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (window is null)
            return null;
        var profile = _source.SelectedProfile;
        var suffixLength = Math.Max(profile?.AccountSuffixLength ?? 4, 0);
        var account = profile?.AccountNumber ?? string.Empty;
        var suffix = suffixLength == 0 || account.Length == 0
            ? string.Empty
            : account[^Math.Min(suffixLength, account.Length)..];
        var suggested = $"银行余额调节表({profile?.UnitShortName}{_source.AsOfDate:yyMMdd}{profile?.BankShortName}{suffix}).xlsx";
        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "保存银行余额调节表",
            SuggestedFileName = suggested,
            DefaultExtension = "xlsx",
            FileTypeChoices = [new FilePickerFileType("Excel 工作簿") { Patterns = ["*.xlsx"] }]
        });
        return file?.Path.LocalPath;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        var cancellation = Interlocked.Exchange(ref _cancellation, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
        GC.SuppressFinalize(this);
    }
}
