using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BiliDownloader.ReleaseAcceptance;

internal sealed record ReleaseFileEntry(string Path, long Length, string Sha256);

internal sealed record ReleaseArchive(string File, long Length, string Sha256);

/// <summary>
/// G12 通用独立包的外置清单。该类型只表达验证器真正需要的发布事实，
/// 不把 BiliDownloader 的业务验收字段混入通用包协议。
/// </summary>
internal sealed record ManagedPluginPackageManifest(
    int SchemaVersion,
    string PluginId,
    string PluginVersion,
    string EntryAssembly,
    string DirectoryName,
    string TargetFramework,
    string RuntimeIdentifier,
    string SourceRevision,
    ReleaseArchive Archive,
    IReadOnlyList<ReleaseFileEntry> Files);

/// <summary>
/// 从最终 ZIP 重新建立文件事实，并与 ZIP 外的 G12 通用清单比较。
/// 验证器不信任打包脚本的暂存目录，因此能阻止路径穿越、遗漏依赖、混入其他 RID、
/// 宿主共享程序集重复携带、清单外文件，以及打包完成后的内容篡改。
/// </summary>
internal sealed partial class PackageVerificationGate(
    string packagePath,
    string manifestPath) : IReleaseGate
{
    private const string PluginPrefix = "Controls/BiliDownloader/";

    internal static IReadOnlyList<string> RequiredPayloadFiles { get; } =
    [
        $"{PluginPrefix}plugin.manifest.json",
        $"{PluginPrefix}BiliDownloader.deps.json",
        $"{PluginPrefix}BiliDownloader.dll",
        $"{PluginPrefix}BiliDownloader.pdb",
        $"{PluginPrefix}Flurl.dll",
        $"{PluginPrefix}Flurl.Http.dll",
        $"{PluginPrefix}Microsoft.Data.Sqlite.dll",
        $"{PluginPrefix}protobuf-net.Core.dll",
        $"{PluginPrefix}protobuf-net.dll",
        $"{PluginPrefix}QRCoder.dll",
        $"{PluginPrefix}SQLitePCLRaw.batteries_v2.dll",
        $"{PluginPrefix}SQLitePCLRaw.core.dll",
        $"{PluginPrefix}SQLitePCLRaw.provider.e_sqlite3.dll",
        $"{PluginPrefix}runtimes/win-x64/native/e_sqlite3.dll",
    ];

    public string Name => "package-integrity";

    public async Task<ReleaseGateResult> ExecuteAsync(
        ReleaseGateContext context,
        CancellationToken cancellationToken)
    {
        var package = Path.GetFullPath(packagePath);
        var sidecar = Path.GetFullPath(manifestPath);
        if (!File.Exists(package))
            return ReleaseGateResult.Fail(Name, "未找到候选插件 ZIP。");
        if (!File.Exists(sidecar))
            return ReleaseGateResult.Fail(Name, "未找到 ZIP 外置发布清单。");

        var manifest = JsonSerializer.Deserialize<ManagedPluginPackageManifest>(
            await File.ReadAllTextAsync(sidecar, cancellationToken),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (manifest is null
            || manifest.SchemaVersion != 1
            || manifest.PluginId != "myavalonia.plugin.bili-downloader"
            || manifest.EntryAssembly != "BiliDownloader.dll"
            || manifest.DirectoryName != "BiliDownloader"
            || manifest.TargetFramework != "net10.0"
            || manifest.RuntimeIdentifier != "win-x64")
        {
            return ReleaseGateResult.Fail(Name, "外置清单的插件身份、框架或 RID 不符合 G12 约束。");
        }

        var packageInfo = new FileInfo(package);
        await using (var stream = File.OpenRead(package))
        {
            var packageHash = Convert.ToHexString(
                await SHA256.HashDataAsync(stream, cancellationToken));
            if (manifest.Archive.File != packageInfo.Name
                || manifest.Archive.Length != packageInfo.Length
                || !manifest.Archive.Sha256.Equals(packageHash, StringComparison.OrdinalIgnoreCase))
            {
                return ReleaseGateResult.Fail(Name, "ZIP 文件名、长度或 SHA-256 与外置清单不一致。");
            }
        }

        var validationRoot = Path.Combine(context.SandboxRoot, "package-validation");
        if (Directory.Exists(validationRoot)) Directory.Delete(validationRoot, true);
        Directory.CreateDirectory(validationRoot);
        try
        {
            using (var archive = ZipFile.OpenRead(package))
            {
                var prefix = Path.GetFullPath(validationRoot).TrimEnd(Path.DirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                var pathSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in archive.Entries)
                {
                    var normalizedEntry = entry.FullName.Replace('\\', '/');
                    var target = Path.GetFullPath(Path.Combine(
                        validationRoot,
                        normalizedEntry.Replace('/', Path.DirectorySeparatorChar)));
                    if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        return ReleaseGateResult.Fail(Name, "ZIP 包含越过验收目录的路径。");
                    if (!normalizedEntry.EndsWith('/') && !pathSet.Add(normalizedEntry))
                        return ReleaseGateResult.Fail(Name, "ZIP 包含大小写冲突或重复路径。");
                }
                archive.ExtractToDirectory(validationRoot);
            }

            var actualFiles = Directory.EnumerateFiles(validationRoot, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(validationRoot, path).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            var declaredFiles = manifest.Files
                .Select(file => file.Path.Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            if (!actualFiles.SequenceEqual(declaredFiles, StringComparer.Ordinal))
                return ReleaseGateResult.Fail(Name, "清单文件集合与 ZIP 实际内容不一致。");

            if (actualFiles.Any(path => !path.StartsWith(PluginPrefix, StringComparison.Ordinal)))
                return ReleaseGateResult.Fail(Name, "ZIP 只能包含 Controls/BiliDownloader/ 这一棵插件目录。");
            if (RequiredPayloadFiles.Any(required =>
                    !actualFiles.Contains(required, StringComparer.Ordinal)))
            {
                return ReleaseGateResult.Fail(Name, "ZIP 缺少入口、清单、调试符号、deps 或私有运行时依赖。");
            }
            if (actualFiles.Any(path => path.StartsWith(
                        $"{PluginPrefix}runtimes/", StringComparison.OrdinalIgnoreCase)
                    && !path.StartsWith(
                        $"{PluginPrefix}runtimes/win-x64/", StringComparison.OrdinalIgnoreCase)))
            {
                return ReleaseGateResult.Fail(Name, "Windows 发布包混入了非 win-x64 运行时资产。");
            }
            if (actualFiles.Any(path => ForbiddenSharedAssemblyRegex().IsMatch(Path.GetFileName(path))))
                return ReleaseGateResult.Fail(Name, "发布包不应复制宿主共享程序集。");

            foreach (var entry in manifest.Files)
            {
                var path = Path.Combine(validationRoot, entry.Path.Replace('/', Path.DirectorySeparatorChar));
                var info = new FileInfo(path);
                if (info.Length != entry.Length)
                    return ReleaseGateResult.Fail(Name, $"文件长度不匹配：{entry.Path}");
                await using var stream = File.OpenRead(path);
                var hash = Convert.ToHexString(
                    await SHA256.HashDataAsync(stream, cancellationToken));
                if (!hash.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                    return ReleaseGateResult.Fail(Name, $"文件摘要不匹配：{entry.Path}");
            }

            return ReleaseGateResult.Pass(
                Name,
                "G12 独立 ZIP 文件集封闭，RID、长度和 SHA-256 全部匹配。",
                new Dictionary<string, object?>
                {
                    ["files"] = actualFiles.Length,
                    ["sourceRevision"] = manifest.SourceRevision,
                    ["pluginVersion"] = manifest.PluginVersion,
                });
        }
        finally
        {
            if (Directory.Exists(validationRoot)) Directory.Delete(validationRoot, true);
        }
    }

    [GeneratedRegex(
        "^(?:MyAvaloniaManagement(?:Common)?|CommunityToolkit\\.Mvvm|Avalonia(?:\\.|$)|Dock\\.|Semi\\.Avalonia|Ursa(?:\\.|$)|Microsoft\\.Extensions\\.|Newtonsoft\\.Json).*\\.dll$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ForbiddenSharedAssemblyRegex();
}
