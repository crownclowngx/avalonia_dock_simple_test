using System.Security.Cryptography;
using System.Text;
using BiliDownloader.Models;
using BiliDownloader.Models.ContentSources;
using BiliDownloader.Services.Persistence;

namespace BiliDownloader.Services.ContentSources;

/// <summary>增量扫描进度只包含计数，不泄露筛选关键词、游标或远端地址。</summary>
public sealed record IncrementalScanProgress(int ScopeCount, int PageCount, int LeafCount, int ResolvedCount);

/// <summary>扫描得到的叶子项目；解析结果只保存在当前 Document 会话内。</summary>
public sealed record IncrementalScannedLeaf(
    ContentSourceItem SourceItem,
    bool MatchesRules,
    IReadOnlyList<BiliVideoItem> ResolvedItems);

/// <summary>
/// 可重复分类的内存扫描快照。它不包含 Provider 游标和临时 URL，可在输出配置变化后只重读 SQLite，
/// 避免把“重新分类”错误实现为一次隐式联网。
/// </summary>
public sealed record IncrementalSourceScanSnapshot(
    ContentSourceDescriptor Descriptor,
    SourceFilterRules Rules,
    IReadOnlyList<IncrementalScannedLeaf> Leaves,
    IReadOnlyList<ContentItemKey> BoundaryKeys,
    bool IsComplete,
    string ScanFingerprint,
    IReadOnlyList<ContentComparisonWarning> Warnings);

public interface IContentSourceScanService
{
    Task<IncrementalSourceScanSnapshot> ScanAsync(
        IContentSourceProvider provider,
        IContentSourceResolutionProvider resolver,
        ContentSourceDescriptor descriptor,
        SourceFilterRules rules,
        IProgress<IncrementalScanProgress>? progress,
        CancellationToken cancellationToken);
}

/// <summary>
/// 递归来源扫描器。它只负责取得一致的远端事实，不读取 SQLite，也不决定 Downloaded/New，
/// 从而使分页协议和分类政策可以独立测试、独立演进。
/// </summary>
public sealed class ContentSourceScanService : IContentSourceScanService
{
    private const int MaxScopes = 10_000;
    private const int MaxPages = 10_000;

    public async Task<IncrementalSourceScanSnapshot> ScanAsync(
        IContentSourceProvider provider,
        IContentSourceResolutionProvider resolver,
        ContentSourceDescriptor descriptor,
        SourceFilterRules rules,
        IProgress<IncrementalScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(rules);
        if (!provider.Capabilities.HasFlag(ContentSourceCapabilities.SupportsIncremental))
            throw new ContentSourceException(ContentSourceErrorCode.UnsupportedOperation, "当前来源不支持增量检查。");
        if (provider.Kind != resolver.Kind || provider.Kind != descriptor.Kind)
            throw new ContentSourceException(ContentSourceErrorCode.ProtocolViolation, "增量扫描的 Provider、解析器与来源类型不一致。");

        var leaves = new List<IncrementalScannedLeaf>();
        var leafKeys = new List<ContentItemKey>();
        var warnings = new List<ContentComparisonWarning>();
        var canonicalFacts = new List<string>();
        var queue = new Queue<ContentItemKey?>();
        var queuedParents = new HashSet<ContentItemKey>();
        queue.Enqueue(null);
        var scopeCount = 0;
        var pageCount = 0;
        var resolvedCount = 0;
        var complete = true;

        try
        {
            while (queue.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var parent = queue.Dequeue();
                if (++scopeCount > MaxScopes)
                    throw Protocol("来源层级数量异常，已停止检查。");

                var scopeItems = new List<ContentSourceItem>();
                var seenTokens = new HashSet<string>(StringComparer.Ordinal);
                var seenKeys = new HashSet<ContentItemKey>();
                string? token = null;
                string? providerSnapshot = null;
                string? firstPageDigest = null;
                do
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (++pageCount > MaxPages)
                        throw Protocol("来源分页数量异常，已停止检查。");
                    if (token is not null && !seenTokens.Add(token))
                        throw Protocol("来源分页游标形成循环，已停止检查。");

                    // 为了产生 RuleExcluded，扫描必须读取完整来源，再在本地应用已保存规则；
                    // 如果把规则提前交给服务端，被排除项目将无法与上一基线区分。
                    var request = new ContentPageRequest(
                        ContentPageRequest.MaxPageSize, token, SourceFilterRules.Empty, parent);
                    var page = await provider.GetPageAsync(descriptor, request, cancellationToken);
                    firstPageDigest ??= DigestKeys(page.Items);
                    if (page.SnapshotToken is not null)
                    {
                        providerSnapshot ??= page.SnapshotToken;
                        if (!string.Equals(providerSnapshot, page.SnapshotToken, StringComparison.Ordinal))
                            throw Protocol("内容源在扫描期间发生变化，请重新检查。");
                    }
                    else if (providerSnapshot is not null)
                    {
                        throw Protocol("内容源快照标识在扫描期间丢失，请重新检查。");
                    }

                    foreach (var item in page.Items)
                        if (seenKeys.Add(item.Key)) scopeItems.Add(item);
                    progress?.Report(new IncrementalScanProgress(scopeCount, pageCount, leafKeys.Count, resolvedCount));
                    token = page.HasMore ? page.NextContinuationToken : null;
                } while (token is not null);

                if (providerSnapshot is null)
                {
                    var verification = await provider.GetPageAsync(
                        descriptor,
                        new ContentPageRequest(ContentPageRequest.MaxPageSize, null, SourceFilterRules.Empty, parent),
                        cancellationToken);
                    if (!string.Equals(firstPageDigest, DigestKeys(verification.Items), StringComparison.Ordinal))
                        throw Protocol("内容源首屏在扫描期间发生变化，请重新检查。");
                }

                canonicalFacts.Add($"scope:{parent?.NativeId ?? "root"}:{providerSnapshot ?? firstPageDigest}");
                foreach (var item in scopeItems)
                {
                    canonicalFacts.Add($"item:{(int)item.Key.SourceKind}:{item.Key.NativeId}");
                    if (item.NodeKind == ContentSourceNodeKind.Container)
                    {
                        if (item.AccessState == ContentAccessState.Available && queuedParents.Add(item.Key))
                            queue.Enqueue(item.Key);
                        continue;
                    }

                    leafKeys.Add(item.Key);
                    var matches = ContentSourceFilterEngine.Apply([item], rules).Count != 0;
                    IReadOnlyList<BiliVideoItem> resolved = [];
                    if (matches && item.AccessState == ContentAccessState.Available)
                    {
                        try
                        {
                            var collection = await resolver.ResolveItemAsync(descriptor, item, cancellationToken);
                            resolved = collection.Items.ToArray();
                            resolvedCount += resolved.Count;
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception)
                        {
                            complete = false;
                            warnings.Add(new ContentComparisonWarning(
                                "resolve_partial", $"“{item.Title}”解析失败，本次结果不推进增量基线。"));
                        }
                    }
                    leaves.Add(new IncrementalScannedLeaf(item, matches, resolved));
                }
            }
        }
        catch (OperationCanceledException)
        {
            complete = false;
            warnings.Add(new ContentComparisonWarning("scan_canceled", "检查已取消，保留已加载预览但不更新基线。"));
        }
        catch (ContentSourceException ex)
        {
            complete = false;
            warnings.Add(new ContentComparisonWarning("scan_partial", ex.Message));
        }
        catch
        {
            complete = false;
            warnings.Add(new ContentComparisonWarning("scan_partial", "来源检查未完整完成，已保留可安全展示的部分结果。"));
        }

        var scanFingerprint = "scan1:" + Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(string.Join('\n', canonicalFacts)))).ToLowerInvariant();
        return new IncrementalSourceScanSnapshot(
            descriptor,
            rules,
            leaves,
            leafKeys.Take(IncrementalBaselineSaveData.MaximumBoundaryItemCount).ToArray(),
            complete,
            scanFingerprint,
            warnings);
    }

    private static string DigestKeys(IEnumerable<ContentSourceItem> items)
    {
        var canonical = string.Join('\n', items.Select(static item => $"{(int)item.Key.SourceKind}:{item.Key.NativeId}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static ContentSourceException Protocol(string message) =>
        new(ContentSourceErrorCode.ProtocolViolation, message);
}

/// <summary>隔离 File.Exists，保证分类测试不依赖开发机文件系统。</summary>
public interface IOutputFileFactProvider
{
    bool Exists(string path);
}

public sealed class SystemOutputFileFactProvider : IOutputFileFactProvider
{
    public bool Exists(string path) => !string.IsNullOrWhiteSpace(path) && File.Exists(path);
}

public interface IContentComparisonPolicy
{
    ContentComparisonResult Classify(
        MediaUnitKey mediaUnitKey,
        RenditionFingerprint fingerprint,
        RenditionSpecification specification,
        string title,
        IReadOnlyList<ContentItemKey> sourceKeys,
        BiliVideoItem item,
        IReadOnlyList<DownloadTaskRecord> relatedTasks);
}

/// <summary>
/// 五类状态的纯分类策略。来源层的 Invalid/RuleExcluded 由比较 Facade 先行产生；
/// 本策略只在远端项目有效且符合规则后判断 Downloaded、InProgress 和 New。
/// </summary>
public sealed class ContentComparisonPolicy : IContentComparisonPolicy
{
    private readonly IOutputFileFactProvider _files;

    public ContentComparisonPolicy(IOutputFileFactProvider files) => _files = files;

    public ContentComparisonResult Classify(
        MediaUnitKey mediaUnitKey,
        RenditionFingerprint fingerprint,
        RenditionSpecification specification,
        string title,
        IReadOnlyList<ContentItemKey> sourceKeys,
        BiliVideoItem item,
        IReadOnlyList<DownloadTaskRecord> relatedTasks)
    {
        var warnings = new List<ContentComparisonWarning>();
        var exact = relatedTasks.Where(task => string.Equals(
            task.RenditionFingerprint, fingerprint.Value, StringComparison.Ordinal)).ToArray();
        var completed = exact.FirstOrDefault(task =>
            DownloadTaskStatusMapper.FromStorageString(task.Status) == DownloadTaskStatus.Completed &&
            _files.Exists(task.OutputFilePath));
        if (completed is not null)
            return Result(ContentComparisonStatus.Downloaded);

        if (exact.Any(IsActiveOrRecoverable))
            return Result(ContentComparisonStatus.InProgress);

        if (exact.Any(task => DownloadTaskStatusMapper.FromStorageString(task.Status) == DownloadTaskStatus.Completed))
            warnings.Add(new ContentComparisonWarning(
                "completed_file_missing", "历史任务已完成，但最终文件不存在；本项按新增处理。"));

        var legacyCandidates = relatedTasks.Where(task =>
            string.IsNullOrWhiteSpace(task.RenditionFingerprint) &&
            task.Aid == mediaUnitKey.Aid && task.Cid == mediaUnitKey.Cid &&
            task.QualityId == specification.VideoQualityId &&
            task.AudioQualityId == specification.AudioQualityId).ToArray();
        if (legacyCandidates.Length > 0)
            warnings.Add(new ContentComparisonWarning(
                "legacy_identity_incomplete",
                "发现媒体相同但输出身份不完整的旧任务；不会静默阻止本次提交。",
                RequiresConfirmation: true));
        return Result(ContentComparisonStatus.New);

        ContentComparisonResult Result(ContentComparisonStatus status) =>
            new(mediaUnitKey, fingerprint, status, title, sourceKeys, item, warnings);
    }

    private static bool IsActiveOrRecoverable(DownloadTaskRecord task)
    {
        var status = DownloadTaskStatusMapper.FromStorageString(task.Status);
        return status is DownloadTaskStatus.Ready or DownloadTaskStatus.FetchingMetadata or
            DownloadTaskStatus.DownloadingVideo or DownloadTaskStatus.VideoReady or
            DownloadTaskStatus.DownloadingAudio or DownloadTaskStatus.AudioReady or
            DownloadTaskStatus.Merging or DownloadTaskStatus.Paused or
            DownloadTaskStatus.Interrupted or DownloadTaskStatus.WaitingForLogin ||
            status == DownloadTaskStatus.Failed && task.IsRetryable;
    }
}

public interface IIncrementalComparisonService
{
    Task<IncrementalComparisonSnapshot> CheckAsync(
        ContentSourceDescriptor descriptor,
        SourceFilterRules rules,
        IncrementalBaselineSaveData baseline,
        RenditionSpecification rendition,
        IProgress<IncrementalScanProgress>? progress,
        CancellationToken cancellationToken);

    Task<IncrementalComparisonSnapshot> ReclassifyAsync(
        IncrementalSourceScanSnapshot sourceSnapshot,
        IncrementalBaselineSaveData baseline,
        RenditionSpecification rendition,
        CancellationToken cancellationToken);
}

/// <summary>
/// 增量比较 Facade：组合来源扫描、任务查询、文件事实和纯分类策略。
/// 它不依赖 ViewModel、Coordinator 或 Document 保存实现，因此检查动作天然没有任务写副作用。
/// </summary>
public sealed class IncrementalComparisonService : IIncrementalComparisonService
{
    private readonly IContentSourceProviderRegistry _registry;
    private readonly IContentSourceScanService _scanner;
    private readonly IDownloadTaskRepository _tasks;
    private readonly IContentComparisonPolicy _policy;

    public IncrementalComparisonService(
        IContentSourceProviderRegistry registry,
        IContentSourceScanService scanner,
        IDownloadTaskRepository tasks,
        IContentComparisonPolicy policy)
    {
        _registry = registry;
        _scanner = scanner;
        _tasks = tasks;
        _policy = policy;
    }

    public async Task<IncrementalComparisonSnapshot> CheckAsync(
        ContentSourceDescriptor descriptor,
        SourceFilterRules rules,
        IncrementalBaselineSaveData baseline,
        RenditionSpecification rendition,
        IProgress<IncrementalScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        rendition.Validate();
        var provider = _registry.GetRequired(descriptor.Kind);
        var resolver = _registry.GetRequiredResolutionProvider(descriptor.Kind);
        var scan = await _scanner.ScanAsync(provider, resolver, descriptor, rules, progress, cancellationToken);
        return await ReclassifyAsync(scan, baseline, rendition, cancellationToken);
    }

    public async Task<IncrementalComparisonSnapshot> ReclassifyAsync(
        IncrementalSourceScanSnapshot sourceSnapshot,
        IncrementalBaselineSaveData baseline,
        RenditionSpecification rendition,
        CancellationToken cancellationToken)
    {
        rendition.Validate();
        var grouped = sourceSnapshot.Leaves
            .SelectMany(leaf => leaf.ResolvedItems.Select(item => (leaf, item)))
            .Where(pair => pair.item.Aid > 0 && pair.item.Cid > 0)
            .GroupBy(pair => pair.item.MediaUnitKey ?? new MediaUnitKey(pair.item.Aid, pair.item.Cid))
            .ToArray();
        var fingerprints = grouped.ToDictionary(
            group => group.Key,
            group => RenditionFingerprint.Create(group.Key, rendition));
        var related = await _tasks.GetByIdentityAsync(
            grouped.Select(group => group.Key).ToArray(),
            fingerprints.Values.Select(value => value.Value).ToArray(),
            cancellationToken);
        var results = new List<ContentComparisonResult>();

        // 来源事实优先：不符合规则或不可访问的项目在读取任务事实之前就固定主状态。
        foreach (var leaf in sourceSnapshot.Leaves.Where(leaf =>
                     !leaf.MatchesRules || leaf.SourceItem.AccessState != ContentAccessState.Available))
        {
            var status = sourceSnapshot.IsComplete && leaf.SourceItem.AccessState != ContentAccessState.Available
                ? ContentComparisonStatus.Invalid
                : ContentComparisonStatus.RuleExcluded;
            results.Add(new ContentComparisonResult(
                null, null, status, leaf.SourceItem.Title, [leaf.SourceItem.Key], null, []));
        }

        foreach (var group in grouped)
        {
            var representative = group.First().item;
            var sourceKeys = group.Select(pair => pair.leaf.SourceItem.Key).Distinct().ToArray();
            var mediaTasks = related.Where(task => task.Aid == group.Key.Aid && task.Cid == group.Key.Cid).ToArray();
            results.Add(_policy.Classify(
                group.Key, fingerprints[group.Key], rendition,
                representative.Title, sourceKeys, representative, mediaTasks));
        }

        if (sourceSnapshot.IsComplete)
        {
            var currentKeys = sourceSnapshot.Leaves.Select(leaf => leaf.SourceItem.Key).ToHashSet();
            foreach (var old in baseline.BoundaryItemKeys)
            {
                if (!Enum.TryParse<ContentSourceKind>(old.SourceKind, out var kind) ||
                    string.IsNullOrWhiteSpace(old.NativeId)) continue;
                var key = new ContentItemKey(kind, old.NativeId);
                if (currentKeys.Contains(key)) continue;
                results.Add(new ContentComparisonResult(
                    null, null, ContentComparisonStatus.Invalid,
                    $"已失效项目 {old.NativeId}", [key], null, []));
            }
        }

        results = results
            .OrderBy(result => Priority(result.Status))
            .ThenBy(result => result.Title, StringComparer.CurrentCulture)
            .ToList();
        var token = CreateComparisonToken(sourceSnapshot.ScanFingerprint, rendition, results);
        IncrementalBaselineSaveData? proposed = null;
        if (sourceSnapshot.IsComplete)
        {
            proposed = new IncrementalBaselineSaveData
            {
                BaselineVersion = IncrementalBaselineSaveData.CurrentVersion,
                LastCompletedCheckAtUtc = DateTimeOffset.UtcNow,
                SnapshotToken = sourceSnapshot.ScanFingerprint,
                BoundaryItemKeys = sourceSnapshot.BoundaryKeys.Select(key => new ContentItemKeySaveData
                {
                    SourceKind = key.SourceKind.ToString(),
                    NativeId = key.NativeId,
                }).ToList(),
            };
        }
        return new IncrementalComparisonSnapshot(
            results, sourceSnapshot.IsComplete, token, proposed, sourceSnapshot.Warnings, sourceSnapshot);
    }

    private static int Priority(ContentComparisonStatus status) => status switch
    {
        ContentComparisonStatus.Invalid => 0,
        ContentComparisonStatus.RuleExcluded => 1,
        ContentComparisonStatus.Downloaded => 2,
        ContentComparisonStatus.InProgress => 3,
        _ => 4,
    };

    private static string CreateComparisonToken(
        string scanFingerprint,
        RenditionSpecification rendition,
        IEnumerable<ContentComparisonResult> results)
    {
        var facts = results.Select(result =>
            $"{result.MediaUnitKey?.ToStorageKey()}|{result.RenditionFingerprint?.Value}|{(int)result.Status}");
        var canonical = string.Join('\n', facts.Prepend(scanFingerprint).Prepend(rendition.ToString()));
        return "cmp1:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
