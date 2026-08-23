using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.Business.Constants;
using Avalonia.Controls;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.Tests;

/// <summary>
/// 验证 V2 强类型身份的词法边界，以及注册表的原子失败语义。
/// </summary>
/// <remarks>
/// G13 后值对象本身只接受规范主 ID，不再承担“先接受历史词法、再由 Registry 拒绝”的双层职责。
/// 所有权与重复检查仍由组合根统一完成，避免把跨贡献规则塞入单个值对象。
/// </remarks>
public sealed class IdentityAndRegistryTests
{
    [Fact]
    public void Host内建身份只有强类型事实源且Welcome动作不接受裸字符串()
    {
        var hostAssembly = typeof(HostExtensionIds).Assembly;
        Assert.Null(hostAssembly.GetType(
            "MyAvaloniaManagement.Business.Constants.DockNameConstant"));

        Assert.Equal(
            "myavalonia.host.tool.plugin-menu",
            HostExtensionIds.PluginMenu.Value);
        Assert.Equal(
            "myavalonia.host.tool.management",
            HostExtensionIds.ToolManagement.Value);

        var injectedConstructor = typeof(
                MyAvaloniaManagement.ViewModels.Hello.WelcomeViewModel)
            .GetConstructors()
            .Single(constructor => constructor.GetParameters().Length == 1);
        Assert.Equal(
            typeof(Action<ToolTypeId>),
            Assert.Single(injectedConstructor.GetParameters()).ParameterType);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" leading")]
    [InlineData("trailing ")]
    [InlineData("contains/slash")]
    [InlineData("包含中文")]
    [InlineData("MyAvalonia.host.document.sample")]
    [InlineData("myavalonia.host.document.sample_name")]
    [InlineData("myavalonia..host.document.sample")]
    [InlineData("myavalonia.host.document.-sample")]
    public void 值对象拒绝空白首尾空格和非法字符(string value)
    {
        Assert.Throws<ArgumentException>(() => new PluginId(value));
        Assert.Throws<ArgumentException>(() => new DocumentTypeId(value));
        Assert.Throws<ArgumentException>(() => new ToolTypeId(value));
        Assert.Throws<ArgumentException>(() => new CreationIntentId(value));
    }

    [Fact]
    public void Document历史别名不再存在且声明式Registry只发布主Id()
    {
        var primary = new DocumentTypeId(
            "myavalonia.plugin.host-tests.document.sample");
        Assert.Null(typeof(PluginRegistry).Assembly.GetType(
            "MyAvaloniaManagement.Business.Helpers.LegacyContributionIdMap"));

        var services = new ServiceCollection();
        var builder = new PluginRegistryBuilder();
        var registration = new PluginRegistration(
            TestPluginIds.Owner, services, builder);
        registration.AddDocument<DeclarativeDocument, EmptyView>(
            new DocumentDescriptor(
                primary,
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
            TestPluginIds.Owner, services, builder);
        var duplicate = new DocumentTypeId(
            "myavalonia.plugin.host-tests.document.duplicate");
        registration.AddDocument<DeclarativeDocument, EmptyView>(
            new DocumentDescriptor(duplicate, "第一项", "第一项", "测试"));
        registration.AddDocument<SecondDeclarativeDocument, EmptyView>(
            new DocumentDescriptor(duplicate, "第二项", "第二项", "测试"));
        // PluginRegistration 本身固定 owner；这里直接构造一个不同 owner 的内部声明，
        // 模拟候选快照被错误拼接，以验证提交前的所有者一致性防线。
        builder.AddDocument(
            new PluginId("myavalonia.plugin.foreign"),
            new DocumentDescriptor(
                new DocumentTypeId(
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
            MyAvaloniaManagement.PluginSdk.DocumentActivation context,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class SecondDeclarativeDocument : DeclarativeDocument;
    private sealed class ForeignDeclarativeDocument : DeclarativeDocument;
    private sealed class EmptyView : UserControl;

}
