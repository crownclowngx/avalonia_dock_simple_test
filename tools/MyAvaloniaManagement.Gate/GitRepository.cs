using System.Security.Cryptography;
using System.Text;

namespace MyAvaloniaManagement.Gate;

internal sealed record SourceSnapshot(
    string Id,
    string Root,
    string Revision,
    string Tree,
    bool Clean,
    int FileCount,
    string Sha256);

internal sealed class GitRepository(ProcessRunner processes)
{
    public async Task<SourceSnapshot> InspectAsync(
        string id,
        string root,
        CancellationToken cancellationToken)
    {
        root = Path.GetFullPath(root);
        RequireDirectory(root, $"仓库 {id}");
        var status = await GitTextAsync(root, ["status", "--porcelain", "--untracked-files=all"], cancellationToken);
        var head = await processes.RunAsync("git", ["rev-parse", "--verify", "--quiet", "HEAD"],
            root, null, null, cancellationToken, quiet: true);
        if (head.ExitCode != 0 && id == "main")
        {
            throw new GateFailureException("主仓库必须具有可验证的 HEAD 提交。");
        }
        // 新建的外部模板仓库可以尚无首次提交；仍用实际工作区摘要验收，不能伪造 revision 或 clean。
        var revision = head.ExitCode == 0 ? head.Output.Trim() : "unversioned";
        var tree = head.ExitCode == 0
            ? await GitTextAsync(root, ["rev-parse", "HEAD^{tree}"], cancellationToken)
            : "unversioned";
        var files = await ListSourceFilesAsync(root, cancellationToken);
        return new(id, root, revision, tree, head.ExitCode == 0 && string.IsNullOrWhiteSpace(status), files.Length,
            ComputeFingerprint(root, files));
    }

    public async Task CopyWorkspaceAsync(
        SourceSnapshot source,
        string destination,
        CancellationToken cancellationToken)
    {
        var files = await ListSourceFilesAsync(source.Root, cancellationToken);
        var before = ComputeFingerprint(source.Root, files);
        if (!string.Equals(before, source.Sha256, StringComparison.Ordinal))
        {
            throw new GateFailureException($"仓库 {source.Id} 在快照前发生变化。");
        }

        Directory.CreateDirectory(destination);
        foreach (var relativePath in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourcePath = Path.Combine(source.Root, relativePath);
            var destinationPath = Path.Combine(destination, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(sourcePath, destinationPath, overwrite: false);
        }

        var copied = ComputeFingerprint(destination, files);
        if (!string.Equals(copied, source.Sha256, StringComparison.Ordinal))
        {
            throw new GateFailureException($"仓库 {source.Id} 的隔离副本指纹不一致。");
        }
    }

    public Task CloneCommitAsync(SourceSnapshot source, string destination, CancellationToken cancellationToken) =>
        CloneCoreAsync(source, destination, cancellationToken);

    private async Task CloneCoreAsync(SourceSnapshot source, string destination, CancellationToken cancellationToken)
    {
        await processes.RunCheckedAsync(
            "git", ["clone", "--no-hardlinks", "--quiet", source.Root, destination],
            source.Root, null, null, cancellationToken);
        await processes.RunCheckedAsync(
            "git", ["checkout", "--detach", "--quiet", source.Revision],
            destination, null, null, cancellationToken);
    }

    private async Task<string[]> ListSourceFilesAsync(string root, CancellationToken cancellationToken)
    {
        var result = await processes.RunCheckedAsync(
            "git", ["-c", "core.quotepath=false", "ls-files", "--cached", "--others", "--exclude-standard"],
            root, null, null, cancellationToken, quiet: true);
        return result.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(path => path.Replace('/', Path.DirectorySeparatorChar))
            .Where(path => File.Exists(Path.Combine(root, path)))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<string> GitTextAsync(string root, string[] arguments, CancellationToken cancellationToken)
    {
        var result = await processes.RunCheckedAsync("git", arguments, root, null, null, cancellationToken, quiet: true);
        return result.Output.Trim();
    }

    internal static string ComputeFingerprint(string root, IReadOnlyList<string> relativePaths)
    {
        using var incremental = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var relativePath in relativePaths)
        {
            var normalized = relativePath.Replace(Path.DirectorySeparatorChar, '/');
            incremental.AppendData(Encoding.UTF8.GetBytes(normalized));
            incremental.AppendData([0]);
            var path = Path.Combine(root, relativePath);
            if (!File.Exists(path))
            {
                throw new GateFailureException($"指纹文件不存在：{path}。");
            }
            using var stream = File.OpenRead(path);
            var buffer = new byte[128 * 1024];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                incremental.AppendData(buffer, 0, read);
            }
            incremental.AppendData([0]);
        }

        return Convert.ToHexString(incremental.GetHashAndReset());
    }

    private static void RequireDirectory(string path, string description)
    {
        if (!Directory.Exists(path))
        {
            throw new GateFailureException($"{description}不存在：{path}。");
        }
    }
}
