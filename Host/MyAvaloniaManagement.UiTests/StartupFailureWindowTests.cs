using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using MyAvaloniaManagement.Business.Diagnostics;
using MyAvaloniaManagement.Views;
using Xunit;

namespace MyAvaloniaManagement.UiTests;

/// <summary>
/// 验证致命启动错误窗口不依赖主工作台服务，并只展示经过清理的信息。
/// </summary>
public sealed class StartupFailureWindowTests
{
    [AvaloniaFact]
    public void 错误窗口显示稳定摘要且不泄漏技术堆栈()
    {
        var context = new HostStartupFailureContext(
            [
                new HostDiagnosticRecord
                {
                    SessionId = Guid.NewGuid(),
                    Sequence = 1,
                    TimestampUtc = DateTimeOffset.UtcNow,
                    Code = "PLUGIN_ID_DUPLICATE",
                    Severity = HostDiagnosticSeverity.Fatal,
                    Phase = HostDiagnosticPhase.PluginModuleDiscovery,
                    Disposition = HostDiagnosticDisposition.AbortStartup,
                    PluginDirectory = "SamplePlugin",
                    UserMessage = "插件身份重复。",
                    TechnicalDetail = "secret-stack-detail",
                }
            ],
            @"C:\diagnostics\session.jsonl");

        var window = new StartupFailureWindow(context);

        Assert.Contains("1 条启动诊断", window.FindControl<TextBlock>("SummaryText")?.Text);
        Assert.Contains("session.jsonl", window.FindControl<TextBlock>("LogPathText")?.Text);
        var items = Assert.IsAssignableFrom<IEnumerable<string>>(
            window.FindControl<ListBox>("DiagnosticList")?.ItemsSource);
        var text = Assert.Single(items);
        Assert.Contains("PLUGIN_ID_DUPLICATE", text);
        Assert.Contains("插件身份重复", text);
        Assert.DoesNotContain("secret-stack-detail", text);
        Assert.True(window.FindControl<Button>("OpenLogButton")?.IsEnabled);
    }
}
