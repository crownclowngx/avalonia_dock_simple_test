using System.Net.Http.Headers;

namespace BiliDownloader.Services.Download;

/// <summary>
/// 多连接分块并行下载器
/// 将文件分成多个 chunk，每个 chunk 使用不同 CDN URL 并行 Range 请求下载，叠加带宽
/// </summary>
public class MultiConnectionDownloader : IDisposable
{
    /// <summary>
    /// 默认分块数
    /// </summary>
    private readonly int _chunkCount;

    /// <summary>
    /// 每个连接一个 HttpClient（避免连接池争用）
    /// </summary>
    private readonly HttpClient[] _clients;

    public MultiConnectionDownloader(int chunkCount = 4)
    {
        _chunkCount = Math.Max(1, chunkCount);
        _clients = new HttpClient[_chunkCount];
        for (int i = 0; i < _chunkCount; i++)
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", HttpConstants.UserAgent);
            client.DefaultRequestHeaders.Add("Referer", HttpConstants.Referer);
            client.DefaultRequestHeaders.Add("Origin", HttpConstants.Origin);
            client.Timeout = TimeSpan.FromMinutes(60);
            _clients[i] = client;
        }
    }

    /// <summary>
    /// 多线程分块下载
    /// </summary>
    /// <param name="urls">多个 CDN URL（Round-Robin 分配给各 chunk）</param>
    /// <param name="outputPath">最终输出文件路径</param>
    /// <param name="cookie">Cookie</param>
    /// <param name="onProgress">进度回调 (总字节, 已下载字节, 速度文本)</param>
    /// <param name="ct">取消令牌</param>
    public async Task DownloadAsync(
        List<string> urls,
        string outputPath,
        string cookie,
        Action<long, long, string> onProgress,
        CancellationToken ct)
    {
        if (urls == null || urls.Count == 0)
            throw new ArgumentException("至少需要一个下载 URL", nameof(urls));

        // 只有一个 URL 或分块数为 1：退化为单连接
        if (urls.Count == 1 || _chunkCount <= 1)
        {
            await DownloadSingleAsync(urls[0], outputPath, cookie, onProgress, ct);
            return;
        }

        // 1. HEAD 请求获取总文件大小
        var primaryUrl = urls[0];
        long totalSize = await GetContentLengthAsync(primaryUrl, cookie, ct);
        if (totalSize <= 0)
        {
            // 不支持 HEAD 或无法获取大小，回退到单连接
            await DownloadSingleAsync(primaryUrl, outputPath, cookie, onProgress, ct);
            return;
        }

        // 2. 计算 chunk 边界
        var chunkCount = Math.Min(_chunkCount, (int)Math.Ceiling((double)totalSize / (1024 * 1024)));
        if (chunkCount < 1) chunkCount = 1;
        var chunkSize = totalSize / chunkCount;

        // 3. 启动并行下载
        var downloadedTotal = 0L; // Interlocked 汇总
        var lastReportedBytes = 0L;
        var lastReportTime = DateTime.UtcNow;
        var speedText = "";
        var lockObj = new object();

        void ReportProgress()
        {
            var now = DateTime.UtcNow;
            var elapsed = (now - lastReportTime).TotalSeconds;
            if (elapsed >= 0.5)
            {
                var current = Interlocked.Read(ref downloadedTotal);
                var bytesPerSecond = (current - lastReportedBytes) / elapsed;
                speedText = FormatSpeed((long)bytesPerSecond);
                lastReportedBytes = current;
                lastReportTime = now;
                onProgress(totalSize, current, speedText);
            }
        }

        var tasks = new Task[chunkCount];
        for (int i = 0; i < chunkCount; i++)
        {
            var chunkIndex = i;
            var start = chunkIndex * chunkSize;
            var end = (chunkIndex == chunkCount - 1) ? totalSize - 1 : (chunkIndex + 1) * chunkSize - 1;
            var chunkPath = $"{outputPath}.chunk{chunkIndex}";

            tasks[chunkIndex] = DownloadChunkAsync(
                urls, chunkIndex, chunkPath, cookie, start, end,
                (bytesDownloaded) =>
                {
                    Interlocked.Add(ref downloadedTotal, bytesDownloaded);
                    ReportProgress();
                },
                _clients[chunkIndex],
                ct);
        }

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            // 取消时保留已下载的 chunk 文件，下次续传
            throw;
        }

        // 4. 合并 chunk 文件
        await MergeChunksAsync(outputPath, chunkCount, ct);

        // 5. 清理 chunk 临时文件
        for (int i = 0; i < chunkCount; i++)
        {
            var chunkPath = $"{outputPath}.chunk{i}";
            try { if (File.Exists(chunkPath)) File.Delete(chunkPath); }
            catch { /* 忽略 */ }
        }

        // 最终进度报告
        onProgress(totalSize, totalSize, "");
    }

    /// <summary>
    /// 下载单个 chunk：支持 CDN 回退重试（最多 3 次，指数退避）
    /// </summary>
    private async Task DownloadChunkAsync(
        List<string> urls,
        int chunkIndex,
        string chunkPath,
        string cookie,
        long rangeStart,
        long rangeEnd,
        Action<long> onBytesDelta,
        HttpClient client,
        CancellationToken ct)
    {
        const int maxRetries = 3;
        Exception? lastException = null;

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            // Round-Robin 选择 CDN URL，每次重试切换不同 CDN
            var url = urls[(chunkIndex + attempt) % urls.Count];

            try
            {
                await DownloadChunkOnceAsync(url, chunkPath, cookie, rangeStart, rangeEnd, onBytesDelta, client, ct);
                return; // 成功
            }
            catch (OperationCanceledException)
            {
                throw; // 取消直接传播
            }
            catch (Exception ex)
            {
                lastException = ex;

                if (attempt < maxRetries - 1)
                {
                    // 指数退避 + 随机 jitter
                    var delayMs = (int)(Math.Pow(2, attempt) * 1000) + Random.Shared.Next(0, 500);
                    await Task.Delay(delayMs, ct);
                }
            }
        }

        throw new Exception($"Chunk {chunkIndex} 下载失败（已重试 {maxRetries} 次）: {lastException?.Message}", lastException);
    }

    /// <summary>
    /// 单次 chunk 下载尝试（无重试逻辑）
    /// </summary>
    private static async Task DownloadChunkOnceAsync(
        string url,
        string chunkPath,
        string cookie,
        long rangeStart,
        long rangeEnd,
        Action<long> onBytesDelta,
        HttpClient client,
        CancellationToken ct)
    {
        // 检查续传：如果 chunk 文件已存在，从断点继续
        long existingBytes = 0;
        if (File.Exists(chunkPath))
        {
            existingBytes = new FileInfo(chunkPath).Length;
        }

        var actualStart = rangeStart + existingBytes;
        if (actualStart > rangeEnd)
        {
            // 这个 chunk 已经完成
            return;
        }

        // 报告已下载的字节（续传时）
        if (existingBytes > 0)
        {
            onBytesDelta(existingBytes);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(cookie))
        {
            request.Headers.Add("Cookie", cookie);
        }
        request.Headers.Range = new RangeHeaderValue(actualStart, rangeEnd);

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        // 校验 Range 响应：服务器忽略 Range 时可能返回 200 OK 和完整文件，继续 Append 会导致数据错位
        if (response.StatusCode != System.Net.HttpStatusCode.PartialContent)
        {
            // 服务器不支持 Range，删除可能不完整的 chunk 并报错
            if (File.Exists(chunkPath)) File.Delete(chunkPath);
            throw new InvalidOperationException(
                $"CDN 返回 {(int)response.StatusCode} 而非 206 Partial Content，无法续传或分块下载");
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);

        var fileMode = existingBytes > 0 ? FileMode.Append : FileMode.Create;
        using var fileStream = new FileStream(chunkPath, fileMode, FileAccess.Write, FileShare.None, 8192);

        var buffer = new byte[8192];
        int bytesRead;

        while ((bytesRead = await stream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            onBytesDelta(bytesRead);
        }
    }

    /// <summary>
    /// 合并所有 chunk 文件为最终文件
    /// </summary>
    private static async Task MergeChunksAsync(string outputPath, int chunkCount, CancellationToken ct)
    {
        using var outputStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192);
        var buffer = new byte[8192];

        for (int i = 0; i < chunkCount; i++)
        {
            var chunkPath = $"{outputPath}.chunk{i}";
            if (!File.Exists(chunkPath))
                throw new FileNotFoundException($"Chunk 文件缺失: {chunkPath}");

            using var chunkStream = new FileStream(chunkPath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192);
            int bytesRead;
            while ((bytesRead = await chunkStream.ReadAsync(buffer, ct)) > 0)
            {
                await outputStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            }
        }
    }

    /// <summary>
    /// HEAD 请求获取 Content-Length
    /// </summary>
    private async Task<long> GetContentLengthAsync(string url, string cookie, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            if (!string.IsNullOrWhiteSpace(cookie))
            {
                request.Headers.Add("Cookie", cookie);
            }

            using var response = await _clients[0].SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            return response.Content.Headers.ContentLength ?? -1;
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>
    /// 单连接回退下载（与原有逻辑一致）
    /// </summary>
    private async Task DownloadSingleAsync(
        string url,
        string outputPath,
        string cookie,
        Action<long, long, string> onProgress,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(cookie))
        {
            request.Headers.Add("Cookie", cookie);
        }

        long existingBytes = 0;
        if (File.Exists(outputPath))
        {
            existingBytes = new FileInfo(outputPath).Length;
            request.Headers.Range = new RangeHeaderValue(existingBytes, null);
        }

        using var response = await _clients[0].SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        // 校验 Range 响应：续传时服务器忽略 Range 返回 200 OK，删除已有文件从头开始
        if (existingBytes > 0 && response.StatusCode != System.Net.HttpStatusCode.PartialContent)
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
            existingBytes = 0;
        }

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        if (existingBytes > 0 && totalBytes > 0)
            totalBytes += existingBytes;

        using var stream = await response.Content.ReadAsStreamAsync(ct);

        var fileMode = existingBytes > 0 && File.Exists(outputPath)
            ? FileMode.Append
            : FileMode.Create;

        using var fileStream = new FileStream(outputPath, fileMode, FileAccess.Write, FileShare.None, 8192);
        var buffer = new byte[8192];
        var downloaded = existingBytes;
        int bytesRead;

        var lastBytes = existingBytes;
        var lastTime = DateTime.UtcNow;
        var speedText = "";

        while ((bytesRead = await stream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            downloaded += bytesRead;

            var now = DateTime.UtcNow;
            var elapsed = (now - lastTime).TotalSeconds;
            if (elapsed >= 0.5)
            {
                var bytesPerSecond = (downloaded - lastBytes) / elapsed;
                speedText = FormatSpeed((long)bytesPerSecond);
                lastBytes = downloaded;
                lastTime = now;
            }

            onProgress(totalBytes, downloaded, speedText);
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
        foreach (var client in _clients)
        {
            client.Dispose();
        }
    }
}
