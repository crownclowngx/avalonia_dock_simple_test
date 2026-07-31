using DaTangAccountingHelpPlug.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagementCommon.Plugin;

namespace DaTangAccountingHelpPlug.Plugin;

/// <summary>
/// DaTang 会计辅助插件接入宿主依赖注入容器的模块入口。
/// </summary>
/// <remarks>
/// 插件当前没有后台任务或插件级资源，因此只注册文档 ViewModel，
/// 不注册没有实际职责的 <see cref="IPluginLifecycle"/>。
/// </remarks>
public sealed class DaTangAccountingHelpPluginModule : IPluginModule
{
    public string PluginId => "DaTangAccountingHelpPlug";

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services)
    {
        // 每次创建文档都应得到独立状态，避免不同发票计算窗口共享路径、日志或计算结果。
        services.AddTransient<InvoiceInfoImportViewModel>();
    }
}
