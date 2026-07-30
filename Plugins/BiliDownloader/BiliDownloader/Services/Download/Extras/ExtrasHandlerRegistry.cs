using BiliDownloader.Services.Infrastructure;

namespace BiliDownloader.Services.Download.Extras;

/// <summary>
/// Extras 处理器注册表：管理所有可用的 extras 处理器实例。
/// DIP：协调器依赖此注册表（抽象集合），不直接依赖具体处理器实现。
/// </summary>
public class ExtrasHandlerRegistry : IDisposable
{
    private readonly Dictionary<string, IExtrasHandler> _handlers = new();

    /// <summary>注册处理器</summary>
    public void Register(IExtrasHandler handler)
    {
        _handlers[handler.Type] = handler;
    }

    /// <summary>按位枚举解析需要执行的处理器列表（按注册顺序）</summary>
    public List<IExtrasHandler> Resolve(ExtrasType enabled)
    {
        if (enabled == ExtrasType.None)
            return new List<IExtrasHandler>();

        var result = new List<IExtrasHandler>();

        // 按固定顺序解析（封面最先，弹幕最后）
        if (enabled.HasFlag(ExtrasType.Cover) && _handlers.TryGetValue("cover", out var cover))
            result.Add(cover);
        if (enabled.HasFlag(ExtrasType.Subtitle) && _handlers.TryGetValue("subtitle", out var subtitle))
            result.Add(subtitle);
        if (enabled.HasFlag(ExtrasType.Danmaku) && _handlers.TryGetValue("danmaku", out var danmaku))
            result.Add(danmaku);

        return result;
    }

    /// <summary>获取所有已注册的处理器</summary>
    public IReadOnlyCollection<IExtrasHandler> GetAll() => _handlers.Values;

    /// <summary>
    /// 创建默认注册表（工厂方法，在协调器初始化时调用）
    /// 注册所有内置处理器。
    /// </summary>
    public static ExtrasHandlerRegistry CreateDefault(IBiliHttpClientFactory httpClientFactory)
    {
        var registry = new ExtrasHandlerRegistry();
        registry.Register(new CoverExtrasHandler(httpClientFactory));
        registry.Register(new SubtitleExtrasHandler());
        registry.Register(new DanmakuExtrasHandler());
        return registry;
    }

    public static ExtrasHandlerRegistry CreateDefault()
        => CreateDefault(new BiliHttpClientFactory());

    public void Dispose()
    {
        foreach (var disposable in _handlers.Values.OfType<IDisposable>())
        {
            disposable.Dispose();
        }
    }
}
