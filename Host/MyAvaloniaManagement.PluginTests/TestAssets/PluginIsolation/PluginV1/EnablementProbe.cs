using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using MyAvaloniaManagement.PluginSdk;
using MyAvaloniaManagement.PluginSdk.UI;

namespace PluginIsolation.Plugin;

/// <summary>重启验收专用入口；只有测试修改清单才会使用，普通隔离夹具入口不变。</summary>
public sealed class EnablementProbeModule : IPluginModule
{
    public EnablementProbeModule() => EnablementTrace.Write("module");
    public void Configure(IPluginRegistration registration)
    {
        EnablementTrace.Write("configure");
        var id = registration.PluginId.Value;
        registration.UseLifecycle<EnablementProbeLifecycle>();
        registration.AddIcon("probe", new VectorIconDefinition("M0,0 H10 V10 Z", 10, 10));
        var document = new DocumentTypeId(id + ".document.probe");
        registration.AddDocument<EnablementDocument, EnablementDocumentView>(new(document, "探针", "测试", "测试"));
        registration.AddTool<EnablementTool, EnablementToolView>(new(new(id + ".tool.probe"), "探针", "测试", ToolDockSide.Right, ToolCloseBehavior.Hide));
        var command = new CommandId(id + ".command.probe");
        registration.AddDocumentCommand(new(command, "探针命令", "测试"), document);
        registration.AddMenuCommandContribution(new(new(id + ".command-placement.probe"), command, WorkbenchMenuLocations.ToolsShared, "probe", 0));
        using var schema = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}");
        registration.AddWorkflowAction<EnablementAction>(new(new(id + ".workflow.probe"), "探针动作", "测试",
            schema.RootElement, schema.RootElement, WorkflowActionRiskFlags.None, WorkflowActionConfirmationPolicy.Never));
    }
}

/// <summary>通过文件探针记录私有 Provider 的实际激活与生命周期；禁用阶段必须没有任何记录。</summary>
public sealed class EnablementProbeLifecycle : IPluginLifecycle, IDisposable
{
    public EnablementProbeLifecycle() => EnablementTrace.Write("lifecycle-constructor");
    public Task InitializeAsync(CancellationToken cancellationToken) { EnablementTrace.Write("initialize"); return Task.CompletedTask; }
    public Task ShutdownAsync(CancellationToken cancellationToken) { EnablementTrace.Write("shutdown"); return Task.CompletedTask; }
    public void Dispose() => EnablementTrace.Write("dispose");
}

public sealed class EnablementDocument : IPluginDocument
{
    public DocumentPresentationState Presentation => new("探针");
    public event EventHandler? PresentationChanged { add { } remove { } }
    public ValueTask InitializeAsync(DocumentActivation activation, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
public sealed class EnablementTool;
public sealed class EnablementDocumentView : UserControl;
public sealed class EnablementToolView : UserControl;
public sealed class EnablementAction : IWorkflowActionHandler
{
    public ValueTask<JsonElement> InvokeAsync(JsonElement arguments, WorkflowActionContext context, CancellationToken cancellationToken)
        => ValueTask.FromResult(arguments.Clone());
}

internal static class EnablementTrace
{
    internal static void Write(string phase)
    {
        var path = Environment.GetEnvironmentVariable("MYAVALONIA_ENABLEMENT_TEST_TRACE");
        if (!string.IsNullOrEmpty(path)) File.AppendAllText(path, phase + Environment.NewLine);
    }
}
