using BiliDownloader.Models;
using BiliDownloader.Services.Api;
using BiliDownloader.Services.Download;
using Flurl.Http.Testing;

namespace BiliDownloader.Tests;

public sealed class BiliApiServiceTests
{
    [Theory]
    [InlineData("BV1abcDEF123", "BV1abcDEF123", true)]
    [InlineData("  bv1ABCdef123  ", "bv1ABCdef123", true)]
    [InlineData("av12345", "av12345", false)]
    [InlineData("https://www.bilibili.com/video/BV1abcDEF123?p=2", "BV1abcDEF123", true)]
    [InlineData("https://www.bilibili.com/video/AV987", "AV987", false)]
    public void 普通视频输入解析覆盖ID与URL(string input, string expectedId, bool expectedBvid)
    {
        var parsed = Assert.IsType<(string Id, bool IsBvid)>(
            BiliApiService.ParseVideoId(input));
        Assert.Equal(expectedId, parsed.Id);
        Assert.Equal(expectedBvid, parsed.IsBvid);
    }

    [Theory]
    [InlineData("ep123", "ep123", false)]
    [InlineData("SS456", "SS456", true)]
    [InlineData("md789", "md789", false)]
    [InlineData("https://www.bilibili.com/bangumi/play/ep12", "ep12", false)]
    [InlineData("https://www.bilibili.com/bangumi/play/ss34", "ss34", true)]
    public void 番剧输入解析覆盖ID与URL(string input, string expectedId, bool expectedSeason)
    {
        var parsed = Assert.IsType<(string Id, bool IsSeasonId)>(
            BiliApiService.ParseBangumiId(input));
        Assert.Equal(expectedId, parsed.Id);
        Assert.Equal(expectedSeason, parsed.IsSeasonId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-video")]
    [InlineData("BV-short")]
    [InlineData("https://b23.tv/abc")]
    public void 非法或异步短链不会被同步解析为普通视频(string input)
    {
        Assert.Null(BiliApiService.ParseVideoId(input));
    }

    [Fact]
    public void 短链识别处理空值与普通链接()
    {
        Assert.True(BiliApiService.IsB23TvLink("https://b23.tv/abc"));
        Assert.False(BiliApiService.IsB23TvLink("https://www.bilibili.com/video/BV1abcDEF123"));
        Assert.False(BiliApiService.IsB23TvLink(""));
    }

    [Fact]
    public async Task 普通视频多P响应映射集合并发送必要请求头()
    {
        using var state = new StaticStateScope();
        using var http = new HttpTest();
        http.ForCallsTo("https://api.bilibili.com/x/web-interface/view*")
            .RespondWith("""
                {
                  "code": 0,
                  "data": {
                    "title": "系列标题",
                    "aid": 101,
                    "bvid": "BV1abcDEF123",
                    "pic": "https://img.test/cover.jpg",
                    "pages": [
                      {"cid": 201, "part": "P1", "duration": 61},
                      {"cid": 202, "part": "P2", "duration": 62}
                    ]
                  }
                }
                """);

        var result = await new BiliApiService().GetVideoCollectionAsync(
            "BV1abcDEF123",
            isBvid: true,
            "SESSDATA=session");

        Assert.Equal("系列标题", result.SeriesTitle);
        Assert.Equal("https://img.test/cover.jpg", result.Cover);
        Assert.Equal(["P1", "P2"], result.Items.Select(x => x.Title));
        Assert.Equal([201L, 202L], result.Items.Select(x => x.Cid));
        Assert.All(result.Items, x => Assert.Equal(101, x.Aid));
        http.ShouldHaveCalled("https://api.bilibili.com/x/web-interface/view*")
            .WithQueryParam("bvid", "BV1abcDEF123")
            .WithHeader("Cookie", "SESSDATA=session")
            .WithHeader("Referer", "*bilibili.com*")
            .Times(1);
    }

    [Fact]
    public async Task AV单视频无Pages时使用根级Cid和Duration()
    {
        using var state = new StaticStateScope();
        using var http = new HttpTest();
        http.RespondWith("""
            {
              "code": 0,
              "data": {
                "title": "单视频",
                "aid": 99,
                "bvid": "BV1single001",
                "cid": 88,
                "duration": 77
              }
            }
            """);

        var result = await new BiliApiService().GetVideoCollectionAsync(
            "av99",
            isBvid: false,
            cookie: "");

        var item = Assert.Single(result.Items);
        Assert.Equal("单视频", item.Title);
        Assert.Equal(88, item.Cid);
        Assert.Equal(77, item.Duration);
        http.ShouldHaveCalled("*x/web-interface/view*")
            .WithQueryParam("aid", "99")
            .WithoutHeader("Cookie")
            .Times(1);
    }

    [Fact]
    public async Task UGC合集会替换Pages并映射每个Episode()
    {
        using var state = new StaticStateScope();
        using var http = new HttpTest();
        http.RespondWith("""
            {
              "code": 0,
              "data": {
                "title": "原始标题",
                "aid": 1,
                "bvid": "BV1abcDEF123",
                "pages": [{"cid": 1, "part": "old"}],
                "ugc_season": {
                  "title": "合集标题",
                  "sections": [{
                    "episodes": [
                      {"title": "A", "aid": 11, "bvid": "BV-A", "cid": 21, "page": {"duration": 31}},
                      {"title": "B", "aid": 12, "bvid": "BV-B", "cid": 22}
                    ]
                  }]
                }
              }
            }
            """);

        var result = await new BiliApiService().GetVideoCollectionAsync(
            "BV1abcDEF123",
            true,
            "");

        Assert.Equal("合集标题", result.SeriesTitle);
        Assert.Equal(["A", "B"], result.Items.Select(x => x.Title));
        Assert.Equal([31, 0], result.Items.Select(x => x.Duration));
    }

    [Fact]
    public async Task 视频信息业务错误会包含服务端消息()
    {
        using var state = new StaticStateScope();
        using var http = new HttpTest();
        http.RespondWith("""{"code":-404,"message":"不存在"}""");

        var ex = await Assert.ThrowsAsync<Exception>(() =>
            new BiliApiService().GetVideoCollectionAsync("av1", false, ""));

        Assert.Contains("不存在", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Md番剧会先解析Season并合并正片与附加分区()
    {
        using var state = new StaticStateScope();
        using var http = new HttpTest();
        http.ForCallsTo("*pgc/review/user*")
            .RespondWith("""{"code":0,"result":{"media":{"season_id":456}}}""");
        http.ForCallsTo("*pgc/view/web/season*")
            .RespondWith("""
                {
                  "code": 0,
                  "result": {
                    "season_title": "番剧",
                    "season_id": 456,
                    "cover": "https://img.test/bangumi.jpg",
                    "episodes": [
                      {"title":"1","long_title":"第一话","aid":1,"bvid":"BV-E1","cid":11,"duration":90000,"ep_id":101}
                    ],
                    "section": [
                      {"episodes":[{"title":"PV","aid":2,"bvid":"BV-PV","cid":12,"duration":30000,"ep_id":102}]}
                    ]
                  }
                }
                """);

        var result = await new BiliApiService().GetBangumiCollectionAsync(
            "md123",
            isSeasonId: false,
            "SESSDATA=s");

        Assert.Equal("番剧", result.SeriesTitle);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal("第一话", result.Items[0].Title);
        Assert.Equal(90, result.Items[0].Duration);
        Assert.Equal(BiliMediaType.Bangumi, result.Items[0].MediaType);
        Assert.Equal(456, result.Items[0].SeasonId);
        Assert.Equal("PV", result.Items[1].Title);
        http.ShouldHaveCalled("*pgc/review/user*")
            .WithQueryParam("media_id", "123")
            .Times(1);
        http.ShouldHaveCalled("*pgc/view/web/season*")
            .WithQueryParam("season_id", "456")
            .Times(1);
    }

    [Fact]
    public async Task Dash会解析普通杜比和HiRes流及可用画质()
    {
        using var state = new StaticStateScope();
        using var http = new HttpTest();
        ConfigureWbiNav(http);
        http.ForCallsTo("*x/player/wbi/playurl*")
            .RespondWith("""
                {
                  "code":0,
                  "data":{
                    "accept_quality":[120,80],
                    "accept_description":["4K","1080P"],
                    "dash":{
                      "video":[{"id":120,"base_url":"https://v.test/main","backup_url":["https://v.test/b1"],"codecid":7,"codecs":"avc1.640033","mime_type":"video/mp4","bandwidth":5000}],
                      "audio":[{"id":30232,"baseUrl":"https://a.test/main","codecid":0,"codecs":"mp4a.40.2","mime_type":"audio/mp4","bandwidth":192000}],
                      "dolby":{"audio":[{"id":30250,"base_url":"https://a.test/dolby","codecs":"ec-3","mime_type":"audio/mp4","bandwidth":384000}]},
                      "flac":{"audio":{"id":30280,"base_url":"https://a.test/flac","codecs":"flac","mime_type":"audio/flac","bandwidth":1000000}}
                    }
                  }
                }
                """);

        var result = await new BiliApiService().GetDashResultAsync(
            1,
            2,
            120,
            "SESSDATA=secret");

        Assert.Equal([(120, "4K"), (80, "1080P")],
            result.AcceptQualities.Select(x => (x.QualityId, x.DisplayName)));
        var video = Assert.Single(result.VideoStreams);
        Assert.Equal("https://v.test/main", video.BaseUrl);
        Assert.Equal(["https://v.test/b1"], video.BackupUrls);
        Assert.Equal("avc1.640033", video.Codecs);
        Assert.Equal("video/mp4", video.MimeType);
        Assert.Equal(DashContainerHint.Mp4, video.ContainerHint);
        Assert.Equal(3, result.AudioStreams.Count);
        Assert.Contains(result.AudioStreams, x => x.Id == 30232 && x.AudioFeature == BiliAudioFeature.Standard);
        Assert.Contains(result.AudioStreams, x => x.Id == 30250 && x.AudioFeature == BiliAudioFeature.Dolby);
        Assert.Contains(result.AudioStreams, x => x.Id == 30280 && x.Bandwidth == 1_000_000
            && x.AudioFeature == BiliAudioFeature.HiRes && x.ContainerHint == DashContainerHint.Flac);
        http.ShouldHaveCalled("*x/player/wbi/playurl*")
            .WithQueryParam("avid", "1")
            .WithQueryParam("cid", "2")
            .WithQueryParam("qn", "120")
            .WithQueryParam("w_rid")
            .WithQueryParam("wts")
            .WithHeader("Cookie", "SESSDATA=secret")
            .Times(1);
    }

    [Fact]
    public async Task 番剧会员错误与无Dash响应会给出明确错误()
    {
        using var state = new StaticStateScope();
        using (var http = new HttpTest())
        {
            ConfigureWbiNav(http);
            http.ForCallsTo("*pgc/player/web/v2/playurl*")
                .RespondWith("""{"code":-10403,"message":"vip"}""");
            var ex = await Assert.ThrowsAsync<MediaAuthorizationException>(() =>
                new BiliApiService().GetDashResultAsync(
                    1, 2, 80, "", BiliMediaType.Bangumi, 3, 4));
            Assert.Contains("大会员", ex.Message, StringComparison.Ordinal);
        }

        StaticStateScope.ResetWbiCache();
        using (var http = new HttpTest())
        {
            ConfigureWbiNav(http);
            http.ForCallsTo("*x/player/wbi/playurl*")
                .RespondWith("""{"code":0,"data":{}}""");
            var ex = await Assert.ThrowsAsync<ResourceUnavailableException>(() =>
                new BiliApiService().GetDashResultAsync(1, 2, 80, ""));
            Assert.Contains("DASH", ex.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task 字幕列表补协议头并转换为标准Srt()
    {
        using var state = new StaticStateScope();
        using var http = new HttpTest();
        ConfigureWbiNav(http);
        http.ForCallsTo("*x/player/wbi/v2*")
            .RespondWith("""
                {"code":0,"data":{"subtitle":{"subtitles":[
                  {"lan":"zh-CN","lan_doc":"中文","subtitle_url":"//sub.test/zh.json"},
                  {"lan":"en","lan_doc":"English","subtitle_url":""}
                ]}}}
                """);
        http.ForCallsTo("https://sub.test/zh.json")
            .RespondWith("""
                {"body":[
                  {"from":1.25,"to":3.5,"content":"第一行"},
                  {"from":3601.001,"to":3602.002,"content":"line 2"}
                ]}
                """);
        var api = new BiliApiService();

        var subtitles = await api.GetSubtitleListAsync(1, 2, "cookie");
        var subtitle = Assert.Single(subtitles);
        Assert.Equal("https://sub.test/zh.json", subtitle.SubtitleUrl);
        var srt = await api.GetSubtitleSrtAsync(subtitle.SubtitleUrl, "cookie");

        Assert.Contains("00:00:01,250 --> 00:00:03,500", srt, StringComparison.Ordinal);
        Assert.Contains("01:00:01,001 --> 01:00:02,002", srt, StringComparison.Ordinal);
        Assert.Contains("第一行", srt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 空字幕Body返回空字符串()
    {
        using var http = new HttpTest();
        http.RespondWith("""{"body":[]}""");

        Assert.Equal("", await new BiliApiService().GetSubtitleSrtAsync(
            "https://sub.test/empty.json",
            ""));
    }

    [Fact]
    public async Task 短链使用HEAD并返回Location()
    {
        using var http = new HttpTest();
        http.RespondWith(
            "",
            302,
            new Dictionary<string, string>
            {
                ["Location"] = "https://www.bilibili.com/video/BV1abcDEF123",
            });

        var resolved = await BiliApiService.ResolveB23TvAsync("https://b23.tv/abc");

        Assert.Equal("https://www.bilibili.com/video/BV1abcDEF123", resolved);
        http.ShouldHaveCalled("https://b23.tv/abc")
            .WithVerb(HttpMethod.Head)
            .Times(1);
    }

    [Fact]
    public async Task 短链缺少Location会失败()
    {
        using var http = new HttpTest();
        http.RespondWith("", 200);

        var ex = await Assert.ThrowsAsync<Exception>(() =>
            BiliApiService.ResolveB23TvAsync("https://b23.tv/missing"));

        Assert.Contains("无法解析", ex.Message, StringComparison.Ordinal);
    }

    private static void ConfigureWbiNav(HttpTest http)
    {
        http.ForCallsTo("https://api.bilibili.com/x/web-interface/nav")
            .RespondWith("""
                {
                  "code":0,
                  "data":{"wbi_img":{
                    "img_url":"https://i.test/abcdefghijklmnopqrstuvwxyz123456.png",
                    "sub_url":"https://i.test/654321zyxwvutsrqponmlkjihgfedcba.png"
                  }}
                }
                """);
    }
}
