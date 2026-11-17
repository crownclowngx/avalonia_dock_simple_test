using System.Text.Json;
using System.Text.Json.Serialization;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace WorkflowActionG1.Provider;

/// <summary>G1 跨 ALC 真实 Provider，只声明一个无风险回显 Action。</summary>
public sealed class ProviderModule : IPluginModule
{
    /// <inheritdoc />
    public void Configure(IPluginRegistration registration)
    {
        using var input = JsonDocument.Parse("""
            {"type":"object","properties":{"value":{"type":"string","maxLength":64}},"required":["value"],"additionalProperties":false}
            """);
        using var output = JsonDocument.Parse("""
            {"type":"object","properties":{"echoed":{"type":"string","maxLength":64},"caller":{"type":"string","maxLength":128}},"required":["echoed","caller"],"additionalProperties":false}
            """);
        registration.AddWorkflowAction<EchoHandler>(new WorkflowActionDescriptor(
            new WorkflowActionId("myavalonia.plugin.workflow-g1-provider.workflow.echo"),
            "G1 回显",
            "验证跨 ALC、私有 DTO 与 invocation scope。",
            input.RootElement,
            output.RootElement,
            WorkflowActionRiskFlags.None,
            WorkflowActionConfirmationPolicy.Never));

        using var artifactOutput = JsonDocument.Parse("""
            {"type":"object","properties":{"audio":{"type":"object","properties":{
              "id":{"type":"string","maxLength":32},
              "mediaType":{"type":"string","maxLength":128},
              "sizeBytes":{"type":"integer","minimum":1}
            },"required":["id","mediaType","sizeBytes"],"additionalProperties":false}},"required":["audio"],"additionalProperties":false}
            """);
        registration.AddWorkflowAction<ArtifactHandler>(new WorkflowActionDescriptor(
            new WorkflowActionId("myavalonia.plugin.workflow-g1-provider.workflow.artifact"),
            "G1 内存产物", "验证跨 ALC 内存产物读取。",
            input.RootElement, artifactOutput.RootElement, WorkflowActionRiskFlags.None,
            WorkflowActionConfirmationPolicy.Never));
    }
}

public sealed class ArtifactHandler : IWorkflowActionHandler
{
    public async ValueTask<JsonElement> InvokeAsync(
        JsonElement arguments, WorkflowActionContext context, CancellationToken cancellationToken)
    {
        var writer = context.ArtifactWriter ?? throw new InvalidOperationException();
        await writer.WriteAsync("MP3"u8.ToArray(), cancellationToken);
        await writer.WriteAsync(" sample"u8.ToArray(), cancellationToken);
        var audio = writer.Seal("audio", "audio/mpeg");
        return JsonSerializer.SerializeToElement(new
        {
            audio = new { id = audio.Id, mediaType = audio.MediaType, sizeBytes = audio.SizeBytes },
        });
    }
}

/// <summary>使用 Provider 私有 DTO 的 scoped Handler。</summary>
public sealed class EchoHandler : IWorkflowActionHandler, IAsyncDisposable
{
    /// <inheritdoc />
    public ValueTask<JsonElement> InvokeAsync(
        JsonElement arguments,
        WorkflowActionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var input = arguments.Deserialize<PrivateInput>() ??
                    throw new InvalidOperationException("G1 输入无法解析。");
        context.Progress.Report(new WorkflowActionProgress("echo", 100, "完成"));
        return ValueTask.FromResult(JsonSerializer.SerializeToElement(new
        {
            echoed = input.Value,
            caller = context.CallerId.Value,
        }));
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private sealed record PrivateInput(
        [property: JsonPropertyName("value")] string Value);
}
