namespace MyAvaloniaManagementCommon.Plugin;

/// <summary>
/// 可选的插件生命周期启动依赖声明。
/// </summary>
/// <remarks>
/// 未实现此接口的生命周期保持零配置接入。依赖只约束
/// <see cref="IPluginLifecycle"/> 的启动顺序，不表示程序集加载依赖。
/// </remarks>
public interface IPluginLifecycleDependencies
{
    /// <summary>
    /// 获取必须先成功初始化的生命周期插件标识。
    /// </summary>
    IReadOnlyCollection<string> RequiredPluginIds { get; }
}
