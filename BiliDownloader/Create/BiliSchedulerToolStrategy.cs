using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagementCommon.ToolCreation;
using BiliDownloader.Services.Download;
using BiliDownloader.ViewModels;

namespace BiliDownloader.Create;

public class BiliSchedulerToolStrategy : IToolCreationStrategy
{
    public Tool CreateTool()
    {
        return new BiliSchedulerToolViewModel(BiliDownloadCoordinator.Instance)
        {
            Id = "BiliSchedulerTool",
            Title = "Bilibili调度工具",
            CanClose = true,
        };
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
