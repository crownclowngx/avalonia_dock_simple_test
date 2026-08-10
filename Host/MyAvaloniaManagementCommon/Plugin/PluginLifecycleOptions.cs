namespace MyAvaloniaManagementCommon.Plugin;

/// <summary>
/// 宿主统一控制的插件生命周期执行期限。
/// </summary>
public sealed class PluginLifecycleOptions
{
    /// <summary>
    /// 单个插件初始化的最长等待时间。
    /// </summary>
    public TimeSpan InitializationTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 单个插件关闭的最长等待时间。
    /// </summary>
    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(10);

    internal void Validate()
    {
        if (InitializationTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(InitializationTimeout),
                "插件初始化超时必须大于零。");
        }

        if (ShutdownTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ShutdownTimeout),
                "插件关闭超时必须大于零。");
        }
    }
}
