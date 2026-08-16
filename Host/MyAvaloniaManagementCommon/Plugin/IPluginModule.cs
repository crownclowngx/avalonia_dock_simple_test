namespace MyAvaloniaManagementCommon.Plugin;

/// <summary>
/// Managed Plugin 的唯一组合入口。
/// </summary>
/// <remarks>
/// 宿主只会创建已经通过严格清单预检的模块，并为它提供绑定当前清单身份的
/// <see cref="IPluginRegistrationContext"/>。模块不能自行声明插件身份，也不能在宿主启动后
/// 再追加贡献；这使一次启动中的服务和扩展集合能够在 UI 出现前完整校验并冻结。
/// </remarks>
public interface IPluginModule
{
    /// <summary>
    /// 注册插件私有服务以及宿主可见的显式贡献。
    /// </summary>
    /// <param name="context">
    /// 由宿主创建的一次性注册上下文。其 <see cref="IPluginRegistrationContext.PluginId"/>
    /// 来自已经验证的 <c>plugin.manifest.json</c>，是本次注册的唯一身份事实。
    /// </param>
    void Configure(IPluginRegistrationContext context);
}
