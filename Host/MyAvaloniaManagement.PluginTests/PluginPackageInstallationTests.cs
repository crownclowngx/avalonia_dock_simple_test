using System.IO.Compression;
using System.Runtime.Loader;
using System.Text.Json.Nodes;
using MyAvaloniaManagement.Business.Plugins.Installation;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>包检查通过真实 ZIP 和文件系统验证，不能因为检查而创建插件加载上下文或执行模块。</summary>
public sealed class PluginPackageInstallationTests
{
    [Theory]
    [InlineData(1, false)] [InlineData(1, true)] [InlineData(2, false)] [InlineData(2, true)]
    public async Task 旧协议两种版本有无配套清单均可检查且不执行插件(int version, bool sidecar)
    {
        using var files = new PluginInstallTestFiles();
        Assert.DoesNotContain(AssemblyLoadContext.All.SelectMany(context => context.Assemblies), assembly => assembly.GetName().Name == "InstallationProbe.Plugin");
        var package = await new PluginPackageInspector(files.Paths).InspectAsync(files.Zip(version, sidecar));
        Assert.Equal($"{version}.0.0", package.Manifest.PluginVersion.ToString(3));
        Assert.Equal(sidecar, package.HasReleaseManifest);
        Assert.True(PluginInstallJson.Hash(package.ArchiveHash));
        Assert.DoesNotContain(AssemblyLoadContext.All.SelectMany(context => context.Assemblies), assembly => assembly.GetName().Name == "InstallationProbe.Plugin");
        Assert.Empty(Directory.GetFileSystemEntries(files.Paths.PluginsRoot));
        Assert.False(File.Exists(Path.Combine(package.PayloadPath, "plugin.build.json")));
    }

    [Theory]
    [InlineData("../outside.txt")] [InlineData("/absolute.txt")] [InlineData("C:/absolute.txt")]
    [InlineData("Controls/Probe/../outside.txt")] [InlineData("Controls/Probe/sub\\..\\bad")]
    [InlineData("Controls/Probe/data:stream")] [InlineData("Controls/Probe/CON.txt")]
    [InlineData("Controls/Probe/name.")] [InlineData("Controls/Probe/name ")]
    [InlineData("Controls/Other/file.txt")] [InlineData("other.txt")]
    [InlineData("Controls/Probe/RESOURCES/说明.txt")]
    public async Task 异常路径和多插件包拒绝且没有活动目录写入(string extra)
    {
        using var files = new PluginInstallTestFiles();
        var zip = files.Zip();
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update)) PluginInstallTestFiles.Write(archive, extra, "bad");
        await Assert.ThrowsAnyAsync<Exception>(() => new PluginPackageInspector(files.Paths).InspectAsync(zip));
        Assert.Empty(Directory.GetFileSystemEntries(files.Paths.PluginsRoot));
        Assert.False(File.Exists(Path.Combine(files.Root, "outside.txt")));
        Assert.Empty(Directory.GetDirectories(Path.Combine(files.Paths.ManagementRoot, "staging")));
    }

    [Theory]
    [InlineData("schemaVersion")] [InlineData("pluginId")] [InlineData("pluginVersion")]
    [InlineData("entryPoint")] [InlineData("sdk")] [InlineData("directoryName")]
    [InlineData("targetFramework")] [InlineData("runtimeIdentifier")] [InlineData("archive")]
    [InlineData("files")] [InlineData("extra")] [InlineData("duplicate")]
    public async Task 错误配套清单不得静默降为仅ZIP(string field)
    {
        using var files = new PluginInstallTestFiles();
        var zip = files.Zip(sidecar: true); var path = Path.ChangeExtension(zip, ".manifest.json");
        var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        if (field == "duplicate") File.WriteAllText(path, File.ReadAllText(path).Replace("\"schemaVersion\":2", "\"schemaVersion\":2,\"schemaVersion\":2"));
        else
        {
            json[field] = field switch
            {
                "schemaVersion" => JsonValue.Create(99), "files" => new JsonArray(),
                "archive" => new JsonObject(), "entryPoint" => new JsonObject(), "sdk" => new JsonObject(),
                _ => JsonValue.Create("incorrect")
            };
            File.WriteAllText(path, json.ToJsonString());
        }
        await Assert.ThrowsAnyAsync<Exception>(() => new PluginPackageInspector(files.Paths).InspectAsync(zip));
        var only = await new PluginPackageInspector(files.Paths).InspectAsync(zip, string.Empty);
        Assert.False(only.HasReleaseManifest);
    }

    [Fact]
    public async Task 改名ZIP可显式选择原配套清单且预览不依赖源文件()
    {
        using var files = new PluginInstallTestFiles();
        var original = files.Zip(sidecar: true);
        var renamed = Path.Combine(files.Root, "已改名 中文.zip"); File.Move(original, renamed);
        var package = await new PluginPackageInspector(files.Paths).InspectAsync(renamed, Path.ChangeExtension(original, ".manifest.json"));
        File.WriteAllText(renamed, "source replaced");
        Assert.Equal(package.ArtifactHash, (await PluginPayloadValidator.ValidateAsync(package.PayloadPath, default)).Artifact.Sha256);
        Assert.True(package.HasReleaseManifest);
    }

    [Theory]
    [InlineData("archive")] [InlineData("expanded")] [InlineData("entries")] [InlineData("sidecar")]
    public async Task 资源预算按真实流和条目执行(string kind)
    {
        using var files = new PluginInstallTestFiles();
        var limits = kind switch
        {
            "archive" => new PluginPackageLimits(ArchiveBytes: 10), "expanded" => new(ExpandedBytes: 10),
            "entries" => new(Entries: 1), _ => new(SidecarBytes: 10)
        };
        await Assert.ThrowsAnyAsync<Exception>(() => new PluginPackageInspector(files.Paths, limits).InspectAsync(files.Zip(sidecar: true)));
        Assert.Empty(Directory.GetFileSystemEntries(files.Paths.PluginsRoot));
    }

    [Theory]
    [InlineData("missing-deps")] [InlineData("version")] [InlineData("sdk")] [InlineData("shared")]
    [InlineData("native")] [InlineData("duplicate-manifest")] [InlineData("file-directory")]
    [InlineData("symlink")] [InlineData("empty")]
    public async Task 结构兼容及资产边界均在执行前拒绝(string kind)
    {
        using var files = new PluginInstallTestFiles(); var zip = files.Zip();
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update))
        {
            switch (kind)
            {
                case "missing-deps": archive.GetEntry("Controls/Probe/InstallationProbe.Plugin.deps.json")!.Delete(); break;
                case "version": case "sdk":
                    archive.GetEntry("Controls/Probe/plugin.manifest.json")!.Delete();
                    PluginInstallTestFiles.Write(archive, "Controls/Probe/plugin.manifest.json", kind == "version"
                        ? PluginInstallTestFiles.Manifest(2) : PluginInstallTestFiles.Manifest(1).Replace("3.0.0", "3.9.0")); break;
                case "shared": PluginInstallTestFiles.Write(archive, "Controls/Probe/Avalonia.Base.dll", "bad"); break;
                case "native": PluginInstallTestFiles.Write(archive, "Controls/Probe/runtimes/linux-x64/native/lib.so", "bad"); break;
                case "duplicate-manifest": PluginInstallTestFiles.Write(archive, "Controls/Probe/plugin.manifest.json", "{}"); break;
                case "file-directory": PluginInstallTestFiles.Write(archive, "Controls/Probe/resources", "bad"); break;
                case "symlink": archive.CreateEntry("Controls/Probe/link").ExternalAttributes = 0xA1FF << 16; break;
                case "empty": foreach (var entry in archive.Entries.ToArray()) entry.Delete(); break;
            }
        }
        await Assert.ThrowsAnyAsync<Exception>(() => new PluginPackageInspector(files.Paths).InspectAsync(zip));
    }
}
