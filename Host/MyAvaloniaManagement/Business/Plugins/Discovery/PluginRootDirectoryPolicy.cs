using System;
using System.IO;

namespace MyAvaloniaManagement.Business.Plugins.Discovery;

/// <summary>macOS 应用包从 .app 同级目录加载插件，便于复制部署且不修改应用包。</summary>
internal static class PluginRootDirectoryPolicy
{
    internal static string Resolve(string baseDirectory, string pluginsDirectory, bool isMacOS)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginsDirectory);

        // 显式绝对路径由测试或调用方拥有，不能再按应用包位置重定向。
        if (Path.IsPathRooted(pluginsDirectory))
        {
            return Path.GetFullPath(pluginsDirectory);
        }

        var executableDirectory = new DirectoryInfo(baseDirectory);
        if (isMacOS &&
            executableDirectory.Name == "MacOS" &&
            executableDirectory.Parent is { Name: "Contents", Parent: { } bundle } &&
            bundle.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase) &&
            bundle.Parent is { } installationDirectory)
        {
            return Path.GetFullPath(Path.Combine(installationDirectory.FullName, pluginsDirectory));
        }

        return Path.GetFullPath(Path.Combine(baseDirectory, pluginsDirectory));
    }
}
