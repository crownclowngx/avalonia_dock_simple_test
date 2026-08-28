using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Commands.Catalog;
using MyAvaloniaManagement.Business.Commands.Execution;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.Tests;

/// <summary>验证 G2 Catalog/Executor 的无 UI 查询、执行、取消和所有权边界。</summary>
public sealed class WorkbenchCommandCatalogExecutorTests
{
    private static readonly CommandId FirstHostId = new("myavalonia.host.command.test.first");
    private static readonly CommandId SecondHostId = new("myavalonia.host.command.test.second");
    private static readonly PluginId PluginOwner = new("myavalonia.plugin.command-tests");
    private static readonly DocumentTypeId PluginDocument =
        new("myavalonia.plugin.command-tests.document.main");
    private static readonly CommandId PluginCommand =
        new("myavalonia.plugin.command-tests.command.run");

    [Fact]
    public void 合并目录按稳定身份排序并区分Host与插件事实()
    {
        var firstHandler = DelegateHostCommandHandler.Completed();
        var secondHandler = DelegateHostCommandHandler.Completed();
        var host = new HostWorkbenchCommandCatalog([
            HostRegistration(SecondHostId, secondHandler),
            HostRegistration(FirstHostId, firstHandler),
        ]);
        var plugins = PluginRegistryWithCommand();

        var catalog = new WorkbenchCommandCatalog(host, plugins);

        Assert.Equal(
            [FirstHostId.Value, SecondHostId.Value, PluginCommand.Value],
            catalog.Entries.Select(item => item.Descriptor.CommandId.Value));
        Assert.True(catalog.TryGet(FirstHostId, out var hostEntry));
        Assert.Same(firstHandler, Assert.IsType<HostWorkbenchCommandCatalogEntry>(hostEntry).Handler);
        Assert.True(catalog.TryGet(PluginCommand, out var pluginEntry));
        var plugin = Assert.IsType<PluginWorkbenchCommandCatalogEntry>(pluginEntry);
        Assert.Equal(PluginOwner, plugin.OwnerId);
        Assert.Equal(PluginDocument, plugin.TargetDocumentTypeId);
        Assert.False(catalog.TryGet(new CommandId("myavalonia.host.command.test.missing"), out _));
    }

    [Fact]
    public void Host目录冻结输入且拒绝重复身份()
    {
        var registrations = new List<HostWorkbenchCommandRegistration>
        {
            HostRegistration(FirstHostId, DelegateHostCommandHandler.Completed()),
        };
        var catalog = new HostWorkbenchCommandCatalog(registrations);
        registrations.Clear();

        Assert.Single(catalog.Registrations);
        Assert.Throws<ArgumentException>(() => new HostWorkbenchCommandCatalog([
            HostRegistration(FirstHostId, DelegateHostCommandHandler.Completed()),
            HostRegistration(FirstHostId, DelegateHostCommandHandler.Completed()),
        ]));
    }

    [Fact]
    public void Host与插件身份碰撞在最终合并时稳定失败()
    {
        var host = new HostWorkbenchCommandCatalog([
            HostRegistration(PluginCommand, DelegateHostCommandHandler.Completed()),
        ]);

        var exception = Assert.Throws<HostCompositionException>(() =>
            new WorkbenchCommandCatalog(host, PluginRegistryWithCommand()));

        var diagnostic = Assert.Single(exception.Diagnostics);
        Assert.Equal(HostDiagnosticCodes.WorkbenchCommandIdDuplicate, diagnostic.Code);
        Assert.Equal(PluginCommand.Value, diagnostic.StableId);
    }

    [Fact]
    public async Task Host命令成功和未知身份返回稳定结果()
    {
        var executions = 0;
        var handler = new DelegateHostCommandHandler(_ =>
        {
            executions++;
            return ValueTask.CompletedTask;
        });
        using var executor = CreateExecutor([
            HostRegistration(FirstHostId, handler),
        ]);

        var succeeded = await executor.ExecuteAsync(FirstHostId);
        var missing = await executor.ExecuteAsync(
            new CommandId("myavalonia.host.command.test.missing"));

        Assert.Equal(WorkbenchCommandExecutionStatus.Succeeded, succeeded.Status);
        Assert.Equal(1, executions);
        Assert.Equal(WorkbenchCommandExecutionStatus.CommandNotFound, missing.Status);
        Assert.Empty(missing.UserMessage);
    }

    [Fact]
    public async Task 插件命令按动态Owner可用性区分不可用与尚无Target()
    {
        using var available = CreateExecutor([], PluginRegistryWithCommand());
        using var unavailable = CreateExecutor(
            [],
            PluginRegistryWithCommand(includeLifecycle: true));

        var targetMissing = await available.ExecuteAsync(PluginCommand);
        var ownerMissing = await unavailable.ExecuteAsync(PluginCommand);

        Assert.Equal(WorkbenchCommandExecutionStatus.TargetUnavailable, targetMissing.Status);
        Assert.Equal(WorkbenchCommandExecutionStatus.OwnerUnavailable, ownerMissing.Status);
    }

    [Fact]
    public async Task 调用取消和关闭取消均被观察且不产生失败诊断()
    {
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new DelegateHostCommandHandler(async cancellationToken =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        });
        var diagnostics = new RecordingDiagnosticSink();
        using var executor = CreateExecutor(
            [HostRegistration(FirstHostId, handler)],
            diagnostics: diagnostics);

        using var callerCancellation = new CancellationTokenSource();
        callerCancellation.Cancel();
        var callerCanceled = await executor.ExecuteAsync(
            FirstHostId,
            callerCancellation.Token);

        var running = executor.ExecuteAsync(FirstHostId).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        executor.BeginShutdown();
        executor.BeginShutdown();
        var shutdownCanceled = await running.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(WorkbenchCommandExecutionStatus.Canceled, callerCanceled.Status);
        Assert.Equal(WorkbenchCommandExecutionStatus.Canceled, shutdownCanceled.Status);
        Assert.True(await executor.WaitForDrainAsync(TimeSpan.FromSeconds(1)));
        Assert.Empty(diagnostics.Drafts);
    }

    [Fact]
    public async Task 关闭后拒绝新命令且多个在途调用全部排空()
    {
        var enteredCount = 0;
        var bothEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new DelegateHostCommandHandler(async cancellationToken =>
        {
            if (Interlocked.Increment(ref enteredCount) == 2)
            {
                bothEntered.TrySetResult();
            }
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        });
        using var executor = CreateExecutor([
            HostRegistration(FirstHostId, handler),
            HostRegistration(SecondHostId, handler),
        ]);

        var first = executor.ExecuteAsync(FirstHostId).AsTask();
        var second = executor.ExecuteAsync(SecondHostId).AsTask();
        await bothEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        executor.BeginShutdown();

        var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
        var rejected = await executor.ExecuteAsync(FirstHostId);

        Assert.All(results, result =>
            Assert.Equal(WorkbenchCommandExecutionStatus.Canceled, result.Status));
        Assert.Equal(WorkbenchCommandExecutionStatus.RejectedDuringShutdown, rejected.Status);
        Assert.True(await executor.WaitForDrainAsync(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task Handler异常被隔离并生成不含正文的稳定结果()
    {
        var diagnostics = new RecordingDiagnosticSink();
        var handler = new DelegateHostCommandHandler(_ =>
            ValueTask.FromException(new InvalidOperationException(
                "secret C:\\private\\command.json")));
        using var executor = CreateExecutor(
            [HostRegistration(FirstHostId, handler)],
            diagnostics: diagnostics);

        var result = await executor.ExecuteAsync(FirstHostId);

        Assert.Equal(WorkbenchCommandExecutionStatus.Failed, result.Status);
        Assert.DoesNotContain("secret", result.UserMessage, StringComparison.OrdinalIgnoreCase);
        var draft = Assert.Single(diagnostics.Drafts);
        Assert.Equal(HostDiagnosticCodes.WorkbenchCommandExecutionFailed, draft.Code);
        Assert.Equal(HostDiagnosticPhase.WorkbenchCommand, draft.Phase);
        Assert.Equal(FirstHostId.Value, draft.StableId);
    }

    [Fact]
    public async Task 非协作OperationCanceledException按Handler失败处理()
    {
        var diagnostics = new RecordingDiagnosticSink();
        using var executor = CreateExecutor(
            [HostRegistration(
                FirstHostId,
                new DelegateHostCommandHandler(_ =>
                    ValueTask.FromException(new OperationCanceledException("未关联取消"))))],
            diagnostics: diagnostics);

        var result = await executor.ExecuteAsync(FirstHostId);

        Assert.Equal(WorkbenchCommandExecutionStatus.Failed, result.Status);
        Assert.Single(diagnostics.Drafts);
    }

    [Fact]
    public async Task 排空宽限必须为正数且生产宽限固定十秒()
    {
        using var executor = CreateExecutor([]);

        Assert.Equal(TimeSpan.FromSeconds(10), executor.ShutdownGrace);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            executor.WaitForDrainAsync(TimeSpan.Zero));
    }

    private static HostWorkbenchCommandRegistration HostRegistration(
        CommandId id,
        IHostWorkbenchCommandHandler handler) => new(
        new CommandDescriptor(id, id.Value, "测试命令"),
        handler);

    private static PluginRegistry PluginRegistryWithCommand(bool includeLifecycle = false) =>
        new(
            [],
            [],
            [],
            includeLifecycle
                ? [new PluginLifecycleDeclaration(PluginOwner, typeof(object))]
                : [],
            workbenchCommands:
            [
                new PluginWorkbenchCommandRegistration(
                    PluginOwner,
                    new CommandDescriptor(PluginCommand, "运行", "运行测试命令"),
                    PluginDocument),
            ]);

    private static WorkbenchCommandExecutor CreateExecutor(
        IReadOnlyList<HostWorkbenchCommandRegistration> hostRegistrations,
        PluginRegistry? plugins = null,
        IHostDiagnosticSink? diagnostics = null)
    {
        plugins ??= new PluginRegistry([], []);
        var states = new PluginLifecycleStateStore(plugins);
        return new WorkbenchCommandExecutor(
            new WorkbenchCommandCatalog(
                new HostWorkbenchCommandCatalog(hostRegistrations),
                plugins),
            new PluginAvailabilityReadModel(states),
            diagnostics);
    }

    private sealed class DelegateHostCommandHandler(
        Func<CancellationToken, ValueTask> execute) : IHostWorkbenchCommandHandler
    {
        public ValueTask ExecuteAsync(CancellationToken cancellationToken) =>
            execute(cancellationToken);

        internal static DelegateHostCommandHandler Completed() =>
            new(_ => ValueTask.CompletedTask);
    }

    private sealed class RecordingDiagnosticSink : IHostDiagnosticSink
    {
        internal List<HostDiagnosticDraft> Drafts { get; } = [];

        public HostDiagnosticRecord Report(HostDiagnosticDraft diagnostic)
        {
            Drafts.Add(diagnostic);
            return new HostDiagnosticRecord
            {
                SessionId = Guid.Empty,
                Sequence = Drafts.Count,
                TimestampUtc = DateTimeOffset.UnixEpoch,
                Code = diagnostic.Code,
                Severity = HostDiagnosticSeverity.Error,
                Phase = diagnostic.Phase,
                Disposition = HostDiagnosticDisposition.Continue,
                UserMessage = "测试诊断",
            };
        }
    }
}
