using BiliDownloader.Models;
using BiliDownloader.Models.ContentSources;

namespace BiliDownloader.Services.ContentSources;

/// <summary>
/// 内容源策略契约。Provider 负责来源语义，不得依赖 UI、任务协调器或持久化实现。
/// </summary>
public interface IContentSourceProvider
{
    ContentSourceKind Kind { get; }
    ContentSourceCapabilities Capabilities { get; }
    int CapabilityVersion { get; }

    ValueTask<ContentSourceDescriptor> NormalizeAsync(string input, CancellationToken cancellationToken);

    Task<ContentPage> GetPageAsync(
        ContentSourceDescriptor descriptor,
        ContentPageRequest request,
        CancellationToken cancellationToken);

}

/// <summary>
/// 可选的内容解析能力。
/// 设计意图：目录可浏览不等于客户端可下载，课程来源因此无需伪造无法履行的方法。
/// </summary>
public interface IContentSourceResolutionProvider
{
    ContentSourceKind Kind { get; }

    Task<BiliVideoCollection> ResolveItemAsync(
        ContentSourceDescriptor descriptor,
        ContentSourceItem item,
        CancellationToken cancellationToken);
}

/// <summary>按来源类型解析 Provider 的只读注册表。</summary>
public interface IContentSourceProviderRegistry
{
    IReadOnlyCollection<IContentSourceProvider> Providers { get; }
    bool TryGet(ContentSourceKind kind, out IContentSourceProvider? provider);
    IContentSourceProvider GetRequired(ContentSourceKind kind);
    bool TryGetResolutionProvider(ContentSourceKind kind, out IContentSourceResolutionProvider? provider);
    IContentSourceResolutionProvider GetRequiredResolutionProvider(ContentSourceKind kind);
}

public interface IFavoriteSourceDiscoveryService
{
    Task<IReadOnlyList<ContentSourceDescriptor>> GetMyFoldersAsync(CancellationToken cancellationToken);
}

public enum ContentSourceErrorCode
{
    InvalidInput,
    LoginRequired,
    Forbidden,
    RiskControlled,
    NotFound,
    RateLimited,
    RemoteFailure,
    ProtocolViolation,
    UnknownProvider,
    UnsupportedOperation,
}

/// <summary>
/// 内容源边界的结构化异常。消息只能包含稳定分类，不得拼入原始 URL、Cookie 或游标。
/// </summary>
public sealed class ContentSourceException : Exception
{
    public ContentSourceException(ContentSourceErrorCode code, string message)
        : base(message)
    {
        Code = code;
    }

    public ContentSourceErrorCode Code { get; }
}
