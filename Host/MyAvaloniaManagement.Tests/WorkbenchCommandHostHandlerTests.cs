using Microsoft.Extensions.DependencyInjection;
using Dock.Model.Controls;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.Business.Commands.Catalog;
using MyAvaloniaManagement.Business.Commands.Execution;
using MyAvaloniaManagement.Business.Docking;
using MyAvaloniaManagement.Business.Documents;
using MyAvaloniaManagement.Business.Layout;

namespace MyAvaloniaManagement.Tests;

/// <summary>验证 Host 打开/保存 Handler 复用既有用例和唯一 DocumentOperationState。</summary>
public sealed class WorkbenchCommandHostHandlerTests
{
    [Fact]
    public async Task 打开选择取消成功完成且不覆盖已有错误()
    {
        using var context = DocumentTestContext.Create();
        var operationState = context.Provider.GetRequiredService<DocumentOperationState>();
        operationState.Apply(DocumentOperationResult.Failure("已有错误"));
        var executor = context.Provider.GetRequiredService<WorkbenchCommandExecutor>();

        var result = await executor.ExecuteAsync(HostWorkbenchCommandIds.OpenDocument);

        Assert.Equal(WorkbenchCommandExecutionStatus.Succeeded, result.Status);
        Assert.Equal("已有错误", operationState.Error);
        Assert.Empty(context.Storage.Writes);
    }

    [Fact]
    public async Task 无活动Document保存被状态查询禁用且不建立第二套错误状态()
    {
        using var context = DocumentTestContext.Create();
        var operationState = context.Provider.GetRequiredService<DocumentOperationState>();
        operationState.Apply(DocumentOperationResult.Failure("已有错误"));

        var result = await context.Provider
            .GetRequiredService<WorkbenchCommandExecutor>()
            .ExecuteAsync(HostWorkbenchCommandIds.SaveDocument);

        Assert.Equal(WorkbenchCommandExecutionStatus.CommandDisabled, result.Status);
        Assert.Equal("已有错误", operationState.Error);
        Assert.Empty(context.Storage.Writes);
    }

    [Fact]
    public async Task 保存成功通过真实协调器写入并清除错误状态()
    {
        using var context = DocumentTestContext.Create();
        var path = Path.Combine(context.TempDirectory, "command-save.mamdoc");
        context.Storage.SavePath = path;
        var operationState = context.Provider.GetRequiredService<DocumentOperationState>();
        operationState.Apply(DocumentOperationResult.Failure("旧错误"));
        await CreateDocumentAsync(context);
        var document = Assert.Single(GetDocuments(context));
        var model = Assert.IsType<TestSavableDocument>(document.Model);
        model.Content = "Command 保存内容";
        model.IsModified = true;
        GetDocumentDock(context).ActiveDockable = document;

        var result = await context.Provider
            .GetRequiredService<WorkbenchCommandExecutor>()
            .ExecuteAsync(HostWorkbenchCommandIds.SaveDocument);

        Assert.Equal(WorkbenchCommandExecutionStatus.Succeeded, result.Status);
        Assert.False(operationState.HasError);
        Assert.False(model.IsDirty);
        Assert.Contains(context.Storage.Writes, item =>
            DocumentPathIdentity.Equals(item.Path, path));
    }

    [Fact]
    public async Task 保存预期失败仍由DocumentOperationState展示且Executor完成()
    {
        using var context = DocumentTestContext.Create();
        context.Storage.SavePath = Path.Combine(context.TempDirectory, "failed-save.mamdoc");
        context.Storage.WriteException = new IOException("secret-write-path");
        await CreateDocumentAsync(context);
        var document = Assert.Single(GetDocuments(context));
        Assert.IsType<TestSavableDocument>(document.Model).IsModified = true;
        GetDocumentDock(context).ActiveDockable = document;

        var result = await context.Provider
            .GetRequiredService<WorkbenchCommandExecutor>()
            .ExecuteAsync(HostWorkbenchCommandIds.SaveDocument);
        var operationState = context.Provider.GetRequiredService<DocumentOperationState>();

        Assert.Equal(WorkbenchCommandExecutionStatus.Succeeded, result.Status);
        Assert.True(operationState.HasError);
        Assert.DoesNotContain(
            "secret-write-path",
            operationState.Error,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task 调用前取消不会进入真实打开用例()
    {
        using var context = DocumentTestContext.Create();
        var path = Path.Combine(context.TempDirectory, "should-not-open.mamdoc");
        context.Storage.OpenPaths = [path];
        context.Storage.AddFile(path, "not-json");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await context.Provider
            .GetRequiredService<WorkbenchCommandExecutor>()
            .ExecuteAsync(HostWorkbenchCommandIds.OpenDocument, cancellation.Token);

        Assert.Equal(WorkbenchCommandExecutionStatus.Canceled, result.Status);
        Assert.Equal(0, context.Storage.ReadCount);
    }

    private static async Task CreateDocumentAsync(TestHostContext context)
    {
        // 生产主窗口准备布局后才存在 DocumentDock；测试只借该入口建立真实工作区骨架，
        // 打开/保存行为仍通过 G2 Executor 执行。
        _ = context.CreateMainWindowViewModel();
        var result = await context.Provider
            .GetRequiredService<DocumentPersistenceCoordinator>()
            .CreateDocumentAsync(TestDocumentIds.TypeId);
        context.Provider.GetRequiredService<DocumentOperationState>().Apply(result);
    }

    private static List<ManagedDocumentDockable> GetDocuments(TestHostContext context) =>
        GetDocumentDock(context).VisibleDockables!
            .OfType<ManagedDocumentDockable>()
            .Where(item => item.Registration.Descriptor.DocumentTypeId == TestDocumentIds.TypeId)
            .ToList();

    private static DocumentDock GetDocumentDock(TestHostContext context) =>
        Assert.IsType<DocumentDock>(context.Workspace.DockFactory.GetDockable<IDocumentDock>(
            DockLayoutIds.Documents));
}
