using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Platform.Storage;
using DaTangAccountingHelpPlug.Constants;
using DaTangAccountingHelpPlug.Plugin;
using DaTangAccountingHelpPlug.ViewModels;
using DaTangAccountingHelpPlug.ViewModels.BankBalanceReconciliation;
using DaTangAccountingHelpPlug.Views;
using DaTangAccountingHelpPlug.Views.BankBalanceReconciliation;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>通过真实 Host V3 组合链验收 DaTang 的声明、Scope、窗口端口和内容协议。</summary>
public sealed class DaTangAccountingHelpPlugV3AcceptanceTests
{
    [Fact]
    public void 模块一次声明两个Document及精确View元数据()
    {
        using var composition = DaTangComposition.Create();
        var plugin = Assert.Single(composition.Registry.Plugins);

        Assert.Equal(DaTangContributionIds.Plugin.Value, plugin.Manifest.PluginId.Value);
        Assert.Equal(2, plugin.DocumentTypes.Count);
        Assert.Empty(plugin.ToolTypes);
        Assert.Empty(composition.Registry.Lifecycles);

        AssertDocument<InvoiceInfoImportViewModel, InvoiceInfoImportView>(
            composition.Registry,
            DaTangContributionIds.InvoiceInfoImportDocument,
            "综合计算发票信息",
            persistable: false);
        AssertDocument<BankBalanceReconciliationViewModel, BankBalanceReconciliationView>(
            composition.Registry,
            DaTangContributionIds.BankBalanceReconciliationDocument,
            "银行余额调节表",
            persistable: true);
    }

    [Fact]
    public void Host窗口端口以同一实例进入插件私有Provider()
    {
        var window = new DeferredWindowInteraction();
        using var composition = DaTangComposition.Create(window);

        Assert.Same(window, composition.HostProvider.GetRequiredService<IPluginWindowInteraction>());
        Assert.Same(
            window,
            composition.PluginProviders.GetRequiredService(
                DaTangContributionIds.Plugin,
                typeof(IPluginWindowInteraction)));
    }

    [Fact]
    public async Task 多Document隔离路径选项日志并按Scope独立释放()
    {
        using var composition = DaTangComposition.Create();
        var activator = composition.HostProvider.GetRequiredService<PluginContributionActivator>();
        using var firstActivation = activator.ActivateDocument(
            DaTangContributionIds.BankBalanceReconciliationDocument);
        using var secondActivation = activator.ActivateDocument(
            DaTangContributionIds.BankBalanceReconciliationDocument);
        var first = Assert.IsType<BankBalanceReconciliationViewModel>(firstActivation.Model);
        var second = Assert.IsType<BankBalanceReconciliationViewModel>(secondActivation.Model);
        await first.InitializeAsync(new NewDocumentActivation("对账 A"), default);
        await second.InitializeAsync(new NewDocumentActivation(string.Empty), default);

        first.Source.EnterpriseLedgerPath = "first.xlsx";
        first.Options.UseLegacyMode = true;
        first.Run.LogEntries.Add("first-log");
        second.Source.EnterpriseLedgerPath = "second.xlsx";

        Assert.NotSame(first.Source, second.Source);
        Assert.NotSame(first.Options, second.Options);
        Assert.NotSame(first.Run, second.Run);
        Assert.Equal("对账 A", first.Presentation.Title);
        Assert.Equal("银行余额调节表", second.Presentation.Title);
        Assert.Equal("first.xlsx", first.Source.EnterpriseLedgerPath);
        Assert.Equal("second.xlsx", second.Source.EnterpriseLedgerPath);
        Assert.True(first.Options.UseLegacyMode);
        Assert.False(second.Options.UseLegacyMode);
        Assert.Empty(second.Run.LogEntries);

        firstActivation.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => first.Run.RunAsync("unused.xlsx"));
        Assert.Equal("second.xlsx", second.Source.EnterpriseLedgerPath);
    }

    [Fact]
    public async Task 银行对账内容以原生Json往返且保存提交点明确()
    {
        using var composition = DaTangComposition.Create();
        var activator = composition.HostProvider.GetRequiredService<PluginContributionActivator>();
        using var sourceActivation = activator.ActivateDocument(
            DaTangContributionIds.BankBalanceReconciliationDocument);
        var source = Assert.IsType<BankBalanceReconciliationViewModel>(sourceActivation.Model);
        await source.InitializeAsync(new NewDocumentActivation("来源标题"), default);
        var dirtyChanges = 0;
        source.IsDirtyChanged += (_, _) => dirtyChanges++;
        source.Source.EnterpriseLedgerPath = "enterprise.xlsx";
        source.Source.BankStatementPath = "bank.xlsx";
        source.Options.PreviousUnreconciledDifference = 12.34m;
        source.Run.LastOutputPath = "result.xlsx";

        Assert.True(source.IsDirty);
        var snapshot = await source.CaptureSaveSnapshotAsync(default);
        var content = snapshot.Content;
        Assert.Equal(1, content.SchemaVersion);
        Assert.Equal(JsonValueKind.Object, content.Payload.ValueKind);
        Assert.Equal(
            [
                "configuration", "selectedProfileId", "enterpriseLedgerPath", "bankStatementPath",
                "receiptEnrichmentPath", "asOfDate", "useLegacyMode", "enableLooseAmountAlignment",
                "previousUnreconciledDifference", "lastOutputPath",
            ],
            content.Payload.EnumerateObject().Select(property => property.Name));
        Assert.True(source.IsDirty);

        using var targetActivation = activator.ActivateDocument(
            DaTangContributionIds.BankBalanceReconciliationDocument);
        var target = Assert.IsType<BankBalanceReconciliationViewModel>(targetActivation.Model);
        await target.InitializeAsync(
            new RestoreDocumentActivation("恢复标题", content),
            default);

        Assert.Equal("恢复标题", target.Presentation.Title);
        Assert.Equal("enterprise.xlsx", target.Source.EnterpriseLedgerPath);
        Assert.Equal("bank.xlsx", target.Source.BankStatementPath);
        Assert.Equal(12.34m, target.Options.PreviousUnreconciledDifference);
        Assert.Equal("result.xlsx", target.Run.LastOutputPath);
        Assert.False(target.IsDirty);
        source.Source.EnterpriseLedgerPath = "captured-later.xlsx";
        source.AcceptChanges(snapshot.Revision);
        Assert.True(source.IsDirty);
        var current = await source.CaptureSaveSnapshotAsync(default);
        source.AcceptChanges(current.Revision);
        Assert.False(source.IsDirty);
        source.AcceptChanges(current.Revision);
        Assert.Equal(2, dirtyChanges);
    }

    [Theory]
    [InlineData(2, "{}")]
    [InlineData(1, "[]")]
    [InlineData(1, "{}")]
    [InlineData(1, "{\"configuration\":{},\"selectedProfileId\":\"\",\"enterpriseLedgerPath\":\"\",\"bankStatementPath\":\"\",\"receiptEnrichmentPath\":\"\",\"asOfDate\":null,\"useLegacyMode\":false,\"enableLooseAmountAlignment\":false,\"previousUnreconciledDifference\":0,\"lastOutputPath\":\"\",\"unknown\":true}")]
    [InlineData(1, "{\"configuration\":{},\"configuration\":{},\"selectedProfileId\":\"\",\"enterpriseLedgerPath\":\"\",\"bankStatementPath\":\"\",\"receiptEnrichmentPath\":\"\",\"asOfDate\":null,\"useLegacyMode\":false,\"enableLooseAmountAlignment\":false,\"previousUnreconciledDifference\":0,\"lastOutputPath\":\"\"}")]
    public async Task 损坏内容严格失败且不提交标题或现有状态(int schemaVersion, string json)
    {
        using var composition = DaTangComposition.Create();
        var activator = composition.HostProvider.GetRequiredService<PluginContributionActivator>();
        using var activation = activator.ActivateDocument(
            DaTangContributionIds.BankBalanceReconciliationDocument);
        var model = Assert.IsType<BankBalanceReconciliationViewModel>(activation.Model);
        await model.InitializeAsync(new NewDocumentActivation("原始标题"), default);
        model.Source.EnterpriseLedgerPath = "before.xlsx";

        using var document = JsonDocument.Parse(json);
        var content = new DocumentContent(schemaVersion, document.RootElement);
        var exception = Assert.Throws<InvalidDataException>(() =>
        {
            _ = model.InitializeAsync(
                new RestoreDocumentActivation("不应提交", content),
                default);
        });

        Assert.DoesNotContain(json, exception.Message, StringComparison.Ordinal);
        Assert.Equal("原始标题", model.Presentation.Title);
        Assert.Equal("before.xlsx", model.Source.EnterpriseLedgerPath);
        Assert.True(model.IsDirty);
    }

    [Theory]
    [InlineData("wrongType")]
    [InlineData("invalidConfiguration")]
    public async Task 错误字段类型与无效配置均原子拒绝(string mutation)
    {
        using var composition = DaTangComposition.Create();
        var activator = composition.HostProvider.GetRequiredService<PluginContributionActivator>();
        using var sourceActivation = activator.ActivateDocument(
            DaTangContributionIds.BankBalanceReconciliationDocument);
        var source = Assert.IsType<BankBalanceReconciliationViewModel>(sourceActivation.Model);
        await source.InitializeAsync(new NewDocumentActivation("source"), default);
        var captured = await source.CaptureSaveSnapshotAsync(default);
        var payload = JsonNode.Parse(captured.Content.Payload.GetRawText())!.AsObject();
        if (mutation == "wrongType")
        {
            payload["previousUnreconciledDifference"] = "bad-number";
        }
        else
        {
            payload["configuration"]!.AsObject()["schemaVersion"] = 2;
        }

        using var document = JsonDocument.Parse(payload.ToJsonString());
        var corrupted = new DocumentContent(1, document.RootElement);
        using var targetActivation = activator.ActivateDocument(
            DaTangContributionIds.BankBalanceReconciliationDocument);
        var target = Assert.IsType<BankBalanceReconciliationViewModel>(targetActivation.Model);
        await target.InitializeAsync(new NewDocumentActivation("原标题"), default);
        target.Source.EnterpriseLedgerPath = "before.xlsx";

        Assert.Throws<InvalidDataException>(() =>
        {
            _ = target.InitializeAsync(
                new RestoreDocumentActivation("不应提交", corrupted),
                default);
        });
        Assert.Equal("原标题", target.Presentation.Title);
        Assert.Equal("before.xlsx", target.Source.EnterpriseLedgerPath);
        Assert.True(target.IsDirty);
    }

    [Fact]
    public async Task Document关闭期间文件选择迟到结果不得写回模型()
    {
        var window = new DeferredWindowInteraction();
        using var composition = DaTangComposition.Create(window);
        var activator = composition.HostProvider.GetRequiredService<PluginContributionActivator>();
        var activation = activator.ActivateDocument(DaTangContributionIds.InvoiceInfoImportDocument);
        var model = Assert.IsType<InvoiceInfoImportViewModel>(activation.Model);
        await model.InitializeAsync(new NewDocumentActivation("关闭竞争"), default);

        var selection = model.SelectFolder("InvoiceSummaryFile");
        await window.Started;
        activation.Dispose();
        window.CompleteOpen("late.xlsx");
        await selection;

        Assert.Equal(string.Empty, model.InvoiceSummaryFilePath);
    }

    [Fact]
    public void 生产程序集不再引用LegacyDockNewtonsoft或Host实现()
    {
        var references = typeof(DaTangAccountingHelpPluginModule).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("MyAvaloniaManagement.PluginSdk", references);
        Assert.Contains("MyAvaloniaManagement.PluginSdk.UI", references);
        Assert.DoesNotContain("MyAvaloniaManagementCommon", references);
        Assert.DoesNotContain("Dock.Model", references);
        Assert.DoesNotContain("Dock.Model.Mvvm", references);
        Assert.DoesNotContain("Newtonsoft.Json", references);
        Assert.DoesNotContain("MyAvaloniaManagement", references);
    }

    private static void AssertDocument<TDocument, TView>(
        PluginRegistry registry,
        DocumentTypeId id,
        string displayName,
        bool persistable)
    {
        Assert.True(registry.TryGetDocumentRegistration(id, out var registration));
        Assert.Equal(typeof(TDocument), registration.ModelType);
        Assert.Equal(typeof(TView), registration.ViewType);
        Assert.Equal(displayName, registration.Descriptor.DisplayName);
        Assert.Equal("大唐-会计", registration.Descriptor.MenuCategory);
        Assert.Equal(persistable, registration.IsPersistable);
    }

    private sealed class DeferredWindowInteraction : IPluginWindowInteraction
    {
        private readonly TaskCompletionSource _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<IReadOnlyList<string>> _openResult =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task Started => _started.Task;

        public Task<IReadOnlyList<string>> PickOpenFilesAsync(
            FilePickerOpenOptions options,
            CancellationToken cancellationToken = default)
        {
            _started.TrySetResult();
            return _openResult.Task;
        }

        public Task<string?> PickSaveFileAsync(
            FilePickerSaveOptions options,
            CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

        public Task<bool> TrySetClipboardTextAsync(
            string text,
            CancellationToken cancellationToken = default) => Task.FromResult(false);

        internal void CompleteOpen(string path) => _openResult.TrySetResult([path]);
    }

    private sealed class DaTangComposition : IDisposable
    {
        private readonly string _directory;
        private readonly HostDiagnosticSession _diagnostics;
        private readonly PluginProviderOwner _pluginProviders;
        private readonly DocumentScopeRegistry _documentScopes;
        private bool _disposed;

        private DaTangComposition(
            string directory,
            HostDiagnosticSession diagnostics,
            ServiceProvider hostProvider,
            PluginProviderOwner pluginProviders,
            DocumentScopeRegistry documentScopes,
            PluginRegistry registry)
        {
            _directory = directory;
            _diagnostics = diagnostics;
            HostProvider = hostProvider;
            _pluginProviders = pluginProviders;
            _documentScopes = documentScopes;
            Registry = registry;
        }

        internal ServiceProvider HostProvider { get; }
        internal PluginRegistry Registry { get; }
        internal PluginProviderOwner PluginProviders => _pluginProviders;

        internal static DaTangComposition Create(IPluginWindowInteraction? windowInteraction = null)
        {
            var directory = Path.Combine(Path.GetTempPath(), $"datang-g10-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var diagnostics = HostDiagnosticSession.Start(directory);
            var registryBuilder = new PluginRegistryBuilder();
            var pluginProviders = new PluginProviderOwner();
            var documentScopes = new DocumentScopeRegistry();
            var services = new ServiceCollection();
            services.AddApplicationServices(registryBuilder, pluginProviders, documentScopes);
            services.AddViewModels();
            if (windowInteraction is not null)
            {
                services.AddSingleton(windowInteraction);
                services.AddSingleton<IPluginWindowInteraction>(windowInteraction);
            }
            services.AddSingleton(diagnostics);
            services.AddSingleton<IHostDiagnosticSink>(diagnostics);
            services.AddSingleton(PluginModuleCatalog.CreateForTests(
            [
                (DaTangContributionIds.Plugin, (IPluginModule)new DaTangAccountingHelpPluginModule()),
            ]));
            var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true,
            });
            pluginProviders.Compose(
                provider.GetRequiredService<PluginModuleCatalog>(),
                provider,
                registryBuilder,
                documentScopes,
                diagnostics);
            var registry = provider.GetRequiredService<PluginRegistry>();
            return new DaTangComposition(
                directory, diagnostics, provider, pluginProviders, documentScopes, registry);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _documentScopes.CloseAll();
            _pluginProviders.Dispose();
            HostProvider.Dispose();
            _diagnostics.Dispose();
            Directory.Delete(_directory, recursive: true);
        }
    }
}
