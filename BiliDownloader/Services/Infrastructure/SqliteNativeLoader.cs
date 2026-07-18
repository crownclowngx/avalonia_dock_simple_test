using System.Runtime.InteropServices;

namespace BiliDownloader.Services.Infrastructure;

/// <summary>
/// SQLite 原生库预加载器：解决插件子目录中 e_sqlite3.dll 无法被 .NET 运行时自动发现的问题
/// </summary>
public static class SqliteNativeLoader
{
    private static bool _loaded;

    /// <summary>
    /// 确保 e_sqlite3 原生库已加载（幂等，多次调用安全）
    /// </summary>
    public static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        try
        {
            var assemblyDir = Path.GetDirectoryName(
                typeof(SqliteNativeLoader).Assembly.Location) ?? "";

            var rid = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "win-x64",
                Architecture.X86 => "win-x86",
                Architecture.Arm64 => "win-arm64",
                Architecture.Arm => "win-arm",
                _ => "win-x64"
            };

            var nativeLibPath = Path.Combine(assemblyDir, "runtimes", rid, "native", "e_sqlite3.dll");
            if (File.Exists(nativeLibPath))
            {
                NativeLibrary.Load(nativeLibPath);
            }
        }
        catch
        {
            // 若预加载失败则忽略，让运行时走默认解析路径
        }
    }
}
