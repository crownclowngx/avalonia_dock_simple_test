using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.PluginSdk.UI;

/// <summary>定义 Workflow Action 专用的可选注册能力。</summary>
/// <remarks>
/// 本接口与 <see cref="IPluginRegistration"/> 分离，使既有插件继续只依赖原有最小接口。
/// Host 的 internal 注册对象可以同时实现两个接口，而无需向已经发布的 v3 接口追加成员。
/// </remarks>
public interface IWorkflowActionRegistration
{
    /// <summary>声明由当前插件私有 Provider 创建的 scoped Handler。</summary>
    void AddWorkflowAction<THandler>(WorkflowActionDescriptor descriptor)
        where THandler : class, IWorkflowActionHandler;

    /// <summary>声明当前插件是 Workflow Action Consumer，并请求 caller-bound Gateway。</summary>
    void UseWorkflowActionGateway();
}

/// <summary>为现有插件注册入口提供兼容新增的 Workflow Action 语法。</summary>
public static class WorkflowActionRegistrationExtensions
{
    private const string UnsupportedMessage =
        "当前 Host 不支持 Workflow Action；需要 Plugin SDK/Host 3.1.0 或更高版本。";

    /// <summary>通过 Host 的可选扩展接口声明一个动作。</summary>
    /// <exception cref="NotSupportedException">当前 Host 尚未实现 SDK 3.1 Workflow Action 扩展面。</exception>
    public static void AddWorkflowAction<THandler>(
        this IPluginRegistration registration,
        WorkflowActionDescriptor descriptor)
        where THandler : class, IWorkflowActionHandler
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(descriptor);
        RequireWorkflowRegistration(registration).AddWorkflowAction<THandler>(descriptor);
    }

    /// <summary>请求由 Host 绑定当前插件身份的唯一 Gateway。</summary>
    /// <exception cref="NotSupportedException">当前 Host 尚未实现 SDK 3.1 Workflow Action 扩展面。</exception>
    public static void UseWorkflowActionGateway(this IPluginRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        RequireWorkflowRegistration(registration).UseWorkflowActionGateway();
    }

    private static IWorkflowActionRegistration RequireWorkflowRegistration(
        IPluginRegistration registration) =>
        registration as IWorkflowActionRegistration ??
        throw new NotSupportedException(UnsupportedMessage);
}
