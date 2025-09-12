using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Model.Mvvm.Controls;

namespace MyPlugTest.ViewModels;

public partial class MyCustomToolViewModel : Tool
{
    [ObservableProperty]
    private string _customProperty = "默认值";
    
    public MyCustomToolViewModel()
    {
        Id = "myCustomTool";
        Title = "我的自定义工具";
        CanClose = false;
    }
}