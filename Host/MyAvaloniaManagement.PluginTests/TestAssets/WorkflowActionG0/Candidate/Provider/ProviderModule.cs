using System.Text.Json;
using System.Text.Json.Serialization;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace WorkflowActionG0.Provider;

/// <summary>G0 独立 ALC 夹具的合法 Managed Plugin 入口。</summary>
/// <remarks>入口不登记生产贡献；测试只通过它让真实加载器验证 manifest、deps 和共享 SDK。</remarks>
public sealed class ProviderModule : IPluginModule
{
    /// <inheritdoc />
    public void Configure(IPluginRegistration registration) =>
        ArgumentNullException.ThrowIfNull(registration);
}

/// <summary>使用私有 DTO 的最小非泛型 JSON Handler。</summary>
public sealed class EchoWorkflowActionHandler : IWorkflowActionHandler
{
    /// <inheritdoc />
    public ValueTask<JsonElement> InvokeAsync(
        JsonElement arguments,
        WorkflowActionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var input = arguments.Deserialize<PrivateInput>() ??
                    throw new InvalidOperationException("G0 输入无法解析。");
        context.Progress.Report(new WorkflowActionProgress("echo", 100, "候选 Handler 已完成。"));
        return ValueTask.FromResult(JsonSerializer.SerializeToElement(new
        {
            input.Value,
            caller = context.CallerId.Value,
        }));
    }

    /// <summary>
    /// 私有 DTO 有意留在 Provider 程序集内。测试会检查 public Handler 签名完全不出现本类型，
    /// 证明 JSON 边界允许插件自行使用强类型业务模型而不污染共享契约。
    /// </summary>
    private sealed record PrivateInput([property: JsonPropertyName("value")] string Value);
}
