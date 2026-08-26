using System.Text.Json;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.Business.Plugins.Registration;
using MyAvaloniaManagement.Business.WorkflowActions;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.Tests;

/// <summary>覆盖 G1 Host Workflow Action 内核的治理、Scope 和竞态边界。</summary>
public sealed class WorkflowActionKernelTests
{
    private static readonly PluginId Owner = new("myavalonia.plugin.provider");
    private static readonly PluginId Caller = new("myavalonia.plugin.consumer");
    private static readonly WorkflowActionId ActionId = new(
        "myavalonia.plugin.provider.workflow.echo");

    [Fact]
    public void SchemaProfile验证结构实例预算和敏感指针()
    {
        var descriptor = Descriptor(WorkflowActionRiskFlags.HandlesSecret,
            WorkflowActionConfirmationPolicy.OncePerRun, ["/password"]);
        WorkflowActionSchemaValidator.ValidateDescriptor(descriptor);
        using var valid = JsonDocument.Parse("{\"value\":\"ok\",\"password\":\"secret\"}");
        WorkflowActionSchemaValidator.ValidateInstance(
            descriptor.InputSchema,
            valid.RootElement,
            WorkflowActionSchemaValidator.MaximumInputBytes);

        using var extra = JsonDocument.Parse(
            "{\"value\":\"ok\",\"password\":\"secret\",\"extra\":1}");
        Assert.Throws<ArgumentException>(() => WorkflowActionSchemaValidator.ValidateInstance(
            descriptor.InputSchema, extra.RootElement,
            WorkflowActionSchemaValidator.MaximumInputBytes));

        Assert.Throws<ArgumentException>(() => Descriptor(
            WorkflowActionRiskFlags.None,
            (WorkflowActionConfirmationPolicy)99));
        Assert.Throws<ArgumentException>(() => Descriptor(
            WorkflowActionRiskFlags.DeletesLocalFiles,
            WorkflowActionConfirmationPolicy.OncePerRun));
        Assert.Throws<ArgumentException>(() => Descriptor(
            WorkflowActionRiskFlags.HandlesSecret,
            WorkflowActionConfirmationPolicy.OncePerRun,
            ["/missing"]));
    }

    [Fact]
    public void SchemaProfile覆盖冻结关键字类型和边界反语料()
    {
        // 这些反语料逐项锁定 G1 的窄 Profile。测试故意直接调用 internal 验证器，
        // 避免将 Host 的安全实现误提升成插件可扩展的通用 Schema 框架。
        string[] invalidSchemas =
        [
            "[]",
            "{}",
            "{\"type\":\"string\"}",
            "{\"type\":\"union\",\"properties\":{},\"additionalProperties\":false}",
            "{\"type\":\"object\",\"additionalProperties\":false}",
            "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":true}",
            "{\"type\":\"object\",\"properties\":{},\"required\":{},\"additionalProperties\":false}",
            "{\"type\":\"object\",\"properties\":{},\"required\":[\"missing\"],\"additionalProperties\":false}",
            "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false,\"oneOf\":[]}",
            "{\"type\":\"object\",\"description\":1,\"properties\":{},\"additionalProperties\":false}",
            "{\"type\":\"object\",\"properties\":{\"items\":{\"type\":\"array\"}},\"additionalProperties\":false}",
            "{\"type\":\"object\",\"properties\":{\"items\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"maxItems\":1025}},\"additionalProperties\":false}",
            "{\"type\":\"object\",\"properties\":{\"items\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"minItems\":2,\"maxItems\":1}},\"additionalProperties\":false}",
            "{\"type\":\"object\",\"properties\":{\"text\":{\"type\":\"string\",\"minLength\":2,\"maxLength\":1}},\"additionalProperties\":false}",
            "{\"type\":\"object\",\"properties\":{\"number\":{\"type\":\"number\",\"minimum\":2,\"maximum\":1}},\"additionalProperties\":false}",
            "{\"type\":\"object\",\"properties\":{\"value\":{\"type\":\"string\",\"enum\":[]}},\"additionalProperties\":false}",
            "{\"type\":\"object\",\"properties\":{\"value\":{\"type\":\"string\",\"enum\":[\"a\",\"a\"]}},\"additionalProperties\":false}",
        ];
        foreach (var schema in invalidSchemas)
        {
            Assert.Throws<ArgumentException>(() => ValidateSchema(schema));
        }

        var tooManyProperties = Enumerable.Range(0, 129)
            .ToDictionary(index => $"p{index}", _ => new { type = "string" });
        var oversizedPropertySchema = JsonSerializer.Serialize(new
        {
            type = "object",
            properties = tooManyProperties,
            additionalProperties = false,
        });
        Assert.Throws<ArgumentException>(() => ValidateSchema(oversizedPropertySchema));

        var oversizedSchema = JsonSerializer.Serialize(new
        {
            type = "object",
            description = new string('x', WorkflowActionSchemaValidator.MaximumSchemaBytes),
            properties = new { },
            additionalProperties = false,
        });
        Assert.Throws<ArgumentException>(() => ValidateSchema(oversizedSchema));
    }

    [Fact]
    public void SchemaProfile覆盖数组标量枚举和实例资源预算()
    {
        const string schema = """
            {
              "type":"object",
              "properties":{
                "items":{"type":"array","items":{"type":"integer","minimum":1,"maximum":3},"minItems":1,"maxItems":2},
                "mode":{"type":"string","enum":["safe","fast"],"minLength":4,"maxLength":4},
                "ratio":{"type":"number","minimum":0.5,"maximum":2.5},
                "enabled":{"type":"boolean"},
                "empty":{"type":"null"}
              },
              "required":["items","mode","ratio","enabled","empty"],
              "additionalProperties":false
            }
            """;
        ValidateInstance(schema,
            "{\"items\":[1,3],\"mode\":\"safe\",\"ratio\":1.5,\"enabled\":true,\"empty\":null}");

        string[] invalidInstances =
        [
            "{\"items\":[],\"mode\":\"safe\",\"ratio\":1.5,\"enabled\":true,\"empty\":null}",
            "{\"items\":[1,2,3],\"mode\":\"safe\",\"ratio\":1.5,\"enabled\":true,\"empty\":null}",
            "{\"items\":[0],\"mode\":\"safe\",\"ratio\":1.5,\"enabled\":true,\"empty\":null}",
            "{\"items\":[1.5],\"mode\":\"safe\",\"ratio\":1.5,\"enabled\":true,\"empty\":null}",
            "{\"items\":[1],\"mode\":\"slow\",\"ratio\":1.5,\"enabled\":true,\"empty\":null}",
            "{\"items\":[1],\"mode\":\"safe\",\"ratio\":3,\"enabled\":true,\"empty\":null}",
            "{\"items\":[1],\"mode\":\"safe\",\"ratio\":1.5,\"enabled\":1,\"empty\":null}",
            "{\"items\":[1],\"mode\":\"safe\",\"ratio\":1.5,\"enabled\":true}",
        ];
        foreach (var instance in invalidInstances)
        {
            Assert.Throws<ArgumentException>(() => ValidateInstance(schema, instance));
        }

        using var schemaDocument = JsonDocument.Parse(schema);
        using var smallDocument = JsonDocument.Parse("{}");
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WorkflowActionSchemaValidator.ValidateInstance(
                schemaDocument.RootElement, smallDocument.RootElement, 0));
        Assert.Throws<ArgumentException>(() =>
            WorkflowActionSchemaValidator.ValidateInstance(
                schemaDocument.RootElement, smallDocument.RootElement, 1));

        var longTextSchema = """
            {"type":"object","properties":{"value":{"type":"string"}},"required":["value"],"additionalProperties":false}
            """;
        var longText = JsonSerializer.Serialize(new
        {
            value = new string('界', WorkflowActionSchemaValidator.MaximumStringBytes),
        });
        Assert.Throws<ArgumentException>(() => ValidateInstance(longTextSchema, longText));
    }

    [Fact]
    public void Catalog只提交一次并产生稳定Revision()
    {
        var descriptor = Descriptor();
        var registry = Registry(descriptor);
        var availability = new PluginAvailabilityReadModel(
            new PluginLifecycleStateStore(registry));
        var catalog = new WorkflowActionCatalogStore();

        catalog.Commit(registry, availability);

        Assert.Equal(71, catalog.ContractRevision.Length);
        Assert.Equal(71, catalog.PresentationRevision.Length);
        Assert.Single(catalog.GetAvailableDescriptors());
        Assert.Throws<InvalidOperationException>(() => catalog.Commit(registry, availability));
    }

    [Fact]
    public void Catalog展示变化只改变Presentation而Schema变化改变Contract()
    {
        var original = Descriptor();
        var renamed = new WorkflowActionDescriptor(
            original.Id, "新回显名称", "新展示说明", original.InputSchema, original.OutputSchema,
            original.Risks, original.ConfirmationPolicy, original.SensitiveInputPointers);
        using var changedOutput = JsonDocument.Parse("""
            {"type":"object","properties":{"echoed":{"type":"integer"}},"required":["echoed"],"additionalProperties":false}
            """);
        var changed = new WorkflowActionDescriptor(
            original.Id, renamed.DisplayName, renamed.Description, original.InputSchema,
            changedOutput.RootElement, original.Risks, original.ConfirmationPolicy,
            original.SensitiveInputPointers);

        var first = CaptureRevisions(original);
        var presentation = CaptureRevisions(renamed);
        var contract = CaptureRevisions(changed);

        Assert.Equal(first.ContractRevision, presentation.ContractRevision);
        Assert.NotEqual(first.PresentationRevision, presentation.PresentationRevision);
        Assert.NotEqual(first.ContractRevision, contract.ContractRevision);
    }

    [Fact]
    public async Task 成功调用绑定Caller并释放独立Scope且限流进度()
    {
        PluginId? observedCaller = null;
        var reports = new List<WorkflowActionProgress>();
        var handler = new DelegateHandler((arguments, context, _) =>
        {
            observedCaller = context.CallerId;
            context.Progress.Report(new WorkflowActionProgress("start", 0, "开始"));
            context.Progress.Report(new WorkflowActionProgress("middle", 50, "过快更新"));
            return ValueTask.FromResult(JsonSerializer.SerializeToElement(new
            {
                echoed = arguments.GetProperty("value").GetString(),
            }));
        });
        var scopeFactory = new FakeScopeFactory(handler);
        var authorizer = new FakeAuthorizer(true);
        var manager = CreateManager(Descriptor(), scopeFactory, authorizer);
        var gateway = new CallerBoundWorkflowActionGateway(Caller, manager);
        await using var run = gateway.CreateRun();

        var result = await run.InvokeAsync(
            Request("ok"),
            new InlineProgress(reports.Add),
            CancellationToken.None);

        Assert.Equal(WorkflowActionInvocationStatus.Succeeded, result.Status);
        Assert.Equal(Caller, observedCaller);
        Assert.Equal("ok", result.Output!.Value.GetProperty("echoed").GetString());
        Assert.Equal(1, scopeFactory.Created);
        Assert.Equal(1, scopeFactory.Disposed);
        Assert.Single(reports);
        Assert.Equal(0, authorizer.Calls);
    }

    [Fact]
    public async Task OncePerRun只缓存同参数批准且敏感摘要被遮蔽()
    {
        var authorizer = new FakeAuthorizer(true);
        var manager = CreateManager(
            Descriptor(WorkflowActionRiskFlags.HandlesSecret,
                WorkflowActionConfirmationPolicy.OncePerRun,
                ["/password"]),
            new FakeScopeFactory(new EchoHandler()),
            authorizer);
        var gateway = new CallerBoundWorkflowActionGateway(Caller, manager);
        await using var firstRun = gateway.CreateRun();

        Assert.Equal(WorkflowActionInvocationStatus.Succeeded,
            (await firstRun.InvokeAsync(Request("a", "secret"), null, CancellationToken.None)).Status);
        Assert.Equal(WorkflowActionInvocationStatus.Succeeded,
            (await firstRun.InvokeAsync(Request("a", "secret"), null, CancellationToken.None)).Status);
        using var reordered = JsonDocument.Parse("{\"password\":\"secret\",\"value\":\"a\"}");
        Assert.Equal(WorkflowActionInvocationStatus.Succeeded,
            (await firstRun.InvokeAsync(
                new WorkflowActionInvocationRequest(ActionId, reordered.RootElement),
                null,
                CancellationToken.None)).Status);
        Assert.Equal(1, authorizer.Calls);
        Assert.DoesNotContain("secret", authorizer.LastSummary, StringComparison.Ordinal);
        Assert.Contains("***", authorizer.LastSummary, StringComparison.Ordinal);

        await using var secondRun = gateway.CreateRun();
        _ = await secondRun.InvokeAsync(Request("a", "secret"), null, CancellationToken.None);
        Assert.Equal(2, authorizer.Calls);
    }

    [Fact]
    public async Task EveryInvocation逐次授权且拒绝失败关闭()
    {
        var authorizer = new FakeAuthorizer(false);
        var manager = CreateManager(
            Descriptor(WorkflowActionRiskFlags.WritesLocalFiles,
                WorkflowActionConfirmationPolicy.EveryInvocation),
            new FakeScopeFactory(new EchoHandler()), authorizer);
        await using var run = manager.CreateRun(Caller);

        var result = await run.InvokeAsync(Request("a"), null, CancellationToken.None);

        Assert.Equal(WorkflowActionInvocationStatus.Rejected, result.Status);
        Assert.Equal("WORKFLOW_ACTION_AUTHORIZATION_DENIED", result.Failure!.Code);
        Assert.Equal(1, authorizer.Calls);
    }

    [Fact]
    public async Task 输入和输出非法都以结构化失败收口()
    {
        var manager = CreateManager(Descriptor(),
            new FakeScopeFactory(new DelegateHandler((_, _, _) =>
                ValueTask.FromResult(JsonSerializer.SerializeToElement(new { wrong = 1 })))),
            new FakeAuthorizer(true));
        await using var run = manager.CreateRun(Caller);
        using var invalid = JsonDocument.Parse("{\"unknown\":1}");

        var input = await run.InvokeAsync(
            new WorkflowActionInvocationRequest(ActionId, invalid.RootElement),
            null, CancellationToken.None);
        var output = await run.InvokeAsync(Request("ok"), null, CancellationToken.None);

        Assert.Equal("WORKFLOW_ACTION_INPUT_INVALID", input.Failure!.Code);
        Assert.Equal("WORKFLOW_ACTION_OUTPUT_INVALID", output.Failure!.Code);
    }

    [Fact]
    public async Task Run并发满额快速拒绝且不建立等待队列()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new DelegateHandler(async (_, _, cancellationToken) =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return JsonSerializer.SerializeToElement(new { echoed = "done" });
        });
        var manager = CreateManager(Descriptor(), new FakeScopeFactory(handler),
            new FakeAuthorizer(true), Limits(maximumRun: 1));
        await using var run = manager.CreateRun(Caller);
        var first = run.InvokeAsync(Request("a"), null, CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var second = await run.InvokeAsync(Request("b"), null, CancellationToken.None);
        release.TrySetResult();
        var completed = await first;

        Assert.Equal("WORKFLOW_ACTION_CONCURRENCY_LIMIT", second.Failure!.Code);
        Assert.Equal(WorkflowActionInvocationStatus.Succeeded, completed.Status);
    }

    [Fact]
    public async Task 超时和Host关闭都协作取消并等待Handler退出()
    {
        var handler = new DelegateHandler(async (_, _, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return JsonSerializer.SerializeToElement(new { echoed = "never" });
        });
        var manager = CreateManager(Descriptor(), new FakeScopeFactory(handler),
            new FakeAuthorizer(true), Limits(timeout: TimeSpan.FromMilliseconds(20)));
        await using var run = manager.CreateRun(Caller);

        var timedOut = await run.InvokeAsync(Request("a"), null, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(WorkflowActionInvocationStatus.TimedOut, timedOut.Status);

        var pending = run.InvokeAsync(Request("b"), null, CancellationToken.None);
        manager.BeginShutdown();
        Assert.True(await manager.WaitForDrainAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(WorkflowActionInvocationStatus.Cancelled, (await pending).Status);
        Assert.Throws<InvalidOperationException>(() => manager.CreateRun(Caller));
        Assert.Empty(manager.GetAvailableActions());
        manager.BeginShutdown();
    }

    [Fact]
    public async Task 未找到取消Handler异常和授权异常均映射稳定失败码()
    {
        var manager = CreateManager(Descriptor(),
            new FakeScopeFactory(new DelegateHandler((_, _, _) =>
                throw new ArgumentException("不得外泄的插件异常"))),
            new ThrowingAuthorizer());
        await using var run = manager.CreateRun(Caller);

        var missing = await run.InvokeAsync(new WorkflowActionInvocationRequest(
            new WorkflowActionId("myavalonia.plugin.provider.workflow.missing"),
            JsonSerializer.SerializeToElement(new { value = "a" })), null, CancellationToken.None);
        Assert.Equal("WORKFLOW_ACTION_NOT_FOUND", missing.Failure!.Code);

        var failed = await run.InvokeAsync(Request("a"), null, CancellationToken.None);
        Assert.Equal("WORKFLOW_ACTION_HANDLER_FAILED", failed.Failure!.Code);
        Assert.DoesNotContain("不得外泄", failed.Failure.Message, StringComparison.Ordinal);

        var guarded = CreateManager(
            Descriptor(WorkflowActionRiskFlags.WritesLocalFiles,
                WorkflowActionConfirmationPolicy.EveryInvocation),
            new FakeScopeFactory(new EchoHandler()), new ThrowingAuthorizer());
        await using var guardedRun = guarded.CreateRun(Caller);
        var authorization = await guardedRun.InvokeAsync(Request("a"), null, CancellationToken.None);
        Assert.Equal("WORKFLOW_ACTION_AUTHORIZATION_DENIED", authorization.Failure!.Code);

        var cancellationManager = CreateManager(Descriptor(),
            new FakeScopeFactory(new DelegateHandler((_, _, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult(
                    JsonSerializer.SerializeToElement(new { echoed = "never" }));
            })),
            new FakeAuthorizer(true));
        await using var cancellationRun = cancellationManager.CreateRun(Caller);
        using var cancelledSource = new CancellationTokenSource();
        cancelledSource.Cancel();
        var cancelled = await cancellationRun.InvokeAsync(
            Request("a"), null, cancelledSource.Token);
        Assert.Equal("WORKFLOW_ACTION_CANCELLED", cancelled.Failure!.Code);
    }

    [Fact]
    public async Task Owner并发Run释放与非法进度均安全收口()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reports = new List<WorkflowActionProgress>();
        var handler = new DelegateHandler(async (_, context, cancellationToken) =>
        {
            context.Progress.Report(new WorkflowActionProgress(new string('x', 65), 1, null));
            context.Progress.Report(new WorkflowActionProgress("unsafe stage", 2, null));
            context.Progress.Report(new WorkflowActionProgress("valid", 3, new string('m', 513)));
            started.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return JsonSerializer.SerializeToElement(new { echoed = "done" });
        });
        var manager = CreateManager(Descriptor(), new FakeScopeFactory(handler),
            new FakeAuthorizer(true), Limits(maximumOwner: 1));
        await using var firstRun = manager.CreateRun(Caller);
        await using var secondRun = manager.CreateRun(Caller);
        var first = firstRun.InvokeAsync(Request("a"), new InlineProgress(reports.Add),
            CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var rejected = await secondRun.InvokeAsync(Request("b"), null, CancellationToken.None);
        Assert.Equal("WORKFLOW_ACTION_CONCURRENCY_LIMIT", rejected.Failure!.Code);
        Assert.False(await manager.WaitForDrainAsync(TimeSpan.FromMilliseconds(1)));
        release.TrySetResult();
        Assert.Equal(WorkflowActionInvocationStatus.Succeeded, (await first).Status);
        Assert.Empty(reports);

        await firstRun.DisposeAsync();
        await firstRun.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            firstRun.InvokeAsync(Request("c"), null, CancellationToken.None));
    }

    [Fact]
    public void Provider和Consumer不能由同一插件同时声明()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        var builder = new PluginRegistryBuilder();
        var registration = new PluginRegistration(Owner, services, builder);
        registration.AddWorkflowAction<EchoHandler>(Descriptor());
        registration.UseWorkflowActionGateway();

        Assert.Throws<HostCompositionException>(registration.Seal);
    }

    private static WorkflowActionRunManager CreateManager(
        WorkflowActionDescriptor descriptor,
        IWorkflowActionScopeFactory scopeFactory,
        IWorkflowActionAuthorizer authorizer,
        WorkflowActionExecutionLimits? limits = null)
    {
        var registry = Registry(descriptor);
        var catalog = new WorkflowActionCatalogStore();
        catalog.Commit(registry, new PluginAvailabilityReadModel(
            new PluginLifecycleStateStore(registry)));
        return new WorkflowActionRunManager(
            catalog,
            scopeFactory,
            authorizer,
            limits ?? Limits(),
            TimeProvider.System);
    }

    private static PluginRegistry Registry(WorkflowActionDescriptor descriptor) => new(
        plugins: [],
        documents: [],
        tools: [],
        lifecycles: [],
        workflowActions: [new PluginWorkflowActionRegistration(Owner, descriptor, typeof(EchoHandler))],
        workflowConsumers: new HashSet<PluginId> { Caller });

    private static WorkflowActionExecutionLimits Limits(
        int maximumRun = 4,
        int maximumOwner = 4,
        TimeSpan? timeout = null) => new(
            maximumRun,
            MaximumConcurrentPerOwner: maximumOwner,
            timeout ?? TimeSpan.FromMinutes(5),
            timeout ?? TimeSpan.FromHours(6),
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(100));

    private static WorkflowActionDescriptor Descriptor(
        WorkflowActionRiskFlags risks = WorkflowActionRiskFlags.None,
        WorkflowActionConfirmationPolicy policy = WorkflowActionConfirmationPolicy.Never,
        IReadOnlyList<string>? pointers = null)
    {
        using var input = JsonDocument.Parse("""
            {
              "type":"object",
              "properties":{
                "value":{"type":"string","maxLength":64},
                "password":{"type":"string","maxLength":64}
              },
              "required":["value"],
              "additionalProperties":false
            }
            """);
        using var output = JsonDocument.Parse("""
            {
              "type":"object",
              "properties":{"echoed":{"type":"string","maxLength":64}},
              "required":["echoed"],
              "additionalProperties":false
            }
            """);
        var descriptor = new WorkflowActionDescriptor(
            ActionId, "回显", "测试动作", input.RootElement, output.RootElement,
            risks, policy, pointers);
        WorkflowActionSchemaValidator.ValidateDescriptor(descriptor);
        return descriptor;
    }

    private static (string ContractRevision, string PresentationRevision) CaptureRevisions(
        WorkflowActionDescriptor descriptor)
    {
        var registry = Registry(descriptor);
        var availability = new PluginAvailabilityReadModel(new PluginLifecycleStateStore(registry));
        var catalog = new WorkflowActionCatalogStore();
        catalog.Commit(registry, availability);
        return (catalog.ContractRevision, catalog.PresentationRevision);
    }

    private static WorkflowActionInvocationRequest Request(string value, string? password = null)
    {
        var arguments = password is null
            ? JsonSerializer.SerializeToElement(new { value })
            : JsonSerializer.SerializeToElement(new { value, password });
        return new WorkflowActionInvocationRequest(ActionId, arguments);
    }

    private static void ValidateSchema(string schema)
    {
        using var document = JsonDocument.Parse(schema);
        WorkflowActionSchemaValidator.ValidateSchema(document.RootElement);
    }

    private static void ValidateInstance(string schema, string instance)
    {
        using var schemaDocument = JsonDocument.Parse(schema);
        using var instanceDocument = JsonDocument.Parse(instance);
        WorkflowActionSchemaValidator.ValidateInstance(
            schemaDocument.RootElement,
            instanceDocument.RootElement,
            WorkflowActionSchemaValidator.MaximumInputBytes);
    }

    private sealed class EchoHandler : IWorkflowActionHandler
    {
        public ValueTask<JsonElement> InvokeAsync(
            JsonElement arguments,
            WorkflowActionContext context,
            CancellationToken cancellationToken) => ValueTask.FromResult(
                JsonSerializer.SerializeToElement(new
                {
                    echoed = arguments.GetProperty("value").GetString(),
                }));
    }

    private sealed class DelegateHandler(
        Func<JsonElement, WorkflowActionContext, CancellationToken, ValueTask<JsonElement>> invoke)
        : IWorkflowActionHandler
    {
        public ValueTask<JsonElement> InvokeAsync(
            JsonElement arguments,
            WorkflowActionContext context,
            CancellationToken cancellationToken) => invoke(arguments, context, cancellationToken);
    }

    private sealed class FakeScopeFactory(IWorkflowActionHandler handler)
        : IWorkflowActionScopeFactory
    {
        internal int Created { get; private set; }
        internal int Disposed { get; private set; }

        public IWorkflowActionInvocationScope CreateWorkflowActionScope(
            PluginId pluginId,
            Type handlerType)
        {
            Assert.Equal(Owner, pluginId);
            Created++;
            return new Scope(handler, () => Disposed++);
        }

        private sealed class Scope(IWorkflowActionHandler handler, Action dispose)
            : IWorkflowActionInvocationScope
        {
            public IWorkflowActionHandler Handler { get; } = handler;
            public ValueTask DisposeAsync()
            {
                dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class FakeAuthorizer(bool result) : IWorkflowActionAuthorizer
    {
        internal int Calls { get; private set; }
        internal string LastSummary { get; private set; } = string.Empty;
        public Task<bool> AuthorizeAsync(
            WorkflowActionAuthorizationRequest request,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastSummary = request.RedactedArgumentSummary;
            return Task.FromResult(result);
        }
    }

    private sealed class ThrowingAuthorizer : IWorkflowActionAuthorizer
    {
        public Task<bool> AuthorizeAsync(
            WorkflowActionAuthorizationRequest request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("不得外泄的授权异常");
    }

    private sealed class InlineProgress(Action<WorkflowActionProgress> report)
        : IProgress<WorkflowActionProgress>
    {
        public void Report(WorkflowActionProgress value) => report(value);
    }
}
