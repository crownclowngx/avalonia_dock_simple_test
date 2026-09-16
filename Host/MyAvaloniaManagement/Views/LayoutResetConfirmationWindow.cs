using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace MyAvaloniaManagement.Views;

/// <summary>只确认布局重置，不处理文件或释放文档；关闭对话框等同取消。</summary>
internal sealed class LayoutResetConfirmationWindow : Window
{
    internal LayoutResetConfirmationWindow()
    {
        Title = "重置布局";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var cancel = new Button { Content = "取消", IsCancel = true };
        var confirm = new Button { Content = "重置布局" };
        cancel.Click += (_, _) => Close(false);
        confirm.Click += (_, _) => Close(true);
        Content = new StackPanel
        {
            Margin = new Thickness(20), Spacing = 18,
            Children =
            {
                new TextBlock { Text = "工具将恢复默认隐藏状态，浮动文档回到主窗口。\n已打开文档及其未保存修改都会保留。", TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 10, Children = { cancel, confirm } },
            },
        };
    }
}
