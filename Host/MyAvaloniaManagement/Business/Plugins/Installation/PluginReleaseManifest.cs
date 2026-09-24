using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MyAvaloniaManagement.Business.Compatibility;
using MyAvaloniaManagement.Business.Plugins.Discovery;

namespace MyAvaloniaManagement.Business.Plugins.Installation;

/// <summary>消费现有 Build 1.1.3/3.4.1 的配套清单；它证明内容相符，不提供发布者签名。</summary>
internal static class PluginReleaseManifest
{
    internal static void Validate(string path, int limit, string archivePath, string archiveHash,
        string directoryName, PluginManifest manifest, ArtifactIdentity artifact)
    {
        using var document = PluginInstallJson.Read(path, limit);
        var root = document.RootElement;
        PluginInstallJson.Fields(root, "schemaVersion", "pluginId", "pluginVersion", "entryPoint", "sdk", "directoryName",
            "targetFramework", "runtimeIdentifier", "sourceRevision", "archive", "files");
        var entry = root.GetProperty("entryPoint"); var sdk = root.GetProperty("sdk"); var archive = root.GetProperty("archive");
        PluginInstallJson.Fields(entry, "assembly", "type");
        PluginInstallJson.Fields(sdk, "minInclusive", "maxExclusive");
        PluginInstallJson.Fields(archive, "file", "length", "sha256");
        if (root.GetProperty("schemaVersion").GetInt32() != 2 || Text(root, "pluginId") != manifest.PluginId.Value ||
            Text(root, "pluginVersion") != manifest.PluginVersion.ToString(3) || Text(root, "directoryName") != directoryName ||
            Text(entry, "assembly") != manifest.EntryPoint.Assembly || Text(entry, "type") != manifest.EntryPoint.Type ||
            Text(sdk, "minInclusive") != manifest.Sdk.MinInclusive.ToString(3) || Text(sdk, "maxExclusive") != manifest.Sdk.MaxExclusive.ToString(3) ||
            Text(root, "runtimeIdentifier") != "win-x64" || Text(root, "targetFramework") != "net10.0" ||
            archive.GetProperty("length").GetInt64() != new FileInfo(archivePath).Length || !SameHash(Text(archive, "sha256"), archiveHash))
            throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "配套发布清单与安装包不一致，或目标框架/平台不受支持。");
        PluginInstallPaths.Segment(Text(archive, "file"));
        _ = Text(root, "sourceRevision");
        var expected = artifact.Files.ToDictionary(f => "Controls/" + directoryName + "/" + f.Path.Replace('\\', '/'), StringComparer.Ordinal);
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = root.GetProperty("files");
        if (files.ValueKind != JsonValueKind.Array || files.GetArrayLength() != expected.Count)
            throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "配套清单的文件集合不完整。");
        foreach (var file in files.EnumerateArray())
        {
            PluginInstallJson.Fields(file, "path", "length", "sha256");
            var name = PluginInstallPaths.Relative(Text(file, "path"));
            if (!found.Add(name) || !expected.TryGetValue(name, out var actual) ||
                file.GetProperty("length").GetInt64() != actual.Length || !SameHash(Text(file, "sha256"), actual.Sha256))
                throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "配套清单中的文件路径、长度或摘要不一致。");
        }
    }

    private static bool SameHash(string actual, string expected) => PluginInstallJson.Hash(actual) &&
        string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString() is { Length: > 0 } text &&
        text.Length <= 2048 && !text.Any(char.IsControl) ? text : throw new PluginInstallException("PLUGIN_INSTALL_INVALID", "配套清单文本字段无效。");
}
