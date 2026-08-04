using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace BiliDownloader.ReleaseAcceptance;

internal sealed record SensitiveScanIssue(string RelativePath, string Rule);

internal sealed record SensitiveScanResult(int FileCount, IReadOnlyList<SensitiveScanIssue> Issues)
{
    public bool Passed => Issues.Count == 0;
}

/// <summary>
/// 面向发布证据的白盒敏感扫描器。二进制文件只查找本次真实 Cookie 的精确字节，
/// 文本和 SQLite 再执行结构化规则，既能发现真实泄漏，也避免因 DLL 内包含“Cookie”类型名误报。
/// </summary>
internal sealed partial class SensitiveEvidenceScanner
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".log", ".json", ".xml", ".csv", ".md", ".config", ".manifest",
    };

    public async Task<SensitiveScanResult> ScanAsync(
        IEnumerable<string> roots,
        string? secret,
        CancellationToken cancellationToken)
    {
        var issues = new List<SensitiveScanIssue>();
        var files = roots
            .Select(Path.GetFullPath)
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Select(path => (Root: root, Path: path)))
            .GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        var secretNeedles = BuildSecretNeedles(secret);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(file.Root, file.Path).Replace('\\', '/');
            if (await ContainsAnySecretAsync(file.Path, secretNeedles, cancellationToken))
            {
                issues.Add(new SensitiveScanIssue(relative, "exact-live-secret"));
            }

            var extension = Path.GetExtension(file.Path);
            if (TextExtensions.Contains(extension))
            {
                var text = await File.ReadAllTextAsync(file.Path, cancellationToken);
                AddTextIssues(relative, text, issues);
            }
            else if (extension.Equals(".db", StringComparison.OrdinalIgnoreCase)
                     || extension.Equals(".sqlite", StringComparison.OrdinalIgnoreCase))
            {
                await ScanSqliteAsync(file.Path, relative, secret, issues, cancellationToken);
            }
        }

        return new SensitiveScanResult(files.Length, issues);
    }

    private static void AddTextIssues(string relative, string text, ICollection<SensitiveScanIssue> issues)
    {
        if (CookieValueRegex().IsMatch(text))
            issues.Add(new SensitiveScanIssue(relative, "reusable-cookie-value"));
        if (AuthorizationRegex().IsMatch(text))
            issues.Add(new SensitiveScanIssue(relative, "authorization-header"));
        if (SignedUrlRegex().IsMatch(text))
            issues.Add(new SensitiveScanIssue(relative, "signed-url-query"));
    }

    private static async Task ScanSqliteAsync(
        string path,
        string relative,
        string? secret,
        ICollection<SensitiveScanIssue> issues,
        CancellationToken cancellationToken)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString();
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var tables = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                tables.Add(reader.GetString(0));
        }

        foreach (var table in tables)
        {
            var quotedTable = QuoteIdentifier(table);
            var textColumns = new List<string>();
            await using (var schema = connection.CreateCommand())
            {
                schema.CommandText = $"PRAGMA table_info({quotedTable})";
                await using var reader = await schema.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var name = reader.GetString(1);
                    var type = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                    if (SensitiveColumnRegex().IsMatch(name))
                        issues.Add(new SensitiveScanIssue(relative, $"sensitive-column:{table}.{name}"));
                    if (type.Contains("TEXT", StringComparison.OrdinalIgnoreCase))
                        textColumns.Add(name);
                }
            }

            foreach (var column in textColumns)
            {
                await using var values = connection.CreateCommand();
                values.CommandText = $"SELECT {QuoteIdentifier(column)} FROM {quotedTable} "
                    + $"WHERE {QuoteIdentifier(column)} IS NOT NULL";
                await using var reader = await values.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var value = reader.GetString(0);
                    if (!string.IsNullOrEmpty(secret) && value.Contains(secret, StringComparison.Ordinal))
                        issues.Add(new SensitiveScanIssue(relative, "exact-live-secret-in-sqlite-text"));
                    AddTextIssues(relative, value, issues);
                }
            }
        }
    }

    private static string QuoteIdentifier(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

    private static IReadOnlyList<byte[]> BuildSecretNeedles(string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret)) return [];

        // 只查完整 Header 会漏掉“按 Cookie 行/JSON 字段拆开保存”的泄漏。键值对保留键名，
        // 值本身仅在长度足够时加入，降低短数字 ID 在 ffmpeg 二进制中偶然命中的概率。
        var values = new HashSet<string>(StringComparer.Ordinal) { secret };
        foreach (var segment in secret.Split(
                     ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            values.Add(segment);
            var separator = segment.IndexOf('=');
            if (separator >= 0 && separator < segment.Length - 1)
            {
                var value = segment[(separator + 1)..].Trim();
                if (value.Length >= 8) values.Add(value);
            }
        }
        return values.Select(Encoding.UTF8.GetBytes).ToArray();
    }

    private static async Task<bool> ContainsAnySecretAsync(
        string path,
        IReadOnlyList<byte[]> needles,
        CancellationToken cancellationToken)
    {
        foreach (var needle in needles)
        {
            if (await ContainsBytesAsync(path, needle, cancellationToken)) return true;
        }
        return false;
    }

    private static async Task<bool> ContainsBytesAsync(
        string path,
        byte[] needle,
        CancellationToken cancellationToken)
    {
        const int bufferSize = 81920;
        var buffer = new byte[bufferSize + needle.Length - 1];
        var carry = 0;
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(carry, bufferSize), cancellationToken);
            if (read == 0) return false;
            var available = carry + read;
            if (buffer.AsSpan(0, available).IndexOf(needle) >= 0) return true;
            carry = Math.Min(needle.Length - 1, available);
            buffer.AsSpan(available - carry, carry).CopyTo(buffer);
        }
    }

    [GeneratedRegex("""(?im)\b(?:SESSDATA|bili_jct|DedeUserID|access_key|refresh_token)\s*=\s*(?!\[REDACTED\]|<redacted>)[^;\s"']+""")]
    private static partial Regex CookieValueRegex();

    [GeneratedRegex(@"(?im)\bauthorization\s*[:=]\s*(?:bearer\s+)?(?!\[REDACTED\]|<redacted>)[^\r\n]+")]
    private static partial Regex AuthorizationRegex();

    [GeneratedRegex("""(?i)https?://[^\s"']+[?&](?:w_rid|wts|sign|token|access_key)=[^&\s"']+""")]
    private static partial Regex SignedUrlRegex();

    [GeneratedRegex(@"(?i)^(?:cookie|cookies|cookie_header|authorization|access_key|refresh_token)$")]
    private static partial Regex SensitiveColumnRegex();
}

/// <summary>将扫描器适配为发布门禁；报告只保留路径和规则名。</summary>
internal sealed class SensitiveEvidenceGate(IEnumerable<string> roots) : IReleaseGate
{
    private readonly IReadOnlyList<string> _roots = roots.ToArray();
    public string Name => "sensitive-evidence";

    public async Task<ReleaseGateResult> ExecuteAsync(
        ReleaseGateContext context,
        CancellationToken cancellationToken)
    {
        var result = await new SensitiveEvidenceScanner().ScanAsync(
            _roots, context.Cookie, cancellationToken);
        var metrics = new Dictionary<string, object?>
        {
            ["files"] = result.FileCount,
            ["issues"] = result.Issues.Select(issue => new { issue.RelativePath, issue.Rule }).ToArray(),
        };
        return result.Passed
            ? ReleaseGateResult.Pass(Name, "数据库、日志、文本和二进制未发现可复用敏感值。", metrics)
            : ReleaseGateResult.Fail(Name, $"发现 {result.Issues.Count} 项敏感证据。", metrics);
    }
}
