using System.Reflection;
using BiliDownloader.Services.Api;
using BiliDownloader.Services.Infrastructure;

namespace BiliDownloader.Tests;

internal sealed class StaticStateScope : IDisposable
{
    private readonly string? _ffmpegPath = FfmpegService.CustomPath;
    private readonly string? _systemPath = Environment.GetEnvironmentVariable("PATH");

    public StaticStateScope()
    {
        ResetWbiCache();
        FfmpegService.CustomPath = null;
    }

    public static void ResetWbiCache()
    {
        var type = typeof(BiliApiService);
        type.GetField("_cachedMixinKey", BindingFlags.Static | BindingFlags.NonPublic)!
            .SetValue(null, null);
        type.GetField("_mixinKeyExpireTime", BindingFlags.Static | BindingFlags.NonPublic)!
            .SetValue(null, DateTime.MinValue);
    }

    public void Dispose()
    {
        FfmpegService.CustomPath = _ffmpegPath;
        Environment.SetEnvironmentVariable("PATH", _systemPath);
        ResetWbiCache();
    }
}
