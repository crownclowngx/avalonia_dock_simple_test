using System;
using System.IO;

namespace MyAvaloniaManagement.Business.Documents;

/// <summary>
/// 统一文档路径的规范化和相等判断。
/// 集中采用 Windows 不区分大小写的身份规则，避免打开查重与保存校验产生不同结论。
/// </summary>
internal static class DocumentPathIdentity
{
    internal static string Normalize(string path) => Path.GetFullPath(path);

    internal static bool Equals(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Normalize(left),
                Normalize(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
