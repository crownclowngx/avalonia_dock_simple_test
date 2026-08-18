using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Documents;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagement.Message;
using MyAvaloniaManagement.ViewModels;
using MyAvaloniaManagement.ViewModels.Tools;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Save;
using Newtonsoft.Json;

namespace MyAvaloniaManagement.Tests;

/// <summary>
/// 验证主窗口 ViewModel 的布局、文件、消息和文档生命周期。
/// </summary>
public sealed class MainWindowViewModelTests
{
    [Fact]
    public void 创建文档会加入文件停靠区()
    {
        using var context = CreateContextWithDocumentStrategy();
        var viewModel = context.CreateMainWindowViewModel();

        viewModel.CreateDocument(TestSavableStrategy.TypeId.Value);

        var dock = GetDocumentDock(context);
        Assert.Contains(dock.VisibleDockables!, item =>
            item is TestSavableDocument);
    }

    [Fact]
    public async Task 打开文档只读取一次并恢复保存数据()
    {
        using var context = CreateContextWithDocumentStrategy();
        var path = Path.Combine(context.TempDirectory, "opened.testdoc");
        context.Storage.AddFile(path, Serialize("标题", "正文"));
        var viewModel = context.CreateMainWindowViewModel();

        await viewModel.OpenDocumentByPath(path);

        var document = GetDocuments(context).Single();
        Assert.Equal(Path.GetFullPath(path), context.GetDocumentFilePath(document));
        Assert.Equal("标题", document.Title);
        Assert.Equal("正文", document.Content);
    }

    [Fact]
    public async Task 批量打开遇到重复和损坏文件仍继续后续文件()
    {
        using var context = CreateContextWithDocumentStrategy();
        var first = Path.Combine(context.TempDirectory, "first.testdoc");
        var broken = Path.Combine(context.TempDirectory, "broken.testdoc");
        var second = Path.Combine(context.TempDirectory, "second.testdoc");
        context.Storage.AddFile(first, Serialize("第一", "A"));
        context.Storage.AddFile(broken, "{broken");
        context.Storage.AddFile(second, Serialize("第二", "B"));
        var viewModel = context.CreateMainWindowViewModel();
        await viewModel.OpenDocumentByPath(first);
        context.Storage.OpenPaths = [first.ToUpperInvariant(), broken, second];

        await viewModel.OpenDocument();

        Assert.Equal(2, GetDocuments(context).Count);
        Assert.Contains(GetDocuments(context), item => item.Title == "第一");
        Assert.Contains(GetDocuments(context), item => item.Title == "第二");
        Assert.True(viewModel.HasDocumentOperationError);
        Assert.Contains("原文件未被修改", viewModel.DocumentOperationError);
    }

    [Fact]
    public async Task 不存在路径不会创建文档()
    {
        using var context = CreateContextWithDocumentStrategy();
        var viewModel = context.CreateMainWindowViewModel();

        await viewModel.OpenDocumentByPath(
            Path.Combine(context.TempDirectory, "missing.testdoc"));

        Assert.Empty(GetDocuments(context));
    }

    [Fact]
    public async Task 未知文档类型不会阻止同批次有效文档()
    {
        using var context = CreateContextWithDocumentStrategy();
        var unknown = Path.Combine(context.TempDirectory, "unknown.testdoc");
        var valid = Path.Combine(context.TempDirectory, "valid.testdoc");
        context.Storage.AddFile(
            unknown,
            Serialize(new DocumentTypeId("unknown"), "未知", string.Empty));
        context.Storage.AddFile(valid, Serialize("有效", "ok"));
        context.Storage.OpenPaths = [unknown, valid];
        var viewModel = context.CreateMainWindowViewModel();

        await viewModel.OpenDocument();

        Assert.Single(GetDocuments(context));
        Assert.Equal("有效", GetDocuments(context)[0].Title);
    }

    [Fact]
    public async Task 新文档保存使用元数据并同步标题路径()
    {
        using var context = CreateContextWithDocumentStrategy();
        var savePath = Path.Combine(context.TempDirectory, "saved.testdoc");
        context.Storage.SavePath = savePath;
        var viewModel = context.CreateMainWindowViewModel();
        viewModel.CreateDocument(TestSavableStrategy.TypeId.Value);
        var document = GetDocuments(context).Single();
        GetDocumentDock(context).ActiveDockable = document;
        document.Content = "保存内容";

        await viewModel.SaveDocument();

        Assert.Equal(TestSavableStrategy.TypeId,
            context.Storage.LastSaveMetadata?.DocumentTypeId);
        Assert.Equal(Path.GetFullPath(savePath), context.GetDocumentFilePath(document));
        Assert.Equal("saved", document.Title);
        var primaryWrite = Assert.Single(context.Storage.Writes, item =>
            item.Path.Equals(Path.GetFullPath(savePath), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(context.Storage.Writes, item =>
            item.Path.Equals(
                Path.GetFullPath(savePath) + DocumentRecoveryRegistry.BackupSuffix,
                StringComparison.OrdinalIgnoreCase));
        using var stored = System.Text.Json.JsonDocument.Parse(primaryWrite.Content);
        Assert.Equal("saved", stored.RootElement.GetProperty("title").GetString());
        Assert.Equal("保存内容", stored.RootElement.GetProperty("payload").GetString());
    }

    [Fact]
    public async Task 取消保存不会写入()
    {
        using var context = CreateContextWithDocumentStrategy();
        var viewModel = context.CreateMainWindowViewModel();
        viewModel.CreateDocument(TestSavableStrategy.TypeId.Value);
        GetDocumentDock(context).ActiveDockable =
            GetDocuments(context).Single();

        await viewModel.SaveDocument();

        Assert.Empty(context.Storage.Writes);
    }

    [Fact]
    public async Task 已有路径直接覆盖且不再打开保存对话框()
    {
        using var context = CreateContextWithDocumentStrategy();
        var path = Path.Combine(context.TempDirectory, "existing.testdoc");
        var viewModel = context.CreateMainWindowViewModel();
        viewModel.CreateDocument(TestSavableStrategy.TypeId.Value);
        var document = GetDocuments(context).Single();
        context.SetDocumentFilePath(document, path);
        GetDocumentDock(context).ActiveDockable = document;

        await viewModel.SaveDocument();

        Assert.Null(context.Storage.LastSaveMetadata);
        Assert.Contains(context.Storage.Writes, item =>
            item.Path.Equals(Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, document.SaveCompletedCount);
    }

    [Fact]
    public async Task 受保护文档_强制另存并在写入成功后解除保护()
    {
        using var context = CreateContextWithDocumentStrategy();
        var original = Path.Combine(context.TempDirectory, "future.testdoc");
        var copy = Path.Combine(context.TempDirectory, "future-copy.testdoc");
        context.Storage.SavePath = copy;
        var viewModel = context.CreateMainWindowViewModel();
        viewModel.CreateDocument(TestSavableStrategy.TypeId.Value);
        var document = GetDocuments(context).Single();
        context.SetDocumentFilePath(document, original);
        document.RequiresSaveAs = true;
        GetDocumentDock(context).ActiveDockable = document;

        await viewModel.SaveDocument();

        Assert.Contains(context.Storage.Writes, item =>
            item.Path.Equals(Path.GetFullPath(copy), StringComparison.OrdinalIgnoreCase));
        Assert.False(document.RequiresSaveAs);
        Assert.Equal(1, document.SaveCompletedCount);
    }

    [Fact]
    public async Task 受保护文档_选择原路径时拒绝覆盖()
    {
        using var context = CreateContextWithDocumentStrategy();
        var original = Path.Combine(context.TempDirectory, "future.testdoc");
        context.Storage.SavePath = original;
        var viewModel = context.CreateMainWindowViewModel();
        viewModel.CreateDocument(TestSavableStrategy.TypeId.Value);
        var document = GetDocuments(context).Single();
        context.SetDocumentFilePath(document, original);
        document.RequiresSaveAs = true;
        GetDocumentDock(context).ActiveDockable = document;

        await viewModel.SaveDocument();

        Assert.Empty(context.Storage.Writes);
        Assert.True(document.RequiresSaveAs);
        Assert.True(viewModel.HasDocumentOperationError);
        Assert.Contains("不同的文件路径", viewModel.DocumentOperationError);
    }

    [Fact]
    public void 更新布局消息触发布局属性通知()
    {
        using var context = CreateContextWithDocumentStrategy();
        var viewModel = context.CreateMainWindowViewModel();
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        context.Messenger.Send(new UpdateLayoutMessage("refresh"));

        Assert.Contains(nameof(viewModel.Layout), changed);
    }

    [Fact]
    public void 打开文件消息调用路径打开流程()
    {
        using var context = CreateContextWithDocumentStrategy();
        var path = Path.Combine(context.TempDirectory, "message.testdoc");
        context.Storage.AddFile(path, Serialize("消息", "content"));
        _ = context.CreateMainWindowViewModel();

        context.Messenger.Send(new OpenFileMessage(path));

        Assert.Single(GetDocuments(context));
    }

    [Fact]
    public void 主题命令更新单选状态并立即持久化()
    {
        using var context = CreateContextWithDocumentStrategy();
        var viewModel = context.CreateMainWindowViewModel();
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        viewModel.SetThemeCommand.Execute("Dark");

        Assert.False(viewModel.IsSystemTheme);
        Assert.False(viewModel.IsLightTheme);
        Assert.True(viewModel.IsDarkTheme);
        Assert.True(File.Exists(context.AppearanceSettingsPath));
        Assert.Contains(nameof(viewModel.IsSystemTheme), changed);
        Assert.Contains(nameof(viewModel.IsLightTheme), changed);
        Assert.Contains(nameof(viewModel.IsDarkTheme), changed);
    }

    [Fact]
    public async Task ConcurrentOpenOfSamePathCreatesOneDocumentAndReadsOnce()
    {
        using var context = CreateContextWithDocumentStrategy();
        var path = Path.Combine(context.TempDirectory, "concurrent.testdoc");
        context.Storage.AddFile(path, Serialize("Concurrent", "content"));
        var viewModel = context.CreateMainWindowViewModel();

        await Task.WhenAll(
            viewModel.OpenDocumentByPath(path),
            viewModel.OpenDocumentByPath(path));

        Assert.Single(GetDocuments(context));
        Assert.Equal(1, context.Storage.ReadCount);
    }

    [Fact]
    public async Task SaveFailureDoesNotMutateDocumentState()
    {
        using var context = CreateContextWithDocumentStrategy();
        var originalPath = Path.Combine(context.TempDirectory, "protected.testdoc");
        var attemptedPath = Path.Combine(context.TempDirectory, "failed-copy.testdoc");
        context.Storage.SavePath = attemptedPath;
        context.Storage.WriteException = new IOException("simulated");
        var viewModel = context.CreateMainWindowViewModel();
        viewModel.CreateDocument(TestSavableStrategy.TypeId.Value);
        var document = GetDocuments(context).Single();
        var originalTitle = document.Title;
        context.SetDocumentFilePath(document, originalPath);
        document.RequiresSaveAs = true;
        document.IsModified = true;
        GetDocumentDock(context).ActiveDockable = document;

        await viewModel.SaveDocument();

        Assert.Equal(originalTitle, document.Title);
        Assert.Equal(originalPath, context.GetDocumentFilePath(document));
        Assert.True(document.IsDirty);
        Assert.True(document.RequiresSaveAs);
        Assert.Equal(0, document.AcceptChangesCount);
        Assert.Equal(0, document.SaveCompletedCount);
        Assert.True(viewModel.HasDocumentOperationError);
        Assert.Empty(context.Storage.Writes);
    }

    [Fact]
    public void OpenMessageObservesExpectedReadFailure()
    {
        using var context = CreateContextWithDocumentStrategy();
        var path = Path.Combine(context.TempDirectory, "message-failure.testdoc");
        context.Storage.AddFile(path, Serialize("Failure", "content"));
        context.Storage.ReadException = new IOException("simulated");
        var viewModel = context.CreateMainWindowViewModel();

        context.Messenger.Send(new OpenFileMessage(path));

        Assert.True(viewModel.HasDocumentOperationError);
        Assert.Empty(GetDocuments(context));
    }

    [Fact]
    public async Task Scoped文档加载失败会立即取消并释放且Dock无残留()
    {
        var probe = new DocumentLifecycleProbe { ThrowOnLoad = true };
        using var context = CreateScopedSavableContext(probe);
        var path = Path.Combine(context.TempDirectory, "broken-scoped.testdoc");
        context.Storage.AddFile(
            path,
            Serialize(TrackedScopedSavableStrategy.TypeId, "损坏文档", "broken"));
        var viewModel = context.CreateMainWindowViewModel();

        await viewModel.OpenDocumentByPath(path);

        Assert.True(viewModel.HasDocumentOperationError);
        Assert.Empty(GetDocumentDock(context).VisibleDockables!
            .OfType<TrackedScopedSavableDocument>());
        Assert.Equal(1, probe.CreatedCount);
        Assert.Equal(1, probe.LoadCount);
        Assert.Equal(1, probe.CancellationCount);
        Assert.Equal(1, probe.DocumentDisposeCount);
        Assert.Equal(1, probe.DependencyDisposeCount);
        Assert.True(probe.AllDocumentsObservedClosing);
    }

    [Fact]
    public async Task Scoped文档成功恢复后由Dock接管并在正常关闭时释放()
    {
        var probe = new DocumentLifecycleProbe();
        using var context = CreateScopedSavableContext(probe);
        var path = Path.Combine(context.TempDirectory, "opened-scoped.testdoc");
        context.Storage.AddFile(
            path,
            Serialize(TrackedScopedSavableStrategy.TypeId, "成功恢复", "content"));
        var viewModel = context.CreateMainWindowViewModel();

        await viewModel.OpenDocumentByPath(path);

        var document = Assert.Single(GetDocumentDock(context).VisibleDockables!
            .OfType<TrackedScopedSavableDocument>());
        Assert.Equal(Path.GetFullPath(path), context.GetDocumentFilePath(document));
        Assert.Equal("成功恢复", document.Title);
        Assert.Equal(0, probe.CancellationCount);
        Assert.Equal(0, probe.DocumentDisposeCount);
        Assert.Equal(0, probe.DependencyDisposeCount);

        context.Factory.CloseDockable(document);

        Assert.DoesNotContain(document, GetDocumentDock(context).VisibleDockables!);
        Assert.Equal(1, probe.CancellationCount);
        Assert.Equal(1, probe.DocumentDisposeCount);
        Assert.Equal(1, probe.DependencyDisposeCount);
        Assert.True(probe.AllDocumentsObservedClosing);
    }

    [Fact]
    public async Task 非Savable文档恢复返回稳定错误并释放Scope()
    {
        var probe = new DocumentLifecycleProbe();
        using var context = CreateScopedNonSavableContext(probe);
        var path = Path.Combine(context.TempDirectory, "non-savable.testdoc");
        context.Storage.AddFile(
            path,
            Serialize(TrackedScopedNonSavableStrategy.TypeId, "不可恢复", "content"));
        var viewModel = context.CreateMainWindowViewModel();

        await viewModel.OpenDocumentByPath(path);

        Assert.Contains(
            "该文档类型不支持从文件恢复",
            viewModel.DocumentOperationError);
        Assert.Empty(GetDocumentDock(context).VisibleDockables!
            .OfType<TrackedScopedNonSavableDocument>());
        Assert.Equal(1, probe.CreatedCount);
        Assert.Equal(0, probe.LoadCount);
        Assert.Equal(1, probe.CancellationCount);
        Assert.Equal(1, probe.DocumentDisposeCount);
        Assert.Equal(1, probe.DependencyDisposeCount);
        Assert.True(probe.AllDocumentsObservedClosing);
    }

    [Fact]
    public async Task 同一损坏文件连续打开会为每次尝试独立创建并回滚Scope()
    {
        var probe = new DocumentLifecycleProbe { ThrowOnLoad = true };
        using var context = CreateScopedSavableContext(probe);
        var path = Path.Combine(context.TempDirectory, "retry-broken.testdoc");
        context.Storage.AddFile(
            path,
            Serialize(TrackedScopedSavableStrategy.TypeId, "重复损坏", "broken"));
        var viewModel = context.CreateMainWindowViewModel();

        await viewModel.OpenDocumentByPath(path);
        await viewModel.OpenDocumentByPath(path);

        Assert.Equal(2, context.Storage.ReadCount);
        Assert.Equal(2, probe.CreatedCount);
        Assert.Equal(2, probe.LoadCount);
        Assert.Equal(2, probe.CancellationCount);
        Assert.Equal(2, probe.DocumentDisposeCount);
        Assert.Equal(2, probe.DependencyDisposeCount);
        Assert.Empty(GetDocumentDock(context).VisibleDockables!
            .OfType<TrackedScopedSavableDocument>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 恢复发布失败会撤销半提交Dock并释放Scope(
        bool throwAfterAdd)
    {
        var probe = new DocumentLifecycleProbe();
        using var context = CreateScopedSavableContext(probe);
        var path = Path.Combine(context.TempDirectory, "publish-failure.testdoc");
        context.Storage.AddFile(
            path,
            Serialize(TrackedScopedSavableStrategy.TypeId, "发布失败", "content"));
        var viewModel = context.CreateMainWindowViewModel();
        var failingDock = ReplaceDocumentDock(
            context,
            viewModel,
            new ThrowingDocumentDock(throwAfterAdd));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => viewModel.OpenDocumentByPath(path));

        Assert.Empty((failingDock.VisibleDockables ?? [])
            .OfType<TrackedScopedSavableDocument>());
        Assert.Equal(1, probe.CreatedCount);
        Assert.Equal(1, probe.LoadCount);
        Assert.Equal(1, probe.CancellationCount);
        Assert.Equal(1, probe.DocumentDisposeCount);
        Assert.Equal(1, probe.DependencyDisposeCount);
        Assert.True(probe.AllDocumentsObservedClosing);
    }

    [Fact]
    public void 主窗口与插件菜单新建发布失败复用同一回滚入口()
    {
        var probe = new DocumentLifecycleProbe();
        using var context = CreateScopedSavableContext(probe);
        var viewModel = context.CreateMainWindowViewModel();
        var failingDock = ReplaceDocumentDock(
            context,
            viewModel,
            new ThrowingDocumentDock(throwAfterAdd: true));
        var pluginMenu = new PlugGroupMenuViewModel(
            context.Factory,
            context.Provider.GetRequiredService<PluginMenuService>());

        Assert.Throws<InvalidOperationException>(() =>
            viewModel.CreateDocument(TrackedScopedSavableStrategy.TypeId.Value));
        Assert.Throws<InvalidOperationException>(() =>
            pluginMenu.CreateDocument(TrackedScopedSavableStrategy.TypeId.Value));
        Assert.Throws<InvalidOperationException>(() =>
            pluginMenu.CreateDocumentEntry(new DocumentCreationMenuEntry(
                TrackedScopedSavableStrategy.TypeId,
                null,
                "测试入口",
                string.Empty,
                string.Empty,
                "测试")));

        Assert.Empty((failingDock.VisibleDockables ?? [])
            .OfType<TrackedScopedSavableDocument>());
        Assert.Equal(3, probe.CreatedCount);
        Assert.Equal(3, probe.CancellationCount);
        Assert.Equal(3, probe.DocumentDisposeCount);
        Assert.Equal(3, probe.DependencyDisposeCount);
    }

    [Fact]
    public async Task 回滚后释放宿主容器不会再次释放DocumentScope()
    {
        var probe = new DocumentLifecycleProbe { ThrowOnLoad = true };
        var context = CreateScopedSavableContext(probe);
        try
        {
            var path = Path.Combine(context.TempDirectory, "dispose-after-rollback.testdoc");
            context.Storage.AddFile(
                path,
                Serialize(TrackedScopedSavableStrategy.TypeId, "回滚后退出", "broken"));
            var viewModel = context.CreateMainWindowViewModel();
            await viewModel.OpenDocumentByPath(path);

            context.Dispose();

            Assert.Equal(1, probe.CancellationCount);
            Assert.Equal(1, probe.DocumentDisposeCount);
            Assert.Equal(1, probe.DependencyDisposeCount);
        }
        finally
        {
            context.Dispose();
        }
    }

    private static TestHostContext CreateContextWithDocumentStrategy()
    {
        return new TestHostContext(
            documentStrategies: [new TestSavableStrategy()]);
    }

    private static TestHostContext CreateScopedSavableContext(
        DocumentLifecycleProbe probe) =>
        new(configureServices: services =>
        {
            services.AddSingleton(probe);
            services.AddScoped<TrackedScopedDependency>();
            services.AddScoped<TrackedScopedSavableDocument>();
            services.AddSingleton<IDocumentCreationStrategy>(provider =>
                new TrackedScopedSavableStrategy(
                    provider.GetRequiredService<IDocumentScopeFactory>()));
        });

    private static TestHostContext CreateScopedNonSavableContext(
        DocumentLifecycleProbe probe) =>
        new(configureServices: services =>
        {
            services.AddSingleton(probe);
            services.AddScoped<TrackedScopedDependency>();
            services.AddScoped<TrackedScopedNonSavableDocument>();
            services.AddSingleton<IDocumentCreationStrategy>(provider =>
                new TrackedScopedNonSavableStrategy(
                    provider.GetRequiredService<IDocumentScopeFactory>()));
        });

    private static DocumentDock ReplaceDocumentDock(
        TestHostContext context,
        MainWindowViewModel viewModel,
        DocumentDock documentDock)
    {
        var root = context.Factory.CreateWorkspaceLayout(documentDock);
        context.Factory.InitLayout(root);
        viewModel.Layout = root;
        return documentDock;
    }

    private static DocumentDock GetDocumentDock(TestHostContext context) =>
        Assert.IsType<DocumentDock>(
            context.Factory.GetDockable<IDocumentDock>("Files"));

    private static List<TestSavableDocument> GetDocuments(
        TestHostContext context) =>
        GetDocumentDock(context).VisibleDockables!
            .OfType<TestSavableDocument>()
            .ToList();

    private static string Serialize(string title, string content) =>
        Serialize(TestSavableStrategy.TypeId, title, content);

    private static string Serialize(
        DocumentTypeId documentTypeId,
        string title,
        string content) =>
        new DocumentEnvelopeSerializer().Serialize(
            HostExtensionIds.Owner,
            documentTypeId,
            title,
            DateTimeOffset.UtcNow,
            new DocumentContentSnapshot(1, content));

    private sealed class ThrowingDocumentDock(bool throwAfterAdd) : DocumentDock
    {
        public override void AddDocument(IDockable document)
        {
            if (!throwAfterAdd)
            {
                throw new InvalidOperationException("模拟 Dock 在加入前失败。");
            }

            base.AddDocument(document);
            throw new InvalidOperationException("模拟 Dock 在加入后失败。");
        }
    }
}
