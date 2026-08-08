using System.Globalization;
using System.Text;
using System.Text.Json;
using BiliDownloader.Models;
using BiliDownloader.Services.Infrastructure;

namespace BiliDownloader.Services.History;

/// <summary>历史安全导出边界。实现必须使用字段白名单并负责临时文件的原子发布。</summary>
public interface ITaskHistoryExporter
{
    Task<TaskHistoryExportResult> ExportAsync(
        TaskHistoryExportRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class TaskHistoryExporter : ITaskHistoryExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] CsvHeaders =
    [
        "taskId", "redownloadedFromTaskId", "mediaUnitKey", "aid", "bvid", "cid", "epId", "seasonId",
        "mediaType", "documentId", "sourceDocumentTitle", "seriesTitle", "itemTitle", "status",
        "videoQualityId", "audioQualityId", "selectedVideoCodec", "actualVideoCodec", "outputContainer",
        "outputMediaMode", "videoDynamicRangePreference", "audioFeaturePreference",
        "requestedMediaFeatures", "expectedMediaFeatures", "actualMediaFeatures",
        "outputFilePath", "filePresenceStatus", "createdAt", "lastUpdatedAt",
        "errorType", "errorSummary",
    ];

    private readonly ITaskHistoryQueryService _history;

    public TaskHistoryExporter(ITaskHistoryQueryService history)
    {
        _history = history;
    }

    public async Task<TaskHistoryExportResult> ExportAsync(
        TaskHistoryExportRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.DestinationPath))
            throw new ArgumentException("导出目标路径为空。", nameof(request));
        var destination = Path.GetFullPath(request.DestinationPath);
        var directory = Path.GetDirectoryName(destination)
            ?? throw new ArgumentException("导出目标目录无效。", nameof(request));
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(
            directory,
            $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");

        var count = 0;
        try
        {
            await using (var stream = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                count = request.Format switch
                {
                    TaskHistoryExportFormat.Csv => await WriteCsvAsync(stream, request, cancellationToken),
                    TaskHistoryExportFormat.Json => await WriteJsonAsync(stream, request, cancellationToken),
                    _ => throw new ArgumentOutOfRangeException(nameof(request), "不支持的历史导出格式。"),
                };
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
            return new TaskHistoryExportResult(count, destination);
        }
        catch
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch { /* 清理失败不能覆盖原始导出异常。 */ }
            throw;
        }
    }

    private async Task<int> WriteCsvAsync(
        Stream stream,
        TaskHistoryExportRequest request,
        CancellationToken cancellationToken)
    {
        // BOM 让常见表格软件无需用户手工选择编码即可正确识别中文。
        await stream.WriteAsync(Encoding.UTF8.GetPreamble(), cancellationToken);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true)
        {
            NewLine = "\r\n",
        };
        await writer.WriteLineAsync(string.Join(',', CsvHeaders));
        var count = 0;
        await foreach (var entry in _history.StreamAsync(
            request.Query, request.TaskIds, cancellationToken).WithCancellation(cancellationToken))
        {
            var row = Project(entry, request.KnownFileStatuses);
            await writer.WriteLineAsync(string.Join(',', ToCsvValues(row).Select(EscapeCsv)));
            count++;
            if (count % 100 == 0) await writer.FlushAsync(cancellationToken);
        }
        await writer.FlushAsync(cancellationToken);
        return count;
    }

    private async Task<int> WriteJsonAsync(
        Stream stream,
        TaskHistoryExportRequest request,
        CancellationToken cancellationToken)
    {
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", 2);
        writer.WriteString("exportedAt", DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture));
        writer.WriteStartArray("items");
        var count = 0;
        await foreach (var entry in _history.StreamAsync(
            request.Query, request.TaskIds, cancellationToken).WithCancellation(cancellationToken))
        {
            JsonSerializer.Serialize(writer, Project(entry, request.KnownFileStatuses), JsonOptions);
            count++;
            if (count % 100 == 0) await writer.FlushAsync(cancellationToken);
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
        await writer.FlushAsync(cancellationToken);
        return count;
    }

    private static TaskHistoryExportRow Project(
        TaskHistoryEntry entry,
        IReadOnlyDictionary<string, FilePresenceStatus>? statuses)
    {
        var status = statuses is not null && statuses.TryGetValue(entry.TaskId, out var known)
            ? known
            : FilePresenceStatus.Unknown;
        return new TaskHistoryExportRow(
            Clean(entry.TaskId),
            Clean(entry.RedownloadedFromTaskId),
            Clean(entry.MediaUnitKey),
            entry.Aid,
            Clean(entry.Bvid),
            entry.Cid,
            entry.EpId,
            entry.SeasonId,
            Clean(entry.MediaType),
            Clean(entry.DocumentId),
            Clean(entry.SourceDocumentTitle),
            Clean(entry.SeriesTitle),
            Clean(entry.ItemTitle),
            Clean(entry.Status),
            entry.VideoQualityId,
            entry.AudioQualityId,
            Clean(entry.SelectedVideoCodec?.ToString()),
            Clean(entry.ActualVideoCodec),
            Clean(entry.OutputContainer?.ToString()),
            Clean(entry.OutputMediaMode?.ToString()),
            Clean(entry.VideoDynamicRangePreference?.ToString()),
            Clean(entry.AudioFeaturePreference?.ToString()),
            Clean(entry.RequestedMediaFeatures?.ToString()),
            Clean(entry.ExpectedMediaFeatures?.ToString()),
            Clean(entry.ActualMediaFeatures?.ToString()),
            Clean(entry.OutputFilePath),
            status.ToString(),
            ToIso8601(entry.CreatedAt),
            ToIso8601(entry.LastUpdatedAt),
            Clean(entry.ErrorType),
            BuildSafeErrorSummary(entry.ErrorMessage));
    }

    private static IEnumerable<string> ToCsvValues(TaskHistoryExportRow row)
    {
        yield return row.TaskId;
        yield return row.RedownloadedFromTaskId;
        yield return row.MediaUnitKey;
        yield return row.Aid.ToString(CultureInfo.InvariantCulture);
        yield return row.Bvid;
        yield return row.Cid.ToString(CultureInfo.InvariantCulture);
        yield return row.EpId.ToString(CultureInfo.InvariantCulture);
        yield return row.SeasonId.ToString(CultureInfo.InvariantCulture);
        yield return row.MediaType;
        yield return row.DocumentId;
        yield return row.SourceDocumentTitle;
        yield return row.SeriesTitle;
        yield return row.ItemTitle;
        yield return row.Status;
        yield return row.VideoQualityId.ToString(CultureInfo.InvariantCulture);
        yield return row.AudioQualityId.ToString(CultureInfo.InvariantCulture);
        yield return row.SelectedVideoCodec;
        yield return row.ActualVideoCodec;
        yield return row.OutputContainer;
        yield return row.OutputMediaMode;
        yield return row.VideoDynamicRangePreference;
        yield return row.AudioFeaturePreference;
        yield return row.RequestedMediaFeatures;
        yield return row.ExpectedMediaFeatures;
        yield return row.ActualMediaFeatures;
        yield return row.OutputFilePath;
        yield return row.FilePresenceStatus;
        yield return row.CreatedAt;
        yield return row.LastUpdatedAt;
        yield return row.ErrorType;
        yield return row.ErrorSummary;
    }

    private static string EscapeCsv(string value)
    {
        var safe = value;
        var first = safe.AsSpan().TrimStart();
        if (!first.IsEmpty && first[0] is '=' or '+' or '-' or '@') safe = "'" + safe;
        return '"' + safe.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';
    }

    private static string BuildSafeErrorSummary(string? value)
    {
        var sanitized = Clean(value);
        var firstLine = sanitized.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => line.Trim())
            .FirstOrDefault(static line => !line.StartsWith("at ", StringComparison.OrdinalIgnoreCase))
            ?? string.Empty;
        return firstLine.Length <= 500 ? firstLine : firstLine[..500];
    }

    private static string Clean(string? value) => SensitiveDataSanitizer.Sanitize(value);

    private static string ToIso8601(DateTime value)
    {
        var local = value.Kind switch
        {
            DateTimeKind.Utc => value.ToLocalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Local),
            _ => value,
        };
        return new DateTimeOffset(local).ToString("O", CultureInfo.InvariantCulture);
    }

    private sealed record TaskHistoryExportRow(
        string TaskId,
        string RedownloadedFromTaskId,
        string MediaUnitKey,
        long Aid,
        string Bvid,
        long Cid,
        long EpId,
        long SeasonId,
        string MediaType,
        string DocumentId,
        string SourceDocumentTitle,
        string SeriesTitle,
        string ItemTitle,
        string Status,
        int VideoQualityId,
        int AudioQualityId,
        string SelectedVideoCodec,
        string ActualVideoCodec,
        string OutputContainer,
        string OutputMediaMode,
        string VideoDynamicRangePreference,
        string AudioFeaturePreference,
        string RequestedMediaFeatures,
        string ExpectedMediaFeatures,
        string ActualMediaFeatures,
        string OutputFilePath,
        string FilePresenceStatus,
        string CreatedAt,
        string LastUpdatedAt,
        string ErrorType,
        string ErrorSummary);
}
