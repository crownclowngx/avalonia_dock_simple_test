using System.Text.Json;
using Dock.Model.Controls;
using Dock.Model.Mvvm.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Docking;
using MyAvaloniaManagement.Business.Documents;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.Tests;

/// <summary>验证 V2 新建、恢复、保存、关闭和 Scope 回滚共用一条生产链。</summary>
public sealed class DocumentPersistenceV2Tests
{
    [Fact]
    public async Task 新建初始化后发布并把合法CreationIntent交给模型()
    {
        using var context = DocumentV2TestContext.Create();
        var viewModel = context.CreateMainWindowViewModel();

        await context.Provider.GetRequiredService<DocumentPersistenceCoordinator>()
            .CreateDocumentAsync(TestDocumentIds.TypeId, new CreationIntentId("sample-intent"));

        var adapter = Assert.Single(GetDocuments(context));
        var model = Assert.IsType<TestSavableDocument>(adapter.Model);
        Assert.Equal("未命名", model.Title);
        Assert.Equal(
            new CreationIntentId("sample-intent"),
            Assert.Single(context.Provider.GetRequiredService<DocumentV2TestProbe>()
                .ActivationContexts).CreationIntentId);
    }

    [Fact]
    public async Task 异步初始化完成之前不会发布标签()
    {
        using var context = DocumentV2TestContext.Create();
        _ = context.CreateMainWindowViewModel();
        var probe = context.Provider.GetRequiredService<DocumentV2TestProbe>();
        probe.InitializeBlocker = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var creation = context.Provider.GetRequiredService<DocumentPersistenceCoordinator>()
            .CreateDocumentAsync(TestDocumentIds.TypeId);
        await Task.Yield();
        Assert.Empty(GetDocuments(context));

        probe.InitializeBlocker.SetResult();
        var result = await creation;
        Assert.False(result.ShouldUpdateError && !string.IsNullOrEmpty(result.Error));
        Assert.Single(GetDocuments(context));
    }

    [Fact]
    public async Task 非法CreationIntent不发布并释放暂存Scope()
    {
        using var context = DocumentV2TestContext.Create();
        _ = context.CreateMainWindowViewModel();

        var result = await context.Provider.GetRequiredService<DocumentPersistenceCoordinator>()
            .CreateDocumentAsync(TestDocumentIds.TypeId, new CreationIntentId("unknown-intent"));

        Assert.True(result.ShouldUpdateError);
        Assert.Empty(GetDocuments(context));
    }

    [Fact]
    public async Task 初始化异常会取消ClosingToken并释放暂存模型()
    {
        using var context = DocumentV2TestContext.Create();
        _ = context.CreateMainWindowViewModel();
        var probe = context.Provider.GetRequiredService<DocumentV2TestProbe>();
        probe.InitializeException = new PluginBoundaryException("secret-initialize");

        var result = await context.Provider.GetRequiredService<DocumentPersistenceCoordinator>()
            .CreateDocumentAsync(TestDocumentIds.TypeId);

        Assert.True(result.ShouldUpdateError);
        Assert.Empty(GetDocuments(context));
        Assert.Equal(1, probe.DisposeCount);
        Assert.True(probe.ClosingObservedDuringDispose);
        Assert.DoesNotContain("secret-initialize", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 保存写入嵌套Json并在主文件成功后提交路径与脏状态()
    {
        using var context = DocumentV2TestContext.Create();
        var path = Path.Combine(context.TempDirectory, "saved.mamdoc");
        context.Storage.SavePath = path;
        var viewModel = context.CreateMainWindowViewModel();
        await viewModel.CreateDocument(TestDocumentIds.TypeId.Value);
        var adapter = Assert.Single(GetDocuments(context));
        var model = Assert.IsType<TestSavableDocument>(adapter.Model);
        model.Content = "保存内容";
        model.IsModified = true;
        GetDocumentDock(context).ActiveDockable = adapter;

        await viewModel.SaveDocument();

        Assert.False(model.IsDirty);
        Assert.Equal(1, model.AcceptChangesCount);
        Assert.Equal(Path.GetFullPath(path), context.GetDocumentFilePath(adapter));
        var primary = Assert.Single(context.Storage.Writes, item =>
            DocumentPathIdentity.Equals(item.Path, path));
        using var json = JsonDocument.Parse(primary.Content);
        Assert.Equal(2, json.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(
            "保存内容",
            json.RootElement.GetProperty("content").GetProperty("payload").GetString());
    }

    [Fact]
    public async Task 主文件失败不提交_备份失败只产生已保存警告()
    {
        using var context = DocumentV2TestContext.Create();
        var path = Path.Combine(context.TempDirectory, "warning.mamdoc");
        context.Storage.SavePath = path;
        var viewModel = context.CreateMainWindowViewModel();
        await viewModel.CreateDocument(TestDocumentIds.TypeId.Value);
        var adapter = Assert.Single(GetDocuments(context));
        var model = Assert.IsType<TestSavableDocument>(adapter.Model);
        model.IsModified = true;
        GetDocumentDock(context).ActiveDockable = adapter;
        context.Storage.WriteOutcomes.Enqueue(new IOException("主文件失败"));

        await viewModel.SaveDocument();

        Assert.True(model.IsDirty);
        Assert.Equal(0, model.AcceptChangesCount);
        Assert.True(viewModel.HasDocumentOperationError);

        context.Storage.WriteOutcomes.Enqueue(null);
        context.Storage.WriteOutcomes.Enqueue(new IOException("备份失败"));
        await viewModel.SaveDocument();
        Assert.False(model.IsDirty);
        Assert.Contains("已保存", viewModel.DocumentOperationError);
    }

    [Fact]
    public async Task 捕获自定义异常或Null内容都不写文件也不提交路径()
    {
        using var context = DocumentV2TestContext.Create();
        var path = Path.Combine(context.TempDirectory, "capture-failure.mamdoc");
        context.Storage.SavePath = path;
        var viewModel = context.CreateMainWindowViewModel();
        await viewModel.CreateDocument(TestDocumentIds.TypeId.Value);
        var adapter = Assert.Single(GetDocuments(context));
        GetDocumentDock(context).ActiveDockable = adapter;
        var probe = context.Provider.GetRequiredService<DocumentV2TestProbe>();

        probe.CaptureException = new PluginBoundaryException("secret-capture");
        await viewModel.SaveDocument();
        Assert.Empty(context.Storage.Writes);
        Assert.Equal(string.Empty, context.GetDocumentFilePath(adapter));
        Assert.DoesNotContain("secret-capture", viewModel.DocumentOperationError, StringComparison.Ordinal);

        probe.CaptureException = null;
        probe.ReturnNullContent = true;
        await viewModel.SaveDocument();
        Assert.Empty(context.Storage.Writes);
        Assert.Equal(string.Empty, context.GetDocumentFilePath(adapter));
    }

    [Fact]
    public async Task 保存取消_非持久化和缺失Host状态都返回稳定结果()
    {
        using var context = DocumentV2TestContext.Create();
        var viewModel = context.CreateMainWindowViewModel();
        await viewModel.CreateDocument(TestDocumentIds.TypeId.Value);
        var adapter = Assert.Single(GetDocuments(context));
        GetDocumentDock(context).ActiveDockable = adapter;

        await viewModel.SaveDocument();
        Assert.Empty(context.Storage.Writes);
        Assert.False(viewModel.HasDocumentOperationError);

        var saveService = context.Provider.GetRequiredService<DocumentSaveService>();
        Assert.True(context.PersistenceStates.Remove(adapter));
        var missingState = await saveService.SaveAsync(adapter);
        Assert.Equal(DocumentSaveStatus.Failed, missingState.Status);

        var welcome = Assert.IsType<ManagedDocumentDockable>(GetDocumentDock(context).VisibleDockables![0]);
        var nonPersistable = await saveService.SaveAsync(welcome);
        Assert.Equal(DocumentSaveStatus.NotPersistable, nonPersistable.Status);
    }

    [Fact]
    public async Task 空文件名使用Host标题回退且StateStore拒绝非法登记()
    {
        using var context = DocumentV2TestContext.Create();
        var viewModel = context.CreateMainWindowViewModel();
        await viewModel.CreateDocument(TestDocumentIds.TypeId.Value);
        var adapter = Assert.Single(GetDocuments(context));
        var store = context.PersistenceStates;
        Assert.Throws<InvalidOperationException>(() =>
            store.Register(adapter, "重复"));
        var welcome = Assert.IsType<ManagedDocumentDockable>(GetDocumentDock(context).VisibleDockables![0]);
        Assert.Throws<InvalidOperationException>(() =>
            store.Register(welcome, "欢迎"));

        context.Storage.SavePath = Path.GetPathRoot(context.TempDirectory)!;
        GetDocumentDock(context).ActiveDockable = adapter;
        await viewModel.SaveDocument();
        Assert.Equal("测试文档", adapter.HostTitle);
    }

    [Fact]
    public async Task AcceptChanges异常不回滚主文件且关闭保存可继续()
    {
        using var context = DocumentV2TestContext.Create();
        var path = Path.Combine(context.TempDirectory, "accept-warning.mamdoc");
        context.Storage.SavePath = path;
        var viewModel = context.CreateMainWindowViewModel();
        await viewModel.CreateDocument(TestDocumentIds.TypeId.Value);
        var adapter = Assert.Single(GetDocuments(context));
        Assert.IsType<TestSavableDocument>(adapter.Model).IsModified = true;
        context.Provider.GetRequiredService<DocumentV2TestProbe>().AcceptChangesException =
            new PluginBoundaryException("secret-accept");
        context.Interactions.CloseChoices.Enqueue(DocumentCloseChoice.Save);

        Assert.True(await viewModel.ConfirmWindowCloseAsync());
        Assert.Equal(Path.GetFullPath(path), context.GetDocumentFilePath(adapter));
        Assert.Equal(2, context.Storage.Writes.Count);
        Assert.Contains(context.Interactions.Errors, message => message.Contains("已保存", StringComparison.Ordinal));
        Assert.DoesNotContain(
            context.Interactions.Errors,
            message => message.Contains("secret-accept", StringComparison.Ordinal));
    }

    [Fact]
    public async Task 打开V2恢复内容且并发同路径只发布一个Scope()
    {
        using var context = DocumentV2TestContext.Create();
        var path = Path.Combine(context.TempDirectory, "opened.mamdoc");
        context.Storage.AddFile(path, Serialize("打开标题", "恢复正文"));
        var viewModel = context.CreateMainWindowViewModel();

        await Task.WhenAll(
            viewModel.OpenDocumentByPath(path),
            viewModel.OpenDocumentByPath(path));

        var adapter = Assert.Single(GetDocuments(context));
        Assert.Equal("恢复正文", Assert.IsType<TestSavableDocument>(adapter.Model).Content);
        Assert.Equal(Path.GetFullPath(path), context.GetDocumentFilePath(adapter));
    }

    [Fact]
    public async Task V1文件严格拒绝且不会创建或写回()
    {
        using var context = DocumentV2TestContext.Create();
        var path = Path.Combine(context.TempDirectory, "legacy.mamdoc");
        context.Storage.AddFile(path, "{\"schemaVersion\":1,\"payload\":\"legacy\"}");
        var viewModel = context.CreateMainWindowViewModel();

        await viewModel.OpenDocumentByPath(path);

        Assert.Empty(GetDocuments(context));
        Assert.Empty(context.Storage.Writes);
        Assert.Contains("不受支持或已损坏", viewModel.DocumentOperationError);
    }

    [Fact]
    public async Task 所有者或Registry持久化能力不匹配都在初始化前拒绝()
    {
        using var ownerContext = DocumentV2TestContext.Create();
        var ownerPath = Path.Combine(ownerContext.TempDirectory, "wrong-owner.mamdoc");
        ownerContext.Storage.AddFile(
            ownerPath,
            Serialize("所有者", "正文").Replace(
                "myavalonia.host\"",
                "myavalonia.plugin.other\"",
                StringComparison.Ordinal));
        var ownerViewModel = ownerContext.CreateMainWindowViewModel();
        await ownerViewModel.OpenDocumentByPath(ownerPath);
        Assert.Empty(GetDocuments(ownerContext));
        Assert.Empty(ownerContext.Provider.GetRequiredService<DocumentV2TestProbe>().ActivationContexts);

        using var capabilityContext = DocumentV2TestContext.Create(persistable: false);
        var capabilityPath = Path.Combine(capabilityContext.TempDirectory, "not-persistable.mamdoc");
        capabilityContext.Storage.AddFile(capabilityPath, Serialize("能力", "正文"));
        var capabilityViewModel = capabilityContext.CreateMainWindowViewModel();
        await capabilityViewModel.OpenDocumentByPath(capabilityPath);
        Assert.Empty(GetDocuments(capabilityContext));
        Assert.Empty(capabilityContext.Provider.GetRequiredService<DocumentV2TestProbe>().ActivationContexts);
    }

    [Fact]
    public async Task 批量打开隔离坏文件并继续发布后续合法文件()
    {
        using var context = DocumentV2TestContext.Create();
        var bad = Path.Combine(context.TempDirectory, "a-bad.mamdoc");
        var good = Path.Combine(context.TempDirectory, "b-good.mamdoc");
        context.Storage.AddFile(bad, "{broken");
        context.Storage.AddFile(good, Serialize("合法", "继续打开"));
        context.Storage.OpenPaths = [string.Empty, Path.Combine(context.TempDirectory, "missing.mamdoc"), bad, good];
        var viewModel = context.CreateMainWindowViewModel();

        await viewModel.OpenDocument();

        var adapter = Assert.Single(GetDocuments(context));
        Assert.Equal("继续打开", Assert.IsType<TestSavableDocument>(adapter.Model).Content);
    }

    [Fact]
    public async Task 坏备份或备份初始化失败都不询问恢复并释放暂存Scope()
    {
        using var badContext = DocumentV2TestContext.Create();
        var badPrimary = Path.Combine(badContext.TempDirectory, "bad-primary.mamdoc");
        badContext.Storage.AddFile(badPrimary, "{broken");
        badContext.Storage.AddFile(badPrimary + DocumentRecoveryRegistry.BackupSuffix, "{also-broken");
        var badViewModel = badContext.CreateMainWindowViewModel();
        await badViewModel.OpenDocumentByPath(badPrimary);
        Assert.Empty(GetDocuments(badContext));
        Assert.Empty(badContext.Interactions.RecoveryRequests);

        using var initContext = DocumentV2TestContext.Create();
        var initPrimary = Path.Combine(initContext.TempDirectory, "init-primary.mamdoc");
        initContext.Storage.AddFile(initPrimary, "{broken");
        initContext.Storage.AddFile(
            initPrimary + DocumentRecoveryRegistry.BackupSuffix,
            Serialize("备份", "初始化失败"));
        initContext.Provider.GetRequiredService<DocumentV2TestProbe>().InitializeException =
            new InvalidOperationException("init-secret");
        var initViewModel = initContext.CreateMainWindowViewModel();
        await initViewModel.OpenDocumentByPath(initPrimary);
        Assert.Empty(GetDocuments(initContext));
        Assert.Empty(initContext.Interactions.RecoveryRequests);
        Assert.Equal(1, initContext.Provider.GetRequiredService<DocumentV2TestProbe>().DisposeCount);
    }

    [Fact]
    public async Task 不存在路径和无活动Document不会改变现有提示()
    {
        using var context = DocumentV2TestContext.Create();
        var viewModel = context.CreateMainWindowViewModel();
        await viewModel.OpenDocumentByPath(Path.Combine(context.TempDirectory, "missing.mamdoc"));
        Assert.False(viewModel.HasDocumentOperationError);

        GetDocumentDock(context).ActiveDockable = null;
        await viewModel.SaveDocument();
        Assert.False(viewModel.HasDocumentOperationError);
        Assert.Empty(context.Storage.Writes);
    }

    [Fact]
    public async Task 主文件损坏时恢复副本即使模型干净也强制另存并参与关闭确认()
    {
        using var context = DocumentV2TestContext.Create();
        var primary = Path.Combine(context.TempDirectory, "broken.mamdoc");
        context.Storage.AddFile(primary, "{broken");
        context.Storage.AddFile(
            primary + DocumentRecoveryRegistry.BackupSuffix,
            Serialize("备份", "安全内容"));
        context.Interactions.RecoveryChoices.Enqueue(true);
        var viewModel = context.CreateMainWindowViewModel();

        await viewModel.OpenDocumentByPath(primary);

        var recovered = Assert.Single(GetDocuments(context));
        Assert.False(Assert.IsType<TestSavableDocument>(recovered.Model).IsDirty);
        Assert.True(viewModel.HasDirtyDocuments());
        context.Interactions.CloseChoices.Enqueue(DocumentCloseChoice.Cancel);
        Assert.False(await viewModel.ConfirmWindowCloseAsync());

        GetDocumentDock(context).ActiveDockable = recovered;
        context.Storage.SavePath = primary;
        await viewModel.SaveDocument();
        Assert.Contains("不能覆盖损坏原件", viewModel.DocumentOperationError);
    }

    [Fact]
    public async Task 拒绝恢复立即释放暂存Scope且不修改输入文件()
    {
        using var context = DocumentV2TestContext.Create();
        var primary = Path.Combine(context.TempDirectory, "declined.mamdoc");
        var backup = primary + DocumentRecoveryRegistry.BackupSuffix;
        context.Storage.AddFile(primary, "{broken");
        context.Storage.AddFile(backup, Serialize("备份", "不采用"));
        var originalPrimary = context.Storage.Files[Path.GetFullPath(primary)];
        var originalBackup = context.Storage.Files[Path.GetFullPath(backup)];
        context.Interactions.RecoveryChoices.Enqueue(false);
        var viewModel = context.CreateMainWindowViewModel();

        await viewModel.OpenDocumentByPath(primary);

        Assert.Empty(GetDocuments(context));
        var probe = context.Provider.GetRequiredService<DocumentV2TestProbe>();
        Assert.Equal(1, probe.DisposeCount);
        Assert.True(probe.ClosingObservedDuringDispose);
        Assert.Equal(originalPrimary, context.Storage.Files[Path.GetFullPath(primary)]);
        Assert.Equal(originalBackup, context.Storage.Files[Path.GetFullPath(backup)]);
        Assert.Empty(context.Storage.Writes);
    }

    private sealed class PluginBoundaryException(string message) : Exception(message);

    private static string Serialize(string title, string content)
    {
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(content));
        return new DocumentEnvelopeSerializer().Serialize(
            MyAvaloniaManagement.Business.Constants.HostExtensionIds.V2Owner,
            TestDocumentIds.TypeId,
            title,
            new DateTimeOffset(2026, 8, 21, 1, 2, 3, TimeSpan.Zero),
            new DocumentContent(1, payload.RootElement));
    }

    private static List<ManagedDocumentDockable> GetDocuments(TestHostContext context) =>
        GetDocumentDock(context).VisibleDockables!
            .OfType<ManagedDocumentDockable>()
            .Where(item => item.Registration.Descriptor.DocumentTypeId == TestDocumentIds.TypeId)
            .ToList();

    private static DocumentDock GetDocumentDock(TestHostContext context) =>
        Assert.IsType<DocumentDock>(context.Factory.GetDockable<IDocumentDock>("Files"));
}
