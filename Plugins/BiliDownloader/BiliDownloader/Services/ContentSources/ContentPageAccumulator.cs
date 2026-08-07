using BiliDownloader.Models.ContentSources;

namespace BiliDownloader.Services.ContentSources;

/// <summary>
/// 分页会话累加器。它集中维护跨页去重与游标前进不变量，避免每个 Provider 重复实现死循环保护。
/// </summary>
public sealed class ContentPageAccumulator
{
    private readonly HashSet<ContentItemKey> _seenKeys = new();
    private readonly List<ContentSourceItem> _items = new();

    public IReadOnlyList<ContentSourceItem> Items => _items.AsReadOnly();

    /// <summary>接纳一页并返回本页首次出现的项目。</summary>
    public IReadOnlyList<ContentSourceItem> Append(
        IContentSourceProvider provider,
        ContentPageRequest request,
        ContentPage page)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(page);

        if (request.ParentKey.HasValue &&
            !provider.Capabilities.HasFlag(ContentSourceCapabilities.SupportsChildPaging))
            throw Protocol("非层级 Provider 收到了子集合分页请求。");
        if (request.ParentKey.HasValue && request.ParentKey.Value.SourceKind != provider.Kind)
            throw Protocol("父集合与 Provider 必须属于同一种内容源。");

        if (!request.ParentKey.HasValue &&
            !provider.Capabilities.HasFlag(ContentSourceCapabilities.SupportsPaging) && page.HasMore)
            throw Protocol("非分页 Provider 返回了下一页游标。");

        var added = new List<ContentSourceItem>();
        foreach (var item in page.Items)
        {
            if (item.Key.SourceKind != provider.Kind)
                throw Protocol("分页项目的来源类型与 Provider 声明不一致。");
            if (request.ParentKey.HasValue && item.ParentKey != request.ParentKey)
                throw Protocol("子项目的父键与分页请求不一致。");
            if (!request.ParentKey.HasValue && item.ParentKey.HasValue)
                throw Protocol("根列表不得返回带父键的子项目。");
            if (_seenKeys.Add(item.Key))
            {
                _items.Add(item);
                added.Add(item);
            }
        }

        if (page.HasMore &&
            string.Equals(request.ContinuationToken, page.NextContinuationToken, StringComparison.Ordinal) &&
            added.Count == 0)
        {
            throw Protocol("内容源分页游标未前进且没有新增项目，已停止分页。");
        }

        return added.AsReadOnly();
    }

    private static ContentSourceException Protocol(string message) =>
        new(ContentSourceErrorCode.ProtocolViolation, message);
}
