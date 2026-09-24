using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyAvaloniaManagement.Business.Compatibility;
using MyAvaloniaManagement.Business.Plugins.Discovery;

namespace MyAvaloniaManagement.Business.Plugins.Installation;

/// <summary>只处理身份和版本决策。相同版本也比较完整载荷，防止同号不同包静默覆盖。</summary>
internal static class PluginInstallPlanner
{
    internal static PluginInstallPlan Plan(CheckedPluginPackage package, IReadOnlyList<InstalledPluginFile> inventory)
    {
        if (inventory.GroupBy(item => item.Manifest.PluginId).Any(group => group.Count() > 1))
            throw new PluginInstallException("PLUGIN_INSTALL_CONFLICT", "磁盘存在重复插件 ID，请先处理重复目录。");
        var old = inventory.SingleOrDefault(item => item.Manifest.PluginId == package.Manifest.PluginId);
        if (old is null)
        {
            if (inventory.Any(item => string.Equals(item.DirectoryName, package.DirectoryName, StringComparison.OrdinalIgnoreCase)))
                throw new PluginInstallException("PLUGIN_INSTALL_CONFLICT", "目标目录已被其他插件占用。");
            return new(PluginInstallAction.Install, package.DirectoryName, null, null, false);
        }
        var comparison = package.Manifest.PluginVersion.CompareTo(old.Manifest.PluginVersion);
        var action = comparison > 0 ? PluginInstallAction.Upgrade : comparison < 0 ? PluginInstallAction.Downgrade :
            old.ArtifactHash == package.ArtifactHash ? PluginInstallAction.Unchanged : PluginInstallAction.Reinstall;
        return new(action, old.DirectoryName, old.Manifest.PluginVersion.ToString(3), old.ArtifactHash,
            action is PluginInstallAction.Reinstall or PluginInstallAction.Downgrade);
    }
}

/// <summary>磁盘盘点只读清单和摘要。未启用、未加载的目录也参与身份冲突检查。</summary>
internal static class PluginInstallInventory
{
    internal static async Task<IReadOnlyList<InstalledPluginFile>> ReadAsync(PluginInstallPaths paths,
        PluginInstallationIndex index, CancellationToken token)
    {
        PluginInstallPaths.AssertNoLinks(paths.PluginsRoot);
        var result = new List<InstalledPluginFile>();
        foreach (var directory in Directory.GetDirectories(paths.PluginsRoot).Order(StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            PluginInstallPaths.AssertNoLinks(directory);
            if (!PluginManifestReader.TryRead(directory, out var manifest, out _, out _))
                throw new PluginInstallException("PLUGIN_INSTALL_CONFLICT", "磁盘存在无法确认身份的插件目录，请先检查安装产物。");
            var hash = await ArtifactFingerprint.CaptureDirectoryAsync(directory, token).ConfigureAwait(false);
            result.Add(new(Path.GetFileName(directory), manifest!, hash.Sha256));
        }
        foreach (var entry in index.Plugins)
        {
            var disk = result.SingleOrDefault(item => string.Equals(item.DirectoryName, entry.DirectoryName, StringComparison.OrdinalIgnoreCase));
            if (disk is null || disk.Manifest.PluginId.Value != entry.PluginId || disk.ArtifactHash != entry.ArtifactHash)
                throw new PluginInstallException("PLUGIN_INSTALL_CONFLICT", "安装登记与磁盘内容不一致，请先处理外部改动。");
        }
        return result;
    }
}
