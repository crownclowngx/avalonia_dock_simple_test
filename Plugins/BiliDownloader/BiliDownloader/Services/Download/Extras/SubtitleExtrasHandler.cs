using BiliDownloader.Models;
using BiliDownloader.Services.Infrastructure;
using BiliDownloader.Services.Naming;

namespace BiliDownloader.Services.Download.Extras;

/// <summary>
/// 字幕附加资源编排器。目录发现、正文获取和格式转换分别依赖窄接口，
/// 本类只决定选择哪些语言、如何发布文件以及如何形成逐项结果。
/// </summary>
public sealed class SubtitleExtrasHandler : IExtrasHandler
{
    private static readonly IPluginLogger Log = PluginLog.For<SubtitleExtrasHandler>();
    private readonly ISubtitleCatalogService? _catalog;
    private readonly ISubtitleContentProvider? _content;
    private readonly SubtitleFormatterRegistry _formatters;
    private readonly ISubtitleMediaMuxer? _muxer;
    private readonly ISubtitleTrackVerifier? _verifier;

    /// <summary>兼容旧测试和第三方注册路径；生产 DI 使用带依赖构造函数。</summary>
    public SubtitleExtrasHandler() : this(null, null,
        new SubtitleFormatterRegistry([new SrtSubtitleFormatter(), new AssSubtitleFormatter(), new VttSubtitleFormatter()]),
        null, null)
    {
    }

    public SubtitleExtrasHandler(
        ISubtitleCatalogService? catalog,
        ISubtitleContentProvider? content,
        SubtitleFormatterRegistry formatters,
        ISubtitleMediaMuxer? muxer,
        ISubtitleTrackVerifier? verifier)
    {
        _catalog = catalog;
        _content = content;
        _formatters = formatters;
        _muxer = muxer;
        _verifier = verifier;
    }

    public string Type => "subtitle";
    public string DisplayName => "字幕";

    public async Task<ExtrasResult> ExecuteAsync(ExtrasContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (context.Cid == 0) return ExtrasResult.Failed(Type, "视频 cid 为空，无法获取字幕");
        // 处理器被注册表解析即表示旧 ExtrasConfig 已启用字幕；没有结构化快照时保持历史“全部 SRT”。
        var options = (context.SubtitleOptions.SelectionMode == SubtitleSelectionMode.None
            ? SubtitleOptions.LegacyEnabled : context.SubtitleOptions).Canonicalize();

        var catalog = _catalog ?? new SubtitleCatalogService(context.ApiService);
        var content = _content ?? new SubtitleContentProvider(context.ApiService);
        IReadOnlyList<SubtitleTrackDescriptor> tracks;
        try
        {
            context.ProgressReporter?.Report("正在获取字幕列表...");
            tracks = await catalog.GetPreferredTracksAsync(context.Aid, context.Cid, context.Cookie, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var safe = SensitiveDataSanitizer.Sanitize(ex.Message);
            return ExtrasResult.FromItems(Type,
            [
                new ExtrasItemResult("subtitle:catalog", ExtrasItemStatus.Failed,
                    "catalog", $"字幕目录获取失败：{safe}"),
            ]);
        }

        var results = new List<ExtrasItemResult>();
        if (options.SelectionMode == SubtitleSelectionMode.SelectedLanguages)
        {
            var selected = options.LanguageKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            tracks = tracks.Where(track => selected.Contains(track.StableLanguageKey)).ToArray();
            var available = tracks.Select(static track => track.StableLanguageKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            // 部分语言缺失也必须逐项落事实；不能因为同一媒体还有另一条可用语言就把缺失项省略。
            results.AddRange(options.LanguageKeys
                .Where(language => !available.Contains(language))
                .Select(language => new ExtrasItemResult(
                    BuildKey(language, options.OutputFormat, options.DeliveryMode),
                    ExtrasItemStatus.Unavailable,
                    "not_available",
                    "该视频没有所选语言的字幕。")));
        }

        if (tracks.Count == 0)
        {
            if (options.SelectionMode != SubtitleSelectionMode.SelectedLanguages)
                results.Add(new ExtrasItemResult(
                    BuildKey("all", options.OutputFormat, options.DeliveryMode),
                    ExtrasItemStatus.Unavailable,
                    "not_available",
                    "该视频没有可用字幕。"));
            return ExtrasResult.FromItems(Type, results);
        }

        var actualOutputDir = string.IsNullOrEmpty(context.SubFolder)
            ? context.OutputDirectory
            : Path.Combine(context.OutputDirectory, context.SubFolder);
        Directory.CreateDirectory(actualOutputDir);
        var formatter = _formatters.Resolve(options.OutputFormat);
        var requestsSoftMux = options.DeliveryMode is SubtitleDeliveryMode.SoftMuxed
            or SubtitleDeliveryMode.ExternalAndSoftMuxed;
        var tempDirectory = string.IsNullOrWhiteSpace(context.TempDirectory)
            ? Path.Combine(Path.GetTempPath(), "BiliDownloader", context.TaskId)
            : context.TempDirectory;
        if (requestsSoftMux) Directory.CreateDirectory(tempDirectory);
        var prepared = new List<PreparedSubtitle>();

        foreach (var track in tracks)
        {
            ct.ThrowIfCancellationRequested();
            var key = BuildKey(track.StableLanguageKey, options.OutputFormat, options.DeliveryMode);
            var isRequestedRetry = context.RetryItemKeys.Count == 0 || context.RetryItemKeys.Contains(key);
            // 软字幕重试必须重建完整字幕集合：muxer 会丢弃旧字幕轨后重新映射，避免重复轨。
            if (!requestsSoftMux && !isRequestedRetry) continue;
            try
            {
                context.ProgressReporter?.Report($"正在下载字幕：{track.DisplayName}...");
                var cues = await content.GetCuesAsync(track, context.Cookie, ct);
                if (cues.Count == 0)
                {
                    results.Add(new ExtrasItemResult(key, ExtrasItemStatus.Unavailable,
                        "empty", "字幕轨没有有效正文。"));
                    continue;
                }

                var outputFiles = new List<string>();
                var formatted = formatter.Format(cues);
                if ((options.DeliveryMode is SubtitleDeliveryMode.External or SubtitleDeliveryMode.ExternalAndSoftMuxed)
                    && isRequestedRetry)
                {
                    var languagePart = FileNameSanitizer.Sanitize(track.StableLanguageKey);
                    var path = Path.Combine(actualOutputDir,
                        $"{context.BaseFileName}.{languagePart}{formatter.FileExtension}");
                    await ExtrasOutputWriter.WriteTextAsync(path, formatted, context, ct);
                    outputFiles.Add(path);
                }

                if (requestsSoftMux)
                {
                    var tempPath = Path.Combine(tempDirectory,
                        $"subtitle-{FileNameSanitizer.Sanitize(track.StableLanguageKey)}-{options.OutputFormat.ToString().ToLowerInvariant()}{formatter.FileExtension}");
                    await File.WriteAllTextAsync(tempPath, formatted, ct);
                    prepared.Add(new PreparedSubtitle(track, key, tempPath, outputFiles));
                }
                else
                {
                    results.Add(new ExtrasItemResult(key, ExtrasItemStatus.Success, OutputFiles: outputFiles));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var safe = SensitiveDataSanitizer.Sanitize(ex.Message);
                Log.Warn($"字幕 {track.StableLanguageKey} 处理失败：{safe}");
                results.Add(new ExtrasItemResult(key, ExtrasItemStatus.Failed,
                    "subtitle", $"字幕处理失败：{safe}"));
            }
        }

        if (requestsSoftMux && prepared.Count > 0)
            await MuxPreparedSubtitlesAsync(context, options, prepared, results, ct);

        return ExtrasResult.FromItems(Type, results);
    }

    private async Task MuxPreparedSubtitlesAsync(
        ExtrasContext context,
        SubtitleOptions options,
        IReadOnlyList<PreparedSubtitle> prepared,
        ICollection<ExtrasItemResult> results,
        CancellationToken cancellationToken)
    {
        var tracks = prepared.Select(item => new SubtitleMuxTrack(
            item.TempPath,
            item.Track.StableLanguageKey,
            item.Track.DisplayName,
            options.OutputFormat)).ToArray();
        var mainPath = context.MainOutputPath;
        var extension = Path.GetExtension(mainPath);
        var candidate = Path.Combine(
            Path.GetDirectoryName(mainPath) ?? context.OutputDirectory,
            $".{Path.GetFileNameWithoutExtension(mainPath)}.subtitle-staging-{Guid.NewGuid():N}{extension}");
        var backup = candidate + ".backup";
        try
        {
            if (_muxer is null || _verifier is null)
                throw new InvalidOperationException("软字幕封装服务未注册。");
            if (string.IsNullOrWhiteSpace(mainPath) || !File.Exists(mainPath))
                throw new FileNotFoundException("已完成主媒体不存在，不能执行软字幕封装。", mainPath);
            await _muxer.MuxSubtitlesAsync(mainPath, tracks, candidate, context.OutputContainer, cancellationToken);
            await _verifier.VerifyAsync(candidate, tracks, context.OutputContainer, cancellationToken);

            // File.Replace 在同目录使用原子替换；若替换过程失败，原主媒体保持不变，backup 仅作恢复保险。
            File.Replace(candidate, mainPath, backup, ignoreMetadataErrors: true);
            TryDelete(backup);
            foreach (var item in prepared)
                results.Add(new ExtrasItemResult(item.Key, ExtrasItemStatus.Success,
                    OutputFiles: item.OutputFiles));
            foreach (var item in prepared) TryDelete(item.TempPath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var safe = SensitiveDataSanitizer.Sanitize(ex.Message);
            Log.Warn($"软字幕封装失败，已保留原主媒体：{safe}");
            foreach (var item in prepared)
                results.Add(new ExtrasItemResult(item.Key, ExtrasItemStatus.PartialSuccess,
                    "soft_mux", $"软字幕封装失败，已保留可播放主文件：{safe}", item.OutputFiles));
        }
        finally
        {
            TryDelete(candidate);
            // 只有主媒体仍存在时才删除孤立 backup；若极端情况下主文件丢失则先恢复。
            if (!File.Exists(mainPath) && File.Exists(backup)) File.Move(backup, mainPath);
            else TryDelete(backup);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    public static string BuildKey(
        string languageKey, SubtitleOutputFormat format, SubtitleDeliveryMode delivery)
        => $"subtitle:{languageKey.Trim().ToLowerInvariant()}:{format.ToString().ToLowerInvariant()}:{delivery.ToString().ToLowerInvariant()}";

    private sealed record PreparedSubtitle(
        SubtitleTrackDescriptor Track,
        string Key,
        string TempPath,
        IReadOnlyList<string> OutputFiles);
}
