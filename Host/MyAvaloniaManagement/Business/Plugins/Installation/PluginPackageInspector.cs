using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace MyAvaloniaManagement.Business.Plugins.Installation;

/// <summary>集中且可缩小的资源预算；测试用小文件验证真实流边界，不创建数 GiB 的无效夹具。</summary>
internal sealed record PluginPackageLimits(long ArchiveBytes = 2L * 1024 * 1024 * 1024,
    long ExpandedBytes = 4L * 1024 * 1024 * 1024, int Entries = 20000, int SidecarBytes = 16 * 1024 * 1024);

/// <summary>
/// 只负责取得不可变候选。先快照、再受限解包及检查，失败仅清理本次暂存；
/// 安装确认和活动目录替换属于另外的用例，检查器从不持有加载上下文。
/// </summary>
internal sealed class PluginPackageInspector(PluginInstallPaths paths, PluginPackageLimits? limits = null)
{
    private readonly PluginPackageLimits _limits = limits ?? new();

    /// <param name="sidecar">null 自动寻找同名清单；空字符串表示用户明确选择仅 ZIP；其他值是显式配套路径。</param>
    internal async Task<CheckedPluginPackage> InspectAsync(string archivePath, string? sidecar = null,
        CancellationToken cancellationToken = default)
    {
        paths.Ensure();
        var id = Guid.NewGuid().ToString("N");
        var stage = paths.Stage(id);
        Directory.CreateDirectory(stage);
        try
        {
            var snapshot = Path.Combine(stage, "package.zip");
            await CopySnapshotAsync(archivePath, snapshot, _limits.ArchiveBytes, cancellationToken).ConfigureAwait(false);
            var releasePath = sidecar ?? (PluginInstallPaths.FilePresent(Path.ChangeExtension(archivePath, ".manifest.json"))
                ? Path.ChangeExtension(archivePath, ".manifest.json") : string.Empty);
            string? releaseSnapshot = null;
            if (releasePath.Length > 0)
            {
                releaseSnapshot = Path.Combine(stage, "release.manifest.json");
                await CopySnapshotAsync(releasePath, releaseSnapshot, _limits.SidecarBytes, cancellationToken).ConfigureAwait(false);
            }
            var archiveHash = await HashAsync(snapshot, cancellationToken).ConfigureAwait(false);
            var payload = paths.Payload(id);
            Directory.CreateDirectory(payload);
            var folder = await ExtractAsync(snapshot, payload, cancellationToken).ConfigureAwait(false);
            var facts = await PluginPayloadValidator.ValidateAsync(payload, cancellationToken).ConfigureAwait(false);
            if (releaseSnapshot is not null)
                PluginReleaseManifest.Validate(releaseSnapshot, _limits.SidecarBytes, snapshot, archiveHash, folder,
                    facts.Manifest, facts.Artifact);
            return new(id, folder, payload, facts.Manifest, archiveHash, facts.Artifact.Sha256, releaseSnapshot is not null);
        }
        catch
        {
            // 清理失败不能掩盖原始拒绝原因；失败暂存没有操作日志，不会被启动链执行。
            try { paths.DeleteStage(id); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    private async Task<string> ExtractAsync(string archivePath, string payload, CancellationToken token)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count is 0 || archive.Entries.Count > _limits.Entries)
            throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "ZIP 条目数量超限或为空。");
        var entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tree = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        string? folder = null;
        long expanded = 0;
        foreach (var entry in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            var name = entry.FullName.Replace('\\', '/');
            var directory = name.EndsWith('/');
            name = directory ? name[..^1] : name;
            PluginInstallPaths.Relative(name);
            if (!entries.Add(name)) throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "ZIP 包含重复路径。");
            var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixType is not (0 or 0x8000 or 0x4000) ||
                (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "ZIP 不允许链接或特殊文件。");
            var parts = name.Split('/');
            if (parts[0] != "Controls" || (parts.Length < 3 && !directory))
                throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "ZIP 必须只有 Controls 下的单个插件目录。");
            if (parts.Length >= 2)
            {
                folder ??= parts[1];
                if (parts[1] != folder) throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "ZIP 不允许多个插件目录。");
            }
            for (var i = 1; i <= parts.Length; i++)
            {
                var key = string.Join('/', parts.Take(i));
                var isDirectory = i < parts.Length || directory;
                if (tree.TryGetValue(key, out var existing) && existing != isDirectory)
                    throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "ZIP 文件与目录路径冲突。");
                tree[key] = isDirectory;
            }
            if (parts.Length < 3) continue;
            var relative = string.Join('/', parts.Skip(2));
            if (parts.Length - 2 > 32) throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "插件目录层级过深。");
            var target = PluginInstallPaths.Under(payload, relative);
            if (directory) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            PluginInstallPaths.AssertNoLinks(target);
            await using var input = entry.Open();
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                65536, FileOptions.Asynchronous);
            expanded += await CopyLimitedAsync(input, output, _limits.ExpandedBytes - expanded, token).ConfigureAwait(false);
        }
        return folder ?? throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "ZIP 没有插件载荷。");
    }

    internal static async Task CopySnapshotAsync(string source, string destination, long limit, CancellationToken token)
    {
        PluginInstallPaths.AssertNoLinks(source);
        PluginInstallPaths.AssertNoLinks(destination);
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
        if (input.Length > limit) throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "安装输入超过大小限制。");
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous);
        await CopyLimitedAsync(input, output, limit, token).ConfigureAwait(false);
        output.Flush(true);
    }

    private static async Task<long> CopyLimitedAsync(Stream input, Stream output, long limit, CancellationToken token)
    {
        var buffer = new byte[65536];
        long total = 0;
        int count;
        while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            total += count;
            if (total > limit) throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "安装包实际解包内容超过限制。");
            await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
        }
        return total;
    }

    internal static async Task<string> HashAsync(string path, CancellationToken token)
    {
        PluginInstallPaths.AssertNoLinks(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false));
    }
}
