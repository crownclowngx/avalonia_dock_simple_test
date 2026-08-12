using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagementCommon.ToolCreation;
using MyPlugTest.ViewModels;
using MyPlugTest.Constants;

namespace MyPlugTest.Models;

public class MyCustomToolStrategy : IToolCreationStrategy
{
    private readonly MyCustomToolViewModel _viewModel;

    public MyCustomToolStrategy(MyCustomToolViewModel viewModel)
    {
        _viewModel = viewModel;
    }

    public Tool CreateTool()
    {
        // Tool 由宿主创建一次；返回模块中注册的 Singleton，确保隐藏和恢复时仍是同一实例。
        _viewModel.Title = "我的自定义工具";
        _viewModel.CanClose = true;
        return _viewModel;
    }
    
    public ToolMetadata GetMetadata()
    {
        return new ToolMetadata(
            SaveDocumentTypeIdConstant.CustomToolId,
            "我的自定义工具",
            ToolDockSide.Right,
            [SaveDocumentTypeIdConstant.LegacyCustomToolId])
        {
            Description = "这是一个通过插件系统加载的自定义工具",
            IconPath = ""
        };
    }
}
