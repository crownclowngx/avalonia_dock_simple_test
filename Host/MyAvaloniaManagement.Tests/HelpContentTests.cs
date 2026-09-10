using System.Text.Json;
using MyAvaloniaManagement.Business.Help;

namespace MyAvaloniaManagement.Tests;

public sealed class HelpContentTests
{
    private readonly HelpContentCatalog _catalog = new();

    [Fact]
    public void EightChaptersHaveGuidesAndAllFourTheoryOriginalsAreEmbedded()
    {
        Assert.Equal(8, HelpContentCatalog.Chapters.Count);
        Assert.Equal(6, HelpContentCatalog.Chapters.Count(a => a.Demo is not null));
        foreach (var article in HelpContentCatalog.Chapters)
        {
            Assert.True(_catalog.ReadGuide(article).Length > 200);
            Assert.DoesNotContain("{{", _catalog.ReadGuide(article));
            if (article.Source is not null) Assert.True(_catalog.ReadRepository(article.Source).Length > 1000);
        }
        Assert.Equal(4, HelpContentCatalog.Chapters.Count(a => a.Source?.StartsWith("docs/theory/") == true));
    }

    [Theory]
    [InlineData("$x_1$", "x_1", false)]
    [InlineData("\\(x_1\\)", "x_1", false)]
    [InlineData("$$\\frac{a}{b}$$", "\\frac{a}{b}", true)]
    [InlineData("\\[\\sum_i p_i\\]", "\\sum_i p_i", true)]
    public void MathDelimitersPreserveTexBeforeMarkdownEscaping(string source, string tex, bool display)
    {
        var html = new HelpMarkdownRenderer(_catalog).RenderMarkdown(source);
        Assert.Contains("data-tex=\"" + System.Net.WebUtility.HtmlEncode(tex) + "\"", html);
        Assert.Contains("data-display=\"" + display.ToString().ToLowerInvariant() + "\"", html);
        Assert.DoesNotContain("HELPMATH", html);
    }

    [Fact]
    public void CodeFencesInlineCodeEscapedDollarsAndHtmlAreNotExecutedOrParsedAsMath()
    {
        const string source = "```text\n$x$ \\(x\\)\n```\n\n~~~text\n$$x$$\n~~~\n\n`$x$` and ``\\[x\\]`` and \\$5.\n\n<script>alert(1)</script>\n\n$x_2$";
        var html = new HelpMarkdownRenderer(_catalog).RenderMarkdown(source);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "data-tex="));
        Assert.Contains("<code>$x$</code>", html);
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("HELPMATH", html);
    }

    [Fact]
    public void SourceLinksResolveAcrossTheoryAndPackagedReferenceDocuments()
    {
        Assert.Equal("activity", _catalog.ResolveLink("docs/theory/attention-centered-dock-workspace-design.md", "./activity-theory-requirements-decomposition.md"));
        Assert.Equal("attention#摘要", _catalog.ResolveLink(null, "help:attention#摘要"));
        var reference = _catalog.ResolveLink("docs/theory/attention-centered-dock-workspace-design.md", "../reference/dock-layout-snapshot-v2.md");
        Assert.NotNull(reference);
        Assert.NotNull(_catalog.Find(reference));
        Assert.Null(_catalog.ResolveLink(null, "file:///C:/private.md"));
        Assert.Null(_catalog.ResolveLink(null, "help:does-not-exist"));
    }

    [Fact]
    public void SearchIncludesCollapsedOriginalAndObservesCancellation()
    {
        var results = _catalog.Search("Scalable Fabric", CancellationToken.None);
        Assert.Contains(results, result => result.ArticleId == "attention");
        Assert.Empty(_catalog.Search("  ", CancellationToken.None));
        Assert.Empty(_catalog.Search("不存在的唯一词组9183745", CancellationToken.None));
        Assert.Throws<OperationCanceledException>(() => _catalog.Search("Document", new CancellationToken(true)));
    }

    [Fact]
    public void RenderEveryChapterAndExportOptionalBrowserFixtures()
    {
        var renderer = new HelpMarkdownRenderer(_catalog);
        var pages = HelpContentCatalog.Chapters.Select(article => renderer.Render(article, article.Id, CancellationToken.None)).ToArray();
        Assert.All(pages, page => Assert.DoesNotContain("HELPMATH", page.Html));
        Assert.True(pages.Any(page => page.Html.Contains("mermaid")), "原文 Mermaid 围栏应保留为可渲染图示。");
        Assert.All(pages.Where(p => p.Id is "attention" or "activity" or "production" or "forkable"),
            page => Assert.Contains("id=\"full-text\"", page.Html));
        var exportDirectory = Environment.GetEnvironmentVariable("MYAVALONIA_HELP_TEST_EXPORT_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(exportDirectory))
        {
            Directory.CreateDirectory(exportDirectory);
            File.WriteAllText(Path.Combine(exportDirectory, "help-pages.json"),
                JsonSerializer.Serialize(pages, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        }
    }

    [Fact]
    public void ReadingStateRoundTripsAtomicallyAndCorruptStateFallsBack()
    {
        var directory = Path.Combine(Path.GetTempPath(), "host-help-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new HelpReadingStateStore(Path.Combine(directory, "help-v1.json"));
            var state = new HelpReadingState { ArticleId = "attention", FontSize = 21, X = -1800, Y = 20 };
            state.PositionFor("attention").FullTextExpanded = true;
            state.PositionFor("attention").ScrollTop = 521;
            Assert.True(store.Save(state));
            Assert.True(store.Save(state with { FontSize = 22 }));
            var restored = store.Load();
            Assert.Equal(22, restored.FontSize);
            Assert.Equal(-1800, restored.X);
            Assert.True(restored.PositionFor("attention").FullTextExpanded);
            Assert.Equal(521, restored.PositionFor("attention").ScrollTop);
            Assert.Single(Directory.GetFiles(directory));
            File.WriteAllText(store.FilePath, "invalid json");
            Assert.Equal("product", store.Load().ArticleId);
            File.WriteAllText(store.FilePath, "{\"FontSize\":900,\"Width\":5,\"Pages\":null}");
            Assert.Equal(26, store.Load().FontSize);
            Assert.Equal(640, store.Load().Width);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
