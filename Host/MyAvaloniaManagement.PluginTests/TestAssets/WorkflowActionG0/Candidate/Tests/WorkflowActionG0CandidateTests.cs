using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Business.Lifecycle;
using MyAvaloniaManagement.Business.Workspace;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace MyAvaloniaManagement.PluginTests;

/// <summary>只在 G0 隔离 3.1 候选副本中编译和执行的协议测试。</summary>
public sealed class WorkflowActionG0CandidateTests
{
    [Theory]
    [InlineData("myavalonia.plugin.sample.workflow.run")]
    [InlineData("myavalonia.plugin.sample.workflow.prepare-download")]
    public void ActionId沿用稳定身份词法(string value)
    {
        Assert.Equal(value, WorkflowActionId.Parse(value).Value);
        Assert.True(WorkflowActionId.TryParse(value, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Upper")]
    [InlineData("a..b")]
    [InlineData("a_b")]
    public void ActionId拒绝非规范输入(string value)
    {
        Assert.Throws<ArgumentException>(() => WorkflowActionId.Parse(value));
        Assert.False(WorkflowActionId.TryParse(value, out _));
    }

    [Fact]
    public void Descriptor请求和结果均取得Json独立快照()
    {
        WorkflowActionDescriptor descriptor;
        WorkflowActionInvocationRequest request;
        WorkflowActionInvocationResult result;
        using (var document = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}"))
        using (var value = JsonDocument.Parse("{\"value\":1}"))
        {
            descriptor = new WorkflowActionDescriptor(
                new WorkflowActionId("myavalonia.plugin.sample.workflow.echo"),
                "回显", "用于 G0 的回显动作", document.RootElement, document.RootElement,
                WorkflowActionRiskFlags.None, WorkflowActionConfirmationPolicy.Never);
            request = new WorkflowActionInvocationRequest(descriptor.Id, value.RootElement);
            result = new WorkflowActionInvocationResult(
                Guid.NewGuid(), WorkflowActionInvocationStatus.Succeeded,
                value.RootElement, failure: null);
        }

        Assert.Equal(JsonValueKind.Object, descriptor.InputSchema.ValueKind);
        Assert.Equal(1, request.Arguments.GetProperty("value").GetInt32());
        Assert.Equal(1, result.Output!.Value.GetProperty("value").GetInt32());
    }

    [Fact]
    public void 风险与确认频率遵守冻结下限()
    {
        WorkflowActionG0SchemaProfile.ValidateConfirmation(
            WorkflowActionRiskFlags.None, WorkflowActionConfirmationPolicy.Never);
        WorkflowActionG0SchemaProfile.ValidateConfirmation(
            WorkflowActionRiskFlags.UsesNetwork | WorkflowActionRiskFlags.LongRunning,
            WorkflowActionConfirmationPolicy.OncePerRun);
        WorkflowActionG0SchemaProfile.ValidateConfirmation(
            WorkflowActionRiskFlags.DeletesLocalFiles | WorkflowActionRiskFlags.WritesLocalFiles,
            WorkflowActionConfirmationPolicy.EveryInvocation);
        WorkflowActionG0SchemaProfile.ValidateConfirmation(
            WorkflowActionRiskFlags.None, WorkflowActionConfirmationPolicy.EveryInvocation);
        WorkflowActionG0SchemaProfile.ValidateConfirmation(
            WorkflowActionRiskFlags.UsesNetwork, WorkflowActionConfirmationPolicy.EveryInvocation);

        Assert.Throws<ArgumentException>(() => WorkflowActionG0SchemaProfile.ValidateConfirmation(
            WorkflowActionRiskFlags.UsesNetwork, WorkflowActionConfirmationPolicy.Never));
        Assert.Throws<ArgumentException>(() => WorkflowActionG0SchemaProfile.ValidateConfirmation(
            WorkflowActionRiskFlags.DeletesLocalFiles, WorkflowActionConfirmationPolicy.OncePerRun));
        Assert.Throws<ArgumentException>(() => WorkflowActionG0SchemaProfile.ValidateConfirmation(
            (WorkflowActionRiskFlags)64, WorkflowActionConfirmationPolicy.EveryInvocation));
        Assert.Throws<ArgumentException>(() => WorkflowActionG0SchemaProfile.ValidateConfirmation(
            WorkflowActionRiskFlags.None, (WorkflowActionConfirmationPolicy)99));
    }

    [Fact]
    public void 窄SchemaProfile接受冻结关键字并拒绝扩展关键字()
    {
        using var valid = JsonDocument.Parse(
            """
            {"type":"object","properties":{"items":{"type":"array","items":{"type":"string","enum":["a","b"],"minLength":1,"maxLength":20},"minItems":0,"maxItems":10},"count":{"type":"integer","enum":[1,2],"minimum":1,"maximum":2}},"required":["items"],"additionalProperties":false}
            """);
        WorkflowActionG0SchemaProfile.ValidateSchema(valid.RootElement);
        WorkflowActionG0SchemaProfile.ValidateSensitivePointers(["/password", "/nested/~0secret"]);

        foreach (var invalidJson in new[]
                 {
                     "{\"type\":\"string\"}",
                     "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":true}",
                     "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false,\"$ref\":\"https://example.test/schema\"}",
                     "{\"type\":\"object\",\"properties\":{\"x\":{\"type\":[\"string\",\"null\"]}},\"additionalProperties\":false}",
                     "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false,\"oneOf\":[]}",
                     "{\"type\":\"object\",\"properties\":{\"x\":{\"type\":\"string\",\"enum\":[1]}},\"additionalProperties\":false}",
                     "{\"type\":\"object\",\"properties\":{\"x\":{\"type\":\"string\",\"minLength\":2,\"maxLength\":1}},\"additionalProperties\":false}",
                     "{\"type\":\"object\",\"properties\":{\"x\":{\"type\":\"number\",\"minimum\":2,\"maximum\":1}},\"additionalProperties\":false}",
                     "{\"type\":\"object\",\"properties\":{\"x\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"minItems\":2,\"maxItems\":1}},\"additionalProperties\":false}",
                 })
        {
            using var invalid = JsonDocument.Parse(invalidJson);
            Assert.Throws<ArgumentException>(() =>
                WorkflowActionG0SchemaProfile.ValidateSchema(invalid.RootElement));
        }
        Assert.Throws<ArgumentException>(() =>
            WorkflowActionG0SchemaProfile.ValidateSensitivePointers(["password"]));
    }

    [Fact]
    public void 全部冻结预算接受恰好上限并拒绝最小越界()
    {
        using var schemaAtLimit = JsonDocument.Parse(
            BuildSchemaWithExactBytes(WorkflowActionG0SchemaProfile.MaximumSchemaBytes));
        WorkflowActionG0SchemaProfile.ValidateSchema(schemaAtLimit.RootElement);
        using var schemaOverLimit = JsonDocument.Parse(
            BuildSchemaWithExactBytes(WorkflowActionG0SchemaProfile.MaximumSchemaBytes + 1));
        Assert.Throws<ArgumentException>(() =>
            WorkflowActionG0SchemaProfile.ValidateSchema(schemaOverLimit.RootElement));

        using var propertiesAtLimit = BuildObjectSchema(
            WorkflowActionG0SchemaProfile.MaximumProperties);
        WorkflowActionG0SchemaProfile.ValidateSchema(propertiesAtLimit.RootElement);
        using var propertiesOverLimit = BuildObjectSchema(
            WorkflowActionG0SchemaProfile.MaximumProperties + 1);
        Assert.Throws<ArgumentException>(() =>
            WorkflowActionG0SchemaProfile.ValidateSchema(propertiesOverLimit.RootElement));

        using var inputAtLimit = BuildJsonWithExactBytes(
            WorkflowActionG0SchemaProfile.MaximumInputBytes, stringCount: 4);
        WorkflowActionG0SchemaProfile.ValidateInstance(
            inputAtLimit.RootElement, WorkflowActionG0SchemaProfile.MaximumInputBytes);
        using var inputOverLimit = BuildJsonWithExactBytes(
            WorkflowActionG0SchemaProfile.MaximumInputBytes + 1, stringCount: 4);
        Assert.Throws<ArgumentException>(() => WorkflowActionG0SchemaProfile.ValidateInstance(
            inputOverLimit.RootElement, WorkflowActionG0SchemaProfile.MaximumInputBytes));

        using var outputAtLimit = BuildJsonWithExactBytes(
            WorkflowActionG0SchemaProfile.MaximumOutputBytes, stringCount: 16);
        WorkflowActionG0SchemaProfile.ValidateInstance(
            outputAtLimit.RootElement, WorkflowActionG0SchemaProfile.MaximumOutputBytes);
        using var outputOverLimit = BuildJsonWithExactBytes(
            WorkflowActionG0SchemaProfile.MaximumOutputBytes + 1, stringCount: 16);
        Assert.Throws<ArgumentException>(() => WorkflowActionG0SchemaProfile.ValidateInstance(
            outputOverLimit.RootElement, WorkflowActionG0SchemaProfile.MaximumOutputBytes));

        using var stringAtLimit = JsonDocument.Parse(JsonSerializer.Serialize(
            new string('a', WorkflowActionG0SchemaProfile.MaximumStringBytes)));
        WorkflowActionG0SchemaProfile.ValidateInstance(
            stringAtLimit.RootElement, WorkflowActionG0SchemaProfile.MaximumOutputBytes);
        using var stringOverLimit = JsonDocument.Parse(JsonSerializer.Serialize(
            new string('a', WorkflowActionG0SchemaProfile.MaximumStringBytes + 1)));
        Assert.Throws<ArgumentException>(() => WorkflowActionG0SchemaProfile.ValidateInstance(
            stringOverLimit.RootElement, WorkflowActionG0SchemaProfile.MaximumOutputBytes));

        using var arrayAtLimit = JsonDocument.Parse(JsonSerializer.Serialize(
            new int[WorkflowActionG0SchemaProfile.MaximumArrayItems]));
        WorkflowActionG0SchemaProfile.ValidateInstance(
            arrayAtLimit.RootElement, WorkflowActionG0SchemaProfile.MaximumOutputBytes);
        using var arrayOverLimit = JsonDocument.Parse(JsonSerializer.Serialize(
            new int[WorkflowActionG0SchemaProfile.MaximumArrayItems + 1]));
        Assert.Throws<ArgumentException>(() => WorkflowActionG0SchemaProfile.ValidateInstance(
            arrayOverLimit.RootElement, WorkflowActionG0SchemaProfile.MaximumOutputBytes));

        using var depthAtLimit = BuildNestedArray(WorkflowActionG0SchemaProfile.MaximumDepth - 1);
        WorkflowActionG0SchemaProfile.ValidateInstance(
            depthAtLimit.RootElement, WorkflowActionG0SchemaProfile.MaximumOutputBytes);
        using var depthOverLimit = BuildNestedArray(WorkflowActionG0SchemaProfile.MaximumDepth);
        Assert.Throws<ArgumentException>(() => WorkflowActionG0SchemaProfile.ValidateInstance(
            depthOverLimit.RootElement, WorkflowActionG0SchemaProfile.MaximumOutputBytes));
    }

    [Fact]
    public void 扩展方法只转发到可选接口且旧Host给出稳定错误()
    {
        using var schema = JsonDocument.Parse(
            "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}");
        var descriptor = new WorkflowActionDescriptor(
            new WorkflowActionId("myavalonia.plugin.sample.workflow.echo"),
            "回显", "G0", schema.RootElement, schema.RootElement,
            WorkflowActionRiskFlags.None, WorkflowActionConfirmationPolicy.Never);
        var supported = new CandidateRegistration();

        supported.AddWorkflowAction<TestHandler>(descriptor);
        supported.UseWorkflowActionGateway();
        Assert.Same(descriptor, supported.Descriptor);
        Assert.True(supported.GatewayRequested);

        IPluginRegistration oldHost = new OldRegistration();
        var exception = Assert.Throws<NotSupportedException>(() =>
            oldHost.UseWorkflowActionGateway());
        Assert.Contains("3.1.0", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Gateway请求和原有注册接口均不允许伪造调用者治理事实()
    {
        var requestProperties = typeof(WorkflowActionInvocationRequest).GetProperties()
            .Select(property => property.Name).Order().ToArray();
        Assert.Equal(["ActionId", "Arguments"], requestProperties);
        Assert.DoesNotContain(requestProperties, name =>
            name.Contains("Caller", StringComparison.Ordinal) ||
            name.Contains("Owner", StringComparison.Ordinal) ||
            name.Contains("Author", StringComparison.Ordinal));

        Assert.DoesNotContain(
            typeof(IPluginRegistration).GetMethods(),
            method => method.Name.Contains("WorkflowAction", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Provider和Consumer位于独立ALC并通过CallerBoundGateway完成Json调用()
    {
        var pluginRoot = RequiredEnvironmentPath("MYAVALONIA_WORKFLOW_G0_PLUGIN_ROOT");
        var snapshot = AssemblyLoaderHelper.Discover(pluginRoot);
        Assert.Empty(snapshot.Diagnostics);
        Assert.Equal(2, snapshot.Assemblies.Count);
        var providerAssembly = Assert.Single(
            snapshot.Assemblies,
            assembly => assembly.GetName().Name == "WorkflowActionG0.Provider");
        var consumerAssembly = Assert.Single(
            snapshot.Assemblies,
            assembly => assembly.GetName().Name == "WorkflowActionG0.Consumer");
        var providerLoadContext = AssemblyLoadContext.GetLoadContext(providerAssembly);
        var consumerLoadContext = AssemblyLoadContext.GetLoadContext(consumerAssembly);
        Assert.NotSame(AssemblyLoadContext.Default, providerLoadContext);
        Assert.NotSame(AssemblyLoadContext.Default, consumerLoadContext);
        Assert.NotSame(providerLoadContext, consumerLoadContext);
        Assert.Same(
            AssemblyLoadContext.Default,
            AssemblyLoadContext.GetLoadContext(typeof(IWorkflowActionHandler).Assembly));

        var handlerType = providerAssembly.GetType(
            "WorkflowActionG0.Provider.EchoWorkflowActionHandler", throwOnError: true)!;
        Assert.True(typeof(IWorkflowActionHandler).IsAssignableFrom(handlerType));
        var handler = Assert.IsAssignableFrom<IWorkflowActionHandler>(
            Activator.CreateInstance(handlerType));
        var privateDto = handlerType.GetNestedType("PrivateInput", BindingFlags.NonPublic);
        Assert.NotNull(privateDto);
        Assert.False(privateDto!.IsVisible);
        Assert.DoesNotContain(
            handlerType.GetMethod(nameof(IWorkflowActionHandler.InvokeAsync))!.GetParameters(),
            parameter => parameter.ParameterType.Assembly == providerAssembly);

        var consumerType = consumerAssembly.GetType(
            "WorkflowActionG0.Consumer.ConsumerInvoker", throwOnError: true)!;
        var consumer = Activator.CreateInstance(consumerType)!;
        var consumerMethod = consumerType.GetMethod("InvokeAsync")!;
        Assert.DoesNotContain(
            consumerMethod.GetParameters(),
            parameter => parameter.ParameterType.Assembly == consumerAssembly);

        using var arguments = JsonDocument.Parse("{\"value\":\"跨 ALC\"}");
        var progress = new List<WorkflowActionProgress>();
        var gateway = new CallerBoundGateway(
            handler,
            new PluginId("myavalonia.plugin.workflow-studio"),
            new InlineProgress(progress.Add));
        var invocation = Assert.IsType<Task<WorkflowActionInvocationResult>>(
            consumerMethod.Invoke(
                consumer,
                [gateway, arguments.RootElement, CancellationToken.None]));
        var result = await invocation;
        Assert.Equal(WorkflowActionInvocationStatus.Succeeded, result.Status);
        var output = Assert.IsType<JsonElement>(result.Output);
        Assert.Equal("跨 ALC", output.GetProperty("Value").GetString());
        Assert.Equal("myavalonia.plugin.workflow-studio", output.GetProperty("caller").GetString());
        Assert.Single(progress);
    }

    [Fact]
    public void 真实三零插件包可由候选三一Host发现并完成组合()
    {
        var pluginRoot = RequiredEnvironmentPath("MYAVALONIA_WORKFLOW_G0_OLD_PLUGIN_ROOT");
        var snapshot = AssemblyLoaderHelper.Discover(pluginRoot);
        Assert.Empty(snapshot.Diagnostics);
        Assert.Equal("MyPlugTest", Assert.Single(snapshot.Assemblies).GetName().Name);
        var catalog = PluginModuleCatalog.Discover(snapshot);
        var diagnosticsRoot = Path.Combine(
            Path.GetTempPath(), $"workflow-g0-old-plugin-{Guid.NewGuid():N}");
        Directory.CreateDirectory(diagnosticsRoot);
        using var diagnostics = HostDiagnosticSession.Start(diagnosticsRoot);
        var registryBuilder = new PluginRegistryBuilder();
        using var pluginProviders = new PluginProviderOwner();
        var documentScopes = new DocumentScopeRegistry();
        var services = new ServiceCollection();
        services.AddApplicationServices(registryBuilder, pluginProviders, documentScopes);
        services.AddViewModels();
        services.AddSingleton(diagnostics);
        services.AddSingleton<IHostDiagnosticSink>(diagnostics);
        services.AddSingleton(catalog);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
        try
        {
            pluginProviders.Compose(
                catalog, provider, registryBuilder, documentScopes, diagnostics);
            var plugin = Assert.Single(provider.GetRequiredService<PluginRegistry>().Plugins);
            Assert.Equal("myavalonia.plugin.my-plug-test", plugin.Manifest.PluginId.Value);
            Assert.Contains("3.0.0", plugin.Manifest.Sdk.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            documentScopes.CloseAll();
            diagnostics.Dispose();
            Directory.Delete(diagnosticsRoot, recursive: true);
        }
    }

    private static string RequiredEnvironmentPath(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        Assert.False(string.IsNullOrWhiteSpace(value), $"缺少 G0 环境变量 {name}。");
        return Path.GetFullPath(value!);
    }

    private static string BuildSchemaWithExactBytes(int byteCount)
    {
        const string prefix = "{\"type\":\"object\",\"description\":\"";
        const string suffix = "\",\"properties\":{},\"additionalProperties\":false}";
        var contentBytes = byteCount - Encoding.UTF8.GetByteCount(prefix + suffix);
        if (contentBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteCount));
        }
        return prefix + new string('a', contentBytes) + suffix;
    }

    private static JsonDocument BuildObjectSchema(int propertyCount)
    {
        var builder = new StringBuilder("{\"type\":\"object\",\"properties\":{");
        for (var index = 0; index < propertyCount; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }
            builder.Append("\"p").Append(index).Append("\":{\"type\":\"string\"}");
        }
        builder.Append("},\"additionalProperties\":false}");
        return JsonDocument.Parse(builder.ToString());
    }

    private static JsonDocument BuildJsonWithExactBytes(int byteCount, int stringCount)
    {
        var fixedBuilder = new StringBuilder("{");
        for (var index = 0; index < stringCount; index++)
        {
            if (index > 0)
            {
                fixedBuilder.Append(',');
            }
            fixedBuilder.Append("\"p").Append(index).Append("\":\"\"");
        }
        fixedBuilder.Append('}');
        var remainingBytes = byteCount - Encoding.UTF8.GetByteCount(fixedBuilder.ToString());

        var builder = new StringBuilder("{");
        for (var index = 0; index < stringCount; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }
            var currentBytes = Math.Min(
                WorkflowActionG0SchemaProfile.MaximumStringBytes,
                remainingBytes);
            builder.Append("\"p").Append(index).Append("\":\"")
                .Append('a', currentBytes).Append('"');
            remainingBytes -= currentBytes;
        }
        builder.Append('}');
        if (remainingBytes != 0 || Encoding.UTF8.GetByteCount(builder.ToString()) != byteCount)
        {
            throw new InvalidOperationException("无法构造精确大小的 G0 JSON 夹具。");
        }
        return JsonDocument.Parse(builder.ToString());
    }

    private static JsonDocument BuildNestedArray(int containerCount) =>
        JsonDocument.Parse(new string('[', containerCount) + "0" + new string(']', containerCount));

    private sealed class InlineProgress(Action<WorkflowActionProgress> report)
        : IProgress<WorkflowActionProgress>
    {
        public void Report(WorkflowActionProgress value) => report(value);
    }

    private sealed class TestHandler : IWorkflowActionHandler
    {
        public ValueTask<JsonElement> InvokeAsync(
            JsonElement arguments,
            WorkflowActionContext context,
            CancellationToken cancellationToken) => ValueTask.FromResult(arguments.Clone());
    }

    /// <summary>
    /// 模拟 Host 为一个 Consumer 创建的最小 Gateway。调用者身份由构造阶段绑定，请求无法覆盖；
    /// 进入 Handler 前和接收返回后都显式克隆 JSON，固定 G0 的 ALC 所有权规则。
    /// </summary>
    private sealed class CallerBoundGateway(
        IWorkflowActionHandler handler,
        PluginId callerId,
        IProgress<WorkflowActionProgress> progress) : IWorkflowActionGateway
    {
        public IReadOnlyList<WorkflowActionDescriptor> GetAvailableActions() => [];

        public IWorkflowActionRun CreateRun() => new Run(handler, callerId, progress);

        private sealed class Run(
            IWorkflowActionHandler handler,
            PluginId callerId,
            IProgress<WorkflowActionProgress> fallbackProgress) : IWorkflowActionRun
        {
        public async Task<WorkflowActionInvocationResult> InvokeAsync(
            WorkflowActionInvocationRequest request,
            IProgress<WorkflowActionProgress>? progress,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            Assert.Equal(
                "myavalonia.plugin.workflow-g0-provider.workflow.echo",
                request.ActionId.Value);
            var invocationId = Guid.NewGuid();
            var inputSnapshot = request.Arguments.Clone();
            var output = await handler.InvokeAsync(
                inputSnapshot,
                new WorkflowActionContext(invocationId, callerId, progress ?? fallbackProgress),
                cancellationToken);
            var outputSnapshot = output.Clone();
            return new WorkflowActionInvocationResult(
                invocationId,
                WorkflowActionInvocationStatus.Succeeded,
                outputSnapshot,
                failure: null);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private class OldRegistration : IPluginRegistration
    {
        public PluginId PluginId { get; } = new("myavalonia.plugin.sample");
        public IServiceCollection Services { get; } = new ServiceCollection();
        public void UseLifecycle<TLifecycle>() where TLifecycle : class, IPluginLifecycle =>
            throw new NotSupportedException();
        public void AddDocument<TDocument, TView>(DocumentDescriptor descriptor)
            where TDocument : class, IPluginDocument where TView : Control, new() =>
            throw new NotSupportedException();
        public void AddPersistableDocument<TDocument, TView>(DocumentDescriptor descriptor)
            where TDocument : class, IPersistablePluginDocument where TView : Control, new() =>
            throw new NotSupportedException();
        public void AddTool<TTool, TView>(ToolDescriptor descriptor)
            where TTool : class where TView : Control, new() =>
            throw new NotSupportedException();
    }

    private sealed class CandidateRegistration : OldRegistration, IWorkflowActionRegistration
    {
        internal WorkflowActionDescriptor? Descriptor { get; private set; }
        internal bool GatewayRequested { get; private set; }
        public void AddWorkflowAction<THandler>(WorkflowActionDescriptor descriptor)
            where THandler : class, IWorkflowActionHandler => Descriptor = descriptor;
        public void UseWorkflowActionGateway() => GatewayRequested = true;
    }
}
