using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Model;
using Dock.Model.Core;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.Views;
using Xunit;

namespace MyAvaloniaManagement.UiTests;

/// <summary>
/// 对已核查初始化无外部业务副作用的插件，进一步运行生产 Workspace 与 Dock 链路。
/// 输入显式列出插件 ID，不自动启动安装目录中所有插件的业务模型或生命周期。
/// </summary>
public sealed class ExternalPluginWorkspaceAcceptanceTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> PluginIds()
    {
        var ids = Environment.GetEnvironmentVariable("MYAVALONIA_WORKSPACE_PLUGIN_IDS");
        if (string.IsNullOrWhiteSpace(ids))
            throw new InvalidOperationException("Workspace 验收必须显式提供 MYAVALONIA_WORKSPACE_PLUGIN_IDS，以逗号分隔。");
        return ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(id => new object[] { id });
    }

    [AvaloniaTheory]
    [MemberData(nameof(PluginIds))]
    public async Task 真实产物的文档创建分割关闭和工具隐藏恢复保持同一视图(string pluginId)
    {
        var root = Environment.GetEnvironmentVariable("MYAVALONIA_EXTERNAL_CONTROLS");
        Assert.False(string.IsNullOrWhiteSpace(root));
        var all = AssemblyLoaderHelper.Discover(root!);
        Assert.Empty(all.Diagnostics);
        var assembly = Assert.Single(all.Assemblies, item => all.GetManifest(item).PluginId.Value == pluginId);
        var snapshot = new PluginDiscoverySnapshot([assembly],
            new Dictionary<Assembly, PluginManifest> { [assembly] = all.GetManifest(assembly) },
            new Dictionary<Assembly, Type> { [assembly] = all.GetModuleType(assembly) }, []);
        using var context = new UiTestContext(modules: PluginModuleCatalog.Discover(snapshot));
        var registry = context.Provider.GetRequiredService<PluginRegistry>();
        Assert.Single(registry.Plugins);
        var window = new MainWindow { Width = 1400, Height = 900, DataContext = context.ViewModel };
        window.Resources[DocumentControlRecycling.ResourceKey] = context.Provider.GetRequiredService<DocumentControlRecycling>();
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.NotEmpty(registry.Documents);
            foreach (var registration in registry.Documents)
            {
                var document = await context.Workspace.CreateAndPublishDocumentAsync(
                    registration.Descriptor.DocumentTypeId, new NewDocumentActivation("V9 兼容性验收"));
                Dispatcher.UIThread.RunJobs();
                var view = Assert.IsAssignableFrom<Control>(document.PreparedView);
                Assert.Same(document.Model, view.DataContext);
                Assert.Contains(window, view.GetVisualAncestors());
                var owner = Assert.IsAssignableFrom<IDock>(document.Owner);
                Assert.True(new DockService().SplitDockable(document, owner, owner, DockOperation.Right, bExecute: true));
                Dispatcher.UIThread.RunJobs();
                Assert.Same(view, document.PreparedView);
                Assert.Contains(window, view.GetVisualAncestors());

                context.Workspace.DockFactory.CloseDockable(document);
                Dispatcher.UIThread.RunJobs();
                Assert.DoesNotContain(document, context.Workspace.GetDocuments());
                Assert.Null(view.DataContext);
                Assert.Null(view.GetVisualParent());
            }
            foreach (var registration in registry.Tools)
            {
                var tool = Assert.IsType<ManagedToolDockable>(context.Workspace.CreatedTools[registration.Descriptor.ToolTypeId.Value]);
                var view = Assert.IsAssignableFrom<Control>(tool.PreparedView);
                Assert.True(context.Workspace.OpenTool(registration.Descriptor.ToolTypeId.Value).Succeeded);
                Assert.True(context.Workspace.SetToolVisibility(tool.Id, false).Succeeded);
                Assert.True(context.Workspace.SetToolVisibility(tool.Id, true).Succeeded);
                Dispatcher.UIThread.RunJobs();
                Assert.Same(view, tool.PreparedView);
                Assert.Same(tool.Model, view.DataContext);
            }
            output.WriteLine($"{pluginId}: {registry.Documents.Count} 个真实 Document 完成创建/分割/关闭；{registry.Tools.Count} 个 Tool 完成隐藏/恢复。");
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
