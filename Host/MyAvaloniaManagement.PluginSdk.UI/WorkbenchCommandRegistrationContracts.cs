using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.PluginSdk.UI;

/// <summary>定义 Workbench Command 专用的可选注册能力。</summary>
/// <remarks>
/// 本接口与已经发布的 <see cref="IPluginRegistration"/> 分离，使旧插件不声明 Command 时继续按
/// 原有路径加载。Host 的 internal 注册对象同时实现本接口，新插件通过扩展方法显式要求该能力。
/// </remarks>
public interface IWorkbenchCommandRegistration
{
    /// <summary>声明一条由当前活动 Document 实例执行的工作台命令。</summary>
    /// <param name="descriptor">命令稳定身份与展示元数据。</param>
    /// <param name="targetDocumentTypeId">必须由当前插件声明的目标 Document 类型。</param>
    /// <remarks>
    /// 注册只冻结描述符和目标身份，不创建 Target、Handler、Scope 或回调。实际实例路由由后续
    /// Host Runtime 完成，因此声明顺序不要求目标 Document 已经先写入注册入口。
    /// </remarks>
    void AddDocumentCommand(
        CommandDescriptor descriptor,
        DocumentTypeId targetDocumentTypeId);

    /// <summary>声明一条命令在 Host 共享菜单末端位置中的展示。</summary>
    /// <param name="descriptor">只包含位置、排序和展示政策的不可变描述符。</param>
    void AddMenuCommandContribution(MenuCommandContributionDescriptor descriptor);

    /// <summary>声明一条命令使用的 Avalonia 键盘 Gesture。</summary>
    /// <param name="descriptor">只包含键枚举、修饰键和命令身份的不可变描述符。</param>
    void AddKeyBindingContribution(KeyBindingContributionDescriptor descriptor);
}

/// <summary>为现有插件注册入口提供兼容新增的 Workbench Command 声明语法。</summary>
public static class WorkbenchCommandRegistrationExtensions
{
    private const string UnsupportedMessage =
        "当前 Host 不支持 Workbench Command；需要 Plugin SDK/Host 3.3.0 或更高版本。";

    /// <summary>通过 Host 的可选能力声明一条 Document Command。</summary>
    /// <param name="registration">当前 manifest 身份绑定的插件注册入口。</param>
    /// <param name="descriptor">命令描述符。</param>
    /// <param name="targetDocumentTypeId">当前插件拥有的目标 Document 类型。</param>
    /// <exception cref="ArgumentNullException">任一参数为 null。</exception>
    /// <exception cref="NotSupportedException">当前 Host 尚未实现 Workbench Command 扩展面。</exception>
    public static void AddDocumentCommand(
        this IPluginRegistration registration,
        CommandDescriptor descriptor,
        DocumentTypeId targetDocumentTypeId)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(targetDocumentTypeId);
        RequireWorkbenchCommandRegistration(registration)
            .AddDocumentCommand(descriptor, targetDocumentTypeId);
    }

    /// <summary>通过 Host 的可选能力声明一条菜单命令贡献。</summary>
    /// <param name="registration">当前 manifest 身份绑定的插件注册入口。</param>
    /// <param name="descriptor">菜单命令贡献描述符。</param>
    /// <exception cref="ArgumentNullException">任一参数为 null。</exception>
    /// <exception cref="NotSupportedException">当前 Host 尚未实现 Workbench Command 扩展面。</exception>
    public static void AddMenuCommandContribution(
        this IPluginRegistration registration,
        MenuCommandContributionDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(descriptor);
        RequireWorkbenchCommandRegistration(registration)
            .AddMenuCommandContribution(descriptor);
    }

    /// <summary>通过 Host 的可选能力声明一条快捷键贡献。</summary>
    /// <param name="registration">当前 manifest 身份绑定的插件注册入口。</param>
    /// <param name="descriptor">快捷键贡献描述符。</param>
    /// <exception cref="ArgumentNullException">任一参数为 null。</exception>
    /// <exception cref="NotSupportedException">当前 Host 尚未实现 Workbench Command 扩展面。</exception>
    public static void AddKeyBindingContribution(
        this IPluginRegistration registration,
        KeyBindingContributionDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(descriptor);
        RequireWorkbenchCommandRegistration(registration)
            .AddKeyBindingContribution(descriptor);
    }

    private static IWorkbenchCommandRegistration RequireWorkbenchCommandRegistration(
        IPluginRegistration registration) =>
        registration as IWorkbenchCommandRegistration ??
        throw new NotSupportedException(UnsupportedMessage);
}
