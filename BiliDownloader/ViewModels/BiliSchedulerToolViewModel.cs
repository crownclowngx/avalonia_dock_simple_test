using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagementCommon.Message;

namespace BiliDownloader.ViewModels;

public partial class BiliSchedulerToolViewModel : Tool
{
    private readonly IMessengerService _messengerService;

    [ObservableProperty]
    private string _schedulerStatus = "调度器就绪";

    public BiliSchedulerToolViewModel()
    {
        _messengerService = new MessengerService();
        // TODO: 注册消息总线监听，等待 Document 发送下载任务
    }
}
