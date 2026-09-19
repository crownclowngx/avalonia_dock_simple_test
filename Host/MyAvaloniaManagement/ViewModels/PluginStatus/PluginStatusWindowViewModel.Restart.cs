using System;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using MyAvaloniaManagement.Business.Restart;

namespace MyAvaloniaManagement.ViewModels.PluginStatus;

/// <summary>看板仅展示已保存的待生效事实，重启用例与主菜单共用；搜索/选中项不缩小应用范围。</summary>
internal sealed partial class PluginStatusWindowViewModel
{
    private readonly IHostRestartActions? _restart;
    public bool HasPendingRestart => _items.Any(item => item.RequiresRestart);
    public bool CanRestart => !_disposed && !IsSavingEnablement && HasPendingRestart && _restart?.CanRequest == true;
    public string RestartMessage => _restart?.Message ?? string.Empty;

    [RelayCommand(CanExecute = nameof(CanRestart))]
    private void Restart() => _restart?.Request();

    private void RestartChanged(object? sender, EventArgs args) => NotifyEnablementChanged();
    private void NotifyRestartChanged()
    {
        if (_disposed) return;
        OnPropertyChanged(nameof(HasPendingRestart));
        OnPropertyChanged(nameof(CanRestart));
        OnPropertyChanged(nameof(RestartMessage));
        RestartCommand.NotifyCanExecuteChanged();
    }
}
