namespace MySmallTools.Business.SecretVideoPlayer;

/// <summary>
/// 把不可信的公开文件名转换为安全、不会覆盖现有文件的输出路径。
/// </summary>
public sealed class DecryptionOutputPathResolver
{
    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public string GetAvailablePath(
        string outputDirectory,
        DecryptionCandidate candidate,
        ISet<string> allocatedPaths)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(allocatedPaths);

        var fullDirectory = Path.GetFullPath(outputDirectory);
        var fallbackName = Path.GetFileNameWithoutExtension(candidate.EncryptedFileName);
        var originalBaseName = Path.GetFileNameWithoutExtension(Path.GetFileName(candidate.OriginalFileName));
        var baseName = SanitizeFileName(originalBaseName, fallbackName);
        var extension = SanitizeExtension(candidate.OriginalExtension);

        for (var suffix = 0; suffix < int.MaxValue; suffix++)
        {
            var fileName = suffix == 0
                ? baseName + extension
                : $"{baseName} ({suffix}){extension}";
            var path = Path.Combine(fullDirectory, fileName);
            if (!File.Exists(path) && allocatedPaths.Add(path))
                return path;
        }

        throw new VideoDecryptionException(
            VideoDecryptionFailureCode.OutputConflict,
            "无法为解密视频分配可用的输出文件名。");
    }

    internal static string SanitizeFileName(string? requestedName, string fallbackName)
    {
        var value = string.IsNullOrWhiteSpace(requestedName) ? fallbackName : requestedName;
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(value
            .Select(character => invalid.Contains(character) || character is '/' or '\\' ? '_' : character)
            .ToArray())
            .Trim()
            .TrimEnd('.', ' ');

        if (string.IsNullOrWhiteSpace(sanitized) || WindowsReservedNames.Contains(sanitized))
        {
            sanitized = new string(fallbackName
                .Select(character => invalid.Contains(character) || character is '/' or '\\' ? '_' : character)
                .ToArray())
                .Trim()
                .TrimEnd('.', ' ');
        }

        if (string.IsNullOrWhiteSpace(sanitized) || WindowsReservedNames.Contains(sanitized))
            sanitized = "decrypted-video";

        return sanitized;
    }

    private static string SanitizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return string.Empty;

        var normalized = extension.StartsWith('.') ? extension : "." + extension;
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        if (normalized.Length > 33 || normalized.Skip(1).Any(character =>
                invalid.Contains(character) || character is '/' or '\\' or '.'))
            return string.Empty;

        return normalized;
    }
}
