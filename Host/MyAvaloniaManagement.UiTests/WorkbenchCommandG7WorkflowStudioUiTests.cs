using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Dock.Model.Controls;
using Dock.Model.Mvvm.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Appearance;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Documents;
using MyAvaloniaManagement.Business.Documents.Ownership;
using MyAvaloniaManagement.Business.Layout;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.Business.Plugins.Discovery;
using MyAvaloniaManagement.Business.Plugins.Registration;
using MyAvaloniaManagement.Business.Storage;
using MyAvaloniaManagement.Business.Workspace;
using MyAvaloniaManagement.Business.WorkflowActions;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.Views;
using MyAvaloniaManagement.ViewModels;
using Xunit;

namespace MyAvaloniaManagement.UiTests;

/// <summary>使用外部 WorkflowStudio 1.2.0 真实包验证 G7 Host-owned 菜单与快捷键投影。</summary>
/// <remarks>
/// 测试不引用 WorkflowStudio 源项目，也不把外部类型加载到默认 ALC。专项脚本提供真实 ZIP 解压目录，
/// 本类再通过生产 Loader、插件 Provider、Workspace、MainWindow XAML 和 Headless 输入完成用户路径验收。
/// </remarks>
public sealed class WorkbenchCommandG7WorkflowStudioUiTests
{
    private const string PackageRootVariable =
        "MYAVALONIA_WORKBENCH_COMMAND_G7_WORKFLOW_PLUGIN_ROOT";

    [AvaloniaFact]
    public async Task Studio三条菜单快捷键随当前真实Document实例投影()
    {
        var configuredRoot = Environment.GetEnvironmentVariable(PackageRootVariable);
        if (string.IsNullOrWhiteSpace(configuredRoot))
        {
            return;
        }

        using var context = new ExternalWorkflowStudioUiContext(
            Path.GetFullPath(configuredRoot));
        var window = new MainWindow { DataContext = context.ViewModel };
        window.Show();
        try
        {
            Assert.DoesNotContain(
                window.GetLogicalDescendants().OfType<MenuItem>(),
                item => Equals(item.Header, "验证当前工作流"));
            var validateBinding = Assert.Single(
                window.KeyBindings,
                item => item.Gesture == new KeyGesture(Key.F6));
            var runBinding = Assert.Single(
                window.KeyBindings,
                item => item.Gesture == new KeyGesture(Key.F5));
            var cancelBinding = Assert.Single(
                window.KeyBindings,
                item => item.Gesture == new KeyGesture(Key.F5, KeyModifiers.Shift));
            Assert.False(validateBinding.Command!.CanExecute(null));
            Assert.False(runBinding.Command!.CanExecute(null));
            Assert.False(cancelBinding.Command!.CanExecute(null));

            var first = await context.CreateDocumentAsync();
            var dock = context.GetDocumentDock();
            dock.ActiveDockable = first;
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

            var validateItem = Assert.Single(
                window.GetLogicalDescendants().OfType<MenuItem>(),
                item => Equals(item.Header, "验证当前工作流"));
            var runItem = Assert.Single(
                window.GetLogicalDescendants().OfType<MenuItem>(),
                item => Equals(item.Header, "运行当前工作流"));
            var cancelItem = Assert.Single(
                window.GetLogicalDescendants().OfType<MenuItem>(),
                item => Equals(item.Header, "取消当前工作流"));
            Assert.True(validateItem.IsEnabled);
            Assert.False(runItem.IsEnabled);
            Assert.False(cancelItem.IsEnabled);
            Assert.Same(validateItem.Command, validateBinding.Command);
            Assert.Same(runItem.Command, runBinding.Command);
            Assert.Same(cancelItem.Command, cancelBinding.Command);

            var firstRiskBefore = ReadStringProperty(first.Model, "RiskSummary");
            window.KeyPressQwerty(PhysicalKey.F6, RawInputModifiers.None);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.NotEqual(firstRiskBefore, ReadStringProperty(first.Model, "RiskSummary"));

            var second = await context.CreateDocumentAsync();
            dock.ActiveDockable = second;
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            var secondRiskBefore = ReadStringProperty(second.Model, "RiskSummary");
            window.KeyPressQwerty(PhysicalKey.F6, RawInputModifiers.None);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

            Assert.NotEqual(secondRiskBefore, ReadStringProperty(second.Model, "RiskSummary"));
            Assert.NotEqual(firstRiskBefore, ReadStringProperty(first.Model, "RiskSummary"));
            Assert.NotSame(first.Model, second.Model);

            dock.ActiveDockable = null;
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            Assert.DoesNotContain(
                window.GetLogicalDescendants().OfType<MenuItem>(),
                item => Equals(item.Header, "验证当前工作流"));
            Assert.False(validateBinding.Command.CanExecute(null));
            Assert.False(runBinding.Command.CanExecute(null));
            Assert.False(cancelBinding.Command.CanExecute(null));
        }
        finally
        {
            window.Close();
        }

        Assert.Empty(window.KeyBindings);
    }

    private static string ReadStringProperty(object model, string propertyName) =>
        Assert.IsType<string>(model.GetType().GetProperty(propertyName)?.GetValue(model));

    /// <summary>为一个真实外部 Studio 包建立完整但不访问用户数据的 Headless Host 组合。</summary>
    private sealed class ExternalWorkflowStudioUiContext : IDisposable
    {
        private static readonly DocumentTypeId StudioDocument =
            new("myavalonia.plugin.workflow-studio.document.studio");

        private readonly HostDiagnosticSession _diagnostics;
        private readonly PluginProviderOwner _pluginProviders;
        private readonly DocumentScopeRegistry _documentScopes;
        private bool _disposed;

        internal ExternalWorkflowStudioUiContext(string pluginRoot)
        {
            TempDirectory = Path.Combine(
                Path.GetTempPath(),
                "MyAvaloniaManagement.UiTests",
                $"workbench-command-g7-{Guid.NewGuid():N}");
            Directory.CreateDirectory(TempDirectory);
            _diagnostics = HostDiagnosticSession.Start(
                Path.Combine(TempDirectory, "diagnostics"));
            _pluginProviders = new PluginProviderOwner();
            _documentScopes = new DocumentScopeRegistry();
            var registryBuilder = new PluginRegistryBuilder();
            var snapshot = AssemblyLoaderHelper.Discover(pluginRoot);
            Assert.Empty(snapshot.Diagnostics);
            var services = new ServiceCollection();
            services.AddApplicationServices(
                registryBuilder,
                _pluginProviders,
                _documentScopes);
            services.AddViewModels();
            services.AddSingleton<IHostStorageService>(new UiStorageService());
            services.AddSingleton(new DockLayoutStore(
                Path.Combine(TempDirectory, DockLayoutStore.LayoutFileName)));
            services.AddSingleton(new AppearanceSettingsStore(
                Path.Combine(
                    TempDirectory,
                    AppearanceSettingsStore.SettingsFileName)));
            services.AddSingleton(_diagnostics);
            services.AddSingleton<IHostDiagnosticSink>(_diagnostics);
            services.AddSingleton(PluginModuleCatalog.Discover(snapshot));
            Provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true,
            });
            _pluginProviders.Compose(
                Provider.GetRequiredService<PluginModuleCatalog>(),
                Provider,
                registryBuilder,
                _documentScopes,
                _diagnostics);
            var registry = Provider.GetRequiredService<PluginRegistry>();
            Provider.GetRequiredService<WorkflowActionCatalogStore>().Commit(
                registry,
                Provider.GetRequiredService<PluginAvailabilityReadModel>());
            Workspace = Provider.GetRequiredService<WorkspaceSession>();
            ViewModel = Provider.GetRequiredService<MainWindowViewModel>();
        }

        internal string TempDirectory { get; }

        internal ServiceProvider Provider { get; }

        internal WorkspaceSession Workspace { get; }

        internal MainWindowViewModel ViewModel { get; }

        internal async Task<ManagedDocumentDockable> CreateDocumentAsync()
        {
            var result = await Provider
                .GetRequiredService<DocumentPersistenceCoordinator>()
                .CreateDocumentAsync(StudioDocument);
            Provider.GetRequiredService<DocumentOperationState>().Apply(result);
            return GetDocumentDock().VisibleDockables!
                .OfType<ManagedDocumentDockable>()
                .Last(item => item.Registration.Descriptor.DocumentTypeId == StudioDocument);
        }

        internal DocumentDock GetDocumentDock() =>
            Assert.IsType<DocumentDock>(Workspace.DockFactory.GetDockable<IDocumentDock>(
                DockLayoutIds.Documents));

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _documentScopes.CloseAll();
            _pluginProviders.Dispose();
            Provider.Dispose();
            _diagnostics.Dispose();
            if (Directory.Exists(TempDirectory))
            {
                Directory.Delete(TempDirectory, recursive: true);
            }
        }
    }
}
