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

        try
        {
            var assemblyDir = Path.GetDirectoryName(
                typeof(SqliteNativeLoader).Assembly.Location) ?? "";

            var os = OperatingSystem.IsWindows()
                ? "win"
                : OperatingSystem.IsLinux()
                    ? "linux"
                    : null;
            if (os is null)
            {
                return;
            }

            var architecture = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "x64",
                Architecture.X86 => "x86",
                Architecture.Arm64 => "arm64",
                Architecture.Arm => "arm",
                _ => throw new PlatformNotSupportedException(
                    $"不支持的 SQLite CPU 架构: {RuntimeInformation.ProcessArchitecture}"),
            };

            var rid = $"{os}-{architecture}";
            var nativeLibraryName = OperatingSystem.IsWindows()
                ? "e_sqlite3.dll"
                : "libe_sqlite3.so";

            var nativeLibPath = Path.Combine(
                assemblyDir,
                "runtimes",
                rid,
                "native",
                nativeLibraryName);
            if (File.Exists(nativeLibPath))
            {
                NativeLibrary.Load(nativeLibPath);
            }

            _loaded = true;
        }
        catch
        {
            // 若预加载失败则忽略，让运行时走默认解析路径
        }
    }
}
