using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Model.Mvvm.Controls;
using CommunityToolkit.Mvvm.Input;

namespace MyPlugTest.ViewModels;

public partial class MyCustomToolViewModel : Tool
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
