using Dock.Model.Mvvm.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Plugin;
using MyAvaloniaManagementCommon.Save;
using MyAvaloniaManagementCommon.ToolCreation;
using Newtonsoft.Json;
using Avalonia.Controls;
using MyAvaloniaManagement.PluginSdk.UI;

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
    public void Document与Tool稳定Id在两种Json边界都保持字符串标量()
    {
        var documentId = new DocumentTypeId("myavalonia.host.document.sample");
        var newtonsoftJson = JsonConvert.SerializeObject(documentId);
        Assert.Equal("\"myavalonia.host.document.sample\"", newtonsoftJson);
        Assert.Equal(documentId, JsonConvert.DeserializeObject<DocumentTypeId>(newtonsoftJson));

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
    public void 历史别名只在旧格式边界规范化且声明式Registry只发布主Id()
    {
        var primary = new DocumentTypeId("myavalonia.host.document.sample");
        var legacyWelcome = new DocumentTypeId("DD7A1E38-07C5-B38C-FB02-1B991896EF49");
        Assert.Equal(
            HostExtensionIds.WelcomeDocument,
            LegacyContributionIdMap.ResolveDocument(legacyWelcome));

        var services = new ServiceCollection();
        var builder = new PluginRegistryBuilder();
        var registration = new PluginRegistration(
            HostExtensionIds.V2Owner, services, builder);
        registration.AddDocument<DeclarativeDocument, EmptyView>(
            new DocumentDescriptor(
                new MyAvaloniaManagement.PluginSdk.DocumentTypeId(primary.Value),
                "示例",
                "示例",
                "测试"));
        registration.Seal();
        using var provider = services.BuildServiceProvider();
        var registry = builder.Build(catalog: null);

        Assert.Equal([primary.Value],
            registry.DocumentDescriptors.Keys.Select(item => item.Value));
    }

    [Fact]
    public void 重复主Id和所有权错误会在插件候选内原子汇总()
    {
        var services = new ServiceCollection();
        var builder = new PluginRegistryBuilder();
        var registration = new PluginRegistration(
            HostExtensionIds.V2Owner, services, builder);
        var duplicate = new MyAvaloniaManagement.PluginSdk.DocumentTypeId(
            "myavalonia.host.document.duplicate");
        registration.AddDocument<DeclarativeDocument, EmptyView>(
            new DocumentDescriptor(duplicate, "第一项", "第一项", "测试"));
        registration.AddDocument<SecondDeclarativeDocument, EmptyView>(
            new DocumentDescriptor(duplicate, "第二项", "第二项", "测试"));
        // PluginRegistration 本身固定 owner；这里直接构造一个不同 owner 的内部声明，
        // 模拟候选快照被错误拼接，以验证提交前的所有者一致性防线。
        builder.AddDocument(
            new MyAvaloniaManagement.PluginSdk.PluginId("myavalonia.plugin.foreign"),
            new DocumentDescriptor(
                new MyAvaloniaManagement.PluginSdk.DocumentTypeId(
                    "myavalonia.plugin.foreign.document.item"),
                "外部",
                "外部",
                "测试"),
            typeof(ForeignDeclarativeDocument),
            typeof(EmptyView),
            static () => new EmptyView(),
            false);

        var exception = Assert.Throws<HostCompositionException>(registration.Seal);

        Assert.Contains(exception.Diagnostics, item => item.Code == "DOCUMENT_ID_DUPLICATE");
        Assert.Contains(exception.Diagnostics, item => item.Code == "EXTENSION_OWNER_MISMATCH");
        Assert.All(exception.Diagnostics, diagnostic =>
            Assert.All(diagnostic.Contributors, contributor =>
            {
                Assert.False(string.IsNullOrWhiteSpace(contributor.TypeName));
                Assert.False(string.IsNullOrWhiteSpace(contributor.AssemblyName));
            }));
    }

    private class DeclarativeDocument : MyAvaloniaManagement.PluginSdk.IPluginDocument
    {
        public MyAvaloniaManagement.PluginSdk.DocumentPresentationState Presentation { get; } = new("示例");
        public event EventHandler? PresentationChanged { add { } remove { } }
        public ValueTask InitializeAsync(
            MyAvaloniaManagement.PluginSdk.DocumentActivationContext context,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class SecondDeclarativeDocument : DeclarativeDocument;
    private sealed class ForeignDeclarativeDocument : DeclarativeDocument;
    private sealed class EmptyView : UserControl;

}
