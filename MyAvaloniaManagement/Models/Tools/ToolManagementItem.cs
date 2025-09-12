using CommunityToolkit.Mvvm.ComponentModel;

namespace MyAvaloniaManagement.Models.Tools;

/// <summary>
/// 工具管理项类，用于绑定列表数据
/// </summary>
public partial class ToolManagementItem : ObservableObject
{
    [ObservableProperty]
    private string _toolId = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private bool _isVisible = true;

    [ObservableProperty]
    private bool _canClose = true;
}