using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using MyAvaloniaManagement.ViewModels.PluginStatus;

namespace MyAvaloniaManagement.Views.PluginStatus;

/// <summary>只适配焦点、键盘和剪贴板；业务查询与状态判断交给 ViewModel。</summary>
internal sealed partial class PluginStatusWindow : Window
{
    public PluginStatusWindow()
    {
        InitializeComponent();
        Opened += (_, _) => PluginSearchBox.Focus();
        KeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape) return;
            args.Handled = true;
            Close();
        };
    }

    private async void CopyDiagnosticsClick(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not PluginStatusWindowViewModel model || !model.HasSelection) return;
        try
        {
            if (Clipboard is not { } clipboard)
            {
                model.CopyFeedback = "剪贴板暂不可用，可在详情中选择文字复制。";
                return;
            }
            var selectedKey = model.SelectedItem!.Key;
            await clipboard.SetTextAsync(model.CreateDiagnosticText());
            // 异步复制期间可能切换选择或关闭窗口，成功提示只能落在原来的详情上。
            if (ReferenceEquals(DataContext, model) && model.SelectedItem?.Key == selectedKey)
                model.CopyFeedback = "诊断信息已复制。";
        }
        catch (Exception)
        {
            if (ReferenceEquals(DataContext, model)) model.CopyFeedback = "复制失败，请重试或手动选择文字复制。";
        }
    }
}
