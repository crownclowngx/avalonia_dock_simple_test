using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.Business.Presentation.Icons;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.Tests;

/// <summary>验证图标的候选原子性、所有者约束及只读查询，不用替身实现另一套注册算法。</summary>
public sealed class IconRegistrationTests
{
    private static readonly PluginId First = new("myavalonia.plugin.icon-first");
    private static readonly PluginId Second = new("myavalonia.plugin.icon-second");
    private static VectorIconDefinition Definition() => new("M0,0 H24 V24 H0 Z", 24, 24);

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData(" ")] [InlineData("Sample")]
    [InlineData("a--b")] [InlineData("-a")] [InlineData("a-")] [InlineData("a/b")]
    [InlineData("builtin:module")] [InlineData("plugin:other/test")] [InlineData("a\n")]
    public void 本地名称不能覆盖或伪造命名空间(string? name) =>
        Assert.Throws<ArgumentException>(() => new PluginRegistryBuilder().AddIcon(First, name!, Definition()));

    [Fact]
    public void 同名不同所有者可共存且Seal与发布之后均不可再写()
    {
        var first = new PluginRegistryBuilder();
        var window = new PluginRegistration(First, new ServiceCollection(), first);
        var value = Definition();
        var key = ((IPluginRegistration)window).AddIcon("text-2", value);
        Assert.Equal($"plugin:{First}/text-2", key);
        window.Seal();
        Assert.Throws<InvalidOperationException>(() => window.AddIcon("later", value));
        var second = new PluginRegistryBuilder();
        second.AddIcon(Second, "text-2", Definition());
        var all = new PluginRegistryBuilder();
        all.Import(first);
        all.Import(second);
        var registry = all.Build(null);
        Assert.Equal(2, registry.Icons.Count);
        Assert.Contains(First, registry.DeclaredOwnerIds);
        Assert.Contains(Second, registry.DeclaredOwnerIds);
        Assert.Same(value, registry.Icons[0].Definition);
        Assert.Throws<InvalidOperationException>(() => all.AddIcon(First, "later", value));
        Assert.Throws<InvalidOperationException>(() => all.Build(null));
        Assert.Throws<NotSupportedException>(() => ((IList<PluginIconRegistration>)registry.Icons).Clear());
    }

    [Fact]
    public void 即使数据相同重复也拒绝整个候选且导入失败无残留()
    {
        var local = new PluginRegistryBuilder();
        var window = new PluginRegistration(First, new ServiceCollection(), local);
        var value = Definition();
        window.AddIcon("same", value);
        window.AddIcon("same", value);
        Assert.Contains(Assert.Throws<HostCompositionException>(window.Seal).Diagnostics,
            item => item.Code == "ICON_REFERENCE_DUPLICATE");
        var all = new PluginRegistryBuilder();
        Assert.Throws<HostCompositionException>(() => all.Import(local));
        Assert.Empty(all.Build(null).Icons);
    }

    [Fact]
    public void 图标唯一贡献同样参与所有者校验及全局排除()
    {
        var first = new PluginRegistryBuilder();
        first.AddIcon(First, "only", Definition());
        Assert.Throws<HostCompositionException>(() => first.ValidateSingleOwner(Second));
        var mixed = new PluginRegistryBuilder();
        mixed.AddIcon(First, "only", Definition());
        mixed.AddIcon(Second, "only", Definition());
        Assert.Throws<HostCompositionException>(() => mixed.ValidateSingleOwner());
        var all = new PluginRegistryBuilder();
        all.Import(first);
        all.Import(first);
        var sink = new Sink();
        Assert.Empty(all.Build(null, sink).Icons);
        Assert.Equal("ICON_REFERENCE_DUPLICATE", Assert.Single(sink.Records).Code);
    }

    [Fact]
    public void 查询使用可信所有者并在缓存之前撤回可用性()
    {
        var all = new PluginRegistryBuilder();
        var key = all.AddIcon(First, "sample", Definition());
        all.AddIcon(Second, "sample", Definition());
        var registry = all.Build(null);
        var state = new PluginLifecycleStateStore(registry);
        var sink = new Sink();
        var catalog = new HostIconCatalog(registry, new(state), sink);
        Assert.Equal(key, catalog.Resolve(new(First, key)).Reference);
        Assert.Same(catalog.Default, catalog.Resolve(new(Second, key)));
        Assert.Same(catalog.Default, catalog.Resolve(new(Second, key)));
        Assert.Single(sink.Records);
        Assert.Same(catalog.Default, catalog.Resolve(new(null, key)));
        Assert.Equal("builtin:table", catalog.Resolve(new(First, "builtin:table")).Reference);
        state.BeginShutdown();
        Assert.Same(catalog.Default, catalog.Resolve(new(First, key)));
        Assert.Same(catalog.Default, catalog.Resolve(new(First, "builtin:table")));
        Assert.Equal("builtin:table", catalog.Resolve(new(null, "builtin:table")).Reference);
    }

    [Fact]
    public void 不同Runtime与调用方原始列表互不影响且坏引用不读取外部资源()
    {
        var contributions = new List<PluginIconRegistration> { new(First, $"plugin:{First}/sample", Definition()) };
        var registry = new PluginRegistry([], [], [], [], icons: contributions);
        contributions.Clear();
        Assert.Single(registry.Icons);
        var sink = new Sink();
        var first = new HostIconCatalog(registry, new(new(registry)), sink);
        var empty = new PluginRegistry([], []);
        var second = new HostIconCatalog(empty, new(new(empty)));
        Assert.NotSame(first.Default.Definition, second.Default.Definition);
        foreach (var reference in new[] { "https://example.com/image.png", "C:/private/image.png", "avares://Other/Icon", "builtin:unknown" })
            Assert.Same(first.Default, first.Resolve(new(null, reference)));
        Assert.All(sink.Records.Take(3), record => Assert.Null(record.StableId));
        Assert.Equal((HostDiagnosticSeverity.Warning, HostDiagnosticDisposition.Continue),
            HostDiagnosticFailurePolicy.Classify("ICON_GEOMETRY_INVALID", HostDiagnosticPhase.IconPresentation));
    }

    private sealed class Sink : IHostDiagnosticSink
    {
        internal List<HostDiagnosticDraft> Records { get; } = [];
        public HostDiagnosticRecord Report(HostDiagnosticDraft diagnostic)
        {
            Records.Add(diagnostic);
            return new HostDiagnosticRecord
            {
                SessionId = Guid.Empty, Sequence = Records.Count, TimestampUtc = DateTimeOffset.UnixEpoch,
                Code = diagnostic.Code, Phase = diagnostic.Phase, Severity = HostDiagnosticSeverity.Warning,
                Disposition = HostDiagnosticDisposition.Continue, UserMessage = "测试诊断",
            };
        }
    }
}
