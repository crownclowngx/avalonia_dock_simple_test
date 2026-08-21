using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DaTangAccountingHelpPlug.Business;
using DaTangAccountingHelpPlug.Business.BankBalanceReconciliation.Profiles;
using DaTangAccountingHelpPlug.Models.BankBalanceReconciliation;
using MyAvaloniaManagement.PluginSdk;

namespace DaTangAccountingHelpPlug.ViewModels.BankBalanceReconciliation;

/// <summary>维护匹配策略及当前 Document 的可导入配置快照。</summary>
public partial class ReconciliationOptionsViewModel : ObservableObject
{
    private readonly ReconciliationProfileLoader _profileLoader;
    private readonly ReconciliationSourceViewModel _source;
    private readonly IReconciliationFileDialogService _fileDialogs;
    private readonly IDocumentLifetime _documentLifetime;

    [ObservableProperty] private bool _useLegacyMode;
    [ObservableProperty] private bool _enableLooseAmountAlignment;
    [ObservableProperty] private decimal _previousUnreconciledDifference;
    [ObservableProperty] private string _configurationStatus = "正在加载内置配置";

    public ReconciliationConfiguration Configuration { get; private set; }

    public ReconciliationOptionsViewModel(
        ReconciliationProfileLoader profileLoader,
        ReconciliationSourceViewModel source,
        IReconciliationFileDialogService fileDialogs,
        IDocumentLifetime documentLifetime)
    {
        _profileLoader = profileLoader ?? throw new ArgumentNullException(nameof(profileLoader));
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _fileDialogs = fileDialogs ?? throw new ArgumentNullException(nameof(fileDialogs));
        _documentLifetime = documentLifetime ?? throw new ArgumentNullException(nameof(documentLifetime));
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
    private async Task ImportConfigurationAsync(CancellationToken commandToken)
    {
        using var linked = CreateLinkedTokenSource(commandToken);
        try
        {
            var path = await _fileDialogs.PickConfigurationImportAsync(linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(path) || _documentLifetime.IsClosing)
                return;

            var selectedId = _source.SelectedProfile?.Id;
            var imported = await _profileLoader.ImportAsync(path, linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            if (_documentLifetime.IsClosing)
                return;
            ApplyConfiguration(imported, selectedId);
            ConfigurationStatus = $"已导入：{Path.GetFileName(path)}";
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            // 关闭或命令取消时不提交迟到配置，也不覆盖原状态文字。
        }
    }

    [RelayCommand]
    private async Task ExportConfigurationAsync(CancellationToken commandToken)
    {
        using var linked = CreateLinkedTokenSource(commandToken);
        try
        {
            var path = await _fileDialogs.PickConfigurationExportAsync(linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(path) || _documentLifetime.IsClosing)
                return;
            await _profileLoader.ExportAsync(Configuration, path, linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            if (!_documentLifetime.IsClosing)
                ConfigurationStatus = $"已导出：{Path.GetFileName(path)}";
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            // 取消是正常控制流；不把 OperationCanceledException 显示为配置错误。
        }
    }

    private CancellationTokenSource CreateLinkedTokenSource(CancellationToken commandToken) =>
        CancellationTokenSource.CreateLinkedTokenSource(
            commandToken,
            _documentLifetime.ClosingToken);

    partial void OnUseLegacyModeChanged(bool value)
    {
        if (!value)
            EnableLooseAmountAlignment = false;
    }
}
