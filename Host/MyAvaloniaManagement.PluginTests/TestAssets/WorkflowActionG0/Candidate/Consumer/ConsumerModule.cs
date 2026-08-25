using System.Text.Json;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace WorkflowActionG0.Consumer;

/// <summary>G0 独立 Consumer ALC 夹具的合法 Managed Plugin 入口。</summary>
/// <remarks>
/// Consumer 只通过 UI 扩展方法声明自己需要 caller-bound Gateway，不依赖 Host 实现类型，
/// 因而保持依赖倒置；G0 测试会单独验证旧 Host 对该可选能力给出稳定错误。
/// </remarks>
public sealed class ConsumerModule : IPluginModule
{
    /// <inheritdoc />
    public void Configure(IPluginRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        registration.UseWorkflowActionGateway();
    }
}

/// <summary>仅通过冻结 SDK/BCL 类型调用动作的最小 Consumer。</summary>
/// <remarks>
/// 本类型有意位于独立插件程序集。公开调用边界只出现 <see cref="IWorkflowActionGateway"/>、
/// <see cref="JsonElement"/>、<see cref="CancellationToken"/> 和结构化 SDK 结果，
/// 不把 Consumer 私有 DTO 或 Host 实现类型带过 ALC 边界。
/// </remarks>
public sealed class ConsumerInvoker
{
    /// <summary>通过 Host 已绑定调用者身份的 Gateway 调用 Provider 回显动作。</summary>
    public async Task<WorkflowActionInvocationResult> InvokeAsync(
        IWorkflowActionGateway gateway,
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        await using var run = gateway.CreateRun();
        return await run.InvokeAsync(
            new WorkflowActionInvocationRequest(
                new WorkflowActionId("myavalonia.plugin.workflow-g0-provider.workflow.echo"),
                arguments),
            progress: null,
            cancellationToken).ConfigureAwait(false);
    }
}
