using System.Security.Cryptography;
using System.Text;
using BiliDownloader.Models.ContentSources;

namespace BiliDownloader.Services.ContentSources;

/// <summary>
/// 分页查询的代际与串行门禁。
/// 设计意图：把“新查询取消旧查询”和“同一时刻只推进一个游标”集中为单一并发不变量。
/// </summary>
public sealed class ContentQueryCoordinator : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource _generationCts = new();
    private long _generation;
    private bool _disposed;

    public long Generation => Interlocked.Read(ref _generation);
    public CancellationToken Token => _generationCts.Token;

    public long Advance()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var generation = Interlocked.Increment(ref _generation);
        _generationCts.Cancel();
        _generationCts.Dispose();
        _generationCts = new CancellationTokenSource();
        return generation;
    }

    public bool IsCurrent(long generation) => generation == Generation;

    public async ValueTask<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken);
        return new GateLease(_gate);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _generationCts.Cancel();
        _generationCts.Dispose();
        _gate.Dispose();
    }

    private sealed class GateLease(SemaphoreSlim gate) : IDisposable
    {
        private SemaphoreSlim? _gate = gate;
        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }
}

/// <summary>页面缓存的完整身份；游标只参与内存比较，禁止输出到日志。</summary>
public readonly record struct ContentPageCacheKey(
    ContentSourceKind SourceKind,
    string StableSourceId,
    int CapabilityVersion,
    ContentItemKey? ParentKey,
    FilterFingerprint FilterFingerprint,
    int PageSize,
    string? ContinuationToken);

/// <summary>内容源页面缓存的最小端口；实现不得持久化页面或游标。</summary>
public interface IContentPageCache
{
    bool TryGet(ContentPageCacheKey key, out ContentPage? page);
    void Set(ContentPageCacheKey key, ContentPage page);
    void Invalidate(
        ContentSourceDescriptor descriptor,
        ContentItemKey? parentKey,
        FilterFingerprint fingerprint);
}

/// <summary>
/// 文档会话级有界 LRU 页面缓存。
/// 设计意图：加速面包屑回访，同时用固定容量阻止大型来源长期占用内存。
/// </summary>
public sealed class MemoryContentPageCache : IContentPageCache
{
    public const int DefaultCapacity = 32;
    private readonly int _capacity;
    private readonly object _gate = new();
    private readonly Dictionary<ContentPageCacheKey, LinkedListNode<Entry>> _entries = [];
    private readonly LinkedList<Entry> _lru = [];

    public MemoryContentPageCache(int capacity = DefaultCapacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public bool TryGet(ContentPageCacheKey key, out ContentPage? page)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var node))
            {
                page = null;
                return false;
            }
            _lru.Remove(node);
            _lru.AddFirst(node);
            page = node.Value.Page;
            return true;
        }
    }

    public void Set(ContentPageCacheKey key, ContentPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                existing.Value = new Entry(key, page);
                _lru.Remove(existing);
                _lru.AddFirst(existing);
                return;
            }

            var node = new LinkedListNode<Entry>(new Entry(key, page));
            _lru.AddFirst(node);
            _entries[key] = node;
            while (_entries.Count > _capacity)
            {
                var oldest = _lru.Last!;
                _lru.RemoveLast();
                _entries.Remove(oldest.Value.Key);
            }
        }
    }

    public void Invalidate(
        ContentSourceDescriptor descriptor,
        ContentItemKey? parentKey,
        FilterFingerprint fingerprint)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        lock (_gate)
        {
            var keys = _entries.Keys.Where(key =>
                key.SourceKind == descriptor.Kind &&
                key.StableSourceId == descriptor.StableSourceId &&
                key.CapabilityVersion == descriptor.CapabilityVersion &&
                key.ParentKey == parentKey &&
                key.FilterFingerprint == fingerprint).ToArray();
            foreach (var key in keys)
            {
                var node = _entries[key];
                _entries.Remove(key);
                _lru.Remove(node);
            }
        }
    }

    private sealed record Entry(ContentPageCacheKey Key, ContentPage Page);
}

/// <summary>全部匹配物化阶段的安全进度，不包含关键词、游标或远端地址。</summary>
public sealed record ContentMaterializationProgress(int PageCount, int MatchCount);

/// <summary>把规则式选择转换为提交前稳定项目快照的窄端口。</summary>
public interface IContentSelectionMaterializer
{
    Task<IReadOnlyList<ContentSourceItem>> MaterializeAllMatchingAsync(
        IContentSourceProvider provider,
        ContentSourceDescriptor descriptor,
        ContentItemKey? parentKey,
        SourceFilterRules rules,
        ContentSelectionState selection,
        IProgress<ContentMaterializationProgress>? progress,
        CancellationToken cancellationToken);
}

/// <summary>
/// 将“全部匹配”规则在提交前物化为稳定项目快照。
/// 设计意图：完整性校验结束前不进入解析边界，任何分页异常都不会产生部分提交。
/// </summary>
public sealed class ContentSelectionMaterializer : IContentSelectionMaterializer
{
    public const int MaterializationPageSize = ContentPageRequest.MaxPageSize;
    private const int MaxPageCount = 10_000;

    public async Task<IReadOnlyList<ContentSourceItem>> MaterializeAllMatchingAsync(
        IContentSourceProvider provider,
        ContentSourceDescriptor descriptor,
        ContentItemKey? parentKey,
        SourceFilterRules rules,
        ContentSelectionState selection,
        IProgress<ContentMaterializationProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(selection);

        var plan = ContentFilterPlanBuilder.Build(rules, provider.Capabilities);
        if (selection.Scope != SelectionScope.AllMatchingResults ||
            selection.AllMatchingFingerprint != plan.Fingerprint)
            throw new ContentSourceException(
                ContentSourceErrorCode.ProtocolViolation,
                "全部匹配选择规则已经失效，请重新确认选择范围。");

        var excluded = selection.ExcludedKeys.ToHashSet();
        var accumulator = new ContentPageAccumulator();
        var seenTokens = new HashSet<string>(StringComparer.Ordinal);
        string? token = null;
        string? snapshotToken = null;
        string? firstPageDigest = null;
        var pageCount = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++pageCount > MaxPageCount)
                throw Protocol("来源分页数量异常，已停止枚举。");
            if (token is not null && !seenTokens.Add(token))
                throw Protocol("来源分页游标形成循环，已停止枚举。");

            var request = new ContentPageRequest(
                MaterializationPageSize, token, plan.ServerRules, parentKey);
            var page = await provider.GetPageAsync(descriptor, request, cancellationToken);
            if (pageCount == 1)
                firstPageDigest = CreatePageDigest(page.Items);
            if (page.SnapshotToken is not null)
            {
                snapshotToken ??= page.SnapshotToken;
                if (!string.Equals(snapshotToken, page.SnapshotToken, StringComparison.Ordinal))
                    throw Protocol("内容源在枚举期间发生变化，请重新确认选择范围。");
            }
            else if (snapshotToken is not null)
            {
                throw Protocol("内容源快照标识在枚举期间丢失，请重新确认选择范围。");
            }

            accumulator.Append(provider, request, page);
            var currentMatches = ContentSourceFilterEngine.Apply(accumulator.Items, plan.ResidualRules)
                .Count(item => IsSelectable(item) && !excluded.Contains(item.Key));
            progress?.Report(new ContentMaterializationProgress(pageCount, currentMatches));
            if (!page.HasMore) break;
            token = page.NextContinuationToken;
        }

        if (snapshotToken is null)
        {
            var verificationRequest = new ContentPageRequest(
                MaterializationPageSize, null, plan.ServerRules, parentKey);
            var verification = await provider.GetPageAsync(descriptor, verificationRequest, cancellationToken);
            if (!string.Equals(firstPageDigest, CreatePageDigest(verification.Items), StringComparison.Ordinal))
                throw Protocol("内容源在枚举期间发生变化，请重新确认选择范围。");
        }

        return ContentSourceFilterEngine.Apply(accumulator.Items, plan.ResidualRules)
            .Where(item => IsSelectable(item) && !excluded.Contains(item.Key))
            .ToArray();
    }

    private static bool IsSelectable(ContentSourceItem item) =>
        item.NodeKind == ContentSourceNodeKind.Media &&
        item.AccessState == ContentAccessState.Available;

    private static string CreatePageDigest(IEnumerable<ContentSourceItem> items)
    {
        var canonical = string.Join("\n", items.Select(static item =>
            $"{(int)item.Key.SourceKind}:{item.Key.NativeId}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static ContentSourceException Protocol(string message) =>
        new(ContentSourceErrorCode.ProtocolViolation, message);
}
