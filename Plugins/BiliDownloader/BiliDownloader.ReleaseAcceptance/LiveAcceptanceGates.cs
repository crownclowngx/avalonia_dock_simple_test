using System.Net;
using System.Net.Http.Headers;
using BiliDownloader.Models;
using BiliDownloader.Services.Api;
using BiliDownloader.Services.Auth;
using BiliDownloader.Services.Infrastructure;
using BiliDownloader.Services.Persistence;
using Microsoft.Data.Sqlite;

namespace BiliDownloader.ReleaseAcceptance;

internal sealed record LiveMediaDescriptor(string VideoUrl, string AudioUrl);

/// <summary>真实下载、固定摘要校验、版本探测与原子激活门禁。</summary>
internal sealed class LiveFfmpegInstallationGate : IReleaseGate
{
    public string Name => "live-ffmpeg-installation";

    public async Task<ReleaseGateResult> ExecuteAsync(
        ReleaseGateContext context,
        CancellationToken cancellationToken)
    {
        var paths = new AcceptanceDataPaths(Path.Combine(context.SandboxRoot, "live"));
        Directory.CreateDirectory(paths.DataDirectory);
        Directory.CreateDirectory(paths.TempDirectory);
        var locator = new FfmpegService(new FfmpegProcessFactory(), paths);
        using var downloader = new HttpFfmpegPackageDownloader();
        var installer = new FfmpegPackageInstaller(
            downloader,
            locator,
            paths,
            new SystemFfmpegInstallPlatform(),
            FfmpegPackageManifest.GyanReleaseEssentials812);
        var result = await installer.InstallOrRepairAsync(cancellationToken);
        if (!result.Success || string.IsNullOrWhiteSpace(result.ExecutablePath))
            return ReleaseGateResult.Fail(Name, "真实 ffmpeg 包未能通过下载、校验和激活。");

        var detected = await locator.DetectAsync(cancellationToken);
        if (!detected.IsReady || string.IsNullOrWhiteSpace(detected.Version))
            return ReleaseGateResult.Fail(Name, "安装后的 ffmpeg 未通过独立版本探测。");

        context.Items["ffmpeg"] = locator;
        context.Items["data-paths"] = paths;
        return ReleaseGateResult.Pass(
            Name,
            "固定 ffmpeg 包已通过 SHA-256、解压、探测和原子激活。",
            new Dictionary<string, object?> { ["version"] = detected.Version });
    }
}

/// <summary>真实账号验证、Bilibili API、DASH 下载与 ffmpeg 合并门禁。</summary>
internal sealed class LiveBilibiliDownloadGate : IReleaseGate
{
    public string Name => "live-bilibili-download";

    public async Task<ReleaseGateResult> ExecuteAsync(
        ReleaseGateContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.Bvid) || string.IsNullOrWhiteSpace(context.Cookie))
            return ReleaseGateResult.Fail(Name, "正式联网门禁缺少测试 BVID 或临时 Cookie。");
        if (!context.Items.TryGetValue("ffmpeg", out var rawFfmpeg)
            || rawFfmpeg is not FfmpegService ffmpeg)
            return ReleaseGateResult.Fail(Name, "ffmpeg 门禁没有提供已验证运行时。");

        var validation = await new BiliLoginService().CheckLoginAsync(context.Cookie, cancellationToken);
        if (validation.Status != LoginValidationStatus.Valid)
            return ReleaseGateResult.Fail(Name, "临时 Cookie 未通过真实账号验证。");

        var parsed = BiliApiService.ParseVideoId(context.Bvid);
        if (parsed is null || !parsed.Value.IsBvid)
            return ReleaseGateResult.Fail(Name, "BILIDOWNLOADER_G8_TEST_BVID 必须是公开 BV 号。");

        var api = new BiliApiService();
        var collection = await api.GetVideoCollectionAsync(parsed.Value.Id, true, context.Cookie);
        var item = collection.Items.FirstOrDefault();
        if (item is null)
            return ReleaseGateResult.Fail(Name, "真实视频解析没有返回可验收分集。");
        var dash = await api.GetDashResultAsync(
            item.Aid,
            item.Cid,
            16,
            context.Cookie,
            item.MediaType,
            item.EpId,
            item.SeasonId);
        var video = dash.VideoStreams.OrderBy(stream => stream.Bandwidth).FirstOrDefault();
        var audio = dash.AudioStreams.OrderBy(stream => stream.Bandwidth).FirstOrDefault();
        if (video is null || audio is null
            || string.IsNullOrWhiteSpace(video.BaseUrl)
            || string.IsNullOrWhiteSpace(audio.BaseUrl))
            return ReleaseGateResult.Fail(Name, "真实 DASH 响应缺少可下载的音视频流。");

        var mediaRoot = Path.Combine(context.SandboxRoot, "live", "media");
        Directory.CreateDirectory(mediaRoot);
        var videoPath = Path.Combine(mediaRoot, "video.m4s");
        var audioPath = Path.Combine(mediaRoot, "audio.m4s");
        var outputPath = Path.Combine(mediaRoot, "merged.mp4");
        using var client = new BiliHttpClientFactory().CreateMediaClient();
        await DownloadToFileAsync(client, video.BaseUrl, videoPath, context.Cookie, cancellationToken);
        await DownloadToFileAsync(client, audio.BaseUrl, audioPath, context.Cookie, cancellationToken);
        await ffmpeg.MergeAsync(videoPath, audioPath, outputPath, cancellationToken);
        var outputLength = new FileInfo(outputPath).Length;
        if (outputLength <= 0)
            return ReleaseGateResult.Fail(Name, "真实 ffmpeg 合并没有生成有效输出。");

        // 签名 CDN URL 只在内存中交给后续 Range 门禁，绝不进入报告指标。
        context.Items["live-media"] = new LiveMediaDescriptor(video.BaseUrl, audio.BaseUrl);
        return ReleaseGateResult.Pass(
            Name,
            "真实账号、视频解析、DASH 下载和 ffmpeg 合并全部通过。",
            new Dictionary<string, object?>
            {
                ["bvid"] = parsed.Value.Id,
                ["durationSeconds"] = item.Duration,
                ["outputBytes"] = outputLength,
            });
    }

    private static async Task DownloadToFileAsync(
        HttpClient client,
        string url,
        string destination,
        string cookie,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Cookie", cookie);
        using var response = await client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(
            destination, FileMode.Create, FileAccess.Write, FileShare.None,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await input.CopyToAsync(output, cancellationToken);
        await output.FlushAsync(cancellationToken);
    }
}

/// <summary>
/// 对真实 CDN 执行二十次分段中断再续传。每次只取至多 1 MiB 的同一前缀，
/// 用基准 Range 的字节事实校验拼接结果，避免为了统计恢复率重复下载完整视频。
/// </summary>
internal sealed class LiveRangeRecoveryGate : IReleaseGate
{
    private const int Attempts = 20;
    private const int MaximumSampleBytes = 1024 * 1024;
    public string Name => "live-range-recovery";

    public async Task<ReleaseGateResult> ExecuteAsync(
        ReleaseGateContext context,
        CancellationToken cancellationToken)
    {
        if (!context.Items.TryGetValue("live-media", out var rawMedia)
            || rawMedia is not LiveMediaDescriptor media
            || string.IsNullOrWhiteSpace(context.Cookie))
            return ReleaseGateResult.Fail(Name, "真实媒体门禁没有提供 Range 验收输入。");

        using var client = new BiliHttpClientFactory().CreateMediaClient();
        var firstByte = await FetchRangeAsync(client, media.VideoUrl, context.Cookie, 0, 0, cancellationToken);
        var total = firstByte.TotalLength;
        if (total is null or < 2)
            return ReleaseGateResult.Fail(Name, "CDN 未返回可用于恢复验收的总长度。");
        var sampleLength = (int)Math.Min(total.Value, MaximumSampleBytes);
        var baseline = (await FetchRangeAsync(
            client, media.VideoUrl, context.Cookie, 0, sampleLength - 1, cancellationToken)).Bytes;
        if (baseline.Length != sampleLength)
            return ReleaseGateResult.Fail(Name, "基准 Range 长度与 CDN 声明不一致。");

        var succeeded = 0;
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var split = Math.Clamp(sampleLength * (attempt + 1) / (Attempts + 1), 1, sampleLength - 1);
            try
            {
                // 首次请求声明整个样本区间，但读取 split 字节后主动释放响应，制造真实的
                // 传输中断；第二次请求再从磁盘应记录的位置继续，而不是预先请求两个完整小块。
                var prefix = await FetchInterruptedPrefixAsync(
                    client,
                    media.VideoUrl,
                    context.Cookie,
                    sampleLength - 1,
                    split,
                    cancellationToken);
                var suffix = (await FetchRangeAsync(
                    client, media.VideoUrl, context.Cookie, split, sampleLength - 1, cancellationToken)).Bytes;
                var combined = new byte[prefix.Length + suffix.Length];
                prefix.CopyTo(combined, 0);
                suffix.CopyTo(combined, prefix.Length);
                if (combined.AsSpan().SequenceEqual(baseline)) succeeded++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // 单次真实网络失败进入成功率统计；异常原文可能含签名 URL，因此不得写入报告。
            }
        }

        var rate = succeeded * 100d / Attempts;
        var metrics = new Dictionary<string, object?>
        {
            ["attempts"] = Attempts,
            ["succeeded"] = succeeded,
            ["successRate"] = rate,
            ["sampleBytes"] = sampleLength,
        };
        return succeeded >= 19
            ? ReleaseGateResult.Pass(Name, "真实 Range 中断恢复率达到 95% 门槛。", metrics)
            : ReleaseGateResult.Fail(Name, "真实 Range 中断恢复率低于 95%。", metrics);
    }

    private static async Task<(byte[] Bytes, long? TotalLength)> FetchRangeAsync(
        HttpClient client,
        string url,
        string cookie,
        long from,
        long to,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new RangeHeaderValue(from, to);
        request.Headers.TryAddWithoutValidation("Cookie", cookie);
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (response.StatusCode != HttpStatusCode.PartialContent)
            throw new InvalidDataException("CDN 没有返回 HTTP 206。");
        var range = response.Content.Headers.ContentRange;
        if (range?.From != from || range.To != to)
            throw new InvalidDataException("CDN Content-Range 与请求区间不一致。");
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.LongLength != to - from + 1)
            throw new InvalidDataException("CDN Range 响应体长度不一致。");
        return (bytes, range.Length);
    }

    private static async Task<byte[]> FetchInterruptedPrefixAsync(
        HttpClient client,
        string url,
        string cookie,
        long requestedTo,
        int bytesToKeep,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new RangeHeaderValue(0, requestedTo);
        request.Headers.TryAddWithoutValidation("Cookie", cookie);
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (response.StatusCode != HttpStatusCode.PartialContent)
            throw new InvalidDataException("CDN 没有返回 HTTP 206。");
        var range = response.Content.Headers.ContentRange;
        if (range?.From != 0 || range.To != requestedTo)
            throw new InvalidDataException("中断样本的 Content-Range 与请求区间不一致。");

        var prefix = new byte[bytesToKeep];
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var offset = 0;
        while (offset < prefix.Length)
        {
            var read = await stream.ReadAsync(prefix.AsMemory(offset), cancellationToken);
            if (read == 0) throw new EndOfStreamException("CDN 在预定中断点之前结束响应。");
            offset += read;
        }

        // 离开 using 块会在响应仍有未读字节时关闭流，下一次请求即代表进程按已保存长度恢复。
        return prefix;
    }
}

/// <summary>
/// 在验收沙箱中走一遍真实生产持久化边界，生成任务库、凭据库、Document 和日志证据。
/// 这里故意保存本次真实 Cookie：只有实际明文进入 AES-GCM 存储，后续原始字节扫描
/// 才能证明“没有泄漏”而不是证明“测试样本里从未出现过凭据”。所有文件均位于隔离目录。
/// </summary>
internal sealed class LivePersistenceEvidenceGate : IReleaseGate
{
    public string Name => "live-persistence-evidence";

    public async Task<ReleaseGateResult> ExecuteAsync(
        ReleaseGateContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.Cookie)
            || !context.Items.TryGetValue("data-paths", out var rawPaths)
            || rawPaths is not AcceptanceDataPaths paths)
        {
            return ReleaseGateResult.Fail(Name, "真实门禁没有提供隔离路径或临时凭据。");
        }

        Directory.CreateDirectory(paths.DataDirectory);
        Directory.CreateDirectory(paths.LogDirectory);
        var cookies = ParseCookieHeader(context.Cookie);
        if (cookies.Count == 0)
            return ReleaseGateResult.Fail(Name, "临时 Cookie 未包含可持久化的名称和值。");

        var credentialStore = new BiliCredentialStore(
            paths,
            new AesGcmCredentialProtector(new InstallationKeyStore(paths)));
        await credentialStore.SaveSessionAsync(
            new BiliCredentialSession(cookies, "G8 验收账号", null),
            cancellationToken);
        var restored = await credentialStore.LoadSessionAsync(cancellationToken);
        if (restored is null || !restored.Cookies.SequenceEqual(cookies))
            return ReleaseGateResult.Fail(Name, "凭据密文未能从生产存储边界正确往返。");

        var taskStore = new DownloadTaskStore(paths);
        await taskStore.InitAsync();
        await taskStore.InsertBatchAsync(
        [
            new DownloadTaskRecord
            {
                TaskId = "g8-evidence-task",
                DocumentId = "g8-evidence-document",
                SourceDocumentTitle = "G8 验收",
                SeriesTitle = "G8 验收",
                ItemTitle = "脱敏持久化证据",
                Bvid = context.Bvid ?? string.Empty,
                QualityId = 16,
                OutputDirectory = paths.TempDirectory,
                Status = DownloadTaskStatusMapper.ToStorageString(DownloadTaskStatus.Completed),
                ErrorMessage = "SESSDATA=[REDACTED]",
            },
        ]);

        var documentPath = Path.Combine(paths.DataDirectory, "g8-document-v2.json");
        await File.WriteAllTextAsync(
            documentPath,
            $$"""{"schemaVersion":2,"sourceBvid":"{{context.Bvid}}","credentialsOmitted":true}""",
            cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(paths.LogDirectory, "g8-acceptance.log"),
            "G8 持久化证据已创建；SESSDATA=[REDACTED]。",
            cancellationToken);

        // Microsoft.Data.Sqlite 默认连接池可能继续持有 WAL 文件；扫描前主动清池，确保磁盘事实稳定。
        SqliteConnection.ClearAllPools();
        return ReleaseGateResult.Pass(
            Name,
            "任务库、加密凭据库、Document 和日志证据已在隔离目录生成。",
            new Dictionary<string, object?>
            {
                ["cookies"] = cookies.Count,
                ["evidenceKinds"] = 4,
            });
    }

    private static IReadOnlyList<BiliCredentialCookie> ParseCookieHeader(string cookieHeader)
    {
        return cookieHeader
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(segment => (Segment: segment, Separator: segment.IndexOf('=')))
            .Where(part => part.Separator > 0 && part.Separator < part.Segment.Length - 1)
            .Select(part => new BiliCredentialCookie(
                part.Segment[..part.Separator].Trim(),
                part.Segment[(part.Separator + 1)..].Trim()))
            .Where(cookie => cookie.Name.Length > 0 && cookie.Value.Length > 0)
            .OrderBy(cookie => cookie.Name, StringComparer.Ordinal)
            .ToArray();
    }
}
