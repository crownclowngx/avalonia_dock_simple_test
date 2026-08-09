using BiliDownloader.Models;
using BiliDownloader.Services.Api;
using BiliDownloader.Services.Auth;
using BiliDownloader.Services.Download.Extras;
using BiliDownloader.Services.Infrastructure;
using BiliDownloader.Services.Naming;

namespace BiliDownloader.Services.Download;

/// <summary>
/// 生产环境下载任务执行器，组合现有 DASH 解析、媒体下载、ffmpeg 合并和附加资源管线。
/// 将这些有网络与文件副作用的能力集中在一个边界内，避免 Coordinator 直接创建具体服务。
/// </summary>
public sealed class BiliDownloadTaskExecutor : IDownloadTaskExecutor, IMediaMergeRetryExecutor, IExtrasRetryExecutor
{
    private static readonly IPluginLogger Log = PluginLog.For<BiliDownloadTaskExecutor>();

    private readonly BiliDownloadService _downloadService;
    private readonly BiliApiService _apiService;
    private readonly ExtrasHandlerRegistry _extrasRegistry;
    private readonly IBiliCredentialProvider _credentialProvider;

    public BiliDownloadTaskExecutor(
        BiliDownloadService downloadService,
        BiliApiService apiService,
        ExtrasHandlerRegistry extrasRegistry,
        IBiliCredentialProvider credentialProvider)
    {
        _downloadService = downloadService;
        _apiService = apiService;
        _extrasRegistry = extrasRegistry;
        _credentialProvider = credentialProvider;
    }

    /// <inheritdoc />
    public async Task<DownloadExecutionResult> ExecuteAsync(
        DownloadTaskRecord task,
        Action<DownloadProgressInfo> onProgress,
        Action<long, long> onBytesChanged,
        CancellationToken cancellationToken)
        => await ExecuteAsync(task, new DownloadExecutionCallbacks(
            onProgress,
            onBytesChanged,
            _ => Task.CompletedTask), cancellationToken);

    /// <inheritdoc />
    public async Task<DownloadExecutionResult> ExecuteAsync(
        DownloadTaskRecord task,
        DownloadExecutionCallbacks callbacks,
        CancellationToken cancellationToken)
    {
        var cookieHeader = _credentialProvider.GetCookieHeader();
        var downloadResult = await _downloadService.DownloadItemAsync(
            task,
            _apiService,
            cookieHeader,
            callbacks.OnProgress,
            callbacks.OnBytesChanged,
            cancellationToken,
            callbacks.OnMediaReadyAsync,
            callbacks.EffectiveMediaSelectionResolvedAsync);

        var extrasSummary = task.ExtrasConfig == 0
            ? null
            : await ExecuteExtrasPipelineAsync(task, cookieHeader, cancellationToken);

        return new DownloadExecutionResult(
            downloadResult.OutputFilePath,
            extrasSummary,
            downloadResult.OutputPlan?.RequiresVideo == false ? null : downloadResult.VideoTransfer,
            downloadResult.OutputPlan?.RequiresAudio == false ? null : downloadResult.AudioTransfer,
            task.ActualMediaFeatures);
    }

    /// <inheritdoc />
    public async Task<DownloadExecutionResult> ExecuteMergeOnlyAsync(
        DownloadTaskRecord task,
        Action<DownloadProgressInfo> onProgress,
        CancellationToken cancellationToken)
    {
        var outputPath = await _downloadService.MergeDownloadedMediaAsync(
            task, onProgress, cancellationToken);

        // 主媒体合并不需要登录；附加资源仍按原有“失败写摘要但不推翻主媒体”的策略执行。
        // 这样仅合并重试不会因为字幕或封面网络问题再次破坏已经发布的 MP4。
        var extrasSummary = task.ExtrasConfig == 0
            ? null
            : await ExecuteExtrasPipelineAsync(
                task, _credentialProvider.GetCookieHeader(), cancellationToken);
        return new DownloadExecutionResult(outputPath, extrasSummary, ActualMediaFeatures: task.ActualMediaFeatures);
    }

    /// <inheritdoc />
    public async Task<string?> ExecuteFailedExtrasAsync(
        DownloadTaskRecord task,
        CancellationToken cancellationToken)
    {
        var existing = ExtrasExecutionSummaryCodec.Deserialize(task.ExtrasResultSummary);
        var retryKeys = existing.Items.Where(static item => item.IsRetryable)
            .Select(static item => item.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (retryKeys.Count == 0) return task.ExtrasResultSummary;
        return await ExecuteExtrasPipelineAsync(
            task, _credentialProvider.GetCookieHeader(), cancellationToken, retryKeys, existing);
    }

    /// <summary>
    /// 顺序执行用户选择的附加资源处理器。单个附加资源失败不会推翻主媒体下载结果，
    /// 但会进入摘要并写入统一日志，供任务中心后续诊断。
    /// </summary>
    private async Task<string?> ExecuteExtrasPipelineAsync(
        DownloadTaskRecord task,
        string cookieHeader,
        CancellationToken cancellationToken,
        IReadOnlySet<string>? retryItemKeys = null,
        ExtrasExecutionSummary? existingSummary = null)
    {
        var extrasType = (ExtrasType)task.ExtrasConfig;
        var handlers = _extrasRegistry.Resolve(extrasType);
        if (handlers.Count == 0)
        {
            return null;
        }

        var results = new List<string>();
        var itemResults = new List<ExtrasItemResult>();
        // G6：附加资源必须与提交阶段保留的 MP4 使用同一基础名；如果继续使用 ItemTitle，
        // 自动序号只会作用于主视频，字幕和封面仍可能覆盖旧文件。
        var baseFileName = string.IsNullOrWhiteSpace(task.OutputFilePath)
            ? FileNameSanitizer.Sanitize(task.ItemTitle)
            : Path.GetFileNameWithoutExtension(task.OutputFilePath);
        var context = new ExtrasContext
        {
            TaskId = task.TaskId,
            Aid = task.Aid,
            Bvid = task.Bvid,
            Cid = task.Cid,
            EpId = task.EpId,
            SeasonId = task.SeasonId,
            MediaType = task.MediaType,
            OutputDirectory = string.IsNullOrWhiteSpace(task.OutputFilePath)
                ? task.OutputDirectory
                : Path.GetDirectoryName(task.OutputFilePath) ?? task.OutputDirectory,
            SubFolder = string.IsNullOrWhiteSpace(task.OutputFilePath) ? task.SubFolder : "",
            BaseFileName = baseFileName,
            MainOutputPath = task.OutputFilePath,
            TempDirectory = task.TempDirectory,
            OutputContainer = task.SelectedOutputContainer
                ?? (string.Equals(Path.GetExtension(task.OutputFilePath), ".mkv", StringComparison.OrdinalIgnoreCase)
                    ? OutputContainer.Mkv : OutputContainer.Mp4),
            ConflictPolicy = task.ConflictPolicy,
            OverwriteConfirmed = task.OverwriteConfirmed,
            Cookie = cookieHeader,
            CoverUrl = task.CoverUrl,
            ApiService = _apiService,
            SubtitleOptions = task.SubtitleOptions,
            DanmakuOptions = task.DanmakuOptions,
            RetryItemKeys = retryItemKeys ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            RetryFailedSegments = existingSummary?.Items
                .Where(static item => item.FailedSegments is { Count: > 0 })
                .ToDictionary(
                    static item => item.Key,
                    static item => item.FailedSegments!,
                    StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase),
            ProgressReporter = new Progress<string>(),
        };

        foreach (var handler in handlers)
        {
            if (retryItemKeys is { Count: > 0 }
                && !retryItemKeys.Any(key => key.Equals(handler.Type, StringComparison.OrdinalIgnoreCase)
                    || key.StartsWith(handler.Type + ":", StringComparison.OrdinalIgnoreCase)))
                continue;
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await handler.ExecuteAsync(context, cancellationToken);
                var safeError = SensitiveDataSanitizer.Sanitize(result.ErrorMessage);
                if (result.Items.Count > 0)
                    itemResults.AddRange(result.Items);
                else
                    itemResults.Add(new ExtrasItemResult(handler.Type,
                        result.Success ? ExtrasItemStatus.Success : ExtrasItemStatus.Failed,
                        result.Success ? null : handler.Type,
                        safeError,
                        result.OutputFiles));
                var status = result.Success ? "OK" : safeError;
                results.Add($"{handler.Type}: {status}");

                if (result.Success)
                {
                    Log.Info($"Extras [{handler.DisplayName}] 成功: {string.Join(", ", result.OutputFiles)}");
                }
                else
                {
                    Log.Warn($"Extras [{handler.DisplayName}] 失败: {safeError}");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var safeError = SensitiveDataSanitizer.Sanitize(ex.Message);
                Log.Warn($"Extras [{handler.DisplayName}] 异常: {safeError}");
                results.Add($"{handler.Type}: FAIL - {safeError}");
                itemResults.Add(new ExtrasItemResult(handler.Type, ExtrasItemStatus.Failed,
                    handler.Type, safeError));
            }
        }

        // 只要内置 G9 处理器返回了逐项结果，就写版本化 JSON；第三方旧处理器仍使用兼容文本。
        return itemResults.Count > 0
            ? ExtrasExecutionSummaryCodec.Serialize(
                (existingSummary ?? new ExtrasExecutionSummary()).Merge(itemResults))
            : string.Join("; ", results);
    }
}
