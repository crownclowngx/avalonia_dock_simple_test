using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DaTangAccountingHelpPlug.Business.BankBalanceReconciliation.Profiles;
using DaTangAccountingHelpPlug.Models.BankBalanceReconciliation;

namespace DaTangAccountingHelpPlug.ViewModels.BankBalanceReconciliation;

/// <summary>维护匹配策略及当前 Document 的可导入配置快照。</summary>
public partial class ReconciliationOptionsViewModel : ObservableObject
{
    private readonly ReconciliationProfileLoader _profileLoader;
    private readonly ReconciliationSourceViewModel _source;

    [ObservableProperty] private bool _useLegacyMode;
    [ObservableProperty] private bool _enableLooseAmountAlignment;
    [ObservableProperty] private decimal _previousUnreconciledDifference;
    [ObservableProperty] private string _configurationStatus = "正在加载内置配置";

    public ReconciliationConfiguration Configuration { get; private set; }

    public ReconciliationOptionsViewModel(
        ReconciliationProfileLoader profileLoader,
        ReconciliationSourceViewModel source)
    {
        _profileLoader = profileLoader;
        _source = source;
        Configuration = profileLoader.LoadDefault();
        ApplyConfiguration(Configuration);
    }

    public void ApplyConfiguration(
        ReconciliationConfiguration configuration,
        string? selectedProfileId = null)
    {
        _profileLoader.Validate(configuration);
        Configuration = configuration;
        OnPropertyChanged(nameof(Configuration));
        _source.ApplyProfiles(configuration.BankProfiles, selectedProfileId);
        ConfigurationStatus = $"配置版本 {configuration.SchemaVersion}：{configuration.BankProfiles.Count} 个银行账户";
    }

    [RelayCommand]
    private async Task ImportConfigurationAsync()
    {
        var window = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (window is null)
            return;
        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "导入银行余额调节配置",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("JSON 配置") { Patterns = ["*.json"] }]
        });
        if (files.Count == 0)
            return;

        var selectedId = _source.SelectedProfile?.Id;
        var imported = await _profileLoader.ImportAsync(files[0].Path.LocalPath);
        ApplyConfiguration(imported, selectedId);
        ConfigurationStatus = $"已导入：{Path.GetFileName(files[0].Path.LocalPath)}";
    }

    [RelayCommand]
    private async Task ExportConfigurationAsync()
    {
        var window = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (window is null)
            return;
        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出银行余额调节配置",
            SuggestedFileName = "reconciliation-profiles.json",
            DefaultExtension = "json",
            FileTypeChoices = [new FilePickerFileType("JSON 配置") { Patterns = ["*.json"] }]
        });
        if (file is null)
            return;
        await _profileLoader.ExportAsync(Configuration, file.Path.LocalPath);
        ConfigurationStatus = $"已导出：{Path.GetFileName(file.Path.LocalPath)}";
    }

    partial void OnUseLegacyModeChanged(bool value)
    {
        if (!value)
            EnableLooseAmountAlignment = false;
    }
}
