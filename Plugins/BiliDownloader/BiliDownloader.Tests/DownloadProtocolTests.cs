using System.Net;
using System.Text;
using BiliDownloader.Models;
using BiliDownloader.Services.Api;
using BiliDownloader.Services.Download;
using BiliDownloader.Services.Infrastructure;
using Flurl.Http.Testing;

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

        var result = await downloader.DownloadAsync(
            [server.Url("cdn-a"), server.Url("cdn-b")],
            output,
            "cookie=value",
            (total, downloaded, _) => progress.Add((total, downloaded)),
            CancellationToken.None);

        Assert.Equal(allBytes, await File.ReadAllBytesAsync(output));
        Assert.Equal(allBytes.LongLength, result.ExpectedBytes);
        Assert.Equal(allBytes.LongLength, result.ActualBytes);
        Assert.True(result.IntegrityPassed);
        Assert.Equal((allBytes.LongLength, allBytes.LongLength), progress.Last());
        Assert.False(File.Exists(output + ".chunk0"));
        Assert.False(File.Exists(output + ".chunk1"));
        var getRequests = server.Requests.Where(x => x.Method == "GET").ToList();
        Assert.Equal(3, getRequests.Count);
        Assert.All(getRequests, request => Assert.Equal("cookie=value", request.Header("Cookie")));
        Assert.Contains(getRequests, x => x.Target.StartsWith("/cdn-a", StringComparison.Ordinal));
        Assert.Contains(getRequests, x => x.Target.StartsWith("/cdn-b", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("wrong-start")]
    [InlineData("short-body")]
    [InlineData("wrong-total")]
    public async Task 多连接拒绝错误Range响应且不生成最终文件(string scenario)
    {
        const int totalLength = 1_100_000;
        await using var server = LoopbackHttpServer.Create(request =>
        {
            if (request.Method == "HEAD")
            {
                return LoopbackResponse.Bytes(
                    [],
                    headers: new Dictionary<string, string>
                    {
                        ["Content-Length"] = totalLength.ToString(),
                    });
            }

            var (start, end) = ParseRange(request.Header("Range")!);
            var bodyLength = checked((int)(end - start + 1));
            if (scenario == "short-body")
            {
                bodyLength--;
            }
            var headerStart = scenario == "wrong-start" ? start + 1 : start;
            var headerTotal = scenario == "wrong-total" ? totalLength + 1 : totalLength;
            return LoopbackResponse.Bytes(
                new byte[bodyLength],
                206,
                new Dictionary<string, string>
                {
                    ["Content-Range"] = $"bytes {headerStart}-{end}/{headerTotal}",
                });
        });
        using var paths = new TestDataPaths();
        Directory.CreateDirectory(paths.RootDirectory);
        var output = Path.Combine(paths.RootDirectory, $"invalid-{scenario}.bin");
        using var httpClient = new HttpClient();
        var runtime = new FakeDownloadRuntime();
        using var downloader = new MultiConnectionDownloader(httpClient, runtime, chunkCount: 2);

        await Assert.ThrowsAsync<DownloadProtocolException>(() =>
            downloader.DownloadAsync(
                [server.Url("a"), server.Url("b")],
                output,
                "",
                (_, _, _) => { },
                CancellationToken.None));

        Assert.False(File.Exists(output));
        Assert.False(File.Exists(output + ".merging"));
        Assert.False(File.Exists(output + ".chunk0"));
        Assert.False(File.Exists(output + ".chunk1"));
        Assert.True(runtime.DelayCount > 0);
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
    public async Task 注入依赖可离线跑通媒体下载与Ffmpeg合并主链路()
    {
        using var state = new StaticStateScope();
        using var apiHttp = new HttpTest();
        apiHttp.ForCallsTo("https://api.bilibili.com/x/web-interface/nav")
            .RespondWith("""
                {"code":0,"data":{"wbi_img":{
                  "img_url":"https://i.test/abcdefghijklmnopqrstuvwxyz123456.png",
                  "sub_url":"https://i.test/654321zyxwvutsrqponmlkjihgfedcba.png"
                }}}
                """);
        apiHttp.ForCallsTo("*x/player/wbi/playurl*")
            .RespondWith("""
                {"code":0,"data":{"dash":{
                  "video":[{"id":80,"base_url":"https://media.test/video","codecid":7}],
                  "audio":[{"id":30232,"base_url":"https://media.test/audio","bandwidth":192000}]
                }}}
                """);
        var videoBytes = Encoding.UTF8.GetBytes("video-data");
        var audioBytes = Encoding.UTF8.GetBytes("audio-data");
        var httpFactory = new StubBiliHttpClientFactory(request =>
        {
            var bytes = request.RequestUri?.AbsolutePath == "/video" ? videoBytes : audioBytes;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes),
            };
        });
        using var paths = new TestDataPaths();
        Directory.CreateDirectory(paths.RootDirectory);
        var ffmpeg = new FakeFfmpegService
        {
            ReadyOverride = true,
            CreateOutputFile = true,
        };
        using var service = new BiliDownloadService(
            paths,
            ffmpeg,
            httpFactory,
            new FakeDownloadRuntime(),
            chunkCount: 1);
        var task = new DownloadTaskRecord
        {
            TaskId = "offline-main",
            ItemTitle = "离线主链路",
            Aid = 1,
            Cid = 2,
            Bvid = "BV1TEST0001",
            QualityId = 80,
            AudioQualityId = 30232,
            OutputDirectory = paths.RootDirectory,
            MediaType = "video",
        };

        var result = await service.DownloadItemAsync(
            task,
            new BiliApiService(),
            "SESSDATA=test",
            _ => { },
            (_, _) => { },
            CancellationToken.None);

        Assert.True(result.VideoTransfer.IntegrityPassed);
        Assert.True(result.AudioTransfer.IntegrityPassed);
        Assert.Equal(videoBytes.Length, result.VideoTransfer.ExpectedBytes);
        Assert.Equal(audioBytes.Length, result.AudioTransfer.ExpectedBytes);
        Assert.Single(ffmpeg.MergeCalls);
        Assert.True(File.Exists(result.OutputFilePath));
        Assert.Equal(2, httpFactory.Requests.Count);
        Assert.All(httpFactory.Requests, request =>
            Assert.Equal("SESSDATA=test", request.Headers["Cookie"]));
    }

    [Fact]
    public void 文件名清洗替换当前平台非法字符并去掉尾点()
    {
        var invalid = Path.GetInvalidFileNameChars().First();
        var result = Services.Naming.FileNameSanitizer.Sanitize($"a{invalid}b...");

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
        var ffmpeg = new FfmpegService(new FfmpegProcessFactory())
        {
            CustomPath = fake,
        };

        Assert.Equal(fake, ffmpeg.ResolveFfmpegPath());
        Assert.False(ffmpeg.IsReady);
        Assert.False(await ffmpeg.ValidatePathAsync(fake));
        Assert.False(await ffmpeg.ValidatePathAsync(
            Path.Combine(paths.RootDirectory, "missing.exe")));
    }

    [Fact]
    public async Task Ffmpeg合并使用安全参数列表并通过注入进程执行()
    {
        using var paths = new TestDataPaths();
        Directory.CreateDirectory(paths.RootDirectory);
        var executable = Path.Combine(paths.RootDirectory, "ffmpeg.exe");
        await File.WriteAllTextAsync(executable, "marker");
        var processFactory = new FakeFfmpegProcessFactory();
        processFactory.Process.StandardOutput = "ffmpeg version test";
        var ffmpeg = new FfmpegService(processFactory) { CustomPath = executable };
        var video = Path.Combine(paths.RootDirectory, "video with space.tmp");
        var audio = Path.Combine(paths.RootDirectory, "audio.tmp");
        var output = Path.Combine(paths.RootDirectory, "output.mp4");

        Assert.True((await ffmpeg.DetectAsync()).IsReady);
        await ffmpeg.MergeAsync(video, audio, output);

        Assert.Equal(executable, processFactory.StartInfo?.FileName);
        Assert.Equal(
            [
                "-hide_banner", "-nostats", "-loglevel", "warning",
                "-i", video, "-i", audio, "-c", "copy", "-shortest", output,
            ],
            processFactory.StartInfo?.ArgumentList);
    }

    [Fact]
    public async Task Ffmpeg取消会终止进程树并清理未完成输出()
    {
        using var paths = new TestDataPaths();
        Directory.CreateDirectory(paths.RootDirectory);
        var executable = Path.Combine(paths.RootDirectory, "ffmpeg.exe");
        var output = Path.Combine(paths.RootDirectory, "partial.mp4");
        await File.WriteAllTextAsync(executable, "marker");
        await File.WriteAllTextAsync(output, "partial");
        var processFactory = new FakeFfmpegProcessFactory();
        processFactory.Process.StandardOutput = "ffmpeg version test";
        var ffmpeg = new FfmpegService(processFactory) { CustomPath = executable };
        Assert.True((await ffmpeg.DetectAsync()).IsReady);
        processFactory.Process.HasExited = false;
        processFactory.Process.BlockUntilCancelled = true;
        using var cancellation = new CancellationTokenSource();

        var mergeTask = ffmpeg.MergeAsync("video.tmp", "audio.tmp", output, cancellation.Token);
        await Task.Yield();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mergeTask);
        Assert.True(processFactory.Process.KillCalled);
        Assert.False(File.Exists(output));
    }

    private static (long Start, long End) ParseRange(string value)
    {
        var parts = value["bytes=".Length..].Split('-', 2);
        return (long.Parse(parts[0]), long.Parse(parts[1]));
    }
}
