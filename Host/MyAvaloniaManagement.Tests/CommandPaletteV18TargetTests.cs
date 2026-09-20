using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Commands.Context;
using MyAvaloniaManagement.Business.Commands.Execution;
using MyAvaloniaManagement.Business.Commands.State;
using MyAvaloniaManagement.Business.Presentation.Commands;
using Avalonia.Threading;

namespace MyAvaloniaManagement.Tests;

/// <summary>用两个真实 scoped Document 验证展示约束穿过共享适配器；不另建测试执行器。</summary>
public sealed class CommandPaletteV18TargetTests
{
    [Fact]
    public async Task 旧页面约束拒绝且同一共享命令的普通调用仍执行当前页()
    {
        using var context = WorkbenchCommandG3TestContext.Create();
        var first = await WorkbenchCommandG3TestContext.CreateDocumentAsync(context, "第一页");
        var second = await WorkbenchCommandG3TestContext.CreateDocumentAsync(context, "第二页");
        context.Workspace.DockFactory.SetActiveDockable(first);
        var store = context.Provider.GetRequiredService<WorkbenchContextStore>();
        var expectation = new WorkbenchCommandTargetExpectation(first.PageId, store.Capture().Snapshot.Revision);
        using var command = new WorkbenchPresentationCommand(WorkbenchCommandG3TestContext.Command,
            context.Provider.GetRequiredService<WorkbenchCommandStateQuery>(),
            context.Provider.GetRequiredService<WorkbenchCommandExecutor>(), Dispatcher.UIThread);
        Assert.True(command.CanExecuteForTarget(expectation));
        context.Workspace.DockFactory.SetActiveDockable(second);
        Assert.False(command.CanExecuteForTarget(expectation));
        Assert.Equal(WorkbenchCommandExecutionStatus.TargetUnavailable, (await command.ExecuteAsync(expectedTarget: expectation)).Status);
        Assert.Equal(0, Assert.IsType<WorkbenchCommandG3Document>(first.Model).ExecutionCount);
        Assert.Equal(0, Assert.IsType<WorkbenchCommandG3Document>(second.Model).ExecutionCount);
        Assert.Equal(WorkbenchCommandExecutionStatus.Succeeded, (await command.ExecuteAsync()).Status);
        Assert.Equal(1, Assert.IsType<WorkbenchCommandG3Document>(second.Model).ExecutionCount);
        // 返回同一页面也已经是新上下文；不能让旧会话的 PageId 恰好相同绕过代次检查。
        context.Workspace.DockFactory.SetActiveDockable(first);
        Assert.Equal(WorkbenchCommandExecutionStatus.TargetUnavailable, (await command.ExecuteAsync(expectedTarget: expectation)).Status);
        var fresh = new WorkbenchCommandTargetExpectation(first.PageId, store.Capture().Snapshot.Revision);
        Assert.Equal(WorkbenchCommandExecutionStatus.Succeeded, (await command.ExecuteAsync(expectedTarget: fresh)).Status);
        Assert.Equal(1, Assert.IsType<WorkbenchCommandG3Document>(first.Model).ExecutionCount);
    }

    [Fact]
    public async Task 目标相同仍重查业务禁用且不因为已有约束绕过CanExecute()
    {
        using var context = WorkbenchCommandG3TestContext.Create();
        var page = await WorkbenchCommandG3TestContext.CreateDocumentAsync(context, "第一页");
        context.Workspace.DockFactory.SetActiveDockable(page);
        var capture = context.Provider.GetRequiredService<WorkbenchContextStore>().Capture();
        var expectation = new WorkbenchCommandTargetExpectation(page.PageId, capture.Snapshot.Revision);
        var model = Assert.IsType<WorkbenchCommandG3Document>(page.Model);
        model.AllowExecute = false;
        var result = await context.Provider.GetRequiredService<WorkbenchCommandExecutor>()
            .ExecuteAsync(WorkbenchCommandG3TestContext.Command, expectedTarget: expectation);
        Assert.Equal(WorkbenchCommandExecutionStatus.CommandDisabled, result.Status);
        Assert.Equal(0, model.ExecutionCount);
    }
}
