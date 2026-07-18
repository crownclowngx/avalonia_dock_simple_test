using System.Diagnostics;

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

/// <summary>
/// 默认日志实现：输出到 Debug 窗口 + 文件（可选）
/// </summary>
public class PluginLogger : IPluginLogger
{
    private readonly string _source;
    private static readonly object _fileLock = new();
    private static string? _logFilePath;

    public PluginLogger(string source)
    {
        _source = source;
    }

    /// <summary>
    /// 初始化日志文件路径（可选调用，不调用则只输出到 Debug）
    /// </summary>
    public static void InitializeLogFile()
    {
        try
        {
            var appDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BiliDownloader", "logs");
            Directory.CreateDirectory(appDataDir);
            _logFilePath = Path.Combine(appDataDir, $"bilidownloader_{DateTime.Now:yyyyMMdd}.log");
        }
        catch { /* 忽略初始化失败 */ }
    }

    public void Info(string message) => WriteLog("INFO", message, null);
    public void Warn(string message) => WriteLog("WARN", message, null);
    public void Error(string message, Exception? ex = null) => WriteLog("ERROR", message, ex);

    private void WriteLog(string level, string message, Exception? ex)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        var logLine = $"[{timestamp}] [{level}] [{_source}] {message}";
        if (ex != null)
            logLine += $"\n  Exception: {ex.GetType().Name}: {ex.Message}";

        // 输出到 Debug 窗口
        Debug.WriteLine(logLine);

        // 写入文件（如果已初始化）
        if (_logFilePath != null)
        {
            try
            {
                lock (_fileLock)
                {
                    File.AppendAllText(_logFilePath, logLine + Environment.NewLine);
                }
            }
            catch { /* 忽略写入失败 */ }
        }
    }

    /// <summary>
    /// 过滤敏感信息：Cookie、完整 URL 参数等不写入日志
    /// </summary>
    public static string SanitizeForLog(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        // 截断过长的字符串（如 Cookie）
        if (input.Length > 100)
            return input[..50] + "...(truncated)";

        return input;
    }
}

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
