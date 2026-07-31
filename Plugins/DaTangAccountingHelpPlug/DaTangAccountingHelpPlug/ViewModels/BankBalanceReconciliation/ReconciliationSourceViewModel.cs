using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DaTangAccountingHelpPlug.Models.BankBalanceReconciliation;

namespace DaTangAccountingHelpPlug.ViewModels.BankBalanceReconciliation;

/// <summary>维护当前 Document 的来源文件和银行 Profile 选择。</summary>
public partial class ReconciliationSourceViewModel : ObservableObject
{
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
    private async Task SelectEnterpriseLedgerAsync() =>
        EnterpriseLedgerPath = await PickWorkbookAsync("选择企业明细账") ?? EnterpriseLedgerPath;

    [RelayCommand]
    private async Task SelectBankStatementAsync() =>
        BankStatementPath = await PickWorkbookAsync("选择银行账") ?? BankStatementPath;

    [RelayCommand]
    private async Task SelectReceiptEnrichmentAsync() =>
        ReceiptEnrichmentPath = await PickWorkbookAsync("选择到款导出表（可选）") ?? ReceiptEnrichmentPath;

    [RelayCommand]
    private void ClearReceiptEnrichment() => ReceiptEnrichmentPath = string.Empty;

    private static async Task<string?> PickWorkbookAsync(string title)
    {
        var window = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (window is null)
            return null;
        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Excel/CSV 文件") { Patterns = ["*.xlsx", "*.xlsm", "*.csv"] }
            ]
        });
        return files.Count == 0 ? null : files[0].Path.LocalPath;
    }
}
