using System.Runtime.InteropServices;
using LibVLCSharp.Shared;

namespace MySmallTools.Business.SecretVideoPlayer;

/// <summary>
/// 负责从 MySmallTools 插件自身目录初始化唯一的 LibVLC 原生运行时。
/// </summary>
/// <remarks>
/// 路径以当前程序集位置为基准，而不是以宿主进程工作目录为基准，因此插件移动到任意宿主目录后仍可自包含运行。
/// 初始化过程使用双重检查锁，保证多个播放器在不同线程同时首次调用时也只执行一次 Core.Initialize。
/// 本实现明确不回退到宿主根目录、PATH 或系统 VLC，避免误用版本不一致的原生库导致难以复现的崩溃。
/// </remarks>
public sealed class LibVlcRuntime
{
    private readonly object _syncRoot = new();
    private bool _initialized;

    /// <summary>
    /// 获取当前 MySmallTools.dll 对应的 LibVLC 私有绝对目录。
    /// </summary>
    public string RuntimeDirectory
    {
        get
        {
            var assemblyDirectory = Path.GetDirectoryName(typeof(LibVlcRuntime).Assembly.Location)
                ?? throw new InvalidOperationException("无法确定 MySmallTools 程序集目录。");
            return Path.GetFullPath(Path.Combine(assemblyDirectory, "native", "win-x64", "libvlc"));
        }
    }

    /// <summary>
    /// 验证平台和必要原生文件，并以线程安全方式完成进程级初始化。
    /// </summary>
    public void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        lock (_syncRoot)
        {
            if (_initialized)
            {
                return;
            }

            if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            {
                throw new PlatformNotSupportedException("安全视频播放器首期仅支持 Windows x64。");
            }

            var runtimeDirectory = RuntimeDirectory;
            var libVlcPath = Path.Combine(runtimeDirectory, "libvlc.dll");
            var libVlcCorePath = Path.Combine(runtimeDirectory, "libvlccore.dll");
            var pluginsDirectory = Path.Combine(runtimeDirectory, "plugins");

            if (!File.Exists(libVlcPath) || !File.Exists(libVlcCorePath) || !Directory.Exists(pluginsDirectory))
            {
                // 错误信息包含实际检测的绝对目录，部署失败时可直接定位缺失文件，而不是得到模糊的 DllNotFoundException。
                throw new FileNotFoundException(
                    $"MySmallTools 的 LibVLC 原生运行库不完整。检测目录: {runtimeDirectory}");
            }

            Core.Initialize(runtimeDirectory);
            _initialized = true;
        }
    }
}
