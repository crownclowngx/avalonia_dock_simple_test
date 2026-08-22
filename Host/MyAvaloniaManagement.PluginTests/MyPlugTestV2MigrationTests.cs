using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Events;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;
using MyPlugTest.Constants;
using MyPlugTest.Models;
using MyPlugTest.Plugin;
using MyPlugTest.Services;
using MyPlugTest.ViewModels;
using MyPlugTest.Views;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>
/// 验证 G9 MyPlugTest 通过最终 V2 组合路径形成声明、Scope、Tool singleton 与内容协议。
/// </summary>
/// <remarks>
/// 这些测试不实现第二套注册上下文，而是复用 Host production 的 PluginProviderOwner、Registry 和
/// Activator。这样测试通过意味着真实所有权链可工作，而不是测试替身恰好接受了迁移后的类型形状。
/// </remarks>
public sealed class MyPlugTestV2MigrationTests
{
    [Fact]
    public void 模块一次声明四个Document一个Tool及精确View元数据()
    {
        using var composition = MyPlugTestComposition.Create();
        var registry = composition.Registry;

        var plugin = Assert.Single(registry.Plugins);
        Assert.Equal(
            MyPlugTestContributionIds.Plugin.Value,
            plugin.Manifest.PluginId.Value);
        Assert.Equal(4, plugin.DocumentTypes.Count);
        Assert.Single(plugin.ToolTypes);
        Assert.Equal(5, registry.DocumentDescriptors.Count);
        Assert.Equal(5, registry.ToolDescriptors.Count);
        Assert.Empty(registry.Lifecycles);

        AssertDocument<TestWelcomeViewModel, TestWelcomeView>(
            registry,
            MyPlugTestContributionIds.WelcomeDocument,
            "欢迎",
            "显示欢迎信息2",
            persistable: true);
        AssertDocument<TestMessageReceiveViewModel, TestMessageReceiveView>(
            registry,
            MyPlugTestContributionIds.MessageReceiverDocument,
            "测试消息订阅组件",
            "消息订阅测试");
        AssertDocument<BatchHttpGetViewModel, BatchHttpGetView>(
            registry,
            MyPlugTestContributionIds.BatchHttpGetDocument,
            "逐行 HTTP GET",
            "将多行网址按输入顺序逐个执行 GET 请求");
        AssertDocument<ExcelGetUrlGeneratorViewModel, ExcelGetUrlGeneratorView>(
            registry,
            MyPlugTestContributionIds.ExcelGetUrlGeneratorDocument,
            "Excel GET 地址生成器",
            "按 Excel 列映射批量生成 GET 请求地址");

        Assert.True(registry.TryGetToolRegistration(
            MyPlugTestContributionIds.CustomTool,
            out var tool));
        Assert.Equal(typeof(MyCustomToolViewModel), tool.ModelType);
        Assert.Equal(typeof(MyCustomToolView), tool.ViewType);
        Assert.Equal("我的自定义工具", tool.Descriptor.DisplayName);
        Assert.Equal(ToolDockSide.Right, tool.Descriptor.DockSide);
        Assert.Equal(ToolCloseBehavior.Hide, tool.Descriptor.CloseBehavior);
    }

    [Fact]
    public async Task 多Document隔离局部状态且Tool保持插件级单例()
    {
        using var composition = MyPlugTestComposition.Create();
        var activator = composition.HostProvider.GetRequiredService<PluginContributionActivator>();
        using var firstActivation = activator.ActivateDocument(MyPlugTestContributionIds.WelcomeDocument);
        using var secondActivation = activator.ActivateDocument(MyPlugTestContributionIds.WelcomeDocument);
        var first = Assert.IsType<TestWelcomeViewModel>(firstActivation.Model);
        var second = Assert.IsType<TestWelcomeViewModel>(secondActivation.Model);

        await first.InitializeAsync(new DocumentActivationContext("欢迎 A"), default);
        await second.InitializeAsync(new DocumentActivationContext(string.Empty), default);
        first.UrlHistory.AddUrl("https://first.test");

        Assert.NotSame(first, second);
        Assert.NotSame(first.UrlHistory, second.UrlHistory);
        Assert.Single(first.UrlHistory.HistoryItems);
        Assert.Empty(second.UrlHistory.HistoryItems);
        Assert.Equal("欢迎 A", first.Presentation.Title);
        Assert.Equal("Test欢迎", second.Presentation.Title);

        var firstTool = activator.ActivateTool(MyPlugTestContributionIds.CustomTool);
        var secondTool = activator.ActivateTool(MyPlugTestContributionIds.CustomTool);
        Assert.Same(firstTool.Model, secondTool.Model);
        Assert.IsType<MyCustomToolViewModel>(firstTool.Model);
    }

    [Fact]
    public async Task Welcome内容以原生Json往返且保存提交点明确()
    {
        using var sourceLifetime = new TestPluginDocumentLifetime();
        using var source = CreateWelcome(sourceLifetime);
        await source.InitializeAsync(new DocumentActivationContext("持久化欢迎"), default);
        var dirtyChanges = 0;
        source.IsDirtyChanged += (_, _) => dirtyChanges++;
        source.Url = "https://roundtrip.test";
        source.ResponseContent = "往返正文";
        source.UrlHistory.AddUrl("https://history-a.test");
        source.UrlHistory.AddUrl("https://history-b.test");

        Assert.True(source.IsDirty);
        var snapshot = await source.CaptureSaveSnapshotAsync(default);
        var content = snapshot.Content;
        Assert.Equal(1, content.SchemaVersion);
        Assert.Equal(JsonValueKind.Object, content.Payload.ValueKind);
        Assert.Equal(
            ["url", "responseContent", "historyItems"],
            content.Payload.EnumerateObject().Select(property => property.Name));
        Assert.DoesNotContain("DisplayTime", content.Payload.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("RequestTime", content.Payload.GetRawText(), StringComparison.Ordinal);
        Assert.True(source.IsDirty);

        using var targetLifetime = new TestPluginDocumentLifetime();
        using var target = CreateWelcome(targetLifetime);
        await target.InitializeAsync(
            new DocumentActivationContext("恢复欢迎", restoredContent: content),
            default);

        Assert.Equal("恢复欢迎", target.Presentation.Title);
        Assert.Equal("https://roundtrip.test", target.Url);
        Assert.Equal("往返正文", target.ResponseContent);
        Assert.Equal(
            ["https://history-b.test", "https://history-a.test"],
            target.UrlHistory.HistoryItems.Select(item => item.Url));
        Assert.False(target.IsDirty);

        source.Url = "https://captured-later.test";
        source.AcceptChanges(snapshot.Revision);
        Assert.True(source.IsDirty);
        var current = await source.CaptureSaveSnapshotAsync(default);
        source.AcceptChanges(current.Revision);
        Assert.False(source.IsDirty);
        source.AcceptChanges(current.Revision);
        Assert.False(source.IsDirty);
        Assert.Equal(2, dirtyChanges);
    }

    [Theory]
    [InlineData(2, "{\"url\":\"https://valid.test\",\"responseContent\":\"ok\",\"historyItems\":[]}")]
    [InlineData(1, "[]")]
    [InlineData(1, "{}")]
    [InlineData(1, "{\"url\":\"https://valid.test\",\"responseContent\":\"ok\",\"historyItems\":[],\"unknown\":true}")]
    [InlineData(1, "{\"url\":\"https://first.test\",\"url\":\"https://second.test\",\"responseContent\":\"ok\",\"historyItems\":[]}")]
    [InlineData(1, "{\"url\":42,\"responseContent\":\"ok\",\"historyItems\":[]}")]
    [InlineData(1, "{\"url\":\"   \",\"responseContent\":\"ok\",\"historyItems\":[]}")]
    [InlineData(1, "{\"url\":\"https://valid.test\",\"responseContent\":42,\"historyItems\":[]}")]
    [InlineData(1, "{\"url\":\"https://valid.test\",\"responseContent\":\"ok\",\"historyItems\":{}}")]
    [InlineData(1, "{\"url\":\"https://valid.test\",\"responseContent\":\"ok\",\"historyItems\":[42]}")]
    [InlineData(1, "{\"url\":\"https://valid.test\",\"responseContent\":\"ok\",\"historyItems\":[{}]}")]
    [InlineData(1, "{\"url\":\"https://valid.test\",\"responseContent\":\"ok\",\"historyItems\":[{\"url\":42}]}")]
    [InlineData(1, "{\"url\":\"https://valid.test\",\"responseContent\":\"ok\",\"historyItems\":[{\"url\":\"   \"}]}")]
    [InlineData(1, "{\"url\":\"https://valid.test\",\"responseContent\":\"ok\",\"historyItems\":[{\"url\":\"https://first.test\",\"url\":\"https://second.test\"}]}")]
    [InlineData(1, "{\"url\":\"https://valid.test\",\"responseContent\":\"ok\",\"historyItems\":[{\"url\":\"https://history.test\",\"extra\":1}]}")]
    public async Task Welcome严格拒绝损坏内容且不提交部分状态(int schemaVersion, string json)
    {
        using var lifetime = new TestPluginDocumentLifetime();
        using var model = CreateWelcome(lifetime);
        await model.InitializeAsync(new DocumentActivationContext("原始标题"), default);
        model.Url = "https://before.test";
        model.ResponseContent = "原始正文";
        model.UrlHistory.AddUrl("https://before-history.test");

        using var document = JsonDocument.Parse(json);
        var content = new DocumentContent(schemaVersion, document.RootElement);
        var exception = Assert.Throws<InvalidDataException>(() =>
        {
            _ = model.InitializeAsync(
                new DocumentActivationContext("不应提交", restoredContent: content),
                default);
        });

        Assert.DoesNotContain(json, exception.Message, StringComparison.Ordinal);
        Assert.Equal("原始标题", model.Presentation.Title);
        Assert.Equal("https://before.test", model.Url);
        Assert.Equal("原始正文", model.ResponseContent);
        Assert.Equal(
            ["https://before-history.test"],
            model.UrlHistory.HistoryItems.Select(item => item.Url));
        Assert.True(model.IsDirty);
    }

    [Fact]
    public async Task Welcome捕获和初始化传播取消且不吞掉关闭信号()
    {
        using var lifetime = new TestPluginDocumentLifetime();
        using var model = CreateWelcome(lifetime);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
        {
            _ = model.InitializeAsync(
                new DocumentActivationContext("取消"),
                cancelled.Token);
        });
        await model.InitializeAsync(new DocumentActivationContext("可用"), default);

        lifetime.Close();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await model.CaptureSaveSnapshotAsync(default));
    }

    [Fact]
    public async Task Welcome恢复使用DocumentContent克隆且不依赖原JsonDocument生命周期()
    {
        DocumentContent content;
        using (var json = JsonDocument.Parse(
                   "{\"url\":\"https://clone.test\",\"responseContent\":\"克隆正文\",\"historyItems\":[{\"url\":\"https://history.test\"}]}"))
        {
            content = new DocumentContent(1, json.RootElement);
        }

        using var lifetime = new TestPluginDocumentLifetime();
        using var model = CreateWelcome(lifetime);
        await model.InitializeAsync(
            new DocumentActivationContext("克隆恢复", restoredContent: content),
            default);

        Assert.Equal("https://clone.test", model.Url);
        Assert.Equal("克隆正文", model.ResponseContent);
        Assert.Equal("https://history.test", Assert.Single(model.UrlHistory.HistoryItems).Url);
        Assert.False(model.IsDirty);
    }

    [Fact]
    public async Task Welcome关闭后领域异常不得迟到回写或发布事件()
    {
        var eventBus = new HostEventBus();
        var published = 0;
        using var subscription = eventBus.Subscribe<RequestResponseMessage>(_ => published++);
        var service = new DeferredFailureUrlContentService();
        using var lifetime = new TestPluginDocumentLifetime();
        using var model = new TestWelcomeViewModel(
            eventBus,
            new UrlHistoryViewModel(),
            service,
            lifetime);
        await model.InitializeAsync(new DocumentActivationContext("关闭竞争"), default);

        var execution = model.SendRequestCommand.ExecuteAsync(null);
        await service.Started;
        lifetime.Close();
        service.Fail();
        await execution;

        Assert.Equal("正在发送请求...", model.ResponseContent);
        Assert.Equal(0, published);
        Assert.Empty(model.UrlHistory.HistoryItems);
    }

    [Fact]
    public void 生产程序集不再引用LegacyDockNewtonsoft或Host实现()
    {
        var references = typeof(MyPlugTestPluginModule).Assembly
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

    private static TestWelcomeViewModel CreateWelcome(IDocumentLifetime lifetime) => new(
        new HostEventBus(),
        new UrlHistoryViewModel(),
        new StubUrlContentService(),
        lifetime);

    private static void AssertDocument<TDocument, TView>(
        PluginRegistry registry,
        DocumentTypeId documentTypeId,
        string displayName,
        string description,
        bool persistable = false)
    {
        Assert.True(registry.TryGetDocumentRegistration(documentTypeId, out var registration));
        Assert.Equal(typeof(TDocument), registration.ModelType);
        Assert.Equal(typeof(TView), registration.ViewType);
        Assert.Equal(displayName, registration.Descriptor.DisplayName);
        Assert.Equal(description, registration.Descriptor.Description);
        Assert.Equal("测试插件", registration.Descriptor.MenuCategory);
        Assert.Equal(persistable, registration.IsPersistable);
    }

    private sealed class StubUrlContentService : IUrlContentService
    {
        public Task<string> GetStringAsync(
            string url,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(string.Empty);
    }

    /// <summary>
    /// 模拟不遵守取消令牌、并在 Document 关闭后才以领域异常完成的网络实现。
    /// 这类替身用于保护模型的最后一道关闭门控，而不是把错误实现当成生产语义。
    /// </summary>
    private sealed class DeferredFailureUrlContentService : IUrlContentService
    {
        private readonly TaskCompletionSource _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<string> _result =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task Started => _started.Task;

        public Task<string> GetStringAsync(
            string url,
            CancellationToken cancellationToken = default)
        {
            _started.TrySetResult();
            return _result.Task;
        }

        internal void Fail() => _result.TrySetException(
            new UrlContentRequestException(503, "迟到错误", new InvalidOperationException("late")));
    }

    private sealed class MyPlugTestComposition : IDisposable
    {
        private readonly string _directory;
        private readonly HostDiagnosticSession _diagnostics;
        private readonly PluginProviderOwner _pluginProviders;
        private readonly DocumentScopeRegistry _documentScopes;
        private bool _disposed;

        private MyPlugTestComposition(
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

        internal static MyPlugTestComposition Create()
        {
            var directory = Path.Combine(Path.GetTempPath(), $"my-plug-test-g9-{Guid.NewGuid():N}");
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
            var registry = provider.GetRequiredService<PluginRegistry>();
            return new MyPlugTestComposition(
                directory,
                diagnostics,
                provider,
                pluginProviders,
                documentScopes,
                registry);
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
            HostProvider.Dispose();
            _diagnostics.Dispose();
            Directory.Delete(_directory, recursive: true);
        }
    }
}
