using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MyAvaloniaManagement.Business.Compatibility;

/// <summary>报告的单一序列化与校验边界。所有入口校验同一契约，窗口不能自行宽松解析。</summary>
internal static class CompatibilityReportJson
{
    internal const int MaximumBytes = 4 * 1024 * 1024;
    internal static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, MaxDepth = 16,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    internal static PluginCompatibilityReport Parse(string json)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaximumBytes) throw new InvalidDataException("报告超过大小限制。");
        // 默认 JSON 解析接受重复键，导入边界显式拒绝，避免不同消费者对同一文本得出不同结论。
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
        AssertUnique(document.RootElement);
        var report = JsonSerializer.Deserialize<PluginCompatibilityReport>(json, Options)
            ?? throw new InvalidDataException("报告为空。");
        Validate(report);
        return report;
    }

    internal static string Serialize(PluginCompatibilityReport report)
    {
        Validate(report);
        return JsonSerializer.Serialize(report, Options);
    }

    internal static void Validate(PluginCompatibilityReport report)
    {
        if (report.SchemaVersion != 1 || report.ReportId == Guid.Empty || report.ExecutedAtUtc == default ||
            report.ExecutedAtUtc > DateTimeOffset.UtcNow.AddMinutes(5) ||
            string.IsNullOrWhiteSpace(report.PluginId) || !Regex.IsMatch(report.PluginId, @"\Amyavalonia\.plugin\.[a-z0-9]+(?:[.-][a-z0-9]+)*\z") ||
            !Version.TryParse(report.PluginVersion, out _) || !IsHash(report.PluginHash) || report.Host is null ||
            !IsHash(report.Host.RuntimeHash) || !IsHash(report.Host.RuleHash) ||
            string.IsNullOrWhiteSpace(report.Host.OperatingSystem) || string.IsNullOrWhiteSpace(report.Host.Architecture) ||
            string.IsNullOrWhiteSpace(report.Host.Framework) || report.Checks is null || report.Checks.Count is 0 or > 1000 ||
            report.Checks.Any(c => c is null || !Enum.IsDefined(c.Level) || !Enum.IsDefined(c.Outcome) ||
                string.IsNullOrWhiteSpace(c.Scenario) || c.Scenario.Length > 200 || c.Detail is null || c.Detail.Length > 2000) ||
            report.Checks.Select(c => (c.Level, c.Scenario)).Distinct().Count() != report.Checks.Count)
            throw new InvalidDataException("报告身份或分项结果无效。");
        var files = report.Host.Files;
        if (files is null || files.Count is 0 or > 10000 || files.Any(f => f is null ||
            string.IsNullOrWhiteSpace(f.Path) || f.Path.Contains('/') || f.Path.Contains('\\') || f.Path.Contains(':') ||
            f.Path.Any(char.IsControl) || f.Length < 0 || !IsHash(f.Sha256)) ||
            files.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Count ||
            ArtifactFingerprint.Hash(files) != report.Host.RuntimeHash)
            throw new InvalidDataException("报告 Host 文件身份无效。");
    }

    internal static bool IsHash(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');

    private static void AssertUnique(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("报告含重复字段。");
                AssertUnique(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) AssertUnique(child);
    }
}
