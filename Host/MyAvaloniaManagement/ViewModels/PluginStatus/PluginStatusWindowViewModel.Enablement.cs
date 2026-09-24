using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyAvaloniaManagement.Business.Plugins.Enablement;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.ViewModels.PluginStatus;

/// <summary>只组织开关交互，文件提交及已保存意图归服务所有；窗口不接触加载器。</summary>
internal sealed partial class PluginStatusWindowViewModel
{
    private readonly IPluginEnablementActions? _enablement;
    private readonly Func<bool> _canOperate;
    [ObservableProperty] private bool _isSavingEnablement;
    [ObservableProperty] private string _enablementFeedback = string.Empty;
    public bool? SelectedNextStartupEnabled => SelectedItem?.NextStartupEnabled;
    public string EnablementNotice => _enablement?.Current.Message ?? string.Empty;
    public bool HasEnablementNotice => EnablementNotice.Length > 0;
    public bool HasEnablementFeedback => EnablementFeedback.Length > 0;
    partial void OnEnablementFeedbackChanged(string value) => OnPropertyChanged(nameof(HasEnablementFeedback));
    public bool CanChangeEnablement => !_disposed && !IsSavingEnablement && _restart?.IsRequested != true && _canOperate() &&
        _enablement?.Current.CanWrite == true && SelectedItem?.CanSetEnablement == true;
    public bool CanReloadEnablement => !_disposed && !IsSavingEnablement && _restart?.IsRequested != true && _canOperate() && _enablement is not null;
    public string EnablementUnavailableReason => IsSavingEnablement ? "正在保存设置…" :
        _restart?.IsRequested == true ? "正在准备重启。" :
        !_canOperate() ? "工作区尚未就绪或正在退出。" : HasEnablementNotice ? EnablementNotice :
        SelectedItem?.CanSetEnablement != true ? "该候选没有可操作的插件身份或设置服务。" : string.Empty;

    partial void OnIsSavingEnablementChanged(bool value) => NotifyEnablementChanged();

    /// <summary>先捕获目标，再等待持久化；期间选中项变化不改变保存目标，关窗不撤销已提交设置。</summary>
    [RelayCommand(CanExecute = nameof(CanChangeEnablement))]
    public async Task ToggleEnablementAsync()
    {
        if (!CanChangeEnablement || SelectedItem is not { } selected ||
            !PluginId.TryParse(selected.PluginId, out var id)) return;
        var enabled = selected.NextStartupEnabled != true;
        IsSavingEnablement = true;
        try
        {
            var result = await _enablement!.SetEnabledAsync(id, enabled);
            if (_disposed) return;
            EnablementFeedback = result.Success
                ? $"{id.Value}：已保存为下次{(enabled ? "启用" : "禁用")}，重启 Host 后生效。"
                : result.Message;
            Refresh();
        }
        catch (Exception)
        {
            if (!_disposed) EnablementFeedback = "保存操作未完成，请重新读取设置以确认结果后重试。";
        }
        finally
        {
            if (!_disposed) { IsSavingEnablement = false; NotifyEnablementChanged(); }
        }
    }

    /// <summary>用户明确要求时读取其他实例的新选择；普通刷新只读内存，始终不改变启动快照。</summary>
    [RelayCommand(CanExecute = nameof(CanReloadEnablement))]
    public async Task ReloadEnablementAsync()
    {
        if (!CanReloadEnablement) return;
        IsSavingEnablement = true;
        try
        {
            await _enablement!.ReloadAsync();
            if (_disposed) return;
            EnablementFeedback = "已重新读取下次启动设置；本次插件运行状态保持不变。";
            Refresh();
        }
        catch (Exception) { if (!_disposed) EnablementFeedback = "重新读取设置失败，请检查配置后重试。"; }
        finally { if (!_disposed) IsSavingEnablement = false; }
    }

    /// <summary>设置和工作区状态变化后重新发布绑定；失败时也发布 IsChecked，以恢复用户点击前的真实意图。</summary>
    internal void NotifyEnablementChanged()
    {
        if (_disposed) return;
        NotifyRestartChanged();
        NotifyInstallationChanged();
        OnPropertyChanged(nameof(SelectedNextStartupEnabled));
        OnPropertyChanged(nameof(EnablementNotice));
        OnPropertyChanged(nameof(HasEnablementNotice));
        OnPropertyChanged(nameof(CanChangeEnablement));
        OnPropertyChanged(nameof(CanReloadEnablement));
        OnPropertyChanged(nameof(EnablementUnavailableReason));
        ToggleEnablementCommand.NotifyCanExecuteChanged();
        ReloadEnablementCommand.NotifyCanExecuteChanged();
    }
}
