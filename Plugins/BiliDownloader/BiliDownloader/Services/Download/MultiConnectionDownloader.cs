using System.Net;
using System.Net.Http.Headers;
using BiliDownloader.Services.Infrastructure;

namespace BiliDownloader.Services.Download;

/// <summary>
/// 多连接分块下载器。所有可合并数据都必须先通过 Range 与长度验证。
/// </summary>
public sealed class MultiConnectionDownloader : IDisposable
{
    private const int MaxRetries = 3;

    private readonly int _chunkCount;
    private readonly HttpClient _client;
    private readonly IDownloadRuntime _runtime;
    private readonly IBandwidthLimiter _bandwidthLimiter;
    private readonly bool _ownsClient;

    public MultiConnectionDownloader(
        HttpClient client,
        IDownloadRuntime runtime,
        int chunkCount = 4,
        IBandwidthLimiter? bandwidthLimiter = null)
        : this(client, runtime, chunkCount,
            bandwidthLimiter ?? new UnlimitedBandwidthLimiter(), ownsClient: false)
    {
    }

    /// <summary>
    /// 兼容独立使用场景；生产 DI 路径使用显式依赖构造函数。
    /// </summary>
    public MultiConnectionDownloader(int chunkCount = 4)
        : this(
            new BiliHttpClientFactory().CreateMediaClient(),
            new SystemDownloadRuntime(),
            chunkCount,
            new UnlimitedBandwidthLimiter(),
            ownsClient: true)
    {
    }

    private MultiConnectionDownloader(
        HttpClient client,
        IDownloadRuntime runtime,
        int chunkCount,
        IBandwidthLimiter bandwidthLimiter,
        bool ownsClient)
    {
        _client = client;
        _runtime = runtime;
        _bandwidthLimiter = bandwidthLimiter;
        _chunkCount = Math.Max(1, chunkCount);
        _ownsClient = ownsClient;
    }

    public async Task<DownloadTransferResult> DownloadAsync(
        List<string> urls,
        string outputPath,
        string cookie,
        Action<long, long, string> onProgress,
        CancellationToken ct)
        => await DownloadAsync(urls, outputPath, cookie, "standalone", onProgress, ct);

    /// <summary>
    /// 下载属于指定任务的媒体流。taskId 只用于公平调度和诊断，不进入 HTTP 请求或文件名。
    /// </summary>
    public async Task<DownloadTransferResult> DownloadAsync(
        List<string> urls,
        string outputPath,
        string cookie,
        string taskId,
        Action<long, long, string> onProgress,
        CancellationToken ct)
    {
        if (urls is null || urls.Count == 0)
        {
            throw new ArgumentException("至少需要一个下载 URL", nameof(urls));
        }

        if (urls.Count == 1 || _chunkCount <= 1)
        {
            return await DownloadSingleAsync(urls[0], outputPath, cookie, taskId, onProgress, ct);
        }

        var totalSize = await GetContentLengthAsync(urls[0], cookie, ct);
        if (totalSize <= 0)
        {
            return await DownloadSingleAsync(urls[0], outputPath, cookie, taskId, onProgress, ct);
        }

        var chunkCount = Math.Min(
            _chunkCount,
            Math.Max(1, (int)Math.Ceiling((double)totalSize / (1024 * 1024))));
        var chunkSize = totalSize / chunkCount;
        var downloadedTotal = 0L;
        var lastReportedBytes = 0L;
        var lastReportTime = _runtime.UtcNow;
        var progressLock = new object();

        void AddProgress(long delta)
        {
            var current = Interlocked.Add(ref downloadedTotal, delta);
            lock (progressLock)
            {
                var now = _runtime.UtcNow;
                var elapsed = (now - lastReportTime).TotalSeconds;
                if (elapsed < 0.5)
                {
                    return;
                }

                var bytesPerSecond = (current - lastReportedBytes) / elapsed;
                lastReportedBytes = current;
                lastReportTime = now;
                onProgress(totalSize, current, FormatSpeed((long)bytesPerSecond));
            }
        }

        var tasks = new List<Task>(chunkCount);
        for (var chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
        {
            var start = chunkIndex * chunkSize;
            var end = chunkIndex == chunkCount - 1
                ? totalSize - 1
                : (chunkIndex + 1) * chunkSize - 1;
            var chunkPath = $"{outputPath}.chunk{chunkIndex}";
            var expectedChunkLength = end - start + 1;

            if (File.Exists(chunkPath))
            {
                var existingLength = new FileInfo(chunkPath).Length;
                if (existingLength > expectedChunkLength)
                {
                    File.Delete(chunkPath);
                }
                else
                {
                    Interlocked.Add(ref downloadedTotal, existingLength);
                }
            }

            tasks.Add(DownloadChunkAsync(
                urls,
                chunkIndex,
                chunkPath,
                cookie,
                start,
                end,
                totalSize,
                taskId,
                AddProgress,
                ct));
        }

        lastReportedBytes = Interlocked.Read(ref downloadedTotal);
        try
        {
            await Task.WhenAll(tasks);
            await MergeChunksAsync(outputPath, chunkCount, totalSize, ct);
        }
        catch
        {
            DeleteIfExists(outputPath + ".merging");
            throw;
        }

        for (var i = 0; i < chunkCount; i++)
        {
            DeleteIfExists($"{outputPath}.chunk{i}");
        }

        onProgress(totalSize, totalSize, "");
        return new DownloadTransferResult(totalSize, totalSize, IntegrityPassed: true);
    }

    private async Task DownloadChunkAsync(
        List<string> urls,
        int chunkIndex,
        string chunkPath,
        string cookie,
        long rangeStart,
        long rangeEnd,
        long expectedTotalLength,
        string taskId,
        Action<long> onBytesDelta,
        CancellationToken ct)
    {
        Exception? lastException = null;
        for (var attempt = 0; attempt < MaxRetries; attempt++)
        {
            var url = urls[(chunkIndex + attempt) % urls.Count];
            try
            {
                await DownloadChunkOnceAsync(
                    url,
                    chunkPath,
                    cookie,
                    rangeStart,
                    rangeEnd,
                    expectedTotalLength,
                    taskId,
                    onBytesDelta,
                    ct);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
                if (attempt < MaxRetries - 1)
                {
                    await _runtime.DelayForRetryAsync(attempt, ct);
                }
            }
        }

        if (lastException is DownloadProtocolException protocolException)
        {
            throw new DownloadProtocolException(
                $"Chunk {chunkIndex} 协议验证失败（已重试 {MaxRetries} 次）: {protocolException.Message}",
                protocolException);
        }

        throw new IOException(
            $"Chunk {chunkIndex} 下载失败（已重试 {MaxRetries} 次）: {lastException?.Message}",
            lastException);
    }

    private async Task DownloadChunkOnceAsync(
        string url,
        string chunkPath,
        string cookie,
        long rangeStart,
        long rangeEnd,
        long expectedTotalLength,
        string taskId,
        Action<long> onBytesDelta,
        CancellationToken ct)
    {
        var expectedChunkLength = rangeEnd - rangeStart + 1;
        var existingBytes = File.Exists(chunkPath) ? new FileInfo(chunkPath).Length : 0;
        if (existingBytes > expectedChunkLength)
        {
            DeleteIfExists(chunkPath);
            existingBytes = 0;
        }
        if (existingBytes == expectedChunkLength)
        {
            return;
        }

        var actualStart = rangeStart + existingBytes;
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        AddCookie(request, cookie);
        request.Headers.Range = new RangeHeaderValue(actualStart, rangeEnd);

        long writtenThisAttempt = 0;
        var rangeValidated = false;
        try
        {
            using var response = await _client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                ct);
            if (response.StatusCode != HttpStatusCode.PartialContent)
            {
                throw new DownloadProtocolException(
                    $"CDN 返回 {(int)response.StatusCode} 而非 206 Partial Content");
            }

            var expectedResponseLength = rangeEnd - actualStart + 1;
            ValidateContentRange(
                response.Content.Headers.ContentRange,
                actualStart,
                rangeEnd,
                expectedTotalLength);
            ValidateDeclaredContentLength(
                response.Content.Headers.ContentLength,
                expectedResponseLength);
            rangeValidated = true;

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            await using var fileStream = new FileStream(
                chunkPath,
                existingBytes > 0 ? FileMode.Append : FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                8192,
                useAsync: true);
            var buffer = new byte[8192];
            while (true)
            {
                var remaining = expectedResponseLength - writtenThisAttempt;
                if (remaining == 0) break;
                var plannedRead = (int)Math.Min(buffer.Length, remaining);
                await _bandwidthLimiter.AcquireAsync(plannedRead, taskId, ct);
                var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, plannedRead), ct);
                if (bytesRead == 0)
                {
                    break;
                }

                if (writtenThisAttempt + bytesRead > expectedResponseLength)
                {
                    throw new DownloadProtocolException(
                        $"Range 响应体超过声明区间，期望 {expectedResponseLength} 字节");
                }

                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                writtenThisAttempt += bytesRead;
                onBytesDelta(bytesRead);
            }

            if (writtenThisAttempt != expectedResponseLength)
            {
                throw new DownloadProtocolException(
                    $"Range 响应体长度错误，期望 {expectedResponseLength}，实际 {writtenThisAttempt}");
            }
        }
        catch (Exception ex)
        {
            RollbackFile(chunkPath, existingBytes);
            if (writtenThisAttempt != 0)
            {
                onBytesDelta(-writtenThisAttempt);
            }
            if (rangeValidated
                && ex is IOException
                && ex is not DownloadProtocolException)
            {
                throw new DownloadProtocolException(
                    $"Range 响应在达到预期长度前中断，已读取 {writtenThisAttempt} 字节",
                    ex);
            }
            throw;
        }
    }

    private async Task<long> GetContentLengthAsync(
        string url,
        string cookie,
        CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            AddCookie(request, cookie);
            using var response = await _client.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            return response.Content.Headers.ContentLength ?? -1;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return -1;
        }
    }

    private async Task<DownloadTransferResult> DownloadSingleAsync(
        string url,
        string outputPath,
        string cookie,
        string taskId,
        Action<long, long, string> onProgress,
        CancellationToken ct)
    {
        var existingBytes = File.Exists(outputPath) ? new FileInfo(outputPath).Length : 0;
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        AddCookie(request, cookie);
        if (existingBytes > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existingBytes, null);
        }

        using var response = await _client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            ct);
        response.EnsureSuccessStatusCode();

        var append = existingBytes > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        long expectedTotal;
        long expectedResponseLength;
        if (append)
        {
            var contentRange = response.Content.Headers.ContentRange;
            if (contentRange?.Length is not long totalLength)
            {
                throw new DownloadProtocolException("续传响应缺少 Content-Range 总长度");
            }
            if (contentRange.To is not long responseEnd)
            {
                throw new DownloadProtocolException("续传响应缺少 Content-Range 终点");
            }

            ValidateContentRange(contentRange, existingBytes, totalLength - 1, totalLength);
            expectedTotal = totalLength;
            expectedResponseLength = responseEnd - existingBytes + 1;
            ValidateDeclaredContentLength(
                response.Content.Headers.ContentLength,
                expectedResponseLength);
        }
        else if (response.StatusCode == HttpStatusCode.PartialContent)
        {
            var contentRange = response.Content.Headers.ContentRange;
            if (contentRange?.Length is not long totalLength)
            {
                throw new DownloadProtocolException("206 响应缺少 Content-Range 总长度");
            }
            ValidateContentRange(contentRange, 0, totalLength - 1, totalLength);
            expectedTotal = totalLength;
            expectedResponseLength = totalLength;
            ValidateDeclaredContentLength(
                response.Content.Headers.ContentLength,
                expectedResponseLength);
        }
        else
        {
            // 服务器忽略 Range 返回 200 时明确从头覆盖。
            existingBytes = 0;
            expectedResponseLength = response.Content.Headers.ContentLength ?? 0;
            expectedTotal = expectedResponseLength;
        }

        var initialLength = append ? existingBytes : 0;
        long writtenThisAttempt = 0;
        var lastBytes = existingBytes;
        var lastTime = _runtime.UtcNow;
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            await using var fileStream = new FileStream(
                outputPath,
                append ? FileMode.Append : FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                8192,
                useAsync: true);
            var buffer = new byte[8192];
            while (true)
            {
                var remaining = expectedResponseLength > 0
                    ? expectedResponseLength - writtenThisAttempt
                    : buffer.Length;
                if (remaining == 0) break;
                var plannedRead = (int)Math.Min(buffer.Length, remaining);
                await _bandwidthLimiter.AcquireAsync(plannedRead, taskId, ct);
                var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, plannedRead), ct);
                if (bytesRead == 0)
                {
                    break;
                }
                if (expectedResponseLength > 0
                    && writtenThisAttempt + bytesRead > expectedResponseLength)
                {
                    throw new DownloadProtocolException(
                        $"响应体超过声明长度 {expectedResponseLength}");
                }

                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                writtenThisAttempt += bytesRead;
                var downloaded = existingBytes + writtenThisAttempt;
                var now = _runtime.UtcNow;
                var elapsed = (now - lastTime).TotalSeconds;
                var speed = "";
                if (elapsed >= 0.5)
                {
                    speed = FormatSpeed((long)((downloaded - lastBytes) / elapsed));
                    lastBytes = downloaded;
                    lastTime = now;
                }
                onProgress(expectedTotal > 0 ? expectedTotal : -1, downloaded, speed);
            }

            if (expectedResponseLength > 0 && writtenThisAttempt != expectedResponseLength)
            {
                throw new DownloadProtocolException(
                    $"响应体长度错误，期望 {expectedResponseLength}，实际 {writtenThisAttempt}");
            }
        }
        catch
        {
            RollbackFile(outputPath, initialLength);
            throw;
        }

        var actualBytes = existingBytes + writtenThisAttempt;
        var integrityPassed = expectedTotal > 0 && actualBytes == expectedTotal;
        if (expectedTotal > 0 && !integrityPassed)
        {
            RollbackFile(outputPath, initialLength);
            throw new DownloadProtocolException(
                $"最终文件长度错误，期望 {expectedTotal}，实际 {actualBytes}");
        }

        onProgress(expectedTotal > 0 ? expectedTotal : -1, actualBytes, "");
        return new DownloadTransferResult(expectedTotal, actualBytes, integrityPassed);
    }

    private static async Task MergeChunksAsync(
        string outputPath,
        int chunkCount,
        long expectedTotalLength,
        CancellationToken ct)
    {
        long sum = 0;
        for (var i = 0; i < chunkCount; i++)
        {
            var chunkPath = $"{outputPath}.chunk{i}";
            if (!File.Exists(chunkPath))
            {
                throw new DownloadProtocolException($"Chunk 文件缺失: {chunkPath}");
            }
            sum += new FileInfo(chunkPath).Length;
        }
        if (sum != expectedTotalLength)
        {
            throw new DownloadProtocolException(
                $"Chunk 总长度错误，期望 {expectedTotalLength}，实际 {sum}");
        }

        var stagingPath = outputPath + ".merging";
        DeleteIfExists(stagingPath);
        try
        {
            await using (var outputStream = new FileStream(
                stagingPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                8192,
                useAsync: true))
            {
                var buffer = new byte[8192];
                for (var i = 0; i < chunkCount; i++)
                {
                    await using var chunkStream = new FileStream(
                        $"{outputPath}.chunk{i}",
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        8192,
                        useAsync: true);
                    while (true)
                    {
                        var bytesRead = await chunkStream.ReadAsync(buffer, ct);
                        if (bytesRead == 0)
                        {
                            break;
                        }
                        await outputStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    }
                }
            }

            if (new FileInfo(stagingPath).Length != expectedTotalLength)
            {
                throw new DownloadProtocolException("合并后的文件长度与预期总长度不一致");
            }
            File.Move(stagingPath, outputPath, overwrite: true);
        }
        catch
        {
            DeleteIfExists(stagingPath);
            throw;
        }
    }

    private static void ValidateContentRange(
        ContentRangeHeaderValue? contentRange,
        long expectedFrom,
        long expectedTo,
        long expectedTotalLength)
    {
        if (contentRange is null
            || !string.Equals(contentRange.Unit, "bytes", StringComparison.OrdinalIgnoreCase)
            || contentRange.From != expectedFrom
            || contentRange.To != expectedTo
            || contentRange.Length != expectedTotalLength)
        {
            throw new DownloadProtocolException(
                $"Content-Range 不匹配，期望 bytes {expectedFrom}-{expectedTo}/{expectedTotalLength}，"
                + $"实际 {contentRange?.ToString() ?? "<missing>"}");
        }
    }

    private static void ValidateDeclaredContentLength(long? declared, long expected)
    {
        if (declared is long actual && actual != expected)
        {
            throw new DownloadProtocolException(
                $"Content-Length 不匹配，期望 {expected}，实际 {actual}");
        }
    }

    private static void AddCookie(HttpRequestMessage request, string cookie)
    {
        if (!string.IsNullOrWhiteSpace(cookie))
        {
            request.Headers.Add("Cookie", cookie);
        }
    }

    private static void RollbackFile(string path, long length)
    {
        if (length == 0)
        {
            DeleteIfExists(path);
            return;
        }
        if (!File.Exists(path))
        {
            return;
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
        stream.SetLength(length);
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // 临时文件清理失败不覆盖原始协议异常。
        }
    }

    private static string FormatSpeed(long bytesPerSecond)
    {
        if (bytesPerSecond <= 0) return "";
        if (bytesPerSecond < 1024) return $"{bytesPerSecond} B/s";
        if (bytesPerSecond < 1024 * 1024) return $"{bytesPerSecond / 1024.0:F1} KB/s";
        return $"{bytesPerSecond / (1024.0 * 1024):F1} MB/s";
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _client.Dispose();
        }
    }
}
