using Dock.Model.Mvvm.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Plugin;
using MyAvaloniaManagementCommon.Save;
using MyAvaloniaManagementCommon.ToolCreation;
using Newtonsoft.Json;

namespace MyAvaloniaManagement.Tests;

/// <summary>
/// 验证强类型身份的词法边界、磁盘表示，以及注册表的原子失败语义。
/// </summary>
/// <remarks>
/// 这些测试刻意同时覆盖“值对象能否安全携带历史别名”和“主 ID 能否进入注册表”两层规则：
/// 历史 GUID、下划线与大写字符仍可被值对象读取，但只能作为 LegacyIds；主 ID 必须通过
/// 组合根的命名空间、所有权和小写点分层校验。
/// </remarks>
public sealed class IdentityAndRegistryTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" leading")]
    [InlineData("trailing ")]
    [InlineData("contains/slash")]
    [InlineData("包含中文")]
    public void 值对象拒绝空白首尾空格和非法字符(string value)
    {
        Assert.Throws<ArgumentException>(() => new PluginId(value));
        Assert.Throws<ArgumentException>(() => new DocumentTypeId(value));
        Assert.Throws<ArgumentException>(() => new ToolTypeId(value));
        Assert.Throws<ArgumentException>(() => new CreationIntentId(value));
    }

    [Theory]
    [InlineData("MyAvalonia.host.document.sample")]
    [InlineData("myavalonia.host.document.sample_name")]
    [InlineData("myavalonia..host.document.sample")]
    [InlineData("myavalonia.host.document.-sample")]
    public void 历史词法可读取但不能冒充规范主Id(string value)
    {
        Assert.False(new DocumentTypeId(value).IsCanonical);
        Assert.False(new ToolTypeId(value).IsCanonical);
    }

    [Fact]
    public void Document与Tool在两种Json边界都保持字符串标量()
    {
        var documentId = new DocumentTypeId("myavalonia.host.document.sample");
        var envelope = new DocumentSaveData
        {
            DocumentTypeId = documentId,
            Title = "sample",
            Content = "{}",
            PluginMetadata = "{}"
        };

        var newtonsoftJson = JsonConvert.SerializeObject(envelope);
        Assert.Contains("\"DocumentTypeId\":\"myavalonia.host.document.sample\"", newtonsoftJson);
        Assert.Equal(
            documentId,
            JsonConvert.DeserializeObject<DocumentSaveData>(newtonsoftJson)!.DocumentTypeId);

        var systemTextDocumentJson = System.Text.Json.JsonSerializer.Serialize(documentId);
        var toolId = new ToolTypeId("myavalonia.host.tool.sample");
        var systemTextToolJson = System.Text.Json.JsonSerializer.Serialize(toolId);
        Assert.Equal("\"myavalonia.host.document.sample\"", systemTextDocumentJson);
        Assert.Equal("\"myavalonia.host.tool.sample\"", systemTextToolJson);
        Assert.Equal(
            toolId,
            System.Text.Json.JsonSerializer.Deserialize<ToolTypeId>(systemTextToolJson));
    }

    [Fact]
    public void 历史别名在创建前规范化且注册表只发布主Id()
    {
        var primary = new DocumentTypeId("myavalonia.host.document.sample");
        var legacy = new DocumentTypeId("OLD-DOCUMENT-ID");
        var strategy = new CapturingDocumentStrategy(
            new DocumentMetadata(primary, "示例", [legacy]));
        var registry = new HostExtensionRegistry([strategy], []);

        var document = registry.CreateDocument(new DocumentCreationParams(legacy));

        Assert.NotNull(document);
        Assert.Equal(primary, strategy.LastParameters!.DocumentTypeId);
        Assert.Equal(primary, registry.ResolveDocumentTypeId(legacy));
        Assert.Equal([primary], registry.DocumentMetadata.Keys);
    }

    [Fact]
    public void 重复别名所有权错误和空元数据会原子汇总为结构化诊断()
    {
        var duplicate = new DocumentTypeId("myavalonia.host.document.duplicate");
        var alias = new DocumentTypeId("OLD-ID");
        var first = new CapturingDocumentStrategy(
            new DocumentMetadata(duplicate, "第一项", [alias]));
        var second = new CapturingDocumentStrategy(
            new DocumentMetadata(duplicate, "第二项"));
        var aliasCollision = new CapturingDocumentStrategy(
            new DocumentMetadata(
                new DocumentTypeId("myavalonia.host.document.alias-owner"),
                "别名冲突",
                [duplicate]));
        var foreign = new CapturingDocumentStrategy(
            new DocumentMetadata(
                new DocumentTypeId("myavalonia.plugin.foreign.document.item"),
                string.Empty));

        var exception = Assert.Throws<HostCompositionException>(() =>
            new HostExtensionRegistry([first, second, aliasCollision, foreign], []));

        Assert.Contains(exception.Diagnostics, item => item.Code == "DOCUMENT_ID_DUPLICATE");
        Assert.Contains(exception.Diagnostics, item => item.Code == "DOCUMENT_ID_ALIAS_DUPLICATE");
        Assert.Contains(exception.Diagnostics, item => item.Code == "EXTENSION_OWNER_MISMATCH");
        Assert.Contains(exception.Diagnostics, item => item.Code == "EXTENSION_METADATA_INVALID");
        Assert.All(exception.Diagnostics, diagnostic =>
            Assert.All(diagnostic.Contributors, contributor =>
            {
                Assert.False(string.IsNullOrWhiteSpace(contributor.TypeName));
                Assert.False(string.IsNullOrWhiteSpace(contributor.AssemblyName));
            }));
    }

    [Fact]
    public void 同程序集多模块在任何服务注册发生前失败()
    {
        FirstModule.ConfigureCount = 0;
        SecondModule.ConfigureCount = 0;

        var exception = Assert.Throws<HostCompositionException>(() =>
            PluginModuleCatalog.Discover([typeof(FirstModule).Assembly]));

        var diagnostic = Assert.Single(
            exception.Diagnostics,
            item => item.Code == "PLUGIN_MODULE_MULTIPLE");
        Assert.Equal(2, diagnostic.Contributors.Count);
        Assert.Equal(0, FirstModule.ConfigureCount);
        Assert.Equal(0, SecondModule.ConfigureCount);
    }

    private sealed class CapturingDocumentStrategy(DocumentMetadata metadata)
        : IDocumentCreationStrategy
    {
        public DocumentCreationParams? LastParameters { get; private set; }

        public Document CreateDocument(DocumentCreationParams @params)
        {
            LastParameters = @params;
            return new Document();
        }

        public DocumentMetadata GetMetadata() => metadata;
    }

    private sealed class FirstModule : IPluginModule
    {
        public FirstModule() { }
        internal static int ConfigureCount { get; set; }
        public PluginId PluginId => new("myavalonia.plugin.first-test");
        public void ConfigureServices(IServiceCollection services) => ConfigureCount++;
    }

    private sealed class SecondModule : IPluginModule
    {
        public SecondModule() { }
        internal static int ConfigureCount { get; set; }
        public PluginId PluginId => new("myavalonia.plugin.second-test");
        public void ConfigureServices(IServiceCollection services) => ConfigureCount++;
    }
}
