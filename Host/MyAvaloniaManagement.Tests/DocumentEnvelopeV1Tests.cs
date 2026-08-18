using System.Text.Json;
using Dock.Model.Mvvm.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Documents;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Plugin;
using MyAvaloniaManagementCommon.Save;

namespace MyAvaloniaManagement.Tests;

/// <summary>
/// 验证唯一 Document 信封 v1 的线格式、资源门限和 Registry 所有权。
/// </summary>
public sealed class DocumentEnvelopeV1Tests
{
    private static readonly DateTimeOffset FixedUtc =
        new(2026, 8, 18, 1, 2, 3, TimeSpan.Zero);

    [Fact]
    public void 内容快照_只接受正整数Schema和非空引用正文()
    {
        var snapshot = new DocumentContentSnapshot(3, string.Empty);

        Assert.Equal(3, snapshot.ContentSchemaVersion);
        Assert.Equal(string.Empty, snapshot.Payload);
        Assert.Throws<ArgumentOutOfRangeException>(() => new DocumentContentSnapshot(0, "{}"));
        Assert.Throws<ArgumentNullException>(() => new DocumentContentSnapshot(1, null!));
    }

    [Fact]
    public void 序列化_准确写出七个camelCase字段并保持Unicode正文()
    {
        var serializer = new DocumentEnvelopeSerializer();
        var payload = "{\"message\":\"你好，G7\"}";

        var json = serializer.Serialize(
            new PluginId("myavalonia.plugin.sample"),
            new DocumentTypeId("myavalonia.plugin.sample.document.report"),
            "示例文档",
            FixedUtc,
            new DocumentContentSnapshot(7, payload));

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(
            [
                "schemaVersion", "pluginId", "documentTypeId", "contentSchemaVersion",
                "title", "savedAtUtc", "payload",
            ],
            root.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("myavalonia.plugin.sample", root.GetProperty("pluginId").GetString());
        Assert.Equal(
            "myavalonia.plugin.sample.document.report",
            root.GetProperty("documentTypeId").GetString());
        Assert.Equal(7, root.GetProperty("contentSchemaVersion").GetInt32());
        Assert.Equal("示例文档", root.GetProperty("title").GetString());
        Assert.Equal(TimeSpan.Zero, root.GetProperty("savedAtUtc").GetDateTimeOffset().Offset);
        Assert.Equal(payload, root.GetProperty("payload").GetString());
        Assert.Equal(json, serializer.Serialize(
            new PluginId("myavalonia.plugin.sample"),
            new DocumentTypeId("myavalonia.plugin.sample.document.report"),
            "示例文档",
            FixedUtc,
            new DocumentContentSnapshot(7, payload)));
    }

    [Fact]
    public void 往返_宿主字段与插件内容保持分离()
    {
        var serializer = new DocumentEnvelopeSerializer();
        var json = ValidEnvelope(serializer, payload: "{\"value\":42}");

        var envelope = serializer.Deserialize(json);

        Assert.Equal(HostExtensionIds.Owner, envelope.PluginId);
        Assert.Equal(TestSavableStrategy.TypeId, envelope.DocumentTypeId);
        Assert.Equal("测试文档", envelope.Title);
        Assert.Equal(FixedUtc, envelope.SavedAtUtc);
        Assert.Equal(1, envelope.Content.ContentSchemaVersion);
        Assert.Equal("{\"value\":42}", envelope.Content.Payload);
    }

    [Theory]
    [MemberData(nameof(InvalidEnvelopeCases))]
    public void 严格读取_拒绝非唯一V1结构(string json)
    {
        var exception = Assert.Throws<DocumentLoadException>(() =>
            new DocumentEnvelopeSerializer().Deserialize(json));

        Assert.DoesNotContain("secret-payload", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void 资源门限_实际信封恰好八MiB可往返_增加一字节即拒绝()
    {
        var serializer = new DocumentEnvelopeSerializer();
        var emptyEnvelope = ValidEnvelope(serializer, payload: string.Empty);
        var fixedBytes = System.Text.Encoding.UTF8.GetByteCount(emptyEnvelope);
        var exactPayload = new string(
            'a',
            DocumentEnvelopeSerializer.MaximumEnvelopeBytes - fixedBytes);

        var exactEnvelope = ValidEnvelope(serializer, exactPayload);

        Assert.Equal(
            DocumentEnvelopeSerializer.MaximumEnvelopeBytes,
            System.Text.Encoding.UTF8.GetByteCount(exactEnvelope));
        serializer.ValidateFileLength(DocumentEnvelopeSerializer.MaximumEnvelopeBytes);
        Assert.Equal(exactPayload, serializer.Deserialize(exactEnvelope).Content.Payload);
        Assert.Throws<DocumentLoadException>(() =>
            serializer.ValidateFileLength(DocumentEnvelopeSerializer.MaximumEnvelopeBytes + 1L));
        Assert.Throws<JsonException>(() => serializer.Serialize(
            HostExtensionIds.Owner,
            TestSavableStrategy.TypeId,
            "测试文档",
            FixedUtc,
            new DocumentContentSnapshot(1, exactPayload + "a")));
    }

    [Fact]
    public void 深度门限_八层可完成解析后再拒绝未知字段_九层由解析器拒绝()
    {
        var serializer = new DocumentEnvelopeSerializer();
        var boundaryException = Assert.Throws<DocumentLoadException>(() =>
            serializer.Deserialize(CreateEnvelopeWithUnknownDepth(
                DocumentEnvelopeSerializer.MaximumJsonDepth - 1)));
        var overLimitException = Assert.Throws<DocumentLoadException>(() =>
            serializer.Deserialize(CreateEnvelopeWithUnknownDepth(
                DocumentEnvelopeSerializer.MaximumJsonDepth)));

        Assert.Null(boundaryException.InnerException);
        Assert.IsAssignableFrom<JsonException>(overLimitException.InnerException);
    }

    [Fact]
    public async Task 保存_身份标题和UTC时间全部由宿主填充()
    {
        var clock = new FixedTimeProvider(FixedUtc);
        using var context = new TestHostContext(
            documentStrategies: [new TestSavableStrategy()],
            configureServices: services => services.AddSingleton<TimeProvider>(clock));
        var path = Path.Combine(context.TempDirectory, "host-owned.mamdoc");
        context.Storage.SavePath = path;
        var viewModel = context.CreateMainWindowViewModel();
        viewModel.CreateDocument(TestSavableStrategy.TypeId.Value);
        var document = Assert.Single(GetDocuments(context));
        document.Content = "插件正文";
        GetDocumentDock(context).ActiveDockable = document;

        await viewModel.SaveDocument();

        var write = Assert.Single(context.Storage.Writes, item =>
            DocumentPathIdentity.Equals(item.Path, path));
        var envelope = new DocumentEnvelopeSerializer().Deserialize(write.Content);
        Assert.Equal(HostExtensionIds.Owner, envelope.PluginId);
        Assert.Equal(TestSavableStrategy.TypeId, envelope.DocumentTypeId);
        Assert.Equal("host-owned", envelope.Title);
        Assert.Equal(FixedUtc, envelope.SavedAtUtc);
        Assert.Equal("插件正文", envelope.Content.Payload);
    }

    [Fact]
    public async Task 打开_所有权不匹配时不发布不写入且不泄漏Document()
    {
        using var context = new TestHostContext(
            documentStrategies: [new TestSavableStrategy()]);
        var path = Path.Combine(context.TempDirectory, "wrong-owner.mamdoc");
        var json = new DocumentEnvelopeSerializer().Serialize(
            new PluginId("myavalonia.plugin.other"),
            TestSavableStrategy.TypeId,
            "错误所有者",
            FixedUtc,
            new DocumentContentSnapshot(1, "secret-payload"));
        context.Storage.AddFile(path, json);
        var viewModel = context.CreateMainWindowViewModel();

        await viewModel.OpenDocumentByPath(path);

        Assert.Empty(GetDocuments(context));
        Assert.Empty(context.Storage.Writes);
        Assert.Contains("插件所有者", viewModel.DocumentOperationError);
        Assert.DoesNotContain("secret-payload", viewModel.DocumentOperationError);
    }

    [Fact]
    public async Task 打开_历史别名与未注册类型均被逐文件拒绝()
    {
        var legacyId = new DocumentTypeId("myavalonia.host.document.legacy-test");
        var metadata = new DocumentMetadata(
            TestSavableStrategy.TypeId,
            "测试文档",
            [legacyId]);
        using var context = new TestHostContext(
            documentStrategies: [new TestSavableStrategy(metadata)]);
        var aliasPath = Path.Combine(context.TempDirectory, "alias.mamdoc");
        var unknownPath = Path.Combine(context.TempDirectory, "unknown.mamdoc");
        var serializer = new DocumentEnvelopeSerializer();
        context.Storage.AddFile(aliasPath, serializer.Serialize(
            HostExtensionIds.Owner,
            legacyId,
            "历史别名",
            FixedUtc,
            new DocumentContentSnapshot(1, "{}")));
        context.Storage.AddFile(unknownPath, serializer.Serialize(
            HostExtensionIds.Owner,
            new DocumentTypeId("myavalonia.host.document.unknown"),
            "未知类型",
            FixedUtc,
            new DocumentContentSnapshot(1, "{}")));
        context.Storage.OpenPaths = [aliasPath, unknownPath];
        var viewModel = context.CreateMainWindowViewModel();

        await viewModel.OpenDocument();

        Assert.Empty(GetDocuments(context));
        Assert.Empty(context.Storage.Writes);
        Assert.True(viewModel.HasDocumentOperationError);
    }

    [Fact]
    public async Task 打开_文件长度超限时不会读取整份文本()
    {
        using var context = new TestHostContext(
            documentStrategies: [new TestSavableStrategy()]);
        var path = Path.Combine(context.TempDirectory, "too-large.mamdoc");
        context.Storage.AddFile(
            path,
            new string('a', DocumentEnvelopeSerializer.MaximumEnvelopeBytes + 1));
        var viewModel = context.CreateMainWindowViewModel();

        await viewModel.OpenDocumentByPath(path);

        Assert.Equal(0, context.Storage.ReadCount);
        Assert.Empty(GetDocuments(context));
        Assert.Empty(context.Storage.Writes);
    }

    [Fact]
    public async Task 打开_插件内容加载失败时不发布不写入并完整释放Scope()
    {
        var probe = new DocumentLifecycleProbe { ThrowOnLoad = true };
        using var context = new TestHostContext(configureServices: services =>
        {
            services.AddSingleton(probe);
            services.AddScoped<TrackedScopedDependency>();
            services.AddScoped<TrackedScopedSavableDocument>();
            services.AddSingleton<IDocumentCreationStrategy>(provider =>
                new TrackedScopedSavableStrategy(
                    provider.GetRequiredService<IDocumentScopeFactory>()));
        });
        var path = Path.Combine(context.TempDirectory, "broken-payload.mamdoc");
        context.Storage.AddFile(path, new DocumentEnvelopeSerializer().Serialize(
            HostExtensionIds.Owner,
            TrackedScopedSavableStrategy.TypeId,
            "损坏内容",
            FixedUtc,
            new DocumentContentSnapshot(1, "secret-payload")));
        var viewModel = context.CreateMainWindowViewModel();

        await viewModel.OpenDocumentByPath(path);

        Assert.Empty(GetDocumentDock(context).VisibleDockables!
            .OfType<TrackedScopedSavableDocument>());
        Assert.Empty(context.Storage.Writes);
        Assert.Equal(1, probe.CreatedCount);
        Assert.Equal(1, probe.LoadCount);
        Assert.Equal(1, probe.CancellationCount);
        Assert.Equal(1, probe.DocumentDisposeCount);
        Assert.Equal(1, probe.DependencyDisposeCount);
        Assert.True(probe.AllDocumentsObservedClosing);
        Assert.DoesNotContain("secret-payload", viewModel.DocumentOperationError);
    }

    public static IEnumerable<object[]> InvalidEnvelopeCases()
    {
        var serializer = new DocumentEnvelopeSerializer();
        var valid = ValidEnvelope(serializer, payload: "secret-payload");
        yield return [valid.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 0")];
        yield return [valid.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 2")];
        yield return [valid.Replace("\"schemaVersion\": 1", "\"schemaVersion\": \"1\"")];
        yield return [valid.Replace("\"schemaVersion\": 1,", "")];
        yield return [valid.Replace("\"schemaVersion\": 1,", "\"schemaVersion\": 1,\n  \"schemaVersion\": 1,")];
        yield return [valid.Replace("\"payload\"", "\"Payload\"")];
        yield return [valid.Replace("\"payload\": \"secret-payload\"", "\"payload\": {\"value\":1}")];
        yield return [valid.Replace("\"savedAtUtc\": \"2026-08-18T01:02:03+00:00\"", "\"savedAtUtc\": \"2026-08-18T09:02:03+08:00\"")];
        yield return [valid.Replace("\"pluginId\": \"myavalonia.host\"", "\"pluginId\": \"INVALID ID\"")];
        yield return [valid.Replace("\"documentTypeId\": \"myavalonia.host.document.test\"", "\"documentTypeId\": \"INVALID ID\"")];
        yield return [valid.Replace("\"contentSchemaVersion\": 1", "\"contentSchemaVersion\": 0")];
        yield return [valid.Insert(valid.LastIndexOf('}'), ",\n  \"unknown\": true")];
        yield return [valid.Insert(valid.LastIndexOf('}'), ",")];
        yield return [valid.Insert(1, "/*comment*/")];
    }

    private static string ValidEnvelope(
        DocumentEnvelopeSerializer serializer,
        string payload) =>
        serializer.Serialize(
            HostExtensionIds.Owner,
            TestSavableStrategy.TypeId,
            "测试文档",
            FixedUtc,
            new DocumentContentSnapshot(1, payload));

    private static string CreateEnvelopeWithUnknownDepth(int nestedObjectCount)
    {
        var nested = "0";
        for (var index = 0; index < nestedObjectCount; index++)
        {
            nested = $"{{\"level\":{nested}}}";
        }

        return $$"""
        {
          "schemaVersion": 1,
          "pluginId": "myavalonia.host",
          "documentTypeId": "myavalonia.host.document.test",
          "contentSchemaVersion": 1,
          "title": "深度测试",
          "savedAtUtc": "2026-08-18T01:02:03+00:00",
          "payload": "{}",
          "nested": {{nested}}
        }
        """;
    }

    private static List<TestSavableDocument> GetDocuments(TestHostContext context) =>
        GetDocumentDock(context).VisibleDockables!
            .OfType<TestSavableDocument>()
            .ToList();

    private static DocumentDock GetDocumentDock(TestHostContext context) =>
        Assert.IsType<DocumentDock>(
            context.Factory.GetDockable<Dock.Model.Controls.IDocumentDock>("Files"));

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
