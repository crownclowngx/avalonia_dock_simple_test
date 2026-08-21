using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BiliDownloader.Services.Api;

internal sealed record BiliUploaderRequestContext(
    Dictionary<string, string> Query,
    string Referer);

/// <summary>
/// 构造空间投稿接口所需的 Web 请求上下文。
/// 设计意图：把易随站点变化的兼容参数与目录映射隔离，API 适配器只负责签名、发送和映射响应。
/// </summary>
internal sealed class BiliUploaderRequestContextFactory
{
    private static readonly (int Width, int Height, int Weight)[] CommonScreens =
    [
        (1920, 1080, 18), (1366, 768, 18), (1536, 864, 17),
        (1280, 720, 8), (2560, 1440, 7), (1440, 900, 5), (1600, 900, 5),
    ];

    // 同一进程使用一致的屏幕维度，避免分页时客户端上下文无故跳变。
    private readonly (int Width, int Height) _screen = ChooseScreen();

    public BiliUploaderRequestContext Create(long uploaderId, int page, int pageSize)
    {
        var query = new Dictionary<string, string>
        {
            ["mid"] = uploaderId.ToString(),
            ["pn"] = page.ToString(),
            ["ps"] = pageSize.ToString(),
            ["order"] = "pubdate",
            ["keyword"] = "",
            ["order_avoided"] = "true",
            ["platform"] = "web",
            ["tid"] = "0",
            ["web_location"] = "1550101",
            ["dm_img_list"] = "[]",
            ["dm_img_str"] = CreateClientHint(16, 64),
            ["dm_cover_img_str"] = CreateClientHint(32, 128),
            ["dm_img_inter"] = CreateInteractionHint(),
        };
        return new BiliUploaderRequestContext(
            query,
            $"https://space.bilibili.com/{uploaderId}/upload/video");
    }

    private string CreateInteractionHint()
    {
        var whRandom = RandomNumberGenerator.GetInt32(114);
        var wh = new[]
        {
            2 * _screen.Width + 2 * _screen.Height + 3 * whRandom,
            4 * _screen.Width - _screen.Height + whRandom,
            whRandom,
        };
        var scrollTop = RandomNumberGenerator.GetInt32(101);
        var offsetRandom = RandomNumberGenerator.GetInt32(514);
        var offset = new[]
        {
            3 * scrollTop + offsetRandom,
            4 * scrollTop + 2 * offsetRandom,
            offsetRandom,
        };
        return JsonSerializer.Serialize(new { ds = Array.Empty<object>(), wh, of = offset });
    }

    private static string CreateClientHint(int minimumLength, int maximumLength)
    {
        const string printable = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz!@#$%^&*_-+=";
        var length = RandomNumberGenerator.GetInt32(minimumLength, maximumLength + 1);
        var value = new StringBuilder(length);
        for (var index = 0; index < length; index++)
            value.Append(printable[RandomNumberGenerator.GetInt32(printable.Length)]);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value.ToString())).TrimEnd('=');
    }

    private static (int Width, int Height) ChooseScreen()
    {
        var choice = RandomNumberGenerator.GetInt32(CommonScreens.Sum(item => item.Weight));
        foreach (var screen in CommonScreens)
        {
            if (choice < screen.Weight) return (screen.Width, screen.Height);
            choice -= screen.Weight;
        }
        return (1920, 1080);
    }
}
