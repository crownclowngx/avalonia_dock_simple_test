using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagementCommon.ToolCreation;
using MyPlugTest.ViewModels;

namespace MyPlugTest.Models;

public class MyCustomToolStrategy : IToolCreationStrategy
{
    public Tool CreateTool()
    {
        return new MyCustomToolViewModel()
        {
            Id = "MyCustomTool",
            Title = "我的自定义工具",
            CanClose = true
        };
    }
    
    public ToolMetadata GetMetadata()
    {
        return new ToolMetadata
        {
            ToolTypeId = "MyCustomTool",
            DisplayName = "我的自定义工具",
            Description = "这是一个通过插件系统加载的自定义工具",
            IconPath = "", // 可选的图标路径
            Alignment = "Right" // 工具将显示在右侧面板
        };
    }
}