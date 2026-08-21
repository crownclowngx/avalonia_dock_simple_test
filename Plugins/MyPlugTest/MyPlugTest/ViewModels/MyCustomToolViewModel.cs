using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MyPlugTest.ViewModels;

/// <summary>展示可编辑属性与操作结果的普通 Tool 模型。</summary>
/// <remarks>
/// 本类型不认识 Dock，也不拥有标题、关闭或停靠状态。Host 根据模块中的不可变 ToolDescriptor
/// 创建唯一 Dock Adapter；模型本身由插件 Provider 以 singleton 方式持有。
/// </remarks>
public partial class MyCustomToolViewModel : ObservableObject
{
    [ObservableProperty]
    private string _customProperty = "默认值";

    [ObservableProperty]
    private string _statusMessage = "等待修改";

    [ObservableProperty]
    private bool _isStatusSuccess;

    [RelayCommand]
    private void UpdateProperty()
    {
        StatusMessage = $"属性已更新为：{CustomProperty}";
        IsStatusSuccess = true;
    }

    [RelayCommand]
    private void ResetProperty()
    {
        CustomProperty = "默认值";
        StatusMessage = "属性已重置";
        IsStatusSuccess = false;
    }
}
