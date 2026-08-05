using BiliDownloader.Models.ContentSources;

namespace BiliDownloader.Services.ContentSources;

/// <summary>
/// Provider 注册表。设计意图：启动时一次性拒绝歧义注册，运行期查找始终是确定性的。
/// </summary>
public sealed class ContentSourceProviderRegistry : IContentSourceProviderRegistry
{
    private const ContentSourceCapabilities AllCapabilities =
        ContentSourceCapabilities.RequiresLogin |
        ContentSourceCapabilities.SupportsPaging |
        ContentSourceCapabilities.SupportsKeyword |
        ContentSourceCapabilities.SupportsDateRange |
        ContentSourceCapabilities.SupportsTypeFilter |
        ContentSourceCapabilities.SupportsIncremental;

    private readonly IReadOnlyDictionary<ContentSourceKind, IContentSourceProvider> _providers;

    public ContentSourceProviderRegistry(IEnumerable<IContentSourceProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        var map = new Dictionary<ContentSourceKind, IContentSourceProvider>();
        foreach (var provider in providers)
        {
            ArgumentNullException.ThrowIfNull(provider);
            ValidateDeclaration(provider);
            if (!map.TryAdd(provider.Kind, provider))
                throw new ContentSourceException(
                    ContentSourceErrorCode.ProtocolViolation,
                    $"内容源类型 {provider.Kind} 存在重复 Provider 注册。");
        }

        _providers = map;
        Providers = Array.AsReadOnly(map.Values.ToArray());
    }

    public IReadOnlyCollection<IContentSourceProvider> Providers { get; }

    public bool TryGet(ContentSourceKind kind, out IContentSourceProvider? provider)
    {
        if (!Enum.IsDefined(kind))
        {
            provider = null;
            return false;
        }

        return _providers.TryGetValue(kind, out provider);
    }

    public IContentSourceProvider GetRequired(ContentSourceKind kind)
    {
        if (TryGet(kind, out var provider))
            return provider!;

        throw new ContentSourceException(
            ContentSourceErrorCode.UnknownProvider,
            $"未注册内容源类型 {kind} 的 Provider。");
    }

    private static void ValidateDeclaration(IContentSourceProvider provider)
    {
        if (!Enum.IsDefined(provider.Kind))
            throw new ContentSourceException(ContentSourceErrorCode.ProtocolViolation, "Provider 声明了未知来源类型。");
        if (provider.CapabilityVersion <= 0)
            throw new ContentSourceException(ContentSourceErrorCode.ProtocolViolation, "Provider 能力版本必须为正数。");
        if ((provider.Capabilities & ~AllCapabilities) != 0)
            throw new ContentSourceException(ContentSourceErrorCode.ProtocolViolation, "Provider 声明了未知能力位。");
    }
}
