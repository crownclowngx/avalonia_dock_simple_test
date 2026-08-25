using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Threading;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.Business.WorkflowActions;

/// <summary>Host internal 授权请求；只携带经过 Host 校验和遮蔽的展示事实。</summary>
internal sealed record WorkflowActionAuthorizationRequest(
    PluginId CallerId,
    PluginId OwnerId,
    WorkflowActionDescriptor Descriptor,
    string RedactedArgumentSummary);

/// <summary>把调用治理与具体 Avalonia 模态窗口分开的窄授权端口。</summary>
internal interface IWorkflowActionAuthorizer
{
    Task<bool> AuthorizeAsync(
        WorkflowActionAuthorizationRequest request,
        CancellationToken cancellationToken);
}

/// <summary>使用宿主拥有的简单模态窗口执行风险确认。</summary>
/// <remarks>
/// 调用器只依赖 <see cref="IWorkflowActionAuthorizer"/>；单元测试可注入确定性替身。生产实现
/// 在无主窗口、UI 已结束、取消或窗口异常时一律拒绝，保证失败关闭。
/// </remarks>
internal sealed class AvaloniaWorkflowActionAuthorizer : IWorkflowActionAuthorizer
{
    public async Task<bool> AuthorizeAsync(
        WorkflowActionAuthorizationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (Dispatcher.UIThread.CheckAccess())
        {
            return await ShowCoreAsync(request, cancellationToken);
        }
        var operation = Dispatcher.UIThread.InvokeAsync(
            () => ShowCoreAsync(request, cancellationToken));
        return await operation;
    }

    private static async Task<bool> ShowCoreAsync(
        WorkflowActionAuthorizationRequest request,
        CancellationToken cancellationToken)
    {
        var owner = (Application.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is null || cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        var approved = false;
        var window = new Window
        {
            Title = "确认 Workflow Action",
            Width = 640,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var approve = new Button { Content = "允许执行", MinWidth = 104 };
        var deny = new Button { Content = "拒绝", MinWidth = 96 };
        approve.Click += (_, _) =>
        {
            approved = true;
            window.Close();
        };
        deny.Click += (_, _) => window.Close();
        window.Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 16,
            Children =
            {
                new TextBlock
                {
                    Text = $"调用插件：{request.CallerId.Value}\n" +
                           $"动作：{request.Descriptor.DisplayName} ({request.Descriptor.Id.Value})\n" +
                           $"所有者：{request.OwnerId.Value}\n风险：{request.Descriptor.Risks}",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = request.RedactedArgumentSummary,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    MaxHeight = 260,
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { approve, deny },
                },
            },
        };

        using var cancellation = cancellationToken.Register(() =>
            Dispatcher.UIThread.Post(window.Close));
        await window.ShowDialog(owner);
        return approved && !cancellationToken.IsCancellationRequested;
    }
}

/// <summary>生成仅供确认窗口短暂显示的敏感字段遮蔽摘要。</summary>
internal static class WorkflowActionArgumentSummary
{
    internal static string Create(
        JsonElement arguments,
        IReadOnlyList<string> sensitivePointers)
    {
        JsonNode? root = JsonNode.Parse(arguments.GetRawText());
        foreach (var pointer in sensitivePointers)
        {
            Redact(root, pointer);
        }
        var text = root?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null";
        return text.Length <= 2048 ? text : text[..2048] + "\n…（摘要已截断）";
    }

    private static void Redact(JsonNode? root, string pointer)
    {
        var segments = pointer.Split('/', StringSplitOptions.RemoveEmptyEntries);
        JsonNode? current = root;
        for (var index = 0; index < segments.Length; index++)
        {
            if (current is not JsonObject obj)
            {
                return;
            }
            var name = segments[index]
                .Replace("~1", "/", StringComparison.Ordinal)
                .Replace("~0", "~", StringComparison.Ordinal);
            if (index == segments.Length - 1)
            {
                if (obj.ContainsKey(name))
                {
                    obj[name] = "***";
                }
                return;
            }
            current = obj[name];
        }
    }
}
