using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagementCommon.Plugin;
using MyPlugTest.Services;
using MyPlugTest.ViewModels;

namespace MyPlugTest.Plugin;

/// <summary>
/// MyPlugTest 选择接入宿主依赖注入的模块入口。
/// <para>
/// 本插件用于展示最小但完整的 Managed Plugin 组合方式：宿主拥有唯一的 Tool，
/// 每次用户创建 Document 时得到新的 ViewModel，并直接复用宿主提供的消息总线。
/// 插件没有数据库、后台队列或其他需要在启动和退出时管理的资源，因此这里只实现
/// <see cref="IPluginModule"/>，不注册没有实际职责的 <c>IPluginLifecycle</c>。
/// </para>
/// </summary>
public sealed class MyPlugTestPluginModule : IPluginModule
{
    public string PluginId => "MyPlugTest";

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services)
    {
        // Tool 在宿主中只存在一个实例，因此其 ViewModel 使用 Singleton。
        // Tool 被隐藏再恢复时仍返回同一对象，不会丢失界面状态或重复创建资源。
        services.AddSingleton<MyCustomToolViewModel>();

        // Document 必须在用户每次执行“新建”时创建独立实例。
        // UrlHistoryViewModel 同样使用 Transient，确保不同欢迎 Document 的历史记录互不串扰。
        services.AddTransient<TestWelcomeViewModel>();
        services.AddTransient<TestMessageReceiveViewModel>();
        services.AddTransient<UrlHistoryViewModel>();

        // URL 请求服务本身不保存单个 Document 的可变状态，可以作为插件级 Singleton 复用。
        // IMessengerService 由宿主注册，本模块刻意不重复注册，保证发送方和接收方共享同一消息事实源。
        services.AddSingleton<IUrlContentService, FlurlUrlContentService>();
    }
}
