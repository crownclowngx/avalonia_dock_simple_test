using System.Security.Cryptography;
using System.Text;
using BiliDownloader.Models.ContentSources;

namespace BiliDownloader.Services.ContentSources;

/// <summary>服务端可执行规则与客户端残余规则的不可变执行计划。</summary>
public sealed record ContentFilterPlan(
    SourceFilterRules ServerRules,
    SourceFilterRules ResidualRules,
    FilterFingerprint Fingerprint)
{
    public bool HasResidualRules =>
        ResidualRules.Keyword is not null ||
        ResidualRules.PublishedFrom.HasValue ||
        ResidualRules.PublishedTo.HasValue ||
        ResidualRules.MediaTypes.Count > 0 ||
        ResidualRules.SortOrder != ContentSourceSortOrder.ProviderDefault;
}

/// <summary>
/// 根据 Provider 能力拆分筛选规则。
/// 设计意图：新增服务端能力只修改 Provider 的能力声明和适配器，不让 UI 识别具体 Provider 类型。
/// </summary>
public static class ContentFilterPlanBuilder
{
    public static ContentFilterPlan Build(
        SourceFilterRules rules,
        ContentSourceCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var keywordOnServer = capabilities.HasFlag(ContentSourceCapabilities.SupportsKeyword);
        var dateOnServer = capabilities.HasFlag(ContentSourceCapabilities.SupportsDateRange);
        var typeOnServer = capabilities.HasFlag(ContentSourceCapabilities.SupportsTypeFilter);

        var server = new SourceFilterRules(
            keywordOnServer ? rules.Keyword : null,
            dateOnServer ? rules.PublishedFrom : null,
            dateOnServer ? rules.PublishedTo : null,
            typeOnServer ? rules.MediaTypes : null,
            ContentSourceSortOrder.ProviderDefault);
        var residual = new SourceFilterRules(
            keywordOnServer ? null : rules.Keyword,
            dateOnServer ? null : rules.PublishedFrom,
            dateOnServer ? null : rules.PublishedTo,
            typeOnServer ? null : rules.MediaTypes,
            rules.SortOrder);

        return new ContentFilterPlan(server, residual, CreateFingerprint(rules));
    }

    public static FilterFingerprint CreateFingerprint(SourceFilterRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var canonical = string.Join("\n",
            rules.Keyword?.Normalize(NormalizationForm.FormKC).ToUpperInvariant() ?? string.Empty,
            rules.PublishedFrom?.ToUniversalTime().Ticks.ToString() ?? string.Empty,
            rules.PublishedTo?.ToUniversalTime().Ticks.ToString() ?? string.Empty,
            string.Join(',', rules.MediaTypes.OrderBy(static type => type).Select(static type => (int)type)),
            ((int)rules.SortOrder).ToString());
        return new FilterFingerprint(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))));
    }
}

/// <summary>无状态的客户端筛选与稳定排序策略。</summary>
public static class ContentSourceFilterEngine
{
    public static IReadOnlyList<ContentSourceItem> Apply(
        IEnumerable<ContentSourceItem> source,
        SourceFilterRules rules)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(rules);

        var indexed = source.Select(static (item, index) => new IndexedItem(item, index))
            .Where(entry => Matches(entry.Item, rules));
        return rules.SortOrder switch
        {
            ContentSourceSortOrder.PublishedNewest => indexed
                .OrderBy(static entry => entry.Item.PublishedAt.HasValue ? 0 : 1)
                .ThenByDescending(static entry => entry.Item.PublishedAt)
                .ThenBy(static entry => entry.Index)
                .Select(static entry => entry.Item).ToArray(),
            ContentSourceSortOrder.PublishedOldest => indexed
                .OrderBy(static entry => entry.Item.PublishedAt.HasValue ? 0 : 1)
                .ThenBy(static entry => entry.Item.PublishedAt)
                .ThenBy(static entry => entry.Index)
                .Select(static entry => entry.Item).ToArray(),
            _ => indexed.OrderBy(static entry => entry.Index).Select(static entry => entry.Item).ToArray(),
        };
    }

    public static bool Matches(ContentSourceItem item, SourceFilterRules rules)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(rules);

        if (rules.Keyword is not null &&
            !item.Title.Contains(rules.Keyword, StringComparison.OrdinalIgnoreCase) &&
            !(item.Author?.Contains(rules.Keyword, StringComparison.OrdinalIgnoreCase) ?? false))
            return false;
        if ((rules.PublishedFrom.HasValue || rules.PublishedTo.HasValue) && !item.PublishedAt.HasValue)
            return false;
        if (rules.PublishedFrom.HasValue && item.PublishedAt < rules.PublishedFrom)
            return false;
        if (rules.PublishedTo.HasValue && item.PublishedAt > rules.PublishedTo)
            return false;
        return rules.MediaTypes.Count == 0 || rules.MediaTypes.Contains(item.ItemType);
    }

    private sealed record IndexedItem(ContentSourceItem Item, int Index);
}
