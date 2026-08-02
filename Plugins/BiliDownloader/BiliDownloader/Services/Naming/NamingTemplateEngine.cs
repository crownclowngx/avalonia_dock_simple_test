using System.Text;

namespace BiliDownloader.Services.Naming;

/// <summary>
/// 命名上下文：模板渲染时所需的视频元数据。
/// <para>
/// 设计思考：从 BiliVideoItem + BiliVideoCollection 构造，将模板引擎与具体模型解耦。
/// 模板引擎只依赖此上下文，不直接引用 BiliVideoItem，保持纯函数特性。
/// UpName 和 PublishDate 来自 Collection 级别（同一批次共享），
/// 番剧场景下 UpName 为空字符串、PublishDate 为 null。
/// </para>
/// </summary>
public sealed class NamingContext
{
    /// <summary>视频标题（当前标题，可能已被用户重命名）</summary>
    public string Title { get; init; } = "";

    /// <summary>列表中的递增序号（从 1 开始）</summary>
    public int Index { get; init; }

    /// <summary>BV 号（如 "BV1xx411c7mD"）</summary>
    public string Bvid { get; init; } = "";

    /// <summary>UP 主名称（番剧场景为空字符串）</summary>
    public string UpName { get; init; } = "";

    /// <summary>发布时间（番剧或无数据时为 null）</summary>
    public DateTime? PublishDate { get; init; }

    /// <summary>系列/合集标题</summary>
    public string SeriesTitle { get; init; } = "";
}

/// <summary>
/// 模板验证结果。
/// </summary>
/// <param name="IsValid">模板是否合法</param>
/// <param name="ErrorMessage">错误描述（合法时为 null）</param>
/// <param name="UnknownVariables">模板中出现的未知变量名列表</param>
public record TemplateValidationResult(
    bool IsValid,
    string? ErrorMessage,
    IReadOnlyList<string> UnknownVariables);

/// <summary>
/// 模板变量描述信息（供 UI 展示可用变量列表）。
/// </summary>
/// <param name="Variable">变量占位符文本（如 "{title}"）</param>
/// <param name="Description">中文描述</param>
/// <param name="Example">示例输出</param>
public record TemplateVariableInfo(string Variable, string Description, string Example);

/// <summary>
/// 命名模板引擎：将模板字符串 + 上下文渲染为合法文件名。
/// <para>
/// 设计思考：纯函数静态类，不使用正则表达式（可读性 + 性能），用 {variable} 占位符替换。
/// 渲染后自动调用 FileNameSanitizer.Sanitize，保证输出始终是合法文件名。
/// 与 G4 的 TaskFilterSortEngine 同为无状态静态类，测试无需 DI 容器。
/// 支持变量：{title}, {index}, {bv}, {up}, {date}, {series}。
/// </para>
/// </summary>
public static class NamingTemplateEngine
{
    /// <summary>默认模板（当模板为空或无效时的回退值）</summary>
    public const string DefaultTemplate = "{index}.{title}";

    /// <summary>支持的变量名集合（用于验证）</summary>
    private static readonly HashSet<string> SupportedVariableNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "title", "index", "bv", "up", "date", "series"
    };

    /// <summary>
    /// 渲染模板为文件名。
    /// <para>
    /// 处理流程：逐字符扫描模板 → 遇到 '{' 时提取变量名 → 替换为上下文值 → 
    /// 最终经过 FileNameSanitizer.Sanitize 确保合法。
    /// 设计思考：不使用 Regex.Replace，因为变量数量固定（6 个），
    /// 简单字符串扫描 O(n) 线性、零额外分配、可读性更高。
    /// </para>
    /// </summary>
    /// <param name="template">模板字符串（如 "{index}.{title}"）</param>
    /// <param name="context">命名上下文</param>
    /// <returns>渲染并清理后的合法文件名</returns>
    public static string Render(string template, NamingContext context)
    {
        if (string.IsNullOrWhiteSpace(template))
            template = DefaultTemplate;

        var sb = new StringBuilder(template.Length + 32);
        var i = 0;

        while (i < template.Length)
        {
            if (template[i] == '{')
            {
                // 查找闭合的 '}'
                var closingIndex = template.IndexOf('}', i + 1);
                if (closingIndex < 0)
                {
                    // 未闭合的花括号，原样保留剩余部分
                    sb.Append(template, i, template.Length - i);
                    break;
                }

                var variableName = template[(i + 1)..closingIndex].Trim();
                var value = ResolveVariable(variableName, context);
                sb.Append(value);
                i = closingIndex + 1;
            }
            else
            {
                sb.Append(template[i]);
                i++;
            }
        }

        // 渲染结果经过文件名安全器清理，确保输出合法
        return FileNameSanitizer.Sanitize(sb.ToString());
    }

    /// <summary>
    /// 验证模板合法性。
    /// <para>
    /// 检查项：空模板、未闭合花括号、未知变量名。
    /// 设计思考：验证在用户输入时实时调用，需要快速返回且给出明确错误信息，
    /// 帮助用户修正模板。未知变量列表供 UI 高亮显示。
    /// </para>
    /// </summary>
    /// <param name="template">待验证的模板字符串</param>
    /// <returns>验证结果（是否合法 + 错误信息 + 未知变量列表）</returns>
    public static TemplateValidationResult Validate(string template)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return new TemplateValidationResult(false, "模板不能为空", Array.Empty<string>());
        }

        var unknownVariables = new List<string>();
        var i = 0;

        while (i < template.Length)
        {
            if (template[i] == '{')
            {
                var closingIndex = template.IndexOf('}', i + 1);
                if (closingIndex < 0)
                {
                    return new TemplateValidationResult(
                        false,
                        $"位置 {i} 处的 '{{' 没有对应的 '}}'",
                        unknownVariables);
                }

                var variableName = template[(i + 1)..closingIndex].Trim();

                // 检测嵌套花括号（如 {{title}}）
                if (variableName.Contains('{') || variableName.Contains('}'))
                {
                    return new TemplateValidationResult(
                        false,
                        "不支持嵌套花括号",
                        unknownVariables);
                }

                if (!SupportedVariableNames.Contains(variableName))
                {
                    unknownVariables.Add(variableName);
                }

                i = closingIndex + 1;
            }
            else
            {
                i++;
            }
        }

        if (unknownVariables.Count > 0)
        {
            var names = string.Join(", ", unknownVariables.Select(v => $"{{{v}}}"));
            return new TemplateValidationResult(
                false,
                $"未知变量：{names}",
                unknownVariables);
        }

        return new TemplateValidationResult(true, null, Array.Empty<string>());
    }

    /// <summary>
    /// 预览模板渲染结果（取前 maxPreview 项）。
    /// <para>
    /// 设计思考：预览让用户在提交前看到命名效果（所见即所得），
    /// 只取前 3 项避免大量渲染，性能开销可忽略。
    /// </para>
    /// </summary>
    /// <param name="template">模板字符串</param>
    /// <param name="items">命名上下文列表</param>
    /// <param name="maxPreview">最大预览数量（默认 3）</param>
    /// <returns>渲染后的文件名预览列表</returns>
    public static List<string> Preview(string template, IReadOnlyList<NamingContext> items, int maxPreview = 3)
    {
        if (items.Count == 0)
            return new List<string>();

        var count = Math.Min(items.Count, maxPreview);
        var results = new List<string>(count);

        for (var i = 0; i < count; i++)
        {
            results.Add(Render(template, items[i]));
        }

        return results;
    }

    /// <summary>
    /// 获取所有支持的模板变量信息（供 UI 展示变量选择列表）。
    /// </summary>
    public static IReadOnlyList<TemplateVariableInfo> GetSupportedVariables() => new List<TemplateVariableInfo>
    {
        new("{title}", "视频标题", "【教程】Avalonia 入门"),
        new("{index}", "列表序号（从 1 开始）", "3"),
        new("{bv}", "BV 号", "BV1xx411c7mD"),
        new("{up}", "UP 主名称", "某UP主"),
        new("{date}", "发布日期（yyyy-MM-dd）", "2026-07-21"),
        new("{series}", "系列/合集标题", "Avalonia 系列教程"),
    };

    /// <summary>
    /// 根据变量名解析上下文中的对应值。
    /// </summary>
    private static string ResolveVariable(string variableName, NamingContext context)
    {
        return variableName.ToLowerInvariant() switch
        {
            "title" => context.Title,
            "index" => context.Index.ToString(),
            "bv" => context.Bvid,
            "up" => context.UpName,
            "date" => context.PublishDate?.ToString("yyyy-MM-dd") ?? "",
            "series" => context.SeriesTitle,
            _ => "" // 未知变量渲染为空（验证器会提前报错）
        };
    }
}
