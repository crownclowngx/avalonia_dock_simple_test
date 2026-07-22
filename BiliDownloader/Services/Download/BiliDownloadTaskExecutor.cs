using BiliDownloader.Models;
using BiliDownloader.Services.Api;
using BiliDownloader.Services.Auth;
using BiliDownloader.Services.Download.Extras;
using BiliDownloader.Services.Infrastructure;

namespace BiliDownloader.Services.Download;

/// <summary>
/// 生产环境下载任务执行器，组合现有 DASH 解析、媒体下载、ffmpeg 合并和附加资源管线。
/// 将这些有网络与文件副作用的能力集中在一个边界内，避免 Coordinator 直接创建具体服务。
/// </summary>
public sealed class BiliDownloadTaskExecutor : IDownloadTaskExecutor
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
    {
        var cookieHeader = _credentialProvider.GetCookieHeader();
        var outputFilePath = await _downloadService.DownloadItemAsync(
            task,
            _apiService,
            cookieHeader,
            onProgress,
            onBytesChanged,
            cancellationToken);

        var extrasSummary = task.ExtrasConfig == 0
            ? null
            : await ExecuteExtrasPipelineAsync(task, cookieHeader, cancellationToken);

        return new DownloadExecutionResult(outputFilePath, extrasSummary);
    }

    /// <summary>
    /// 顺序执行用户选择的附加资源处理器。单个附加资源失败不会推翻主媒体下载结果，
    /// 但会进入摘要并写入统一日志，供任务中心后续诊断。
    /// </summary>
    private async Task<string?> ExecuteExtrasPipelineAsync(
        DownloadTaskRecord task,
        string cookieHeader,
        CancellationToken cancellationToken)
    {
        var extrasType = (ExtrasType)task.ExtrasConfig;
        var handlers = _extrasRegistry.Resolve(extrasType);
        if (handlers.Count == 0)
        {
            return null;
        }

        var results = new List<string>();
        var baseFileName = BiliDownloadService.SanitizeFileName(task.ItemTitle);
        var context = new ExtrasContext
        {
            TaskId = task.TaskId,
            Aid = task.Aid,
            Bvid = task.Bvid,
            Cid = task.Cid,
            EpId = task.EpId,
            SeasonId = task.SeasonId,
            MediaType = task.MediaType,
            OutputDirectory = task.OutputDirectory,
            SubFolder = task.SubFolder,
            BaseFileName = baseFileName,
            Cookie = cookieHeader,
            CoverUrl = task.CoverUrl,
            ApiService = _apiService,
            ProgressReporter = new Progress<string>(),
        };

        foreach (var handler in handlers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await handler.ExecuteAsync(context, cancellationToken);
                var safeError = SensitiveDataSanitizer.Sanitize(result.ErrorMessage);
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
            }
        }

        return string.Join("; ", results);
    }
}
