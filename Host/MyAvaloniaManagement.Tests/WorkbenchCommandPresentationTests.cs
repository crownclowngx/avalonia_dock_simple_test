using Avalonia.Threading;
using Dock.Model.Controls;
using Dock.Model.Mvvm.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Commands.Catalog;
using MyAvaloniaManagement.Business.Commands.Execution;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Documents;
using MyAvaloniaManagement.Business.Presentation.Commands;

namespace MyAvaloniaManagement.Tests;

/// <summary>验证 G4 Host-only Presentation 的身份、状态、执行重查与订阅所有权。</summary>
public sealed class WorkbenchCommandPresentationTests
{
    [Fact]
    public void 组合根只创建一份打开保存展示且主窗口共享同一实例()
    {
        using var context = DocumentTestContext.Create(
            useProductionWorkbenchPresentation: true);
        var firstViewModel = context.CreateMainWindowViewModel();
        var secondViewModel = context.CreateMainWindowViewModel();
        var presentation = context.Provider
            .GetRequiredService<HostWorkbenchCommandPresentation>();

        Assert.Same(presentation, firstViewModel.WorkbenchCommands);
        Assert.Same(firstViewModel.WorkbenchCommands, secondViewModel.WorkbenchCommands);
        Assert.Same(presentation.Open, firstViewModel.WorkbenchCommands.Open);
        Assert.Same(presentation.Save, firstViewModel.WorkbenchCommands.Save);
        Assert.Equal(
            HostWorkbenchCommandIds.OpenDocument,
            Assert.IsType<WorkbenchPresentationCommand>(presentation.Open).CommandId);
        Assert.Equal(
            HostWorkbenchCommandIds.SaveDocument,
            Assert.IsType<WorkbenchPresentationCommand>(presentation.Save).CommandId);
    }

    [Fact]
    public async Task 保存展示覆盖不可持久化与持久化目标且执行时重新拒绝旧状态()
    {
        using (var nonPersistableContext = DocumentTestContext.Create(
                   persistable: false,
                   useProductionWorkbenchPresentation: true))
        {
            _ = nonPersistableContext.CreateMainWindowViewModel();
            var nonPersistableSave = Assert.IsType<WorkbenchPresentationCommand>(
                nonPersistableContext.Provider
                    .GetRequiredService<HostWorkbenchCommandPresentation>().Save);
            await CreateDocumentAsync(nonPersistableContext);
            var nonPersistableDock = GetDocumentDock(nonPersistableContext);
            nonPersistableDock.ActiveDockable = nonPersistableDock.VisibleDockables!
                .OfType<ManagedDocumentDockable>()
                .Single(item => item.Registration.Descriptor.DocumentTypeId == TestDocumentIds.TypeId);

            Assert.False(nonPersistableSave.CanExecute(null));
            var disabled = await nonPersistableSave.ExecuteAsync();
            Assert.Equal(WorkbenchCommandExecutionStatus.CommandDisabled, disabled.Status);
            Assert.Empty(nonPersistableContext.Storage.Writes);
        }

        using var context = DocumentTestContext.Create(
            useProductionWorkbenchPresentation: true);
        _ = context.CreateMainWindowViewModel();
        var save = Assert.IsType<WorkbenchPresentationCommand>(context.Provider
            .GetRequiredService<HostWorkbenchCommandPresentation>().Save);
        var dock = GetDocumentDock(context);
        Assert.False(save.CanExecute(null));

        await CreateDocumentAsync(context);
        var document = dock.VisibleDockables!
            .OfType<ManagedDocumentDockable>()
            .Single(item => item.Registration.Descriptor.DocumentTypeId == TestDocumentIds.TypeId);
        dock.ActiveDockable = document;
        Assert.True(save.CanExecute(null));

        // 菜单已经观察到 Enabled 后再清空活动目标；适配器不得相信几毫秒前的 UI 状态。
        dock.ActiveDockable = null;
        var result = await save.ExecuteAsync();

        Assert.Equal(WorkbenchCommandExecutionStatus.CommandDisabled, result.Status);
        Assert.Empty(context.Storage.Writes);
        Assert.False(save.CanExecute(null));
    }

    [Fact]
    public async Task 释放适配器后禁用且迟到状态变化不再通知()
    {
        using var context = DocumentTestContext.Create(
            useProductionWorkbenchPresentation: true);
        _ = context.CreateMainWindowViewModel();
        var command = new WorkbenchPresentationCommand(
            HostWorkbenchCommandIds.SaveDocument,
            context.Provider.GetRequiredService<Business.Commands.State.WorkbenchCommandStateQuery>(),
            context.Provider.GetRequiredService<WorkbenchCommandExecutor>(),
            Dispatcher.UIThread);
        var refreshes = 0;
        command.CanExecuteChanged += (_, _) => refreshes++;

        command.Dispose();
        command.Dispose();
        await CreateDocumentAsync(context);
        GetDocumentDock(context).ActiveDockable = GetDocumentDock(context).VisibleDockables!
            .OfType<ManagedDocumentDockable>()
            .Single(item => item.Registration.Descriptor.DocumentTypeId == TestDocumentIds.TypeId);
        var result = await command.ExecuteAsync();

        Assert.False(command.CanExecute(null));
        Assert.Equal(WorkbenchCommandExecutionStatus.CommandDisabled, result.Status);
        Assert.Equal(0, refreshes);
    }

    private static async Task CreateDocumentAsync(TestHostContext context)
    {
        var result = await context.Provider
            .GetRequiredService<DocumentPersistenceCoordinator>()
            .CreateDocumentAsync(TestDocumentIds.TypeId);
        context.Provider.GetRequiredService<DocumentOperationState>().Apply(result);
    }

    private static DocumentDock GetDocumentDock(TestHostContext context) =>
        Assert.IsType<DocumentDock>(context.Workspace.DockFactory.GetDockable<IDocumentDock>(
            Business.Layout.DockLayoutIds.Documents));
}
