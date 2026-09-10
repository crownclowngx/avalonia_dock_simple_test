using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Markdig;
using Markdig.Extensions.AutoIdentifiers;

namespace MyAvaloniaManagement.Business.Help;

internal sealed record HelpPage(string Id, string Title, string Description, string Html,
    string PlainText, string? Demo, string RequestId);

/// <summary>公式在 Markdown 解析前成为不透明令牌，代码围栏与行内代码保持原样。</summary>
internal sealed class HelpMarkdownRenderer(HelpContentCatalog catalog)
{
    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions().UseAutoIdentifiers(AutoIdentifierOptions.GitHub).DisableHtml().Build();

    internal HelpPage Render(HelpArticle article, string requestId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var guide = catalog.ReadGuide(article);
        var body = article.Source is null ? string.Empty : catalog.ReadRepository(article.Source);
        var html = new StringBuilder();
        if (guide.Length > 0)
            html.Append("<section data-source=\"\">").Append(RenderMarkdown(guide)).Append("</section>");
        if (article.Demo is not null)
            html.Append("<section class=\"demo-section\" aria-label=\"交互演示\"><div id=\"help-demo\"></div></section>");
        cancellationToken.ThrowIfCancellationRequested();
        if (body.Length > 0)
        {
            var source = WebUtility.HtmlEncode(article.Source);
            if (guide.Length > 0) html.Append("<details id=\"full-text\"><summary>完整正文与参考文献 <span>展开深入阅读</span></summary>");
            html.Append("<section data-source=\"").Append(source).Append("\">")
                .Append(RenderMarkdown(body)).Append("</section>");
            if (guide.Length > 0) html.Append("</details>");
        }
        return new(article.Id, article.Title, article.Description, html.ToString(),
            guide + "\n\n" + body, article.Demo, requestId);
    }

    internal string RenderMarkdown(string markdown)
    {
        var (protectedText, formulas) = ProtectMath(markdown);
        var html = Markdown.ToHtml(protectedText, _pipeline);
        foreach (var formula in formulas)
        {
            var tag = formula.Display ? "div" : "span";
            var replacement = $"<{tag} class=\"math-source{(formula.Display ? " display" : "")}\" data-tex=\"{WebUtility.HtmlEncode(formula.Tex)}\" data-display=\"{formula.Display.ToString().ToLowerInvariant()}\">{WebUtility.HtmlEncode(formula.Tex)}</{tag}>";
            if (formula.Display) html = html.Replace("<p>" + formula.Token + "</p>", replacement, StringComparison.Ordinal);
            html = html.Replace(formula.Token, replacement, StringComparison.Ordinal);
        }
        return html;
    }

    private sealed record Formula(string Token, string Tex, bool Display);

    private static (string Text, List<Formula> Formulas) ProtectMath(string input)
    {
        var text = input.Replace("\r\n", "\n", StringComparison.Ordinal);
        var output = new StringBuilder();
        var formulas = new List<Formula>();
        var prefix = "HELPMATH" + Guid.NewGuid().ToString("N");
        for (var i = 0; i < text.Length;)
        {
            // Recognize fenced code at a line start (up to three spaces), including longer fences.
            if (i == 0 || text[i - 1] == '\n')
            {
                var lineEnd = text.IndexOf('\n', i);
                if (lineEnd < 0) lineEnd = text.Length;
                var match = Regex.Match(text[i..lineEnd], @"^ {0,3}(`{3,}|~{3,})");
                if (match.Success)
                {
                    var fence = match.Groups[1].Value;
                    var end = lineEnd;
                    var closing = new Regex("^ {0,3}" + Regex.Escape(fence[0].ToString()) + "{" + fence.Length + @",}\s*$");
                    while (end < text.Length)
                    {
                        var next = text.IndexOf('\n', end + 1);
                        if (next < 0) next = text.Length;
                        if (closing.IsMatch(text[(end + 1)..next])) { end = next; break; }
                        end = next;
                    }
                    output.Append(text.AsSpan(i, end - i)); i = end; continue;
                }
            }
            if (text[i] == '`')
            {
                var count = 1;
                while (i + count < text.Length && text[i + count] == '`') count++;
                var marker = new string('`', count);
                var end = text.IndexOf(marker, i + count, StringComparison.Ordinal);
                if (end >= 0) { output.Append(text.AsSpan(i, end + count - i)); i = end + count; continue; }
            }
            string? delimiter = null;
            string? close = null;
            var display = false;
            if (text[i] == '\\' && i + 1 < text.Length && text[i + 1] is '(' or '[')
            {
                delimiter = text.Substring(i, 2); close = text[i + 1] == '(' ? "\\)" : "\\]";
                display = text[i + 1] == '[';
            }
            else if (text[i] == '\\' && i + 1 < text.Length)
            { output.Append(text.AsSpan(i, 2)); i += 2; continue; }
            else if (text[i] == '$')
            {
                display = i + 1 < text.Length && text[i + 1] == '$';
                if (display || (i + 1 < text.Length && !char.IsWhiteSpace(text[i + 1])))
                    delimiter = close = display ? "$$" : "$";
            }
            if (delimiter is not null)
            {
                var start = i + delimiter.Length;
                var end = text.IndexOf(close!, start, StringComparison.Ordinal);
                while (end > 0 && text[end - 1] == '\\' && close == "$")
                    end = text.IndexOf(close, end + 1, StringComparison.Ordinal);
                if (end > start && (display || !text[start..end].Contains('\n'))
                    && (display || !char.IsWhiteSpace(text[end - 1])))
                {
                    var token = prefix + formulas.Count + "END";
                    formulas.Add(new(token, text[start..end].Trim(), display));
                    output.Append(display ? "\n\n" + token + "\n\n" : token);
                    i = end + close!.Length; continue;
                }
            }
            output.Append(text[i++]);
        }
        return (output.ToString(), formulas);
    }
}
