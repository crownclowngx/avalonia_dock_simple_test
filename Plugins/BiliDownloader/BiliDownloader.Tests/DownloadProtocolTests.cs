using System.Net;
using System.Text;
using BiliDownloader.Services.Download;
using BiliDownloader.Services.Infrastructure;

namespace BiliDownloader.Tests;

public sealed class DownloadProtocolTests
{
    [Fact]
    public void Cdn排序优先Mirror并补充Upos且去重()
    {
        var mirror = "https://upos-sz-mirrorali.bilivideo.com/file?os=uposbv";
        var upos = "https://upos.example.test/file?os=upos";

        var result = CdnUrlHelper.FilterAndSortUrls(
            upos,
            [mirror, upos, ""]);

        Assert.Equal([mirror, upos], result);
    }

    [Fact]
    public void 无Mirror时会改写Upos域名并保留原地址()
    {
        const string source = "https://upos.example.test/path/video.m4s?os=upos&x=1";

        var result = CdnUrlHelper.FilterAndSortUrls(source, []);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, x => x.Contains("upos-sz-mirrorali.bilivideo.com", StringComparison.Ordinal));
        Assert.Contains(source, result);
    }

    [Fact]
    public void Bcache畸形和空URL处理稳定()
    {
        Assert.Empty(CdnUrlHelper.FilterAndSortUrls("", []));
        var bcache = "https://cn-bj.example.test/file?os=bcache";
        var result = CdnUrlHelper.FilterAndSortUrls(
            bcache,
            ["not a valid url", "https://other.example.test/file"]);
        Assert.Contains(result, x => x.Contains("mirror", StringComparison.Ordinal));
        Assert.Contains(bcache, result);
    }

    [Fact]
    public async Task 流下载支持206续传并报告完整总长度()
    {
        var allBytes = Encoding.UTF8.GetBytes("0123456789");
        await using var server = LoopbackHttpServer.Create(request =>
        {
            Assert.Equal("bytes=4-", request.Header("Range"));
            Assert.Equal("SESSDATA=test", request.Header("Cookie"));
            return LoopbackResponse.Bytes(
                allBytes[4..],
                206,
                new Dictionary<string, string>
                {
                    ["Content-Range"] = "bytes 4-9/10",
                });
        });
        using var paths = new TestDataPaths();
        var output = Path.Combine(paths.RootDirectory, "stream.bin");
        Directory.CreateDirectory(paths.RootDirectory);
        await File.WriteAllBytesAsync(output, allBytes[..4]);
        var reports = new List<(long Total, long Downloaded)>();
        using var service = new BiliDownloadService(paths, chunkCount: 1);

        await service.DownloadStreamAsync(
            server.Url("file"),
            output,
            "SESSDATA=test",
            4,
            (total, downloaded, _) => reports.Add((total, downloaded)),
            CancellationToken.None);

        Assert.Equal(allBytes, await File.ReadAllBytesAsync(output));
        Assert.Equal((10L, 10L), reports.Last());
    }

    [Fact]
    public async Task 流续传遇到200会删除旧内容从头覆盖()
    {
        var fresh = Encoding.UTF8.GetBytes("fresh-content");
        await using var server = LoopbackHttpServer.Create(_ =>
            LoopbackResponse.Bytes(fresh));
        using var paths = new TestDataPaths();
        Directory.CreateDirectory(paths.RootDirectory);
        var output = Path.Combine(paths.RootDirectory, "stream.bin");
        await File.WriteAllTextAsync(output, "stale");
        using var service = new BiliDownloadService(paths, chunkCount: 1);

        await service.DownloadStreamAsync(
            server.Url("file"),
            output,
            "",
            existingBytes: 5,
            (_, _, _) => { },
            CancellationToken.None);

        Assert.Equal(fresh, await File.ReadAllBytesAsync(output));
    }

    [Fact]
    public async Task 单连接下载器可从断点继续()
    {
        var allBytes = Encoding.UTF8.GetBytes("abcdefghij");
        await using var server = LoopbackHttpServer.Create(request =>
        {
            Assert.Equal("bytes=3-", request.Header("Range"));
            return LoopbackResponse.Bytes(
                allBytes[3..],
                206,
                new Dictionary<string, string>
                {
                    ["Content-Range"] = "bytes 3-9/10",
                });
        });
        using var paths = new TestDataPaths();
        Directory.CreateDirectory(paths.RootDirectory);
        var output = Path.Combine(paths.RootDirectory, "single.bin");
        await File.WriteAllBytesAsync(output, allBytes[..3]);
        using var downloader = new MultiConnectionDownloader(chunkCount: 1);

        await downloader.DownloadAsync(
            [server.Url("file")],
            output,
            "",
            (_, _, _) => { },
            CancellationToken.None);

        Assert.Equal(allBytes, await File.ReadAllBytesAsync(output));
    }

    [Fact]
    public async Task 多连接Range分块会精确合并并清理Chunk()
    {
        var allBytes = Enumerable.Range(0, 2_200_000)
            .Select(i => (byte)(i % 251))
            .ToArray();
        await using var server = LoopbackHttpServer.Create(request =>
        {
            if (request.Method == "HEAD")
            {
                return LoopbackResponse.Bytes(
                    [],
                    headers: new Dictionary<string, string>
                    {
                        ["Content-Length"] = allBytes.Length.ToString(),
                    });
            }

            var (start, end) = ParseRange(request.Header("Range")!);
            return LoopbackResponse.Bytes(
                allBytes[(int)start..((int)end + 1)],
                206,
                new Dictionary<string, string>
                {
                    ["Content-Range"] = $"bytes {start}-{end}/{allBytes.Length}",
                });
        });
        using var paths = new TestDataPaths();
        Directory.CreateDirectory(paths.RootDirectory);
        var output = Path.Combine(paths.RootDirectory, "multi.bin");
        var progress = new List<(long Total, long Downloaded)>();
        using var downloader = new MultiConnectionDownloader(chunkCount: 4);

        await downloader.DownloadAsync(
            [server.Url("cdn-a"), server.Url("cdn-b")],
            output,
            "cookie=value",
            (total, downloaded, _) => progress.Add((total, downloaded)),
            CancellationToken.None);

        Assert.Equal(allBytes, await File.ReadAllBytesAsync(output));
        Assert.Equal((allBytes.LongLength, allBytes.LongLength), progress.Last());
        Assert.False(File.Exists(output + ".chunk0"));
        Assert.False(File.Exists(output + ".chunk1"));
        var getRequests = server.Requests.Where(x => x.Method == "GET").ToList();
        Assert.Equal(3, getRequests.Count);
        Assert.All(getRequests, request => Assert.Equal("cookie=value", request.Header("Cookie")));
        Assert.Contains(getRequests, x => x.Target.StartsWith("/cdn-a", StringComparison.Ordinal));
        Assert.Contains(getRequests, x => x.Target.StartsWith("/cdn-b", StringComparison.Ordinal));
    }

    [Fact]
    public async Task 多连接会从已有Chunk偏移继续()
    {
        var allBytes = Enumerable.Range(0, 2_100_000)
            .Select(i => (byte)(i % 239))
            .ToArray();
        await using var server = LoopbackHttpServer.Create(request =>
        {
            if (request.Method == "HEAD")
            {
                return LoopbackResponse.Bytes(
                    [],
                    headers: new Dictionary<string, string>
                    {
                        ["Content-Length"] = allBytes.Length.ToString(),
                    });
            }

            var (start, end) = ParseRange(request.Header("Range")!);
            return LoopbackResponse.Bytes(
                allBytes[(int)start..((int)end + 1)],
                206,
                new Dictionary<string, string>
                {
                    ["Content-Range"] = $"bytes {start}-{end}/{allBytes.Length}",
                });
        });
        using var paths = new TestDataPaths();
        Directory.CreateDirectory(paths.RootDirectory);
        var output = Path.Combine(paths.RootDirectory, "resume-multi.bin");
        await File.WriteAllBytesAsync(output + ".chunk0", allBytes[..100]);
        using var downloader = new MultiConnectionDownloader(chunkCount: 2);

        await downloader.DownloadAsync(
            [server.Url("a"), server.Url("b")],
            output,
            "",
            (_, _, _) => { },
            CancellationToken.None);

        Assert.Equal(allBytes, await File.ReadAllBytesAsync(output));
        Assert.Contains(server.Requests, request =>
            request.Header("Range")?.StartsWith("bytes=100-", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Head失败会回退单连接而不访问外网()
    {
        var bytes = Encoding.UTF8.GetBytes("fallback");
        await using var server = LoopbackHttpServer.Create(request =>
            request.Method == "HEAD"
                ? LoopbackResponse.Text("no head", 500)
                : LoopbackResponse.Bytes(bytes));
        using var paths = new TestDataPaths();
        Directory.CreateDirectory(paths.RootDirectory);
        var output = Path.Combine(paths.RootDirectory, "fallback.bin");
        using var downloader = new MultiConnectionDownloader(chunkCount: 2);

        await downloader.DownloadAsync(
            [server.Url("a"), server.Url("b")],
            output,
            "",
            (_, _, _) => { },
            CancellationToken.None);

        Assert.Equal(bytes, await File.ReadAllBytesAsync(output));
        Assert.Contains(server.Requests, x => x.Method == "HEAD");
        Assert.Contains(server.Requests, x => x.Method == "GET");
    }

    [Fact]
    public async Task 下载器拒绝空URL集合()
    {
        using var downloader = new MultiConnectionDownloader();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            downloader.DownloadAsync([], "unused", "", (_, _, _) => { }, CancellationToken.None));

        Assert.Equal("urls", ex.ParamName);
    }

    [Fact]
    public void 文件名清洗替换当前平台非法字符并去掉尾点()
    {
        var invalid = Path.GetInvalidFileNameChars().First();
        var result = BiliDownloadService.SanitizeFileName($"a{invalid}b...");

        Assert.Equal("a_b", result);
    }

    [Fact]
    public async Task Ffmpeg路径优先自定义文件且无效文件验证失败()
    {
        using var state = new StaticStateScope();
        using var paths = new TestDataPaths();
        Directory.CreateDirectory(paths.RootDirectory);
        var fake = Path.Combine(paths.RootDirectory, "ffmpeg.exe");
        await File.WriteAllTextAsync(fake, "not an executable");
        FfmpegService.CustomPath = fake;

        Assert.Equal(fake, FfmpegService.ResolveFfmpegPath());
        Assert.True(FfmpegService.IsReady);
        Assert.False(await FfmpegService.ValidatePathAsync(fake));
        Assert.False(await FfmpegService.ValidatePathAsync(
            Path.Combine(paths.RootDirectory, "missing.exe")));
    }

    private static (long Start, long End) ParseRange(string value)
    {
        var parts = value["bytes=".Length..].Split('-', 2);
        return (long.Parse(parts[0]), long.Parse(parts[1]));
    }
}
