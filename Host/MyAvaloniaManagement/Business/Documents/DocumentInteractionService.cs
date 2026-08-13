using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;

namespace MyAvaloniaManagement.Business.Documents;

internal enum DocumentCloseChoice
{
    Save,
    Discard,
    Cancel,
}

/// <summary>
/// 文档持久化所需的最小用户交互边界。
/// </summary>
/// <remarks>
/// 协调器只依赖语义选择，不依赖 Avalonia Window。这使关闭决策可以在单元测试中精确
/// 编排，也避免把系统文件选择器、Dock 和确认窗口揉进同一个 UI 服务。
/// </remarks>
internal interface IDocumentInteractionService
{
    Task<DocumentCloseChoice> ConfirmCloseAsync(
        IReadOnlyList<string> documentNames,
        bool isApplicationExit);

    Task<bool> ConfirmRecoveryAsync(string fileName);

    Task ShowErrorAsync(string message);
}

/// <summary>
/// 使用简单模态窗口实现文档关闭和恢复确认。
/// </summary>
internal sealed class AvaloniaDocumentInteractionService : IDocumentInteractionService
{
    public Task<DocumentCloseChoice> ConfirmCloseAsync(
        IReadOnlyList<string> documentNames,
        bool isApplicationExit)
    {
        var names = string.Join(
            Environment.NewLine,
            documentNames.Select(name => $"• {name}"));
        var message = isApplicationExit
            ? $"以下 Document 包含尚未保存的更改：{Environment.NewLine}{Environment.NewLine}{names}"
            : $"该 Document 包含尚未保存的更改：{Environment.NewLine}{Environment.NewLine}{names}";
        return ShowChoiceAsync(
            "保存更改",
            message,
            isApplicationExit ? "保存全部" : "保存",
            isApplicationExit ? "放弃全部" : "不保存");
    }

    public async Task<bool> ConfirmRecoveryAsync(string fileName)
    {
        var result = await ShowChoiceAsync(
            "发现恢复备份",
            $"“{fileName}”的主文件已损坏，但存在最近一次成功保存的恢复备份。\n\n是否从备份打开？恢复内容必须另存为新文件，损坏原件不会被修改。",
            "从备份恢复",
            null);
        return result == DocumentCloseChoice.Save;
    }

    public async Task ShowErrorAsync(string message)
    {
        _ = await ShowChoiceAsync(
            "Document 操作失败",
            message,
            "确定",
            null,
            showCancel: false);
    }

    private static async Task<DocumentCloseChoice> ShowChoiceAsync(
        string title,
        string message,
        string acceptText,
        string? discardText,
        bool showCancel = true)
    {
        var owner = (Application.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is null)
        {
            return DocumentCloseChoice.Cancel;
        }

        var result = DocumentCloseChoice.Cancel;
        var window = new Window
        {
            Title = title,
            Width = 520,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        var accept = new Button { Content = acceptText, MinWidth = 96 };
        accept.Click += (_, _) =>
        {
            result = DocumentCloseChoice.Save;
            window.Close();
        };
        buttons.Children.Add(accept);

        if (!string.IsNullOrWhiteSpace(discardText))
        {
            var discard = new Button { Content = discardText, MinWidth = 96 };
            discard.Click += (_, _) =>
            {
                result = DocumentCloseChoice.Discard;
                window.Close();
            };
            buttons.Children.Add(discard);
        }

        if (showCancel)
        {
            var cancel = new Button { Content = "取消", MinWidth = 96 };
            cancel.Click += (_, _) => window.Close();
            buttons.Children.Add(cancel);
        }

        window.Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 20,
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    MaxWidth = 620,
                },
                buttons,
            },
        };

        await window.ShowDialog(owner);
        return result;
    }
}
