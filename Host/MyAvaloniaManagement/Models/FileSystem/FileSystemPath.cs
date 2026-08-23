using System;
using System.IO;
using System.Security;

namespace MyAvaloniaManagement.Models.FileSystem;

/// <summary>表示经过规范化的绝对文件系统路径类别。</summary>
internal enum FileSystemPathKind
{
    /// <summary>普通绝对目录，包括本地目录和 UNC 共享下的子目录。</summary>
    Directory,

    /// <summary>本地驱动器根，例如 <c>C:\</c>。</summary>
    LocalDriveRoot,

    /// <summary>UNC 共享根，例如 <c>\\server\share</c>。</summary>
    UncShareRoot,
}

/// <summary>文件系统路径规范化的成功结果。</summary>
internal readonly record struct FileSystemPathResult(
    string NormalizedPath,
    FileSystemPathKind Kind);

/// <summary>
/// 在 Host 文件树边界规范化并分类 Windows 绝对目录路径。
/// </summary>
/// <remarks>
/// 本类只解释路径字符串，不访问磁盘或网络。“路径是否存在”由存储端口判定，
/// 从而让 UNC 用例可以在无网络单测中验证。
/// </remarks>
internal static class FileSystemPath
{
    /// <summary>
    /// 将输入规范化为明确的绝对路径并返回分类；空白、相对、设备或非法路径返回失败。
    /// </summary>
    internal static bool TryNormalize(
        string? path,
        out FileSystemPathResult result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var candidate = path.Trim();
        if (IsBareDrive(candidate))
        {
            candidate += Path.DirectorySeparatorChar;
        }

        // 设备命名空间不是文件树可展示的用户目录。在 Path.GetFullPath
        // 之前拒绝，避免将 \\?\C: 误分类为 UNC 共享根。
        if (candidate.StartsWith(@"\\?\", StringComparison.Ordinal) ||
            candidate.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            if (!Path.IsPathFullyQualified(candidate))
            {
                return false;
            }

            var normalized = Path.GetFullPath(candidate);
            if (IsLocalDriveRoot(normalized))
            {
                result = new(
                    normalized,
                    FileSystemPathKind.LocalDriveRoot);
                return true;
            }

            if (normalized.StartsWith(@"\\", StringComparison.Ordinal))
            {
                var components = normalized
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Substring(2)
                    .Split(
                        [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                        StringSplitOptions.RemoveEmptyEntries);
                if (components.Length < 2)
                {
                    return false;
                }

                var uncRoot = $@"\\{components[0]}\{components[1]}";
                if (components.Length == 2)
                {
                    result = new(uncRoot, FileSystemPathKind.UncShareRoot);
                    return true;
                }

                result = new(
                    Path.TrimEndingDirectorySeparator(normalized),
                    FileSystemPathKind.Directory);
                return true;
            }

            result = new(
                Path.TrimEndingDirectorySeparator(normalized),
                FileSystemPathKind.Directory);
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or
                NotSupportedException or
                PathTooLongException or
                SecurityException)
        {
            return false;
        }
    }

    private static bool IsBareDrive(string path) =>
        path.Length == 2 &&
        char.IsAsciiLetter(path[0]) &&
        path[1] == Path.VolumeSeparatorChar;

    private static bool IsLocalDriveRoot(string path)
    {
        var root = Path.GetPathRoot(path);
        return root is not null &&
               root.Length >= 3 &&
               char.IsAsciiLetter(root[0]) &&
               root[1] == Path.VolumeSeparatorChar &&
               string.Equals(
                   Path.TrimEndingDirectorySeparator(root),
                   Path.TrimEndingDirectorySeparator(path),
                   StringComparison.OrdinalIgnoreCase);
    }
}
