using Dock.Model.Mvvm.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.Business.Documents;
using MyAvaloniaManagement.Business.Helpers;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Save;

namespace MyAvaloniaManagement.Tests;

/// <summary>
/// 验证 Document 保存 V1 的公共状态、关闭保护和恢复副本语义。
/// </summary>
public sealed class DocumentPersistenceV1Tests
{
    [Fact]
    public async Task 主文件成功但备份失败_状态已提交并返回稳定警告()
    {
        using var context = new TestHostContext(
            documentStrategies: [new TestSavableStrategy()]);
        var path = Path.Combine(context.TempDirectory, "backup-warning.mamdoc");
        context.Storage.SavePath = path;
        context.Storage.WriteOutcomes.Enqueue(null);
        context.Storage.WriteOutcomes.Enqueue(new IOException("backup failed"));
        var viewModel = context.CreateMainWindowViewModel();
        viewModel.CreateDocument(TestSavableStrategy.TypeId.Value);
        var document = GetDocuments(context).Single();
        document.IsModified = true;
        GetDocumentDock(context).ActiveDockable = document;

        await viewModel.SaveDocument();

        Assert.False(document.IsDirty);
        Assert.Equal(1, document.AcceptChangesCount);
        Assert.Equal(Path.GetFullPath(path), context.GetDocumentFilePath(document));
        Assert.True(viewModel.HasDocumentOperationError);
        Assert.Contains("备份更新失败", viewModel.DocumentOperationError);
        Assert.Single(context.Storage.Writes);
    }

    [Fact]
    public async Task 主文件损坏且备份有效_确认后恢复为强制另存副本()
    {
        using var context = new TestHostContext(
            documentStrategies: [new TestSavableStrategy()]);
        var primary = Path.Combine(context.TempDirectory, "broken.mamdoc");
        var backup = primary + DocumentRecoveryRegistry.BackupSuffix;
        var originalContent = "{broken";
        context.Storage.AddFile(primary, originalContent);
        context.Storage.AddFile(backup, Serialize("最近保存", "safe"));
        context.Interactions.RecoveryChoices.Enqueue(true);
        var viewModel = context.CreateMainWindowViewModel();

        await viewModel.OpenDocumentByPath(primary);

        var recovered = Assert.Single(GetDocuments(context));
        Assert.Equal(string.Empty, context.GetDocumentFilePath(recovered));
        Assert.True(recovered.IsDirty);
        Assert.Contains("已恢复", recovered.Title);
        Assert.Equal("safe", recovered.Content);
        Assert.Equal(originalContent, context.Storage.Files[Path.GetFullPath(primary)]);
        Assert.Single(context.Interactions.RecoveryRequests);

        await viewModel.OpenDocumentByPath(primary);
        Assert.Single(GetDocuments(context));
        Assert.Single(context.Interactions.RecoveryRequests);

        GetDocumentDock(context).ActiveDockable = recovered;
        context.Storage.SavePath = primary;
        await viewModel.SaveDocument();
        Assert.True(recovered.IsDirty);
        Assert.Empty(context.Storage.Writes);
        Assert.Contains("不能覆盖损坏原件", viewModel.DocumentOperationError);

        context.Storage.SavePath = backup;
        await viewModel.SaveDocument();
        Assert.True(recovered.IsDirty);
        Assert.Empty(context.Storage.Writes);
        Assert.Contains("不能覆盖损坏原件", viewModel.DocumentOperationError);

        var newPath = Path.Combine(context.TempDirectory, "recovered-copy.mamdoc");
        context.Storage.SavePath = newPath;
        await viewModel.SaveDocument();

        Assert.False(recovered.IsDirty);
        Assert.Equal(Path.GetFullPath(newPath), context.GetDocumentFilePath(recovered));
        Assert.Equal(originalContent, context.Storage.Files[Path.GetFullPath(primary)]);
        Assert.Contains(context.Storage.Writes, item =>
            item.Path.Equals(Path.GetFullPath(newPath), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task 主文件和备份均损坏_不会发布半恢复Document()
    {
        using var context = new TestHostContext(
            documentStrategies: [new TestSavableStrategy()]);
        var primary = Path.Combine(context.TempDirectory, "both-broken.mamdoc");
        context.Storage.AddFile(primary, "{primary-broken");
        context.Storage.AddFile(
            primary + DocumentRecoveryRegistry.BackupSuffix,
            "{backup-broken");
        context.Interactions.RecoveryChoices.Enqueue(true);
        var viewModel = context.CreateMainWindowViewModel();

        await viewModel.OpenDocumentByPath(primary);

        Assert.Empty(GetDocuments(context));
        Assert.True(viewModel.HasDocumentOperationError);
        Assert.Contains("主文件及恢复备份均已损坏", viewModel.DocumentOperationError);
        Assert.Empty(context.Interactions.RecoveryRequests);
    }

    [Fact]
    public async Task 脏Document关闭被取消时不释放Scope_放弃后只释放一次()
    {
        var probe = new DocumentLifecycleProbe();
        using var context = CreateScopedContext(probe);
        var viewModel = context.CreateMainWindowViewModel();
        viewModel.CreateDocument(TrackedScopedSavableStrategy.TypeId.Value);
        var document = Assert.Single(GetDocumentDock(context).VisibleDockables!
            .OfType<TrackedScopedSavableDocument>());
        document.IsModified = true;
        context.Interactions.CloseChoices.Enqueue(DocumentCloseChoice.Cancel);

        context.Factory.CloseDockable(document);

        Assert.Contains(document, GetDocumentDock(context).VisibleDockables!);
        Assert.Equal(0, probe.CancellationCount);
        Assert.Equal(0, probe.DocumentDisposeCount);

        context.Interactions.CloseChoices.Enqueue(DocumentCloseChoice.Discard);
        context.Factory.CloseDockable(document);

        Assert.DoesNotContain(document, GetDocumentDock(context).VisibleDockables!);
        Assert.Equal(1, probe.CancellationCount);
        Assert.Equal(1, probe.DocumentDisposeCount);
    }

    [Fact]
    public async Task 对话框期间重复关闭只创建一个请求()
    {
        using var context = new TestHostContext(
            documentStrategies: [new TestSavableStrategy()]);
        var viewModel = context.CreateMainWindowViewModel();
        viewModel.CreateDocument(TestSavableStrategy.TypeId.Value);
        var document = GetDocuments(context).Single();
        document.IsModified = true;
        var pending = new TaskCompletionSource<DocumentCloseChoice>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        context.Interactions.PendingCloseChoice = pending;

        context.Factory.CloseDockable(document);
        context.Factory.CloseDockable(document);

        Assert.Single(context.Interactions.CloseRequests);
        Assert.Contains(document, GetDocumentDock(context).VisibleDockables!);

        pending.SetResult(DocumentCloseChoice.Discard);
        await Task.Delay(20);

        Assert.DoesNotContain(document, GetDocumentDock(context).VisibleDockables!);
    }

    [Fact]
    public void 关闭前选择保存但取消路径_保持Document和生命周期()
    {
        using var context = new TestHostContext(
            documentStrategies: [new TestSavableStrategy()]);
        var viewModel = context.CreateMainWindowViewModel();
        viewModel.CreateDocument(TestSavableStrategy.TypeId.Value);
        var document = GetDocuments(context).Single();
        document.IsModified = true;
        context.Interactions.CloseChoices.Enqueue(DocumentCloseChoice.Save);

        context.Factory.CloseDockable(document);

        Assert.Contains(document, GetDocumentDock(context).VisibleDockables!);
        Assert.True(document.IsDirty);
        Assert.Empty(context.Storage.Writes);
    }

    [Fact]
    public async Task 窗口退出对全部脏Document只显示一个汇总请求()
    {
        using var context = new TestHostContext(
            documentStrategies: [new TestSavableStrategy()]);
        var viewModel = context.CreateMainWindowViewModel();
        viewModel.CreateDocument(TestSavableStrategy.TypeId.Value);
        viewModel.CreateDocument(TestSavableStrategy.TypeId.Value);
        foreach (var document in GetDocuments(context))
        {
            document.IsModified = true;
        }
        context.Interactions.CloseChoices.Enqueue(DocumentCloseChoice.Discard);

        var approved = await viewModel.ConfirmWindowCloseAsync();

        Assert.True(approved);
        var request = Assert.Single(context.Interactions.CloseRequests);
        Assert.True(request.IsExit);
        Assert.Equal(2, request.Names.Count);
    }

    [Fact]
    public async Task 窗口保存全部在首个失败处停止_已保存项保持干净()
    {
        using var context = new TestHostContext(
            documentStrategies: [new TestSavableStrategy()]);
        var viewModel = context.CreateMainWindowViewModel();
        viewModel.CreateDocument(TestSavableStrategy.TypeId.Value);
        viewModel.CreateDocument(TestSavableStrategy.TypeId.Value);
        var documents = GetDocuments(context);
        context.SetDocumentFilePath(
            documents[0],
            Path.Combine(context.TempDirectory, "first.mamdoc"));
        context.SetDocumentFilePath(
            documents[1],
            Path.Combine(context.TempDirectory, "second.mamdoc"));
        documents.ForEach(document => document.IsModified = true);
        context.Storage.WriteOutcomes.Enqueue(null);
        context.Storage.WriteOutcomes.Enqueue(null);
        context.Storage.WriteOutcomes.Enqueue(new IOException("second failed"));
        context.Interactions.CloseChoices.Enqueue(DocumentCloseChoice.Save);

        var approved = await viewModel.ConfirmWindowCloseAsync();

        Assert.False(approved);
        Assert.False(documents[0].IsDirty);
        Assert.True(documents[1].IsDirty);
        Assert.Contains(context.Interactions.Errors, message =>
            message.Contains("保存文档失败", StringComparison.Ordinal));
    }

    [Fact]
    public void 可保存Document缺少公共状态契约时拒绝发布()
    {
        using var context = new TestHostContext(
            documentStrategies: [new MissingSaveStateStrategy()]);
        _ = context.CreateMainWindowViewModel();

        var exception = Assert.Throws<HostCompositionException>(() =>
            context.Factory.CreateAndPublishDocument(
                new DocumentCreationParams(MissingSaveStateStrategy.TypeId)));

        Assert.Equal(
            "DOCUMENT_SAVE_STATE_MISSING",
            Assert.Single(exception.Diagnostics).Code);
        Assert.Empty(GetDocumentDock(context).VisibleDockables!
            .OfType<MissingSaveStateDocument>());
    }

    private static TestHostContext CreateScopedContext(DocumentLifecycleProbe probe) =>
        new(configureServices: services =>
        {
            services.AddSingleton(probe);
            services.AddScoped<TrackedScopedDependency>();
            services.AddScoped<TrackedScopedSavableDocument>();
            services.AddSingleton<IDocumentCreationStrategy>(provider =>
                new TrackedScopedSavableStrategy(
                    provider.GetRequiredService<IDocumentScopeFactory>()));
        });

    private static DocumentDock GetDocumentDock(TestHostContext context) =>
        Assert.IsType<DocumentDock>(
            context.Factory.GetDockable<Dock.Model.Controls.IDocumentDock>("Files"));

    private static List<TestSavableDocument> GetDocuments(TestHostContext context) =>
        GetDocumentDock(context).VisibleDockables!
            .OfType<TestSavableDocument>()
            .ToList();

    private static string Serialize(string title, string content) =>
        new DocumentEnvelopeSerializer().Serialize(
            HostExtensionIds.Owner,
            TestSavableStrategy.TypeId,
            title,
            DateTimeOffset.UtcNow,
            new DocumentContentSnapshot(1, content));

    private sealed class MissingSaveStateDocument : Document, ISavableDocument
    {
        public DocumentContentSnapshot CreateContentSnapshot() => throw new NotSupportedException();
        public void RestoreContent(DocumentContentSnapshot snapshot) => throw new NotSupportedException();
    }

    private sealed class MissingSaveStateStrategy : IDocumentCreationStrategy
    {
        internal static readonly DocumentTypeId TypeId =
            new("myavalonia.host.document.missing-save-state");

        public Document CreateDocument(DocumentCreationParams @params) => new MissingSaveStateDocument();

        public DocumentMetadata GetMetadata() =>
            new(TypeId, "缺少状态契约") { MenuCategory = "测试" };
    }
}
