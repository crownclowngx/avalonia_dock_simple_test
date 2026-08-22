using System.Text.Json;
using MyAvaloniaManagement.PluginSdk;

namespace MyPlugTest.Persistence;

/// <summary>
/// 负责 TestWelcome Document 自有 content schema 1 的严格编码与解码。
/// </summary>
/// <remarks>
/// Host 只验证外层 Document V2 信封，不解释插件 payload。本 Codec 把 JSON 规则从 ViewModel 中分离，
/// 使 ViewModel 只负责界面状态和提交时机。读取时先把完整 payload 验证到不可变临时状态，再由调用方
/// 一次提交，避免后段字段损坏时留下半恢复状态。当前 schema 1 不读取旧字符串快照，也不接受宽松字段别名。
/// </remarks>
internal static class TestWelcomeDocumentContentCodec
{
    internal const int SchemaVersion = 1;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
    private static readonly string[] RootPropertyNames =
        ["url", "responseContent", "historyItems"];

    /// <summary>把当前业务快照编码为由 <see cref="DocumentContent"/> 拥有的原生 JSON。</summary>
    internal static DocumentContent Encode(
        string url,
        string responseContent,
        IEnumerable<string> historyUrls)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(responseContent);
        ArgumentNullException.ThrowIfNull(historyUrls);

        var payload = JsonSerializer.SerializeToElement(new WelcomePayload(
            url,
            responseContent,
            historyUrls.Select(static item => new HistoryPayload(item)).ToArray()),
            SerializerOptions);
        return new DocumentContent(SchemaVersion, payload);
    }

    /// <summary>严格读取 schema 1，并返回尚未影响 ViewModel 的完整临时状态。</summary>
    /// <exception cref="InvalidDataException">schema 或 payload 不符合 MyPlugTest 当前内容协议。</exception>
    internal static TestWelcomeDocumentState Decode(DocumentContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.SchemaVersion != SchemaVersion)
        {
            throw Invalid("测试文档内容版本不受支持。");
        }

        var payload = content.Payload;
        if (payload.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("测试文档内容必须是 JSON 对象。");
        }

        var properties = ReadExactProperties(payload, RootPropertyNames, "测试文档内容字段无效。");
        var url = ReadRequiredString(properties["url"], "测试文档 URL 字段无效。", allowWhiteSpace: false);
        var responseContent = ReadRequiredString(
            properties["responseContent"],
            "测试文档响应字段无效。",
            allowWhiteSpace: true);
        var historyElement = properties["historyItems"];
        if (historyElement.ValueKind != JsonValueKind.Array)
        {
            throw Invalid("测试文档历史记录必须是数组。");
        }

        var historyUrls = new List<string>();
        foreach (var item in historyElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw Invalid("测试文档历史记录项必须是 JSON 对象。");
            }

            var itemProperties = ReadExactProperties(
                item,
                ["url"],
                "测试文档历史记录字段无效。");
            historyUrls.Add(ReadRequiredString(
                itemProperties["url"],
                "测试文档历史记录 URL 无效。",
                allowWhiteSpace: false));
        }

        return new TestWelcomeDocumentState(url, responseContent, historyUrls);
    }

    private static Dictionary<string, JsonElement> ReadExactProperties(
        JsonElement element,
        IReadOnlyCollection<string> expectedNames,
        string errorMessage)
    {
        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!expectedNames.Contains(property.Name, StringComparer.Ordinal) ||
                !properties.TryAdd(property.Name, property.Value))
            {
                throw Invalid(errorMessage);
            }
        }

        if (properties.Count != expectedNames.Count ||
            expectedNames.Any(name => !properties.ContainsKey(name)))
        {
            throw Invalid(errorMessage);
        }

        return properties;
    }

    private static string ReadRequiredString(
        JsonElement element,
        string errorMessage,
        bool allowWhiteSpace)
    {
        if (element.ValueKind != JsonValueKind.String)
        {
            throw Invalid(errorMessage);
        }

        var value = element.GetString()!;
        if (!allowWhiteSpace && string.IsNullOrWhiteSpace(value))
        {
            throw Invalid(errorMessage);
        }

        return value;
    }

    private static InvalidDataException Invalid(string message) => new(message);

    private sealed record WelcomePayload(
        string Url,
        string ResponseContent,
        IReadOnlyList<HistoryPayload> HistoryItems);

    private sealed record HistoryPayload(string Url);
}

/// <summary>表示已经完整验证、可以原子提交到欢迎 Document 的业务状态。</summary>
internal sealed record TestWelcomeDocumentState(
    string Url,
    string ResponseContent,
    IReadOnlyList<string> HistoryUrls);
