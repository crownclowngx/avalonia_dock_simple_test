using System.Text;
using System.Text.Json;
using MyAvaloniaManagement.Business.Documents;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.Tests;

/// <summary>验证 Document V2 唯一线格式、嵌套 JSON 与资源边界。</summary>
public sealed class DocumentEnvelopeV2Tests
{
    private static readonly PluginId Owner = new("myavalonia.plugin.document-v2-test");
    private static readonly DocumentTypeId TypeId =
        new("myavalonia.plugin.document-v2-test.document.sample");
    private static readonly DateTimeOffset FixedUtc =
        new(2026, 8, 21, 1, 2, 3, TimeSpan.Zero);

    [Theory]
    [InlineData("{\"name\":\"中文\",\"value\":42}", JsonValueKind.Object)]
    [InlineData("[1,true,null]", JsonValueKind.Array)]
    [InlineData("\"文本\"", JsonValueKind.String)]
    [InlineData("123", JsonValueKind.Number)]
    [InlineData("null", JsonValueKind.Null)]
    public void 往返保持原生Json且只写六个根字段(string payloadJson, JsonValueKind kind)
    {
        var serializer = new DocumentEnvelopeSerializer();
        var json = serializer.Serialize(Owner, TypeId, "V2 文档", FixedUtc, Content(payloadJson));

        using var parsed = JsonDocument.Parse(json);
        Assert.Equal(
            ["schemaVersion", "pluginId", "documentTypeId", "title", "savedAtUtc", "content"],
            parsed.RootElement.EnumerateObject().Select(item => item.Name));
        Assert.Equal(2, parsed.RootElement.GetProperty("schemaVersion").GetInt32());
        var nested = parsed.RootElement.GetProperty("content");
        Assert.Equal(["schemaVersion", "payload"], nested.EnumerateObject().Select(item => item.Name));
        Assert.Equal(kind, nested.GetProperty("payload").ValueKind);

        var envelope = serializer.Deserialize(json);
        Assert.Equal(Owner, envelope.PluginId);
        Assert.Equal(TypeId, envelope.DocumentTypeId);
        Assert.Equal("V2 文档", envelope.Title);
        Assert.Equal(FixedUtc, envelope.SavedAtUtc);
        using var expectedPayload = JsonDocument.Parse(payloadJson);
        Assert.True(JsonElement.DeepEquals(
            expectedPayload.RootElement,
            envelope.Content.Payload));
    }

    [Fact]
    public void 反序列化结果不依赖内部JsonDocument生命周期()
    {
        var serializer = new DocumentEnvelopeSerializer();
        var envelope = serializer.Deserialize(
            serializer.Serialize(Owner, TypeId, "克隆", FixedUtc, Content("{\"value\":7}")));

        Assert.Equal(7, envelope.Content.Payload.GetProperty("value").GetInt32());
    }

    [Theory]
    [MemberData(nameof(InvalidCases))]
    public void 严格读取拒绝V1和所有非唯一结构(string caseName, string json)
    {
        var exception = Assert.Throws<DocumentEnvelopeException>(() =>
            new DocumentEnvelopeSerializer().Deserialize(json));

        Assert.False(string.IsNullOrWhiteSpace(caseName));
        Assert.DoesNotContain("secret-payload", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 八MiB边界在读取前与序列化后都执行()
    {
        var serializer = new DocumentEnvelopeSerializer();
        var empty = serializer.Serialize(Owner, TypeId, "大小", FixedUtc, Content("\"\""));
        var remaining = DocumentEnvelopeSerializer.MaximumEnvelopeBytes - Encoding.UTF8.GetByteCount(empty);
        var exact = serializer.Serialize(
            Owner,
            TypeId,
            "大小",
            FixedUtc,
            Content(JsonSerializer.Serialize(new string('a', remaining))));

        Assert.Equal(DocumentEnvelopeSerializer.MaximumEnvelopeBytes, Encoding.UTF8.GetByteCount(exact));
        serializer.ValidateFileLength(DocumentEnvelopeSerializer.MaximumEnvelopeBytes);
        Assert.Equal(remaining, serializer.Deserialize(exact).Content.Payload.GetString()!.Length);
        Assert.Throws<DocumentEnvelopeException>(() =>
            serializer.ValidateFileLength(DocumentEnvelopeSerializer.MaximumEnvelopeBytes + 1L));
        Assert.Throws<JsonException>(() => serializer.Serialize(
            Owner,
            TypeId,
            "大小",
            FixedUtc,
            Content(JsonSerializer.Serialize(new string('a', remaining + 1)))));
    }

    [Fact]
    public void Json深度超过八层时拒绝且不泄漏正文()
    {
        var deep = "0";
        for (var index = 0; index < 10; index++)
        {
            deep = $"{{\"level\":{deep}}}";
        }

        var json =
            "{\"schemaVersion\":2,\"pluginId\":\"myavalonia.plugin.document-v2-test\"," +
            "\"documentTypeId\":\"myavalonia.plugin.document-v2-test.document.sample\"," +
            "\"title\":\"深度\",\"savedAtUtc\":\"2026-08-21T01:02:03+00:00\"," +
            "\"content\":{\"schemaVersion\":1,\"payload\":" + deep + "}}";
        var exception = Assert.Throws<DocumentEnvelopeException>(() =>
            new DocumentEnvelopeSerializer().Deserialize(json));
        Assert.IsAssignableFrom<JsonException>(exception.InnerException);
    }

    [Fact]
    public void 写入参数与空文件边界严格防御()
    {
        var serializer = new DocumentEnvelopeSerializer();
        var content = Content("null");
        Assert.Throws<ArgumentNullException>(() =>
            serializer.Serialize(null!, TypeId, "标题", FixedUtc, content));
        Assert.Throws<ArgumentNullException>(() =>
            serializer.Serialize(Owner, null!, "标题", FixedUtc, content));
        Assert.Throws<ArgumentException>(() =>
            serializer.Serialize(Owner, TypeId, "  ", FixedUtc, content));
        Assert.Throws<ArgumentException>(() =>
            serializer.Serialize(Owner, TypeId, "标题", FixedUtc.ToOffset(TimeSpan.FromHours(8)), content));
        Assert.Throws<ArgumentNullException>(() =>
            serializer.Serialize(Owner, TypeId, "标题", FixedUtc, null!));
        Assert.Throws<DocumentEnvelopeException>(() => serializer.Deserialize(string.Empty));
        Assert.Throws<DocumentEnvelopeException>(() => serializer.ValidateFileLength(0));
    }

    public static IEnumerable<object[]> InvalidCases()
    {
        var valid = ValidJson();
        yield return ["v1", valid.Replace("\"schemaVersion\":2", "\"schemaVersion\":1", StringComparison.Ordinal)];
        yield return ["duplicate-root", valid.Replace("\"schemaVersion\":2,", "\"schemaVersion\":2,\"schemaVersion\":2,", StringComparison.Ordinal)];
        yield return ["case", valid.Replace("\"pluginId\"", "\"PluginId\"", StringComparison.Ordinal)];
        yield return ["title", valid.Replace("\"title\":\"V2 文档\"", "\"title\":\"   \"", StringComparison.Ordinal)];
        yield return ["offset", valid.Replace("+00:00", "+08:00", StringComparison.Ordinal)];
        yield return ["plugin-id", valid.Replace("\"pluginId\":\"myavalonia.plugin.document-v2-test\"", "\"pluginId\":\"INVALID\"", StringComparison.Ordinal)];
        yield return ["document-id", valid.Replace("myavalonia.plugin.document-v2-test.document.sample", "INVALID", StringComparison.Ordinal)];
        yield return ["root-not-object", "[]"];
        yield return ["root-schema-type", valid.Replace("\"schemaVersion\":2", "\"schemaVersion\":\"2\"", StringComparison.Ordinal)];
        yield return ["plugin-type", valid.Replace("\"pluginId\":\"myavalonia.plugin.document-v2-test\"", "\"pluginId\":2", StringComparison.Ordinal)];
        yield return ["content-not-object", valid.Replace("\"content\":{\"schemaVersion\":1,\"payload\":{\"ok\":true}}", "\"content\":null", StringComparison.Ordinal)];
        yield return ["content-schema-zero", valid.Replace("\"content\":{\"schemaVersion\":1", "\"content\":{\"schemaVersion\":0", StringComparison.Ordinal)];
        yield return ["missing-content", "{\"schemaVersion\":2,\"pluginId\":\"myavalonia.plugin.document-v2-test\",\"documentTypeId\":\"myavalonia.plugin.document-v2-test.document.sample\",\"title\":\"x\",\"savedAtUtc\":\"2026-08-21T01:02:03+00:00\"}"];
        yield return ["duplicate-content", "{\"schemaVersion\":2,\"pluginId\":\"myavalonia.plugin.document-v2-test\",\"documentTypeId\":\"myavalonia.plugin.document-v2-test.document.sample\",\"title\":\"x\",\"savedAtUtc\":\"2026-08-21T01:02:03+00:00\",\"content\":{\"schemaVersion\":1,\"schemaVersion\":1,\"payload\":null}}"];
        yield return ["unknown-content", "{\"schemaVersion\":2,\"pluginId\":\"myavalonia.plugin.document-v2-test\",\"documentTypeId\":\"myavalonia.plugin.document-v2-test.document.sample\",\"title\":\"x\",\"savedAtUtc\":\"2026-08-21T01:02:03+00:00\",\"content\":{\"schemaVersion\":1,\"payload\":null,\"unknown\":\"secret-payload\"}}"];
        yield return ["unknown-root", valid.Insert(valid.LastIndexOf('}'), ",\n  \"unknown\": \"secret-payload\"")];
        yield return ["comment", valid.Insert(1, "/*comment*/")];
        yield return ["trailing", valid.Insert(valid.LastIndexOf('}'), ",")];
    }

    private static string ValidJson() =>
        "{\"schemaVersion\":2,\"pluginId\":\"myavalonia.plugin.document-v2-test\"," +
        "\"documentTypeId\":\"myavalonia.plugin.document-v2-test.document.sample\"," +
        "\"title\":\"V2 文档\",\"savedAtUtc\":\"2026-08-21T01:02:03+00:00\"," +
        "\"content\":{\"schemaVersion\":1,\"payload\":{\"ok\":true}}}";

    private static DocumentContent Content(string json)
    {
        using var document = JsonDocument.Parse(json);
        return new DocumentContent(1, document.RootElement);
    }
}
