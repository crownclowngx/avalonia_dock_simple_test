using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using BiliDownloader.Models;

namespace BiliDownloader.Services.Infrastructure;

/// <summary>
/// G4: 破坏性操作确认服务接口。
/// 设计思考：ROADMAP 明确要求"批量删除、重来等破坏性操作会展示任务数量并要求确认"。
/// 通过 DIP 将确认逻辑抽象为接口，实现以下目标：
/// 1. ViewModel 不直接依赖 Avalonia 对话框 API，保持可测试性；
/// 2. 测试中可注入 Fake 实现，验证"确认通过"和"确认拒绝"两条路径；
/// 3. 未来可替换为内联确认条、Toast 确认等不同 UX 形态，不改动 VM 代码。
/// </summary>
public interface IConfirmationService
{
    /// <summary>
    /// 向用户展示确认对话框，等待用户决策。
    /// </summary>
    /// <param name="title">对话框标题（如"批量删除确认"）</param>
    /// <param name="message">确认消息正文（应包含操作数量和影响描述）</param>
    /// <returns>true 表示用户确认执行，false 表示用户取消</returns>
    Task<bool> ConfirmAsync(string title, string message);
}

public interface IUserPromptService : IConfirmationService
{
    Task<DeleteTaskPromptResult> ConfirmDeleteAsync(int taskCount, bool hasOutputFiles);
    Task<bool> ConfirmSubmissionAsync(SubmissionPreflightReport report);
}

/// <summary>
/// 未注入提示边界时采用安全取消，绝不隐式批准破坏性操作。
/// </summary>
public sealed class SafeCancellationConfirmationService : IUserPromptService
{
    public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(false);
    public Task<DeleteTaskPromptResult> ConfirmDeleteAsync(int taskCount, bool hasOutputFiles)
        => Task.FromResult(DeleteTaskPromptResult.Cancelled);
    public Task<bool> ConfirmSubmissionAsync(SubmissionPreflightReport report) => Task.FromResult(false);
}

/// <summary>Application-owned modal prompts for destructive task actions.</summary>
public sealed class AvaloniaUserPromptService : IUserPromptService
{
    public async Task<bool> ConfirmAsync(string title, string message)
    {
        var owner = GetOwner();
        if (owner is null) return false;
        var result = await CreateConfirmationWindow(title, message).ShowDialog<bool>(owner);
        return result;
    }

    public async Task<DeleteTaskPromptResult> ConfirmDeleteAsync(int taskCount, bool hasOutputFiles)
    {
        var owner = GetOwner();
        if (owner is null) return DeleteTaskPromptResult.Cancelled;

        var tempCheck = new CheckBox { Content = "同时删除未完成的临时文件" };
        var outputCheck = new CheckBox
        {
            Content = "同时删除已经下载的成品文件",
            IsEnabled = hasOutputFiles,
        };
        var cancel = new Button { Content = "取消", MinWidth = 76 };
        var confirm = new Button { Content = "删除任务记录", MinWidth = 108 };
        var buttons = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancel, confirm },
        };
        var window = new Window
        {
            Title = "删除任务",
            Width = 430,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = $"将从任务中心移除 {taskCount} 个任务。默认不会删除本地文件。", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    tempCheck,
                    outputCheck,
                    buttons,
                },
            },
        };
        cancel.Click += (_, _) => window.Close(DeleteTaskPromptResult.Cancelled);
        confirm.Click += (_, _) => window.Close(new DeleteTaskPromptResult(
            true, tempCheck.IsChecked == true, outputCheck.IsChecked == true));
        return await window.ShowDialog<DeleteTaskPromptResult>(owner)
            ?? DeleteTaskPromptResult.Cancelled;
    }

    public async Task<bool> ConfirmSubmissionAsync(SubmissionPreflightReport report)
    {
        var owner = GetOwner();
        if (owner is null) return false;
        var issues = report.GlobalIssues.Concat(report.Items.SelectMany(item => item.Issues))
            .Select(issue => "• " + issue.Message).Distinct().Take(8).ToArray();
        var destructive = report.Submission.Profile.ConflictPolicy == FileConflictPolicy.Overwrite;
        var message = $"可提交 {report.ReadyCount} 项，跳过 {report.SkipCount} 项，"
            + $"警告 {report.WarningCount} 项，阻止 {report.BlockedCount} 项。"
            + (issues.Length == 0 ? "" : Environment.NewLine + string.Join(Environment.NewLine, issues));
        var cancel = new Button { Content = "取消", MinWidth = 76 };
        var confirm = new Button
        {
            Content = destructive ? $"确认覆盖 {report.Items.Count(item => item.HasConflict)} 项" : "确认并提交",
            MinWidth = 112,
        };
        var window = new Window
        {
            Title = destructive ? "确认覆盖已有文件" : "确认提交预检结果",
            Width = 520,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        cancel.Click += (_, _) => window.Close(false);
        confirm.Click += (_, _) => window.Close(true);
        window.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 14,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, confirm },
                },
            },
        };
        return await window.ShowDialog<bool>(owner);
    }

    private static Window CreateConfirmationWindow(string title, string message)
    {
        var cancel = new Button { Content = "取消", MinWidth = 76 };
        var confirm = new Button { Content = "继续", MinWidth = 76 };
        var window = new Window
        {
            Title = title,
            Width = 410,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        cancel.Click += (_, _) => window.Close(false);
        confirm.Click += (_, _) => window.Close(true);
        window.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 14,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, confirm },
                },
            },
        };
        return window;
    }

    private static Window? GetOwner()
        => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}
