using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace BiliDownloader.ReleaseAcceptance;

internal sealed record ReleaseFileEntry(string Path, long Length, string Sha256);

internal sealed record ReleaseManifest(
    int SchemaVersion,
    string PluginId,
    string Release,
    string TargetFramework,
    string RuntimeIdentifier,
    string SourceRevision,
    bool Publishable,
    IReadOnlyList<ReleaseFileEntry> Files);

/// <summary>
/// 从 ZIP 自身重新建立文件事实并与清单比较。验证器不信任脚本的复制结果，
/// 因而能阻止遗漏依赖、混入其他 RID、清单外文件或打包后内容变化。
/// </summary>
internal sealed class PackageVerificationGate(string packagePath) : IReleaseGate
{
    private const string ManifestName = "bilidownloader.release.json";
    internal static IReadOnlyList<string> RequiredPayloadFiles { get; } =
    [
        "BiliDownloader.deps.json",
        "BiliDownloader.dll",
        "BiliDownloader.pdb",
        "Flurl.dll",
        "Flurl.Http.dll",
        "Microsoft.Data.Sqlite.dll",
        "protobuf-net.Core.dll",
        "protobuf-net.dll",
        "QRCoder.dll",
        "SQLitePCLRaw.batteries_v2.dll",
        "SQLitePCLRaw.core.dll",
        "SQLitePCLRaw.provider.e_sqlite3.dll",
        "runtimes/win-x64/native/e_sqlite3.dll",
    ];
    public string Name => "package-integrity";

    public async Task<ReleaseGateResult> ExecuteAsync(
        ReleaseGateContext context,
        CancellationToken cancellationToken)
    {
        var package = Path.GetFullPath(packagePath);
        if (!File.Exists(package))
            return ReleaseGateResult.Fail(Name, "未找到候选插件 ZIP。");

        var validationRoot = Path.Combine(context.SandboxRoot, "package-validation");
        if (Directory.Exists(validationRoot)) Directory.Delete(validationRoot, true);
        Directory.CreateDirectory(validationRoot);
        try
        {
            using (var archive = ZipFile.OpenRead(package))
            {
                var prefix = Path.GetFullPath(validationRoot).TrimEnd(Path.DirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                foreach (var entry in archive.Entries)
                {
                    var target = Path.GetFullPath(Path.Combine(
                        validationRoot,
                        entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                    if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        return ReleaseGateResult.Fail(Name, "ZIP 包含越过验收目录的路径。");
                }
                archive.ExtractToDirectory(validationRoot);
            }

            var manifestPath = Path.Combine(validationRoot, ManifestName);
            if (!File.Exists(manifestPath))
                return ReleaseGateResult.Fail(Name, "ZIP 缺少发布清单。");
            var manifest = JsonSerializer.Deserialize<ReleaseManifest>(
                await File.ReadAllTextAsync(manifestPath, cancellationToken),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (manifest is null
                || manifest.PluginId != "BiliDownloader"
                || manifest.Release != "p0"
                || manifest.TargetFramework != "net10.0"
                || manifest.RuntimeIdentifier != "win-x64")
            {
                return ReleaseGateResult.Fail(Name, "发布清单的插件、版本、框架或 RID 不符合 G8 约束。");
            }

            var actualFiles = Directory.EnumerateFiles(validationRoot, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(validationRoot, path).Replace('\\', '/'))
                .Where(path => !path.Equals(ManifestName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            var declaredFiles = manifest.Files
                .Select(file => file.Path.Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            if (!actualFiles.SequenceEqual(declaredFiles, StringComparer.Ordinal))
                return ReleaseGateResult.Fail(Name, "清单文件集合与 ZIP 实际内容不一致。");

            if (RequiredPayloadFiles.Any(required =>
                    !actualFiles.Contains(required, StringComparer.Ordinal)))
            {
                return ReleaseGateResult.Fail(Name, "ZIP 缺少插件、调试符号、deps 或私有运行时依赖。");
            }
            if (actualFiles.Any(path => path.StartsWith("runtimes/", StringComparison.OrdinalIgnoreCase)
                    && !path.StartsWith("runtimes/win-x64/", StringComparison.OrdinalIgnoreCase)))
                return ReleaseGateResult.Fail(Name, "Windows 发布包混入了非 win-x64 运行时资产。");
            if (actualFiles.Any(path => path.Equals("MyAvaloniaManagementCommon.dll", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith("Avalonia.", StringComparison.OrdinalIgnoreCase)))
                return ReleaseGateResult.Fail(Name, "发布包不应复制宿主共享程序集。");

            foreach (var entry in manifest.Files)
            {
                var path = Path.Combine(validationRoot, entry.Path.Replace('/', Path.DirectorySeparatorChar));
                var info = new FileInfo(path);
                if (info.Length != entry.Length)
                    return ReleaseGateResult.Fail(Name, $"文件长度不匹配：{entry.Path}");
                await using var stream = File.OpenRead(path);
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
                if (!hash.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                    return ReleaseGateResult.Fail(Name, $"文件摘要不匹配：{entry.Path}");
            }

            return ReleaseGateResult.Pass(
                Name,
                "ZIP 文件集封闭，RID、长度和 SHA-256 全部匹配。",
                new Dictionary<string, object?>
                {
                    ["files"] = actualFiles.Length,
                    ["sourceRevision"] = manifest.SourceRevision,
                    ["manifestPublishable"] = manifest.Publishable,
                });
        }
        finally
        {
            if (Directory.Exists(validationRoot)) Directory.Delete(validationRoot, true);
        }
    }
}
