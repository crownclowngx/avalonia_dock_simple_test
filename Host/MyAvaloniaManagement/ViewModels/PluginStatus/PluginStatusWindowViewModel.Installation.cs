using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyAvaloniaManagement.Business.Plugins.Installation;

namespace MyAvaloniaManagement.ViewModels.PluginStatus;

/// <summary>安装交互只传递明确意图。服务拥有后台检查和磁盘提交，窗口关闭后忽略迟到展示即可。</summary>
internal sealed partial class PluginStatusWindowViewModel
{
    private readonly IPluginInstallationActions? _installation;
    private string? _selectedZip;
    [ObservableProperty] private bool _isInstallationBusy;
    [ObservableProperty] private bool _installationExpanded;
    [ObservableProperty] private bool _allowPackageReplacement;
    [ObservableProperty] private string _installationFeedback = string.Empty;

    public bool HasInstallation => _installation is not null;
    public bool CanInspectPackage => !_disposed && !IsInstallationBusy && _installation is not null &&
        _restart?.IsRequested != true && _canOperate();
    public bool CanChoosePackageManifest => CanInspectPackage && _selectedZip is not null;
    public bool CanCommitPackage => CanInspectPackage && _installation?.Preview is { } preview &&
        preview.Plan.Action != PluginInstallAction.Unchanged && (!preview.Plan.RequiresExplicitChoice || AllowPackageReplacement);
    public bool CanCancelPendingInstall => CanInspectPackage && _installation?.Status.CanCancel == true;
    public bool CanRestoreSelectedPackage => CanInspectPackage && SelectedItem is not null;
    public bool RequiresPackageReplacement => _installation?.Preview?.Plan.RequiresExplicitChoice == true;
    public string InstallationStatusText => _installation?.Status.Message ?? string.Empty;
    public string PackagePreviewText => _installation?.Preview?.Summary ?? "选择 ZIP 后先检查；确认安装不会改变本次运行版本。";
    public string PackageActionText => _installation?.Preview?.Plan.Action switch
    {
        PluginInstallAction.Install => "确认安装（重启生效）", PluginInstallAction.Upgrade => "确认更新（重启生效）",
        PluginInstallAction.Reinstall => "确认重新安装", PluginInstallAction.Downgrade => "确认降级",
        PluginInstallAction.Restore => "确认恢复上一版本", PluginInstallAction.Unchanged => "已安装相同内容", _ => "确认安装"
    };

    partial void OnIsInstallationBusyChanged(bool value) => NotifyInstallationChanged();
    partial void OnAllowPackageReplacementChanged(bool value) => NotifyInstallationChanged();

    internal Task InspectPackageAsync(string zip, string? sidecar = null)
    {
        if (!CanInspectPackage) return Task.CompletedTask;
        _selectedZip = zip; AllowPackageReplacement = false; InstallationExpanded = true;
        return RunInstallationAsync(() => _installation!.InspectAsync(zip, sidecar), "检查完成，请审阅候选信息。");
    }

    internal Task SelectPackageManifestAsync(string manifest) => _selectedZip is null
        ? Task.CompletedTask : InspectPackageAsync(_selectedZip, manifest);

    [RelayCommand(CanExecute = nameof(CanChoosePackageManifest))]
    private Task RecheckZipOnlyAsync() => InspectPackageAsync(_selectedZip!, string.Empty);

    [RelayCommand(CanExecute = nameof(CanCommitPackage))]
    private Task CommitPackageAsync() => RunInstallationAsync(() => _installation!.CommitAsync(AllowPackageReplacement), "已保存待应用操作，重启后生效。");

    [RelayCommand(CanExecute = nameof(CanCancelPendingInstall))]
    private Task CancelPendingInstallAsync() => RunInstallationAsync(() => _installation!.CancelPendingAsync(), "待应用操作已取消。");

    [RelayCommand(CanExecute = nameof(CanRestoreSelectedPackage))]
    private Task RestoreSelectedPackageAsync()
    {
        var id = SelectedItem!.PluginId;
        AllowPackageReplacement = false; InstallationExpanded = true;
        return RunInstallationAsync(() => _installation!.PrepareRestoreAsync(id), "请确认恢复版本；业务数据不会自动回退。");
    }

    [RelayCommand]
    private void CancelPackageInspection() => _installation?.CancelInspection();

    [RelayCommand(CanExecute = nameof(CanInspectPackage))]
    public Task RefreshInstallationAsync() => _installation is null ? Task.CompletedTask :
        RunInstallationAsync(() => _installation.RefreshAsync(), string.Empty);

    private async Task RunInstallationAsync(Func<Task> operation, string success)
    {
        if (!CanInspectPackage) return;
        IsInstallationBusy = true; InstallationFeedback = "正在处理安装包…";
        try
        {
            await operation();
            if (!_disposed) InstallationFeedback = success;
        }
        catch (OperationCanceledException) { if (!_disposed) InstallationFeedback = "检查已取消，当前插件未改变。"; }
        catch (PluginInstallException exception) { if (!_disposed) InstallationFeedback = exception.Message; }
        catch (Exception) { if (!_disposed) InstallationFeedback = "安装操作未完成，请核对包格式、配套清单、权限及磁盘内容后刷新状态。"; }
        finally { if (!_disposed) { IsInstallationBusy = false; NotifyInstallationChanged(); } }
    }

    internal void NotifyInstallationChanged()
    {
        if (_disposed) return;
        foreach (var name in new[] { nameof(HasInstallation), nameof(CanInspectPackage), nameof(CanChoosePackageManifest),
                     nameof(CanCommitPackage), nameof(CanCancelPendingInstall), nameof(CanRestoreSelectedPackage),
                     nameof(RequiresPackageReplacement), nameof(InstallationStatusText), nameof(PackagePreviewText), nameof(PackageActionText) })
            OnPropertyChanged(name);
        CommitPackageCommand.NotifyCanExecuteChanged(); CancelPendingInstallCommand.NotifyCanExecuteChanged();
        RestoreSelectedPackageCommand.NotifyCanExecuteChanged(); RefreshInstallationCommand.NotifyCanExecuteChanged();
        RecheckZipOnlyCommand.NotifyCanExecuteChanged(); NotifyRestartChanged();
    }
}
