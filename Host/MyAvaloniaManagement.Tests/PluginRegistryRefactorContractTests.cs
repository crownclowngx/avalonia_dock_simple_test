using Avalonia.Controls;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.Tests;

/// <summary>在原 Builder 入口锁定 V12 的顺序与失败原子性，不依赖提取后的内部算法。</summary>
public sealed class PluginRegistryRefactorContractTests
{
    private static readonly PluginId First = new("myavalonia.plugin.v12-first");
    private static readonly PluginId Second = new("myavalonia.plugin.v12-second");
    private static readonly DocumentTypeId SharedDocument = new("shared.document.collision");
    private static readonly CommandId SharedCommand = new("shared.command.collision");

    [Fact]
    public void 全局诊断保持贡献种类和所有者发现顺序且不激活View()
    {
        var builder = ConflictingBuilder();
        var sink = new RecordingSink();
        var registry = builder.Build(null, sink);

        Assert.Equal(
            ["ICON_REFERENCE_DUPLICATE", "DOCUMENT_ID_DUPLICATE", "DOCUMENT_ID_DUPLICATE",
                HostDiagnosticCodes.WorkbenchCommandIdDuplicate, HostDiagnosticCodes.WorkbenchCommandIdDuplicate],
            sink.Drafts.Select(item => item.Code));
        Assert.Equal([First, First, Second, First, Second], sink.Drafts.Select(item => item.PluginId));
        Assert.Equal([$"plugin:{First.Value}/same", SharedDocument.Value, SharedDocument.Value,
            SharedCommand.Value, SharedCommand.Value], sink.Drafts.Select(item => item.StableId));
        Assert.All(sink.Drafts, item => Assert.Equal(HostDiagnosticPhase.ExtensionDiscovery, item.Phase));
        Assert.Null(sink.Drafts[0].AssemblyName);
        Assert.All(sink.Drafts.Skip(1), item => Assert.Equal(typeof(FirstModel).Assembly.GetName().Name, item.AssemblyName?.Name));
        Assert.Empty(registry.Documents);
        Assert.Empty(registry.WorkbenchCommands);
        Assert.Empty(registry.Icons);
    }

    [Fact]
    public void 诊断抛错立即停止且尚未提交Provider所有权()
    {
        var builder = ConflictingBuilder();
        using var providers = new PluginProviderOwner();
        var failure = new InvalidOperationException("受控诊断故障");
        var sink = new RecordingSink { Failure = failure };

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => builder.Build(null, sink, providers)));
        Assert.Single(sink.Drafts);
        // 空所有者也只能提交一次。此调用成功说明失败的 Build 没有提前越过提交边界。
        providers.CommitRegistryResult(new HashSet<PluginId>());
        Assert.Throws<InvalidOperationException>(() => builder.Build(null));
        Assert.Throws<InvalidOperationException>(() => builder.AddIcon(First, "late", Icon()));
    }

    [Fact]
    public void Registry索引构造失败仍未提交Provider且Builder保持封闭()
    {
        var builder = new PluginRegistryBuilder();
        AddDocument(builder, First, typeof(FirstModel));
        AddDocument(builder, First, typeof(SecondModel));
        using var providers = new PluginProviderOwner();

        // 同 Owner 重复本应在局部 Seal 拒绝；绕过局部入口模拟损坏内部事实。
        Assert.Throws<ArgumentException>(() => builder.Build(null, pluginProviders: providers));
        providers.CommitRegistryResult(new HashSet<PluginId>());
        Assert.Throws<InvalidOperationException>(() => builder.ValidateSingleOwner());
    }

    [Fact]
    public void 局部组合异常保持排序并且失败导入不留下部分贡献()
    {
        var local = new PluginRegistryBuilder();
        AddDocument(local, First, typeof(FirstModel));
        AddDocument(local, First, typeof(FirstModel));
        local.AddIcon(First, "same", Icon());
        local.AddIcon(First, "same", Icon());
        var exception = Assert.Throws<HostCompositionException>(() => local.ValidateSingleOwner());
        Assert.Equal(
            ["DOCUMENT_CONTRIBUTION_TYPE_DUPLICATE", "DOCUMENT_ID_DUPLICATE",
                "ICON_REFERENCE_DUPLICATE", "VIEW_MODEL_REGISTRATION_DUPLICATE"],
            exception.Diagnostics.Select(item => item.Code));
        var global = new PluginRegistryBuilder();
        Assert.Throws<HostCompositionException>(() => global.Import(local));
        Assert.Empty(global.Build(null).DeclaredOwnerIds);
    }

    [Fact]
    public void 多所有者且多生命周期保留原失败边界()
    {
        var builder = new PluginRegistryBuilder();
        builder.AddLifecycle(First, typeof(FirstModel));
        builder.AddLifecycle(Second, typeof(SecondModel));
        // 这是已有内部畸形输入的 SingleOrDefault 异常；等价重构不顺手改为另一种诊断。
        Assert.Throws<InvalidOperationException>(() => builder.ValidateSingleOwner());
    }

    private static PluginRegistryBuilder ConflictingBuilder()
    {
        var builder = new PluginRegistryBuilder();
        AddDocument(builder, First, typeof(FirstModel));
        AddDocument(builder, Second, typeof(SecondModel));
        builder.AddDocumentCommand(First, new(SharedCommand, "第一", "测试"), SharedDocument);
        builder.AddDocumentCommand(Second, new(SharedCommand, "第二", "测试"), SharedDocument);
        builder.AddIcon(First, "same", Icon());
        builder.AddIcon(First, "same", Icon());
        return builder;
    }

    private static void AddDocument(PluginRegistryBuilder builder, PluginId owner, Type model) =>
        builder.AddDocument(owner, new(SharedDocument, "测试", "测试", "测试"), model,
            typeof(UserControl), () => throw new InvalidOperationException("校验不能构造 View"), false);

    private static VectorIconDefinition Icon() => new("M0,0 H24 V24 H0 Z", 24, 24);
    private sealed class FirstModel;
    private sealed class SecondModel;

    private sealed class RecordingSink : IHostDiagnosticSink
    {
        internal List<HostDiagnosticDraft> Drafts { get; } = [];
        internal Exception? Failure { get; init; }
        public HostDiagnosticRecord Report(HostDiagnosticDraft draft)
        {
            Drafts.Add(draft);
            if (Failure is not null) throw Failure;
            return HostDiagnosticRedactionPolicy.Create(Guid.Empty, draft, DateTimeOffset.UnixEpoch);
        }
    }
}
