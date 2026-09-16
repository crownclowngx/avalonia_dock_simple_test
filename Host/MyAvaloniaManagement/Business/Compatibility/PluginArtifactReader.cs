using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MyAvaloniaManagement.Business.Plugins.Discovery;

namespace MyAvaloniaManagement.Business.Compatibility;

/// <summary>只读插件产物，不将 DLL 加载到任何运行上下文，不执行模块或初始化代码。</summary>
internal static class PluginArtifactReader
{
    internal const string BuildFileName = "plugin.build.json";

    internal static async Task<PluginArtifact> ReadAsync(string root, CancellationToken token = default)
    {
        if (!PluginManifestReader.TryRead(root, out var manifest, out var code, out _))
            throw new InvalidDataException($"插件清单无效：{code}");
        var identity = await ArtifactFingerprint.CaptureDirectoryAsync(root, token);
        var entry = Path.Combine(root, manifest!.EntryPoint.Assembly);
        var references = ReadReferences(entry);
        PluginBuildInfo? build = null;
        var metadata = Path.Combine(root, BuildFileName);
        if (File.Exists(metadata))
        {
            if (new FileInfo(metadata).Length > 1024 * 1024) throw new InvalidDataException("构建信息超过大小限制。");
            build = JsonSerializer.Deserialize<PluginBuildInfo>(await File.ReadAllTextAsync(metadata, token), CompatibilityReportJson.Options);
            if (build is null || build.SchemaVersion != 1 || build.Packages is null || build.Packages.Count > 10000 ||
                build.Packages.Any(p => p is null || string.IsNullOrWhiteSpace(p.Id) || string.IsNullOrWhiteSpace(p.Version)))
                throw new InvalidDataException("构建信息格式无效。");
        }
        return new PluginArtifact(manifest.PluginId.Value, PluginVersionText.Format(manifest.PluginVersion),
            manifest.Sdk.ToString(), manifest.EntryPoint.Assembly, identity, build, references);
    }

    internal static IReadOnlyList<BuildDependency> ReadReferences(string file)
    {
        using var stream = File.OpenRead(file);
        using var pe = new PEReader(stream);
        if (!pe.HasMetadata) return [];
        var reader = pe.GetMetadataReader();
        return reader.AssemblyReferences.Select(handle => reader.GetAssemblyReference(handle))
            .Select(reference => new BuildDependency(reader.GetString(reference.Name), reference.Version.ToString()))
            .OrderBy(item => item.Id, StringComparer.Ordinal).ToArray();
    }

    /// <summary>验证文件仍对应本次已加载模块。磁盘被替换时不能用新文件的哈希代表旧的内存实例。</summary>
    internal static Guid ReadModuleId(string file)
    {
        using var stream = File.OpenRead(file);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        return reader.GetGuid(reader.GetModuleDefinition().Mvid);
    }
}
