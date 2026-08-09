using BiliDownloader.Models;
using BiliDownloader.Services.Api;
using BiliDownloader.Services.Infrastructure;

namespace BiliDownloader.Services.Download.Extras;

/// <summary>
/// 弹幕处理器只负责编排“分段获取—解码—规范化—多格式发布”。格式细节位于策略类，
/// 单个格式写入失败不会阻止其他格式，单段失败则形成可独立重试的 PartialSuccess。
/// </summary>
public sealed class DanmakuExtrasHandler : IExtrasHandler
{
    private const int SegmentDurationSeconds = 360;
    private static readonly IPluginLogger Log = PluginLog.For<DanmakuExtrasHandler>();
    private readonly IBiliDanmakuApi? _api;
    private readonly DanmakuFormatterRegistry _formatters;
    private readonly IDanmakuRequestPacer _pacer;

    public DanmakuExtrasHandler() : this(null,
        new DanmakuFormatterRegistry([new XmlDanmakuFormatter(), new AssDanmakuFormatter(), new JsonDanmakuFormatter()]),
        new FixedDanmakuRequestPacer())
    {
    }

    public DanmakuExtrasHandler(
        IBiliDanmakuApi? api,
        DanmakuFormatterRegistry formatters,
        IDanmakuRequestPacer pacer)
    {
        _api = api;
        _formatters = formatters;
        _pacer = pacer;
    }

    public string Type => "danmaku";
    public string DisplayName => "弹幕";

    public async Task<ExtrasResult> ExecuteAsync(ExtrasContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (context.Cid == 0) return ExtrasResult.Failed(Type, "视频 cid 为空，无法获取弹幕");
        var options = (context.DanmakuOptions.Formats.Count == 0
            ? DanmakuOptions.LegacyEnabled : context.DanmakuOptions).Canonicalize();

        var actualOutputDir = string.IsNullOrEmpty(context.SubFolder)
            ? context.OutputDirectory
            : Path.Combine(context.OutputDirectory, context.SubFolder);
        Directory.CreateDirectory(actualOutputDir);
        var api = _api ?? context.ApiService;
        var segmentCount = Math.Max(1, (int)Math.Ceiling(context.Duration / (double)SegmentDurationSeconds));
        var isRetry = context.RetryItemKeys.Count > 0;
        var retrySegments = context.RetryFailedSegments
            .Where(pair => context.RetryItemKeys.Contains(pair.Key)
                && pair.Key.StartsWith("danmaku:", StringComparison.OrdinalIgnoreCase))
            .SelectMany(static pair => pair.Value)
            .Where(segment => segment > 0 && segment <= segmentCount)
            .ToHashSet();
        var cacheDirectory = Path.Combine(
            string.IsNullOrWhiteSpace(context.TempDirectory)
                ? Path.Combine(Path.GetTempPath(), "BiliDownloader", context.TaskId)
                : context.TempDirectory,
            "danmaku-segments");
        Directory.CreateDirectory(cacheDirectory);
        var allElements = new List<DanmakuElem>();
        var failedSegments = new List<int>();
        for (var segment = 1; segment <= segmentCount; segment++)
        {
            ct.ThrowIfCancellationRequested();
            var cachePath = Path.Combine(cacheDirectory, $"segment-{segment}.bin");
            // 初次执行请求全部分段；独立重试只请求摘要标记失败或缓存缺失的分段。
            // 缓存只位于任务临时目录，不写入 SQLite/导出；成功段可用于重新生成失败格式。
            var shouldFetch = !isRetry || retrySegments.Contains(segment) || !File.Exists(cachePath);
            context.ProgressReporter?.Report(shouldFetch
                ? $"正在获取弹幕（{segment}/{segmentCount}）..."
                : $"正在读取弹幕缓存（{segment}/{segmentCount}）...");
            try
            {
                byte[] protobuf;
                if (shouldFetch)
                {
                    protobuf = await api.GetDanmakuSegmentAsync(
                        context.Cid, segment, context.Aid, context.Cookie, ct);
                    await WriteSegmentCacheAsync(cachePath, protobuf, ct);
                }
                else
                {
                    protobuf = await File.ReadAllBytesAsync(cachePath, ct);
                }
                allElements.AddRange(ProtobufDanmakuDecoder.Decode(protobuf));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failedSegments.Add(segment);
                Log.Warn($"弹幕段 {segment}/{segmentCount} 获取失败：{SensitiveDataSanitizer.Sanitize(ex.Message)}");
            }
            if (shouldFetch && segment < segmentCount) await _pacer.WaitAsync(ct);
        }

        var normalized = DanmakuNormalizer.Normalize(allElements);
        var results = new List<ExtrasItemResult>();
        foreach (var format in options.Formats)
        {
            var key = BuildKey(format);
            if (context.RetryItemKeys.Count > 0 && !context.RetryItemKeys.Contains(key)) continue;
            if (normalized.Count == 0)
            {
                var status = failedSegments.Count == 0 ? ExtrasItemStatus.Unavailable : ExtrasItemStatus.Failed;
                results.Add(new ExtrasItemResult(key, status,
                    failedSegments.Count == 0 ? "empty" : "segments",
                    failedSegments.Count == 0 ? "未获取到任何弹幕数据。" : "所有可用弹幕分段获取失败。",
                    FailedSegments: failedSegments));
                continue;
            }

            try
            {
                var formatter = _formatters.Resolve(format);
                var path = Path.Combine(actualOutputDir, context.BaseFileName + formatter.FileExtension);
                await ExtrasOutputWriter.WriteTextAsync(path, formatter.Format(normalized), context, ct);
                results.Add(new ExtrasItemResult(key,
                    failedSegments.Count == 0 ? ExtrasItemStatus.Success : ExtrasItemStatus.PartialSuccess,
                    failedSegments.Count == 0 ? null : "segments",
                    failedSegments.Count == 0 ? null : $"{failedSegments.Count} 个弹幕分段获取失败。",
                    [path], failedSegments));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var safe = SensitiveDataSanitizer.Sanitize(ex.Message);
                results.Add(new ExtrasItemResult(key, ExtrasItemStatus.Failed,
                    "format", $"弹幕 {format} 生成失败：{safe}", FailedSegments: failedSegments));
            }
        }

        return ExtrasResult.FromItems(Type, results);
    }

    /// <summary>
    /// 分段缓存也采用同目录临时文件发布，避免应用在写入中断后留下“存在但不完整”的缓存，
    /// 后续重试会把缺失缓存重新视为需要补取的分段。
    /// </summary>
    private static async Task WriteSegmentCacheAsync(
        string path, byte[] content, CancellationToken cancellationToken)
    {
        var temporary = path + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, content, cancellationToken);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }

    public static string BuildKey(DanmakuOutputFormat format)
        => $"danmaku:{format.ToString().ToLowerInvariant()}";
}

/// <summary>分段请求节奏边界；测试可使用零等待实现，生产固定 200ms，避免随机数破坏可复现性。</summary>
public interface IDanmakuRequestPacer
{
    Task WaitAsync(CancellationToken cancellationToken);
}

public sealed class FixedDanmakuRequestPacer(TimeSpan? delay = null) : IDanmakuRequestPacer
{
    private readonly TimeSpan _delay = delay ?? TimeSpan.FromMilliseconds(200);
    public Task WaitAsync(CancellationToken cancellationToken)
        => _delay <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(_delay, cancellationToken);
}
