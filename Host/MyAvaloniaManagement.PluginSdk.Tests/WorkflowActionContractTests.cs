using System.Text.Json;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.PluginSdk.Tests;

/// <summary>验证 Workflow Action 3.1 公共值对象和 JSON 所有权。</summary>
public sealed class WorkflowActionContractTests
{
    [Fact]
    public void ActionId遵循稳定身份规则()
    {
        var id = new WorkflowActionId("myavalonia.plugin.sample.workflow.echo");

        Assert.Equal("myavalonia.plugin.sample.workflow.echo", id.Value);
        Assert.True(WorkflowActionId.TryParse(id.Value, out var parsed));
        Assert.Equal(id, parsed);
        Assert.False(WorkflowActionId.TryParse("Sample Workflow", out _));
    }

    [Fact]
    public void Descriptor请求和结果均克隆Json与集合()
    {
        using var inputSchema = JsonDocument.Parse(
            "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}");
        using var outputSchema = JsonDocument.Parse(
            "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}");
        var pointers = new List<string> { "/password" };
        var descriptor = new WorkflowActionDescriptor(
            new WorkflowActionId("myavalonia.plugin.sample.workflow.echo"),
            "回显",
            "测试回显",
            inputSchema.RootElement,
            outputSchema.RootElement,
            WorkflowActionRiskFlags.HandlesSecret,
            WorkflowActionConfirmationPolicy.OncePerRun,
            pointers);
        pointers.Clear();

        using var arguments = JsonDocument.Parse("{\"value\":1}");
        var request = new WorkflowActionInvocationRequest(descriptor.Id, arguments.RootElement);
        var result = new WorkflowActionInvocationResult(
            Guid.NewGuid(),
            WorkflowActionInvocationStatus.Succeeded,
            arguments.RootElement,
            failure: null);

        Assert.Equal(JsonValueKind.Object, descriptor.InputSchema.ValueKind);
        Assert.Single(descriptor.SensitiveInputPointers);
        Assert.Equal(1, request.Arguments.GetProperty("value").GetInt32());
        Assert.Equal(1, result.Output!.Value.GetProperty("value").GetInt32());
    }

    [Fact]
    public void Progress和Context拒绝非法边界()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WorkflowActionProgress("running", 101, null));
        Assert.Throws<ArgumentException>(() =>
            new WorkflowActionContext(Guid.Empty, new PluginId("myavalonia.plugin.sample"),
                new InlineProgress()));
    }

    private sealed class InlineProgress : IProgress<WorkflowActionProgress>
    {
        public void Report(WorkflowActionProgress value)
        {
        }
    }
}
