namespace BiliDownloader.Services.Infrastructure;

/// <summary>
/// 日志工厂：获取指定源的日志实例
/// </summary>
public static class PluginLog
{
    static PluginLog()
    {
        PluginLogger.InitializeLogFile();
    }

    /// <summary>
    /// 获取指定类型/模块的日志实例
    /// </summary>
    public static IPluginLogger For<T>() => new PluginLogger(typeof(T).Name);

    /// <summary>
    /// 获取指定名称的日志实例
    /// </summary>
    public static IPluginLogger For(string source) => new PluginLogger(source);
}
