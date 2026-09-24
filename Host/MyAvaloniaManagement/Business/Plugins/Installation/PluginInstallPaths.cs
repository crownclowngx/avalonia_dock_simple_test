using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace MyAvaloniaManagement.Business.Plugins.Installation;

/// <summary>集中拥有安装路径规则。包中目录只是一段名字，永远不能指定 Host 的绝对写入位置。</summary>
internal sealed class PluginInstallPaths
{
    internal string PluginsRoot { get; }
    internal string ManagementRoot { get; }

    internal PluginInstallPaths(string pluginsRoot)
    {
        PluginsRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(pluginsRoot));
        AssertNoLinks(PluginsRoot);
        var parent = Path.GetDirectoryName(PluginsRoot) ?? throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "插件根不能是卷根。");
        var suffix = string.Equals(Path.GetFileName(PluginsRoot), "Controls", StringComparison.OrdinalIgnoreCase)
            ? string.Empty : "-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(PluginsRoot.ToUpperInvariant())))[..16];
        ManagementRoot = Path.Combine(parent, ".plugin-management" + suffix);
        AssertNoLinks(ManagementRoot);
    }

    internal string Stage(string id) => Owned(Path.Combine(ManagementRoot, "staging", OperationId(id)));
    internal string Payload(string id) => Owned(Path.Combine(Stage(id), "payload"));
    internal string Backup(string id) => Owned(Path.Combine(ManagementRoot, "backups", OperationId(id)));
    internal string Target(string directory) => Under(PluginsRoot, Segment(directory));
    internal string Owned(string path) => Under(ManagementRoot, Path.GetRelativePath(ManagementRoot, path));

    internal void Ensure()
    {
        AssertNoLinks(PluginsRoot); AssertNoLinks(ManagementRoot);
        Directory.CreateDirectory(PluginsRoot); Directory.CreateDirectory(ManagementRoot);
    }

    /// <summary>递归删除之前再次核对绝对归属和祖先链接；只用于本工具拥有的暂存目录。</summary>
    internal void DeleteStage(string id)
    {
        var path = Stage(id);
        if (Directory.Exists(path)) { AssertTreeHasNoLinks(path); Directory.Delete(path, true); }
    }

    internal static string OperationId(string value) => Guid.TryParseExact(value, "N", out var id) && id != Guid.Empty
        ? value : throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "安装操作标识无效。");

    internal static string Segment(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 180 || value is "." or ".." ||
            value.Any(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c)) || value.EndsWith(' ') || value.EndsWith('.') ||
            Regex.IsMatch(value, @"\A(?:CON|PRN|AUX|NUL|COM[0-9¹²³]|LPT[0-9¹²³])(?:\.|\z)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new PluginInstallException("PLUGIN_PACKAGE_PATH", "安装包包含不支持的路径名称。");
        return value;
    }

    internal static string Relative(string value)
    {
        var normalized = value.Replace('\\', '/');
        if (normalized.Length > 2048 || normalized.StartsWith('/') || normalized.EndsWith('/') ||
            normalized.Split('/').Length > 35)
            throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "安装包路径过深或无效。");
        foreach (var part in normalized.Split('/')) Segment(part);
        return normalized;
    }

    internal static string Under(string root, string relative)
    {
        Relative(relative);
        var full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)) throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "安装路径越界。");
        AssertNoLinks(full);
        return full;
    }

    internal static void AssertNoLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "安装路径不能经过文件系统链接。");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    /// <summary>只把真正的“不存在”视为可选输入缺失；权限错误、目录占位都必须向上报告，不能降级校验。</summary>
    internal static bool FilePresent(string path)
    {
        AssertNoLinks(path);
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.Directory) != 0)
                throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "安装元数据路径被目录占用。");
            return true;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    private static void AssertTreeHasNoLinks(string root)
    {
        AssertNoLinks(root);
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            AssertNoLinks(entry);
            if (Directory.Exists(entry)) AssertTreeHasNoLinks(entry);
        }
    }
}
