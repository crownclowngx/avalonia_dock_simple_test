using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MyAvaloniaManagement.Gate;

internal sealed class PackageBuilder(ProcessRunner processes)
{
    private static readonly DateTimeOffset ArchiveTimestamp =
        new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public async Task<PackageEvidence> BuildAsync(
        PluginConfiguration plugin,
        string repositoryRoot,
        string outputRoot,
        bool deterministic,
        IReadOnlyDictionary<string, string?> environment,
        CancellationToken cancellationToken)
    {
        var builds = deterministic ? 2 : 1;
        var evidence = new List<PackageEvidence>();
        for (var build = 1; build <= builds; build++)
        {
            var root = Path.Combine(outputRoot, plugin.Id, $"build-{build}");
            Directory.CreateDirectory(root);
            var project = Path.Combine(repositoryRoot, plugin.Project);
            if (deterministic)
            {
                await processes.RunCheckedAsync(
                    "dotnet",
                    ["build", project, "-t:Rebuild", "-c", "Release", "--no-restore", "--nologo",
                        "-warnaserror", "-p:ContinuousIntegrationBuild=true", "-p:SkipPluginDeploy=true"],
                    repositoryRoot, environment, Path.Combine(root, "build.log"), cancellationToken);
            }

            // Normal solution builds already produce the complete managed-plugin payload in bin.
            // Packaging that payload here avoids invoking the legacy PowerShell package target and
            // prevents verify from restoring or compiling a repository for a second time.
            var pluginRoot = Path.Combine(Path.GetDirectoryName(project)!, "bin", "Release", "net10.0");
            if (!Directory.Exists(pluginRoot))
            {
                throw new GateFailureException($"插件 {plugin.Id} 未生成 Release 输出目录：{pluginRoot}。");
            }

            var zipPath = Path.Combine(root, $"{plugin.AssemblyName}-win-x64.zip");
            CreateDeterministicZip(pluginRoot, plugin.DirectoryName, zipPath);

            evidence.Add(ValidatePackage(plugin, zipPath, deterministic));
        }

        if (deterministic && !string.Equals(evidence[0].Sha256, evidence[1].Sha256, StringComparison.Ordinal))
        {
            throw new GateFailureException($"插件 {plugin.Id} 两次包构建哈希不一致。");
        }

        return evidence[0] with { Deterministic = deterministic };
    }

    public static string Extract(PackageEvidence package, string destination)
    {
        Directory.CreateDirectory(destination);
        ZipFile.ExtractToDirectory(package.ArchivePath, destination, overwriteFiles: false);
        return destination;
    }

    internal static PackageEvidence ValidatePackage(
        PluginConfiguration plugin,
        string zipPath,
        bool deterministic)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var names = archive.Entries.Select(entry => entry.FullName.Replace('\\', '/')).ToArray();
        var prefix = $"Controls/{plugin.DirectoryName}/";
        foreach (var required in new[]
                 {
                     $"{prefix}plugin.manifest.json",
                     $"{prefix}{plugin.AssemblyName}.dll",
                     $"{prefix}{plugin.AssemblyName}.deps.json",
                 })
        {
            if (!names.Contains(required, StringComparer.Ordinal))
            {
                throw new GateFailureException($"插件 {plugin.Id} 包缺少 {required}。");
            }
        }

        var forbidden = names.FirstOrDefault(name => Regex.IsMatch(
            name,
            @"(^|/)(MyAvaloniaManagement\.PluginSdk(?:\.UI|\.Workflow)?|Avalonia(?:\.|$)|Dock\.|Microsoft\.Extensions\.).*\.dll$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
        if (forbidden is not null)
        {
            throw new GateFailureException($"插件 {plugin.Id} 包混入共享程序集：{forbidden}。");
        }

        var manifestEntry = archive.GetEntry($"{prefix}plugin.manifest.json")!;
        using var manifestStream = manifestEntry.Open();
        using var manifest = JsonDocument.Parse(manifestStream, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 8,
        });
        ValidateManifest(manifest.RootElement, plugin);
        var manifestSha = HashEntry(manifestEntry);
        return new(
            manifest.RootElement.GetProperty("pluginId").GetString()!,
            Path.GetFullPath(zipPath),
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(zipPath))),
            archive.Entries.Count,
            manifestSha,
            deterministic);
    }

    private static void ValidateManifest(JsonElement root, PluginConfiguration plugin)
    {
        var expectedRoot = new[] { "entryPoint", "pluginId", "pluginVersion", "schemaVersion", "sdk" };
        var actualRoot = root.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();
        if (!actualRoot.SequenceEqual(expectedRoot, StringComparer.Ordinal) ||
            root.GetProperty("schemaVersion").GetInt32() != 2)
        {
            throw new GateFailureException($"插件 {plugin.Id} manifest 根字段或 schemaVersion 不合法。");
        }

        var entryPoint = root.GetProperty("entryPoint");
        if (entryPoint.GetProperty("assembly").GetString() != $"{plugin.AssemblyName}.dll" ||
            string.IsNullOrWhiteSpace(entryPoint.GetProperty("type").GetString()))
        {
            throw new GateFailureException($"插件 {plugin.Id} manifest 入口不合法。");
        }

        var sdk = root.GetProperty("sdk");
        if (!Version.TryParse(sdk.GetProperty("minInclusive").GetString(), out var minimum) ||
            !Version.TryParse(sdk.GetProperty("maxExclusive").GetString(), out var maximum) ||
            minimum >= maximum)
        {
            throw new GateFailureException($"插件 {plugin.Id} SDK 兼容区间不合法。");
        }
    }

    private static void CreateDeterministicZip(string pluginRoot, string directoryName, string zipPath)
    {
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var file in Directory.EnumerateFiles(pluginRoot, "*", SearchOption.AllDirectories)
                     .Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(pluginRoot, file).Replace('\\', '/');
            var entry = archive.CreateEntry($"Controls/{directoryName}/{relative}", CompressionLevel.Optimal);
            entry.LastWriteTime = ArchiveTimestamp;
            using var input = File.OpenRead(file);
            using var output = entry.Open();
            input.CopyTo(output);
        }
    }

    private static string HashEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
