using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MyAvaloniaManagement.Business.Plugins.Installation;

/// <summary>
/// 文件句柄表达真实租约，不靠锁文件是否存在判断占用。先 runtime 后 operations；
/// 仅写待办时可单独持有 operations，但持有它时绝不等待 runtime，避免锁顺序反转。
/// </summary>
internal static class PluginInstallationLease
{
    internal static FileStream Runtime(PluginInstallPaths paths, bool exclusive)
    {
        paths.Ensure();
        var path = paths.Owned(Path.Combine(paths.ManagementRoot, "runtime.lock"));
        if (!File.Exists(path))
        {
            try { using var created = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite); }
            catch (IOException) when (File.Exists(path)) { }
        }
        PluginInstallPaths.AssertNoLinks(path);
        return new FileStream(path, FileMode.Open, FileAccess.Read, exclusive ? FileShare.None : FileShare.Read);
    }

    internal static async Task<FileStream> OperationsAsync(PluginInstallPaths paths, CancellationToken token)
    {
        var path = paths.Owned(Path.Combine(paths.ManagementRoot, "operations.lock"));
        if (!File.Exists(path))
        {
            try { using var created = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite); }
            catch (IOException) when (File.Exists(path)) { }
        }
        var timer = Stopwatch.StartNew();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                PluginInstallPaths.AssertNoLinks(path);
                return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            }
            catch (IOException) when (timer.Elapsed < TimeSpan.FromSeconds(5))
            { await Task.Delay(50, token).ConfigureAwait(false); }
        }
    }
}
