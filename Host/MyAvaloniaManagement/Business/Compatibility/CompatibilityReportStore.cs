using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MyAvaloniaManagement.Business.Compatibility;

/// <summary>Host 所有的报告存储。仅保存经过校验的数据，不根据导入内容执行命令或打开网络地址。</summary>
internal sealed class CompatibilityReportStore(string directory)
{
    internal string DirectoryPath { get; } = Path.GetFullPath(directory);

    internal async Task<(IReadOnlyList<StoredCompatibilityReport> Reports, int Invalid)> ReadAsync(CancellationToken token)
    {
        if (!Directory.Exists(DirectoryPath)) return ([], 0);
        var files = Directory.EnumerateFiles(DirectoryPath, "*.json").Take(2001).ToArray();
        if (files.Length > 2000) throw new InvalidDataException("报告过多，请整理历史报告后重试。");
        var reports = new List<StoredCompatibilityReport>();
        var invalid = 0;
        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            try { reports.Add(new StoredCompatibilityReport(await ReadFileAsync(file, token), "本地验收报告 / 显式导入")); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException) { invalid++; }
        }
        return (reports.OrderByDescending(r => r.Report.ExecutedAtUtc).ToArray(), invalid);
    }

    internal async Task ImportAsync(string file, CancellationToken token)
    {
        var report = await ReadFileAsync(file, token);
        Directory.CreateDirectory(DirectoryPath);
        var destination = Path.Combine(DirectoryPath, report.ReportId.ToString("N") + ".json");
        var json = CompatibilityReportJson.Serialize(report);
        if (File.Exists(destination))
        {
            if (CompatibilityReportJson.Serialize(await ReadFileAsync(destination, token)) != json)
                throw new InvalidDataException("相同报告 ID 的内容冲突，保留已有记录。");
            return;
        }
        var temporary = Path.Combine(DirectoryPath, report.ReportId.ToString("N") + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, json, token);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static async Task<PluginCompatibilityReport> ReadFileAsync(string file, CancellationToken token)
    {
        await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        if (stream.Length > CompatibilityReportJson.MaximumBytes) throw new InvalidDataException("报告超过大小限制。");
        using var reader = new StreamReader(stream);
        return CompatibilityReportJson.Parse(await reader.ReadToEndAsync(token));
    }
}
