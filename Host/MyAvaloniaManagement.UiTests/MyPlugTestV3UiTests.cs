using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Documents;
using MyAvaloniaManagement.Business.Docking;
using MyAvaloniaManagement.Business.Storage;
using MyAvaloniaManagement.Business.Workspace;
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

/// <summary>在真实 Headless Avalonia、V3 Workspace 与 Host Adapter 上验收 G9 MyPlugTest。</summary>
public sealed class MyPlugTestV3UiTests
{
    [AvaloniaFact]
    public async Task 四个Document与一个Tool通过HostAdapter绑定普通模型()
    {
        using var composition = MyPlugTestUiComposition.Create();
        var workspace = composition.Workspace;

        await AssertDocument<TestWelcomeViewModel, TestWelcomeView>(
            workspace,
            MyPlugTestContributionIds.WelcomeDocument,
            "Welcome 自定义标题");
        await AssertDocument<TestMessageReceiveViewModel, TestMessageReceiveView>(
            workspace,
            MyPlugTestContributionIds.MessageReceiverDocument,
            "消息接收测试");
        await AssertDocument<BatchHttpGetViewModel, BatchHttpGetView>(
            workspace,
            MyPlugTestContributionIds.BatchHttpGetDocument,
            "逐行 HTTP GET");
        await AssertDocument<ExcelGetUrlGeneratorViewModel, ExcelGetUrlGeneratorView>(
            workspace,
            MyPlugTestContributionIds.ExcelGetUrlGeneratorDocument,
            "Excel GET 地址生成器");

        var toolId = MyPlugTestContributionIds.CustomTool.Value;
        var tool = Assert.IsType<ManagedToolDockable>(workspace.CreatedTools[toolId]);
        var originalModel = tool.Model;
        Assert.IsType<MyCustomToolView>(tool.PreparedView);
        Assert.Same(tool.Model, tool.PreparedView!.DataContext);
        Assert.Equal("我的自定义工具", tool.Title);
        Assert.True(tool.CanClose);
        Assert.False(tool.CanFloat);
        Assert.Equal(ToolDockSide.Right, tool.Registration.Descriptor.DockSide);
        AssertViewBindings(tool.PreparedView, tool.Model);

        // Tool 的关闭语义是隐藏而不是释放。通过 Workspace 的显隐提交入口往返一次，
        // 可同时证明 Session 没有绕过插件 Provider 重建 singleton 模型。
        Assert.True(workspace.TrySetToolVisibility(toolId, false));
        Assert.True(workspace.TrySetToolVisibility(toolId, true));
        Assert.Same(tool, workspace.CreatedTools[toolId]);
        Assert.Same(originalModel, tool.Model);
    }

    [AvaloniaFact]
    public async Task 后台事件投递与单个Scope释放保持订阅隔离()
    {
        using var composition = MyPlugTestUiComposition.Create();
        var workspace = composition.Workspace;
        var bus = composition.EventBus;
        var broadTypeDeliveries = 0;
        using var broadTypeSubscription = bus.Subscribe<object>(_ => broadTypeDeliveries++);
        var firstAdapter = await workspace.CreateAndPublishDocumentAsync(
            MyPlugTestContributionIds.MessageReceiverDocument,
            new NewDocumentActivation("接收 A"));
        var secondAdapter = await workspace.CreateAndPublishDocumentAsync(
            MyPlugTestContributionIds.MessageReceiverDocument,
            new NewDocumentActivation("接收 B"));
        var first = Assert.IsType<TestMessageReceiveViewModel>(firstAdapter.Model);
        var second = Assert.IsType<TestMessageReceiveViewModel>(secondAdapter.Model);

        await Task.Run(() => bus.Publish(new RequestResponseMessage(
            "第一次响应",
            "https://first.test")));
        Dispatcher.UIThread.RunJobs();
        Assert.Single(first.Messages);
        Assert.Single(second.Messages);
        Assert.Equal(0, broadTypeDeliveries);

        // 关闭必须经过 Dock -> Workspace 回调链。直接 Dispose Adapter 会绕过 Session 的
        // 所有权集合，无法证明真实标签关闭同时释放 Scope 与事件订阅令牌。
        workspace.DockFactory.CloseDockable(firstAdapter);
        Assert.DoesNotContain(firstAdapter, workspace.GetDocuments());
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
    public async Task Host保存旧修订后保留期间新编辑并可由下一次保存清脏()
    {
        using var composition = MyPlugTestUiComposition.Create();
        var document = await composition.Workspace.CreateAndPublishDocumentAsync(
            MyPlugTestContributionIds.WelcomeDocument,
            new NewDocumentActivation("保存竞争"));
        var model = Assert.IsType<TestWelcomeViewModel>(document.Model);
        model.Url = "https://captured.test";
        model.ResponseContent = "捕获时正文";
        composition.Storage.SavePath = Path.Combine(composition.DirectoryPath, "welcome.mydoc");

        var saveService = composition.Provider.GetRequiredService<DocumentSaveService>();
        var firstSave = saveService.SaveAsync(document);
        await composition.Storage.PrimaryWriteStarted.WaitAsync(TimeSpan.FromSeconds(5));

        // 主文件写入尚未提交时产生一个更高修订。Host 稍后只能确认已捕获的旧修订，
        // 不能因为磁盘写入成功就把这次新编辑错误清除。
        model.ResponseContent = "保存期间的新正文";
        composition.Storage.ReleasePrimaryWrite();
        var firstResult = await firstSave;

        Assert.Equal(DocumentSaveStatus.Saved, firstResult.Status);
        Assert.True(firstResult.HasPendingChanges);
        Assert.True(model.IsDirty);
        Assert.True(document.IsModified);

        var secondResult = await saveService.SaveAsync(document);
        Assert.Equal(DocumentSaveStatus.Saved, secondResult.Status);
        Assert.False(secondResult.HasPendingChanges);
        Assert.False(model.IsDirty);
        Assert.False(document.IsModified);
    }

    [AvaloniaFact]
    public async Task 三个非持久化Document显式拒绝Restore激活()
    {
        using var composition = MyPlugTestUiComposition.Create();
        var workspace = composition.Workspace;
        using var json = JsonDocument.Parse("{}");
        var content = new DocumentContent(1, json.RootElement);

        foreach (var documentTypeId in new[]
                 {
                     MyPlugTestContributionIds.MessageReceiverDocument,
                     MyPlugTestContributionIds.BatchHttpGetDocument,
                     MyPlugTestContributionIds.ExcelGetUrlGeneratorDocument,
                 })
        {
            await Assert.ThrowsAsync<NotSupportedException>(() => workspace.CreateDocumentAsync(
                documentTypeId,
                new RestoreDocumentActivation("错误恢复", content)).AsTask());
        }
    }

    private static async Task AssertDocument<TModel, TView>(
        WorkspaceSession workspace,
        DocumentTypeId documentTypeId,
        string title)
        where TModel : class
        where TView : Control
    {
        var adapter = await workspace.CreateAndPublishDocumentAsync(
            documentTypeId,
            new NewDocumentActivation(title));
        try
        {
            var model = Assert.IsType<TModel>(adapter.Model);
            var view = Assert.IsType<TView>(adapter.PreparedView);
            Assert.Same(model, view.DataContext);
            Assert.Equal(title, adapter.Title);
            Assert.False(adapter.CanFloat);
            AssertViewBindings(view, model);
        }
        finally
        {
            workspace.DockFactory.CloseDockable(adapter);
        }
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
            DocumentScopeRegistry documentScopes,
            WorkspaceSession workspace,
            G9StorageService storage)
        {
            _directory = directory;
            _diagnostics = diagnostics;
            Provider = provider;
            _pluginProviders = pluginProviders;
            _documentScopes = documentScopes;
            Workspace = workspace;
            Storage = storage;
        }

        internal ServiceProvider Provider { get; }

        internal WorkspaceSession Workspace { get; }

        internal G9StorageService Storage { get; }

        internal string DirectoryPath => _directory;

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
            var storage = new G9StorageService();
            var services = new ServiceCollection();
            services.AddApplicationServices(registryBuilder, pluginProviders, documentScopes);
            services.AddViewModels();
            services.AddSingleton<IHostStorageService>(storage);
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
            var workspace = provider.GetRequiredService<WorkspaceSession>();
            var layout = workspace.CreateLayout();
            // 生产启动由 DockLayoutCoordinator 在创建或恢复布局后调用同一 InitLayout。
            // G9 上下文没有主窗口生命周期，因此在这里完成框架初始化，之后的发布、关闭和显隐
            // 才会经过真实 Dock 回调，而不是依靠未初始化集合的偶然行为。
            workspace.DockFactory.InitLayout(layout);
            return new MyPlugTestUiComposition(
                directory,
                diagnostics,
                provider,
                pluginProviders,
                documentScopes,
                workspace,
                storage);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Workspace.Dispose();
            _documentScopes.CloseAll();
            _pluginProviders.Dispose();
            Provider.Dispose();
            _diagnostics.Dispose();
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>
    /// 为保存竞争测试提供受控的内存文件边界。只有第一次主文件写入会暂停，测试因而能精确地在
    /// “插件已捕获旧修订、Host 尚未完成原子提交”之间插入一次编辑；备份和后续保存保持普通完成，
    /// 不把额外并发状态混入被验收的 Revision 语义。
    /// </summary>
    private sealed class G9StorageService : IHostStorageService
    {
        private readonly Dictionary<string, string> _files =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly TaskCompletionSource _primaryWriteStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _continuePrimaryWrite =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _writeCount;

        internal string? SavePath { get; set; }

        internal Task PrimaryWriteStarted => _primaryWriteStarted.Task;

        internal void ReleasePrimaryWrite() => _continuePrimaryWrite.TrySetResult();

        public Task<IReadOnlyList<string>> PickOpenFilesAsync() =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<string?> PickSaveFileAsync(string documentDisplayName) =>
            Task.FromResult(SavePath);

        public Task<string?> PickFolderAsync() => Task.FromResult<string?>(null);

        public bool FileExists(string path) => _files.ContainsKey(Path.GetFullPath(path));

        public long GetFileLength(string path) =>
            System.Text.Encoding.UTF8.GetByteCount(_files[Path.GetFullPath(path)]);

        public Task<string> ReadAllTextAsync(string path) =>
            Task.FromResult(_files[Path.GetFullPath(path)]);

        public async Task WriteAllTextAsync(string path, string content)
        {
            if (Interlocked.Increment(ref _writeCount) == 1)
            {
                _primaryWriteStarted.TrySetResult();
                await _continuePrimaryWrite.Task;
            }

            _files[Path.GetFullPath(path)] = content;
        }
    }
}
