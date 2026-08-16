using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagementCommon.ToolCreation;
using BiliDownloader.Services.Download;
using BiliDownloader.ViewModels;
using BiliDownloader.Constants;

namespace BiliDownloader.Create;

public class BiliSchedulerToolStrategy : IToolCreationStrategy
{
    private readonly Func<BiliSchedulerToolViewModel> _viewModelFactory;
    private BiliSchedulerToolViewModel? _viewModel;

    public BiliSchedulerToolStrategy(Func<BiliSchedulerToolViewModel> viewModelFactory)
    {
        _viewModelFactory = viewModelFactory ??
            throw new ArgumentNullException(nameof(viewModelFactory));
    }

    public Tool CreateTool()
    {
        // Tool 由宿主保证只创建一次；这里返回 DI 中的单例 ViewModel，
        // 隐藏和恢复 Tool 时不会创建新的 Coordinator 或任务队列。
        // Registry 构建时只创建策略，不提前解析依赖 PluginLifecycleManager 的 Tool ViewModel。
        // 首次布局创建发生在 Registry 完整发布后；缓存保证隐藏和恢复仍使用同一 Tool 实例。
        var viewModel = _viewModel ??= _viewModelFactory();
        viewModel.Title = "Bilibili调度工具";
        viewModel.CanClose = true;
        return viewModel;
    }

    public ToolMetadata GetMetadata()
    {
        return new ToolMetadata(
            SaveDocumentTypeIdConstant.SchedulerToolId,
            "Bilibili调度工具",
            ToolDockSide.Right,
            [SaveDocumentTypeIdConstant.LegacySchedulerToolId])
        {
            Description = "下载调度与ffmpeg处理管理",
            IconPath = ""
        };
    }
}
