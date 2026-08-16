using DaTangAccountingHelpPlug.Create.BankBalanceReconciliation;
using DaTangAccountingHelpPlug.Constants;
using DaTangAccountingHelpPlug.Plugin;
using DaTangAccountingHelpPlug.ViewModels.BankBalanceReconciliation;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagementCommon.Plugin;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagementCommon.DocumentCreation;
using Xunit;

namespace DaTangAccountingHelpPlug.Tests.BankBalanceReconciliation;

public sealed class ReconciliationDocumentScopeTests
{
    [Fact]
    public async Task 两个Document的路径选项日志和取消状态完全隔离()
    {
        var services = new ServiceCollection();
        services.AddSingleton<DocumentScopeManager>();
        services.AddSingleton<IDocumentScopeFactory>(provider =>
            provider.GetRequiredService<DocumentScopeManager>());
        new DaTangAccountingHelpPluginModule().Configure(new TestPluginRegistrationContext(
            new PluginId("myavalonia.plugin.datang-accounting-help"), services));

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        });
        var manager = provider.GetRequiredService<DocumentScopeManager>();
        var strategy = new BankBalanceReconciliationDocumentStrategy(manager);
        var first = Assert.IsType<BankBalanceReconciliationViewModel>(
            strategy.CreateDocument(new DocumentCreationParams(
                SaveDocumentTypeIdConstant.BankBalanceReconciliationDocument)));
        var second = Assert.IsType<BankBalanceReconciliationViewModel>(
            strategy.CreateDocument(new DocumentCreationParams(
                SaveDocumentTypeIdConstant.BankBalanceReconciliationDocument)));

        first.Source.EnterpriseLedgerPath = "first.xlsx";
        first.Options.UseLegacyMode = true;
        first.Run.LogEntries.Add("first-log");
        second.Source.EnterpriseLedgerPath = "second.xlsx";
        second.Options.UseLegacyMode = false;

        Assert.NotSame(first.Source, second.Source);
        Assert.NotSame(first.Options, second.Options);
        Assert.NotSame(first.Run, second.Run);
        Assert.Equal("first.xlsx", first.Source.EnterpriseLedgerPath);
        Assert.Equal("second.xlsx", second.Source.EnterpriseLedgerPath);
        Assert.True(first.Options.UseLegacyMode);
        Assert.False(second.Options.UseLegacyMode);
        Assert.Empty(second.Run.LogEntries);

        Assert.True(manager.Release(first));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => first.Run.RunAsync("unused.xlsx"));
        Assert.Equal("second.xlsx", second.Source.EnterpriseLedgerPath);
        Assert.True(manager.Release(second));
    }

    [Fact]
    public void 保存Document仅持久化配置路径选项和结果摘要()
    {
        var services = new ServiceCollection();
        services.AddSingleton<DocumentScopeManager>();
        services.AddSingleton<IDocumentScopeFactory>(provider =>
            provider.GetRequiredService<DocumentScopeManager>());
        new DaTangAccountingHelpPluginModule().Configure(new TestPluginRegistrationContext(
            new PluginId("myavalonia.plugin.datang-accounting-help"), services));
        using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<DocumentScopeManager>();
        var document = manager.CreateDocument<BankBalanceReconciliationViewModel>();
        document.Source.EnterpriseLedgerPath = "enterprise.xlsx";
        document.Source.BankStatementPath = "bank.xlsx";
        document.Options.PreviousUnreconciledDifference = 12.34m;
        document.Run.LastOutputPath = "result.xlsx";
        Assert.True(document.IsDirty);

        var saveData = document.CreateSaveDocumentMetaData("document.json");

        Assert.True(document.IsDirty);
        Assert.Contains("enterprise.xlsx", saveData.Content);
        Assert.Contains("result.xlsx", saveData.Content);
        Assert.DoesNotContain("EnterpriseEntries", saveData.Content);
        Assert.DoesNotContain("BankEntries", saveData.Content);
        document.AcceptChanges();
        Assert.False(document.IsDirty);
        Assert.True(manager.Release(document));
    }
}
