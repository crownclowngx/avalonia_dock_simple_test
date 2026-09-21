using System.Diagnostics;

namespace MyAvaloniaManagement.PluginTests;

internal sealed record FixtureCopyReceipt(int Files, long Bytes, double Milliseconds);

/// <summary>
/// 复制构建目标准备的运行闭包。先核对入口再创建目的目录，缺输入不能依赖安装目录补齐。
/// 每次仍复制到调用者独占目录，不使用硬链接或共享可写缓存；收据只描述准备成本。
/// </summary>
internal static class RestartHarnessFiles
{
    internal static FixtureCopyReceipt Copy(string source, string destination)
    {
        foreach (var name in new[] { "MyAvaloniaManagement.RestartHarness.dll", "MyAvaloniaManagement.RestartHarness.deps.json",
                     "MyAvaloniaManagement.RestartHarness.runtimeconfig.json", "MyAvaloniaManagement.dll", "Avalonia.Base.dll", "Dock.Avalonia.dll",
                     "MyAvaloniaManagement.RestartHarness" + (OperatingSystem.IsWindows() ? ".exe" : "") })
            if (!File.Exists(Path.Combine(source, name))) throw new FileNotFoundException($"重启夹具缺少运行文件：{name}");

        var watch = Stopwatch.StartNew();
        var files = Directory.GetFiles(source, "*", SearchOption.AllDirectories);
        long bytes = 0;
        foreach (var file in files)
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: false);
            bytes += new FileInfo(file).Length;
        }
        return new(files.Length, bytes, watch.Elapsed.TotalMilliseconds);
    }
}
