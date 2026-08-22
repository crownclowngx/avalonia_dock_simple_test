using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Docking;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;
using MyPlugTest.Constants;
using MyPlugTest.Models;
using MyPlugTest.Messaging;
using MyPlugTest.Plugin;
using MyPlugTest.ViewModels;
using MyPlugTest.Views;
using Xunit;

namespace MyAvaloniaManagement.UiTests;

/// <summary>在真实 Headless Avalonia 与 Host Adapter 上验证 G9 MyPlugTest 的 V2 视觉组合。</summary>
public sealed class MyPlugTestV2UiTests
{
    [AvaloniaFact]
    public async Task 四个Document与一个Tool通过HostAdapter绑定普通模型()
    {
        using var composition = MyPlugTestUiComposition.Create();
        var factory = composition.Provider.GetRequiredService<IHostDockableFactory>();

        await AssertDocument<TestWelcomeViewModel, TestWelcomeView>(
            factory,
            MyPlugTestContributionIds.WelcomeDocument,
            "Welcome 自定义标题");
        await AssertDocument<TestMessageReceiveViewModel, TestMessageReceiveView>(
            factory,
            MyPlugTestContributionIds.MessageReceiverDocument,
            "消息接收测试");
        await AssertDocument<BatchHttpGetViewModel, BatchHttpGetView>(
            factory,
            MyPlugTestContributionIds.BatchHttpGetDocument,
            "逐行 HTTP GET");
        await AssertDocument<ExcelGetUrlGeneratorViewModel, ExcelGetUrlGeneratorView>(
            factory,
            MyPlugTestContributionIds.ExcelGetUrlGeneratorDocument,
            "Excel GET 地址生成器");

        using var firstTool = Assert.IsType<ManagedToolDockable>(
            factory.CreateTool(MyPlugTestContributionIds.CustomTool));
        using var secondTool = Assert.IsType<ManagedToolDockable>(
            factory.CreateTool(MyPlugTestContributionIds.CustomTool));
        Assert.IsType<MyCustomToolView>(firstTool.PreparedView);
        Assert.Same(firstTool.Model, firstTool.PreparedView!.DataContext);
        Assert.Same(firstTool.Model, secondTool.Model);
        Assert.Equal("我的自定义工具", firstTool.Title);
        Assert.True(firstTool.CanClose);
        Assert.False(firstTool.CanFloat);
        Assert.Equal(ToolDockSide.Right, firstTool.Registration.Descriptor.DockSide);
        AssertViewBindings(firstTool.PreparedView, firstTool.Model);
    }

    [AvaloniaFact]
    public async Task 后台事件投递与单个Scope释放保持订阅隔离()
    {
        using var composition = MyPlugTestUiComposition.Create();
        var factory = composition.Provider.GetRequiredService<IHostDockableFactory>();
        var bus = composition.EventBus;
        var broadTypeDeliveries = 0;
        using var broadTypeSubscription = bus.Subscribe<object>(_ => broadTypeDeliveries++);
        var firstAdapter = Assert.IsType<ManagedDocumentDockable>(await factory.CreateDocumentAsync(
            MyPlugTestContributionIds.MessageReceiverDocument,
            new NewDocumentActivation("接收 A")));
        using var secondAdapter = Assert.IsType<ManagedDocumentDockable>(await factory.CreateDocumentAsync(
            MyPlugTestContributionIds.MessageReceiverDocument,
            new NewDocumentActivation("接收 B")));
        var first = Assert.IsType<TestMessageReceiveViewModel>(firstAdapter.Model);
        var second = Assert.IsType<TestMessageReceiveViewModel>(secondAdapter.Model);

        await Task.Run(() => bus.Publish(new RequestResponseMessage(
            "第一次响应",
            "https://first.test")));
        Dispatcher.UIThread.RunJobs();
        Assert.Single(first.Messages);
        Assert.Single(second.Messages);
        Assert.Equal(0, broadTypeDeliveries);

        firstAdapter.Dispose();
        await Task.Run(() => bus.Publish(new RequestResponseMessage(
            "第二次响应",
            "https://second.test")));
        Dispatcher.UIThread.RunJobs();

        Assert.Single(first.Messages);
        Assert.Equal(2, second.Messages.Count);
        Assert.Equal(0, broadTypeDeliveries);
        Assert.Contains("https://second.test", second.Messages[1].Content, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task 三个非持久化Document显式拒绝Restore激活()
    {
        using var composition = MyPlugTestUiComposition.Create();
        var factory = composition.Provider.GetRequiredService<IHostDockableFactory>();
        using var json = JsonDocument.Parse("{}");
        var content = new DocumentContent(1, json.RootElement);

        foreach (var documentTypeId in new[]
                 {
                     MyPlugTestContributionIds.MessageReceiverDocument,
                     MyPlugTestContributionIds.BatchHttpGetDocument,
                     MyPlugTestContributionIds.ExcelGetUrlGeneratorDocument,
                 })
        {
            await Assert.ThrowsAsync<NotSupportedException>(() => factory.CreateDocumentAsync(
                documentTypeId,
                new RestoreDocumentActivation("错误恢复", content)).AsTask());
        }
    }

    private static async Task AssertDocument<TModel, TView>(
        IHostDockableFactory factory,
        DocumentTypeId documentTypeId,
        string title)
        where TModel : class
        where TView : Control
    {
        using var adapter = Assert.IsType<ManagedDocumentDockable>(await factory.CreateDocumentAsync(
            documentTypeId,
            new NewDocumentActivation(title)));
        var model = Assert.IsType<TModel>(adapter.Model);
        var view = Assert.IsType<TView>(adapter.PreparedView);
        Assert.Same(model, view.DataContext);
        Assert.Equal(title, adapter.Title);
        Assert.False(adapter.CanFloat);
        AssertViewBindings(view, model);
    }

    /// <summary>
    /// 不依赖控件排列顺序，按稳定占位文字定位每个 View 的关键绑定。
    /// 这比只检查 DataContext 更进一步：能够发现 XAML 仍绑定旧 Dock 属性或拼错普通模型属性。
    /// </summary>
    private static void AssertViewBindings(Control? view, object model)
    {
        Assert.NotNull(view);
        switch (view, model)
        {
            case (TestWelcomeView, TestWelcomeViewModel welcome):
                welcome.Url = "https://binding.test";
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(
                    welcome.Url,
                    FindTextBox(view, "请输入网址，例如 https://example.com").Text);
                break;

            case (TestMessageReceiveView, TestMessageReceiveViewModel receiver):
                var list = Assert.Single(view.GetLogicalDescendants().OfType<ListBox>());
                Assert.Same(receiver.Messages, list.ItemsSource);
                break;

            case (BatchHttpGetView, BatchHttpGetViewModel batch):
                batch.RequestLines = "https://binding.test/batch";
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(
                    batch.RequestLines,
                    FindTextBox(view, "http://example.com\nhttp://httpbin.org/get").Text);
                break;

            case (ExcelGetUrlGeneratorView, ExcelGetUrlGeneratorViewModel excel):
                excel.BaseAddress = "https://binding.test/excel";
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(
                    excel.BaseAddress,
                    FindTextBox(view, "https://example.com/api 或 https://example.com/api?fixed=1").Text);
                break;

            case (MyCustomToolView, MyCustomToolViewModel tool):
                tool.CustomProperty = "绑定值";
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(tool.CustomProperty, FindTextBox(view, "请输入自定义属性值").Text);
                break;

            default:
                throw new Xunit.Sdk.XunitException($"G9 缺少 {view.GetType().Name} 的关键绑定断言。");
        }
    }

    private static TextBox FindTextBox(Control view, string placeholder) =>
        Assert.Single(view.GetLogicalDescendants().OfType<TextBox>(), textBox =>
            string.Equals(textBox.PlaceholderText, placeholder, StringComparison.Ordinal));

    private sealed class MyPlugTestUiComposition : IDisposable
    {
        private readonly string _directory;
        private readonly HostDiagnosticSession _diagnostics;
        private readonly PluginProviderOwner _pluginProviders;
        private readonly DocumentScopeRegistry _documentScopes;
        private bool _disposed;

        private MyPlugTestUiComposition(
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

        internal IMyPlugTestEventBus EventBus =>
            (IMyPlugTestEventBus)_pluginProviders.GetRequiredService(
                MyPlugTestContributionIds.Plugin,
                typeof(IMyPlugTestEventBus));

        internal static MyPlugTestUiComposition Create()
        {
            var directory = Path.Combine(Path.GetTempPath(), $"my-plug-test-g9-ui-{Guid.NewGuid():N}");
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
                (MyPlugTestContributionIds.Plugin, (IPluginModule)new MyPlugTestPluginModule()),
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
            return new MyPlugTestUiComposition(
                directory,
                diagnostics,
                provider,
                pluginProviders,
                documentScopes);
        }

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
            Directory.Delete(_directory, recursive: true);
        }
    }
}
