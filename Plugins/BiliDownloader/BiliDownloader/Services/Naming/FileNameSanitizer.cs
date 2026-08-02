using System.Security.Cryptography;
using System.Text;

namespace BiliDownloader.Services.Naming;

/// <summary>
/// 文件名安全器：确保生成的文件名在 Windows/Linux 上合法且不超长。
/// <para>
/// 设计思考：将文件名安全逻辑从 BiliDownloadService.SanitizeFileName 提取为独立静态类（SRP）。
/// 纯函数、无状态、无 DI 依赖，与 G4 的 TaskFilterSortEngine 设计一致。
/// 增强点：原实现仅替换非法字符，本类额外处理 Windows 保留名（CON/PRN/NUL 等）、
/// 尾部点号/空格、空输入回退和路径总长度截断。
/// </para>
/// </summary>
public static class FileNameSanitizer
{
    private static readonly HashSet<char> InvalidFileNameCharacters = BuildInvalidCharacterSet();

    /// <summary>Windows 保留设备名（不区分大小写，含扩展名变体如 CON.txt）</summary>
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>空输入或清理后为空时的回退文件名</summary>
    private const string FallbackName = "download";

    /// <summary>Windows MAX_PATH 限制</summary>
    private const int MaxPathLength = 260;

    /// <summary>
    /// 清理文件名，确保不含非法字符、不是保留名、不为空。
    /// <para>
    /// 处理顺序：替换非法字符 → 去除首尾空格 → 去除尾部点号 → 检测保留名 → 空回退。
    /// 设计思考：先替换再修剪，避免替换产生的尾部点号被遗漏。
    /// </para>
    /// </summary>
    /// <param name="name">原始文件名（不含扩展名）</param>
    /// <returns>合法的文件名</returns>
    public static string Sanitize(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return FallbackName;

        // 第一步：替换所有非法字符为下划线
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            sb.Append(InvalidFileNameCharacters.Contains(c) ? '_' : c);
        }

        // 第二步：去除首尾空格和尾部点号（Windows 不允许文件名以点号或空格结尾）
        var result = sb.ToString().Trim().TrimEnd('.');

        // 第三步：如果清理后为空（例如原标题全是非法字符），回退默认名
        if (string.IsNullOrWhiteSpace(result))
            return FallbackName;

        // 第四步：检测 Windows 保留名（CON、NUL、COM1 等）
        // 保留名不区分大小写，且含扩展名变体（如 CON.txt 也非法）
        var nameWithoutExt = result.Contains('.')
            ? result[..result.IndexOf('.')]
            : result;

        if (ReservedNames.Contains(nameWithoutExt))
        {
            // 追加下划线打破保留名，如 CON → CON_
            result += "_";
        }

        return result;
    }

    /// <summary>
    /// 确保完整路径不超过 Windows MAX_PATH (260) 限制。
    /// <para>
    /// 设计思考：超限时截断文件名部分（保留扩展名），截断后追加原始文件名的
    /// MD5 前 6 位哈希，保证不同标题截断后不会碰撞。
    /// 例如：目录 200 字符 + 文件名 100 字符 + ".mp4" = 304 > 260，
    /// 则文件名被截断为 260 - 200 - 4 - 7 = 49 字符（含 "_" + 6位哈希）。
    /// </para>
    /// </summary>
    /// <param name="directory">输出目录（完整路径）</param>
    /// <param name="fileName">文件名（不含扩展名，已经过 Sanitize 处理）</param>
    /// <param name="extension">扩展名（含点号，如 ".mp4"）</param>
    /// <returns>不超过 MAX_PATH 的文件名（不含扩展名）</returns>
    public static string EnsurePathLength(string directory, string fileName, string extension)
    {
        // 计算目录 + 路径分隔符 + 扩展名占用的长度
        var dirLength = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length;
        var overhead = dirLength + 1 + extension.Length; // +1 为路径分隔符

        var availableForName = MaxPathLength - overhead;

        // 可用空间足够，无需截断
        if (fileName.Length <= availableForName)
            return fileName;

        // 可用空间不足以容纳哈希后缀（至少需要 7 字符：_ + 6位哈希），回退极短名
        if (availableForName < 8)
            throw new PathTooLongException("输出目录本身过长，请选择更短的输出目录。");

        // 截断文件名并追加短哈希保证唯一性
        var hash = ComputeShortHash(fileName);
        var truncatedLength = availableForName - 7; // "_" + 6位哈希 = 7 字符
        var truncated = TruncateWithoutSplittingSurrogatePair(fileName, truncatedLength).TrimEnd('.', ' ');

        return $"{truncated}_{hash}";
    }

    /// <summary>
    /// 验证完整路径是否在长度限制内（供提交预检使用）。
    /// </summary>
    /// <param name="fullPath">完整文件路径</param>
    /// <returns>路径是否合法（长度 ≤ 260）</returns>
    public static bool IsPathLengthValid(string fullPath)
    {
        return fullPath.Length <= MaxPathLength;
    }

    /// <summary>
    /// 计算字符串的 MD5 前 6 位十六进制哈希（用于截断后保证唯一性）。
    /// </summary>
    private static HashSet<char> BuildInvalidCharacterSet()
    {
        var result = new HashSet<char>(Path.GetInvalidFileNameChars())
        {
            '<', '>', ':', '"', '/', '\\', '|', '?', '*'
        };

        for (var value = 0; value < 32; value++)
        {
            result.Add((char)value);
        }

        return result;
    }

    private static string TruncateWithoutSplittingSurrogatePair(string value, int maxUtf16Length)
    {
        if (value.Length <= maxUtf16Length)
        {
            return value;
        }

        var length = maxUtf16Length;
        if (length > 0 && char.IsHighSurrogate(value[length - 1]))
        {
            length--;
        }

        return value[..length];
    }

    private static string ComputeShortHash(string input)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes, 0, 3).ToLowerInvariant(); // 3 字节 = 6 位十六进制
    }
}
