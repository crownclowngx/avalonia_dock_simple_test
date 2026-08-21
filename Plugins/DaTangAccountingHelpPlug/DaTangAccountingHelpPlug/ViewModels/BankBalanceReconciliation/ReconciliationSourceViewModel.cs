using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DaTangAccountingHelpPlug.Business;
using DaTangAccountingHelpPlug.Models.BankBalanceReconciliation;
using MyAvaloniaManagement.PluginSdk;

namespace DaTangAccountingHelpPlug.ViewModels.BankBalanceReconciliation;

/// <summary>维护当前 Document 的来源文件和银行 Profile 选择。</summary>
public partial class ReconciliationSourceViewModel : ObservableObject
{
    private readonly IReconciliationFileDialogService _fileDialogs;
    private readonly IDocumentLifetime _documentLifetime;

    /// <summary>创建只属于当前银行对账 Document Scope 的来源选择模型。</summary>
    public ReconciliationSourceViewModel(
        IReconciliationFileDialogService fileDialogs,
        IDocumentLifetime documentLifetime)
    {
        _fileDialogs = fileDialogs ?? throw new ArgumentNullException(nameof(fileDialogs));
        _documentLifetime = documentLifetime ?? throw new ArgumentNullException(nameof(documentLifetime));
    }

    public ObservableCollection<BankReconciliationProfile> Profiles { get; } = [];

    [ObservableProperty] private BankReconciliationProfile? _selectedProfile;
    [ObservableProperty] private string _enterpriseLedgerPath = string.Empty;
    [ObservableProperty] private string _bankStatementPath = string.Empty;
    [ObservableProperty] private string _receiptEnrichmentPath = string.Empty;
    [ObservableProperty] private DateTimeOffset? _asOfDate = DateTimeOffset.Now.Date;

    public void ApplyProfiles(
        IEnumerable<BankReconciliationProfile> profiles,
        string? selectedProfileId = null)
    {
        Profiles.Clear();
        foreach (var profile in profiles)
            Profiles.Add(profile);
        SelectedProfile = Profiles.FirstOrDefault(profile =>
                              profile.Id.Equals(selectedProfileId, StringComparison.OrdinalIgnoreCase))
                          ?? Profiles.FirstOrDefault();
    }

    [RelayCommand]
    private Task SelectEnterpriseLedgerAsync(CancellationToken cancellationToken) =>
        SelectWorkbookAsync("选择企业明细账", path => EnterpriseLedgerPath = path, cancellationToken);

    [RelayCommand]
    private Task SelectBankStatementAsync(CancellationToken cancellationToken) =>
        SelectWorkbookAsync("选择银行账", path => BankStatementPath = path, cancellationToken);

    [RelayCommand]
    private Task SelectReceiptEnrichmentAsync(CancellationToken cancellationToken) =>
        SelectWorkbookAsync("选择到款导出表（可选）", path => ReceiptEnrichmentPath = path, cancellationToken);

    [RelayCommand]
    private void ClearReceiptEnrichment() => ReceiptEnrichmentPath = string.Empty;

    private async Task SelectWorkbookAsync(
        string title,
        Action<string> commit,
        CancellationToken commandToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            commandToken,
            _documentLifetime.ClosingToken);
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            var path = await _fileDialogs.PickSourceWorkbookAsync(title, linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(path) && !_documentLifetime.IsClosing)
            {
                commit(path);
            }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            // 系统选择器可能在 Document 关闭后才返回；取消路径必须保持原属性不变。
        }
    }
}
