using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagementCommon.ToolCreation;
using MyPlugTest.ViewModels;

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
        _viewModel.Id = "MyCustomTool";
        _viewModel.Title = "我的自定义工具";
        _viewModel.CanClose = true;
        return _viewModel;
    }
    
    public ToolMetadata GetMetadata()
    {
        return new ToolMetadata
        {
            ToolTypeId = "MyCustomTool",
            DisplayName = "我的自定义工具",
            Description = "这是一个通过插件系统加载的自定义工具",
            IconPath = "", // 当前示例没有自定义图标，保留空路径即可。
            Alignment = "Right" // 保持历史行为：工具固定显示在右侧面板。
        };
    }
}
