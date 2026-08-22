using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using System.Text.Json;
using DaTangAccountingHelpPlug.Constants;
using DaTangAccountingHelpPlug.Plugin;
using DaTangAccountingHelpPlug.ViewModels;
using DaTangAccountingHelpPlug.ViewModels.BankBalanceReconciliation;
using DaTangAccountingHelpPlug.Views;
using DaTangAccountingHelpPlug.Views.BankBalanceReconciliation;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Docking;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;
using Xunit;

namespace MyAvaloniaManagement.UiTests;

/// <summary>在 Headless Avalonia 中验证 DaTang 两类 V2 Document 的真实 View 组合和绑定。</summary>
public sealed class DaTangAccountingHelpPlugV2UiTests
{
    [AvaloniaFact]
    public async Task Host窗口端口拒绝空参数取消调用和后台线程调用()
    {
        using var composition = DaTangUiComposition.Create();
        var interaction = composition.Provider.GetRequiredService<IPluginWindowInteraction>();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            interaction.PickOpenFilesAsync(null!));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            interaction.PickOpenFilesAsync(new FilePickerOpenOptions(), cancelled.Token));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            interaction.TrySetClipboardTextAsync(null!));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(() =>
            interaction.PickSaveFileAsync(new FilePickerSaveOptions())));

        // Headless 运行时没有经典桌面主窗口；端口必须把这种环境稳定映射为空结果，
        // 不能让插件为了判断可用性而取得或探测 Application/Window 实例。
        Assert.Empty(await interaction.PickOpenFilesAsync(new FilePickerOpenOptions()));
        Assert.Null(await interaction.PickSaveFileAsync(new FilePickerSaveOptions()));
        Assert.False(await interaction.TrySetClipboardTextAsync("copy"));
    }

    [AvaloniaFact]
    public async Task 两类Document通过HostAdapter创建View并绑定普通模型()
    {
        using var composition = DaTangUiComposition.Create();
        var factory = composition.Provider.GetRequiredService<IHostDockableFactory>();

        using var invoiceAdapter = Assert.IsType<ManagedDocumentDockable>(
            await factory.CreateDocumentAsync(
                DaTangContributionIds.InvoiceInfoImportDocument,
                new NewDocumentActivation("发票 UI")));
        var invoiceModel = Assert.IsType<InvoiceInfoImportViewModel>(invoiceAdapter.Model);
        var invoiceView = Assert.IsType<InvoiceInfoImportView>(invoiceAdapter.PreparedView);
        Assert.Same(invoiceModel, invoiceView.DataContext);
        Assert.Equal("发票 UI", invoiceAdapter.Title);
        Assert.False(invoiceAdapter.CanFloat);

        using var reconciliationAdapter = Assert.IsType<ManagedDocumentDockable>(
            await factory.CreateDocumentAsync(
                DaTangContributionIds.BankBalanceReconciliationDocument,
                new NewDocumentActivation("对账 UI")));
        var reconciliationModel = Assert.IsType<BankBalanceReconciliationViewModel>(
            reconciliationAdapter.Model);
        var reconciliationView = Assert.IsType<BankBalanceReconciliationView>(
            reconciliationAdapter.PreparedView);
        Assert.Same(reconciliationModel, reconciliationView.DataContext);
        Assert.Equal("对账 UI", reconciliationAdapter.Title);
        Assert.False(reconciliationAdapter.CanFloat);
    }

    [AvaloniaFact]
    public async Task 银行对账组合View把三个子模型绑定到精确子View()
    {
        using var composition = DaTangUiComposition.Create();
        var factory = composition.Provider.GetRequiredService<IHostDockableFactory>();
        using var adapter = Assert.IsType<ManagedDocumentDockable>(
            await factory.CreateDocumentAsync(
                DaTangContributionIds.BankBalanceReconciliationDocument,
                new NewDocumentActivation(string.Empty)));
        var model = Assert.IsType<BankBalanceReconciliationViewModel>(adapter.Model);
        var view = Assert.IsType<BankBalanceReconciliationView>(adapter.PreparedView);

        Assert.Same(
            model.Source,
            Assert.Single(view.GetLogicalDescendants().OfType<ReconciliationSourceView>()).DataContext);
        Assert.Same(
            model.Options,
            Assert.Single(view.GetLogicalDescendants().OfType<ReconciliationOptionsView>()).DataContext);
        Assert.Same(
            model.Run,
            Assert.Single(view.GetLogicalDescendants().OfType<ReconciliationRunView>()).DataContext);

        model.Source.EnterpriseLedgerPath = "binding-enterprise.xlsx";
        var pathBox = Assert.Single(
            view.GetLogicalDescendants().OfType<TextBox>(),
            textBox => string.Equals(textBox.Text, "binding-enterprise.xlsx", StringComparison.Ordinal));
        Assert.Equal(model.Source.EnterpriseLedgerPath, pathBox.Text);
    }

    [AvaloniaFact]
    public async Task 恢复失败不发布半成品且后续Document仍可创建()
    {
        using var composition = DaTangUiComposition.Create();
        var factory = composition.Provider.GetRequiredService<IHostDockableFactory>();
        using var json = JsonDocument.Parse("{}");
        var invalidContent = new DocumentContent(2, json.RootElement);

        await Assert.ThrowsAsync<InvalidDataException>(() => factory.CreateDocumentAsync(
            DaTangContributionIds.BankBalanceReconciliationDocument,
            new RestoreDocumentActivation("损坏恢复", invalidContent)).AsTask());

        // 失败的银行模型、View 和 Scope 由工厂局部释放；同一插件 Provider
        // 仍能创建完整发票 Adapter，证明失败没有发布或毒化后续激活。
        using var valid = Assert.IsType<ManagedDocumentDockable>(await factory.CreateDocumentAsync(
            DaTangContributionIds.InvoiceInfoImportDocument,
            new NewDocumentActivation("恢复后续")));
        Assert.Equal("恢复后续", valid.Title);
        Assert.IsType<InvoiceInfoImportView>(valid.PreparedView);
    }

    [AvaloniaFact]
    public async Task 非持久化发票Document显式拒绝Restore激活()
    {
        using var composition = DaTangUiComposition.Create();
        var factory = composition.Provider.GetRequiredService<IHostDockableFactory>();
        using var json = JsonDocument.Parse("{}");
        var content = new DocumentContent(1, json.RootElement);

        await Assert.ThrowsAsync<NotSupportedException>(() => factory.CreateDocumentAsync(
            DaTangContributionIds.InvoiceInfoImportDocument,
            new RestoreDocumentActivation("错误恢复", content)).AsTask());
    }

    private sealed class DaTangUiComposition : IDisposable
    {
        private readonly string _directory;
        private readonly HostDiagnosticSession _diagnostics;
        private readonly PluginProviderOwner _pluginProviders;
        private readonly DocumentScopeRegistry _documentScopes;
        private bool _disposed;

        private DaTangUiComposition(
            string directory,
            HostDiagnosticSession diagnostics,
            ServiceProvider provider,
            PluginProviderOwner pluginProviders,
            DocumentScopeRegistry documentScopes)
        {
            _directory = directory;
            _diagnostics = diagnostics;
            Provider = provider;
            _pluginProviders = pluginProviders;
            _documentScopes = documentScopes;
        }

        internal ServiceProvider Provider { get; }

        internal static DaTangUiComposition Create()
        {
            var directory = Path.Combine(Path.GetTempPath(), $"datang-g10-ui-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            var diagnostics = HostDiagnosticSession.Start(directory);
            var registryBuilder = new PluginRegistryBuilder();
            var pluginProviders = new PluginProviderOwner();
            var documentScopes = new DocumentScopeRegistry();
            var services = new ServiceCollection();
            services.AddApplicationServices(registryBuilder, pluginProviders, documentScopes);
            services.AddViewModels();
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
            _ = provider.GetRequiredService<PluginRegistry>();
            return new DaTangUiComposition(
                directory, diagnostics, provider, pluginProviders, documentScopes);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _documentScopes.CloseAll();
            _pluginProviders.Dispose();
            Provider.Dispose();
            _diagnostics.Dispose();
            Directory.Delete(_directory, recursive: true);
        }
    }
}
