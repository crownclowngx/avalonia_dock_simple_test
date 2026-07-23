namespace BiliDownloader.Services.Infrastructure;

/// <summary>
/// 插件内轻量日志接口
/// </summary>
public interface IPluginLogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? ex = null);
}
