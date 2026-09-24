using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using MyAvaloniaManagement.Business.Plugins.Installation;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>测试独占的小型真实文件集；所有写入和故障注入只发生在自身临时根，绝不接触个人安装目录。</summary>
internal sealed class PluginInstallTestFiles : IDisposable
{
    internal const string Id = "myavalonia.plugin.installation-probe";
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "V23 安装测试", Guid.NewGuid().ToString("N"));
    internal PluginInstallPaths Paths { get; }
    internal PluginInstallationStore Store { get; }
    internal PluginInstallTestFiles()
    {
        // 构造时验证所有正向输入，避免负向测试把“夹具缺失”误判为格式拒绝。
        foreach (var version in new[] { 1, 2 })
        foreach (var name in new[] { "InstallationProbe.Plugin.dll", "InstallationProbe.Plugin.deps.json" })
        {
            var input = Path.Combine(AppContext.BaseDirectory, "InstallationProbe", "V" + version, name);
            if (!File.Exists(input)) throw new FileNotFoundException("缺少真实版本安装夹具。", input);
        }
        Paths = new(Path.Combine(Root, "app", "Controls")); Paths.Ensure(); Store = new(Paths);
    }

    internal static string Manifest(int version) => JsonSerializer.Serialize(new
    {
        schemaVersion = 2, pluginId = Id, pluginVersion = $"{version}.0.0",
        entryPoint = new { assembly = "InstallationProbe.Plugin.dll", type = "InstallationProbe.Module" },
        sdk = new { minInclusive = "3.0.0", maxExclusive = "4.0.0" }
    });

    internal string Zip(int version = 1, bool sidecar = false, string folder = "Probe")
    {
        var path = Path.Combine(Root, Guid.NewGuid().ToString("N") + ".zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var prefix = "Controls/" + folder + "/";
            foreach (var name in new[] { "InstallationProbe.Plugin.dll", "InstallationProbe.Plugin.deps.json" })
            {
                var input = Path.Combine(AppContext.BaseDirectory, "InstallationProbe", "V" + version, name);
                if (!File.Exists(input)) throw new FileNotFoundException("缺少真实版本夹具，不能跳过安装验证。", input);
                archive.CreateEntryFromFile(input, prefix + name);
            }
            Write(archive, prefix + "plugin.manifest.json", Manifest(version));
            Write(archive, prefix + "resources/说明.txt", "插件资源 " + version);
        }
        if (sidecar) Sidecar(path, version, folder);
        return path;
    }

    internal static void Write(ZipArchive archive, string path, string text)
    {
        using var writer = new StreamWriter(archive.CreateEntry(path).Open()); writer.Write(text);
    }

    internal void Sidecar(string zip, int version, string folder)
    {
        using var archive = ZipFile.OpenRead(zip);
        var files = archive.Entries.Select(entry =>
        {
            using var stream = entry.Open();
            return new { path = entry.FullName, length = entry.Length, sha256 = Convert.ToHexString(SHA256.HashData(stream)) };
        }).ToArray();
        using var manifest = JsonDocument.Parse(Manifest(version));
        var root = manifest.RootElement;
        File.WriteAllText(Path.ChangeExtension(zip, ".manifest.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 2, pluginId = Id, pluginVersion = $"{version}.0.0",
            entryPoint = root.GetProperty("entryPoint"), sdk = root.GetProperty("sdk"), directoryName = folder,
            targetFramework = "net10.0", runtimeIdentifier = "win-x64", sourceRevision = "protocol-fixture",
            archive = new { file = Path.GetFileName(zip), length = new FileInfo(zip).Length, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(zip))) }, files
        }));
    }

    internal void Deploy(int version, string directory = "Probe")
    {
        var zip = Zip(version, folder: directory);
        ZipFile.ExtractToDirectory(zip, Path.GetDirectoryName(Paths.PluginsRoot)!);
    }

    internal async Task<PluginInstallOperation> StageAsync(int version)
    {
        var service = new PluginInstallationService(Paths, Store);
        await service.InspectAsync(Zip(version), null);
        await service.CommitAsync(true);
        await service.StopAsync();
        return Store.ReadOperation()!;
    }

    public void Dispose()
    {
        var expected = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "V23 安装测试")) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(Root);
        if (!target.StartsWith(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("测试清理目录越界。");
        if (Directory.Exists(target)) Directory.Delete(target, true);
    }
}
