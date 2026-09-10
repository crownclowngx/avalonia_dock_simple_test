using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;

namespace MyAvaloniaManagement.Business.Help;

internal sealed record HelpArticle(string Id, string Title, string Description,
    string Guide, string? Source = null, string? Demo = null)
{
    public string Label => Title;
}

internal sealed record HelpSearchResult(string ArticleId, string Title, string Excerpt, string Query);

/// <summary>Host 的只读帮助目录。原文从同一仓库资源收录，导航身份不依赖安装路径。</summary>
internal sealed class HelpContentCatalog
{
    private readonly Assembly _assembly = typeof(HelpContentCatalog).Assembly;
    private readonly Dictionary<string, string> _repositoryResources;
    internal static readonly IReadOnlyList<HelpArticle> Chapters = Array.AsReadOnly(new[]
    {
        new HelpArticle("product", "产品介绍", "一个承载多种专业能力的桌面工作台", "product", Demo: "workspace"),
        new HelpArticle("workbench", "工作台使用", "文档、工具、布局与快捷键", "workbench"),
        new HelpArticle("architecture", "架构介绍", "理解边界、契约与资源所有权", "architecture", "Host/MyAvaloniaManagement/docs/design/architecture.md", "architecture"),
        new HelpArticle("attention", "注意力与工作台", "选择不确定性与空间布局", "attention", "docs/theory/attention-centered-dock-workspace-design.md", "attention"),
        new HelpArticle("activity", "活动与需求分解", "从一个目标到正确的能力边界", "activity", "docs/theory/activity-theory-requirements-decomposition.md", "activity"),
        new HelpArticle("production", "约束驱动的软件工业化", "用明确规则组织软件生产", "production", "docs/theory/约束驱动的软件工业化.md", "production"),
        new HelpArticle("forkable", "可分叉软件基底", "让能力可以保存、恢复与演进", "forkable", "docs/theory/ai-enabled-forkable-software-bases.md", "forkable"),
        new HelpArticle("references", "术语与参考资料", "概念索引、来源与阅读路径", "references"),
    });

    public HelpContentCatalog()
    {
        _repositoryResources = _assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith("Help.Repository.", StringComparison.Ordinal))
            .ToDictionary(name => name["Help.Repository.".Length..].Replace('\\', '/'),
                name => name, StringComparer.OrdinalIgnoreCase);
    }

    internal HelpArticle? Find(string? id)
    {
        var chapter = Chapters.FirstOrDefault(a => a.Id == id);
        if (chapter is not null) return chapter;
        if (id?.StartsWith("reference:", StringComparison.Ordinal) != true) return null;
        var source = id[10..];
        if (!_repositoryResources.ContainsKey(source)) return null;
        var title = ReadRepository(source).Split('\n').FirstOrDefault(l => l.StartsWith("# "))?[2..].Trim()
            ?? Path.GetFileNameWithoutExtension(source);
        return new HelpArticle(id, title, "随 Host 收录的参考文档 · 请留意文中的版本与状态", string.Empty, source);
    }

    internal string ReadGuide(HelpArticle article) => article.Guide.Length == 0 ? string.Empty
        : ReadResource("Help.Guide." + article.Guide + ".md")
            .Replace("{{ProductVersion}}", _assembly.GetName().Version?.ToString(3) ?? "未知", StringComparison.Ordinal)
            .Replace("{{SdkVersion}}", typeof(PluginSdk.DocumentTypeId).Assembly.GetName().Version?.ToString(3) ?? "未知", StringComparison.Ordinal);

    internal string ReadRepository(string source) =>
        _repositoryResources.TryGetValue(source, out var resource) ? ReadResource(resource) : string.Empty;

    internal string ReadFullText(HelpArticle article) => ReadGuide(article) +
        (article.Source is null ? string.Empty : "\n\n" + ReadRepository(article.Source));

    internal string? ResolveLink(string? source, string href)
    {
        if (href.StartsWith("help:", StringComparison.Ordinal))
        {
            var target = href[5..].Split('#')[0];
            return Find(target) is null ? null : href[5..];
        }
        if (href.StartsWith('#')) return href;
        if (!Uri.TryCreate(new Uri("https://help.invalid/" + (source ?? "")), href, out var uri)
            || uri.Host != "help.invalid") return null;
        var path = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
        var chapter = Chapters.FirstOrDefault(a => a.Source == path);
        if (chapter is not null) return chapter.Id + uri.Fragment;
        return _repositoryResources.ContainsKey(path) ? "reference:" + path + uri.Fragment : null;
    }

    internal IReadOnlyList<HelpSearchResult> Search(string query, CancellationToken cancellationToken)
    {
        query = query.Trim();
        if (query.Length == 0) return [];
        var results = new List<HelpSearchResult>();
        foreach (var article in Chapters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = ReadFullText(article);
            var position = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (position < 0 && !article.Title.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
            var start = Math.Max(0, position - 36);
            var excerpt = Regex.Replace(text.Substring(start, Math.Min(135, text.Length - start)), @"\s+", " ");
            results.Add(new(article.Id, article.Title, excerpt, query));
        }
        return results;
    }

    private string ReadResource(string name)
    {
        using var stream = _assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Help resource missing: " + name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
