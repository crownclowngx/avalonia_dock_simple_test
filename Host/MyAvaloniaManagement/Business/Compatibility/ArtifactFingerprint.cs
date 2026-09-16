using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MyAvaloniaManagement.Business.Compatibility;

/// <summary>计算只读产物指纹。目录遍历与文件读取归本类，业务身份及报告匹配不属于它。</summary>
/// <remarks>拒绝链接和路径碰撞；相对路径规范化后参与哈希。mtime 只检测读取竞态，不能替代内容哈希。</remarks>
internal static class ArtifactFingerprint
{
    internal static async Task<ArtifactIdentity> CaptureDirectoryAsync(string root, CancellationToken token = default)
    {
        root = Path.GetFullPath(root);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("产物目录不存在。");
        var paths = Enumerate(root).Order(StringComparer.Ordinal).ToArray();
        if (paths.Length == 0 || paths.Length > 50000) throw new InvalidDataException("产物文件数量不合法。");
        var result = await CaptureFilesAsync(paths.Select(path => (Path.GetRelativePath(root, path), path)), token);
        if (!paths.SequenceEqual(Enumerate(root).Order(StringComparer.Ordinal)))
            throw new IOException("检查过程中产物文件集合发生变化。");
        return result;
    }

    private static IEnumerable<string> Enumerate(string root)
    {
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("产物不能包含目录链接。");
        foreach (var path in Directory.EnumerateFileSystemEntries(root))
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("产物不能包含文件链接。");
            if ((attributes & FileAttributes.Directory) != 0)
            {
                foreach (var file in Enumerate(path)) yield return file;
            }
            else yield return path;
        }
    }

    internal static async Task<ArtifactIdentity> CaptureFilesAsync(IEnumerable<(string Name, string Path)> files,
        CancellationToken token = default)
    {
        var items = new List<ArtifactFile>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, path) in files)
        {
            token.ThrowIfCancellationRequested();
            var normalized = name.Replace('\\', '/');
            if (Path.IsPathRooted(normalized) || normalized.Split('/').Any(p => p is "" or "." or "..") ||
                normalized.Any(char.IsControl) || !names.Add(normalized))
                throw new InvalidDataException("产物相对路径不合法或重复。");
            var before = new FileInfo(path);
            var length = before.Length;
            var modified = before.LastWriteTimeUtc;
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
            var after = new FileInfo(path);
            if (after.Length != length || after.LastWriteTimeUtc != modified)
                throw new IOException("检查过程中产物文件发生变化。");
            items.Add(new ArtifactFile(normalized, length, hash));
        }
        var ordered = items.OrderBy(item => item.Path, StringComparer.Ordinal).ToArray();
        return new ArtifactIdentity(Hash(ordered), ordered);
    }

    internal static string Hash(IEnumerable<ArtifactFile> files)
    {
        // 长度前缀避免路径中的分隔字符影响边界；排序固定，不依赖文件系统枚举顺序。
        var text = new StringBuilder();
        foreach (var file in files.OrderBy(item => item.Path, StringComparer.Ordinal))
            text.Append(file.Path.Length).Append(':').Append(file.Path).Append(':')
                .Append(file.Length).Append(':').Append(file.Sha256).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
}
