using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagementCommon.ToolCreation;
using BiliDownloader.Services.Download;
using BiliDownloader.ViewModels;

namespace BiliDownloader.Create;

public class BiliSchedulerToolStrategy : IToolCreationStrategy
{
    private readonly BiliSchedulerToolViewModel _viewModel;

    public BiliSchedulerToolStrategy(BiliSchedulerToolViewModel viewModel)
    {
        _viewModel = viewModel;
    }

    public Tool CreateTool()
    {
        // Tool 由宿主保证只创建一次；这里返回 DI 中的单例 ViewModel，
        // 隐藏和恢复 Tool 时不会创建新的 Coordinator 或任务队列。
        _viewModel.Id = "BiliSchedulerTool";
        _viewModel.Title = "Bilibili调度工具";
        _viewModel.CanClose = true;
        return _viewModel;
    }

    public ToolMetadata GetMetadata()
    {
        return new ToolMetadata
        {
            ToolTypeId = "BiliSchedulerTool",
            DisplayName = "Bilibili调度工具",
            Description = "下载调度与ffmpeg处理管理",
            IconPath = "",
            Alignment = "Right"
        };
    }
}
