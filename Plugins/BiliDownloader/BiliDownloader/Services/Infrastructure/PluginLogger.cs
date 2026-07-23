using System.Diagnostics;

namespace BiliDownloader.Services.Infrastructure;

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
            var paths = new BiliDataPaths();
            Directory.CreateDirectory(paths.LogDirectory);
            _logFilePath = Path.Combine(
                paths.LogDirectory,
                $"bilidownloader_{DateTime.Now:yyyyMMdd}.log");
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
            logLine += $"\n  Exception: {ex}";

        logLine = SensitiveDataSanitizer.Sanitize(logLine);

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
        => SensitiveDataSanitizer.Sanitize(input);
}
