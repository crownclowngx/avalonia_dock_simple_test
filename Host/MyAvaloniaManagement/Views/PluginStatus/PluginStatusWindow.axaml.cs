using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
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

    /// <summary>平台文件选择只留在 View；关闭窗口或选择取消都不会触发后续读取。</summary>
    private async void ImportReportClick(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not PluginStatusWindowViewModel model) return;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "导入插件兼容报告", AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("兼容报告 JSON") { Patterns = ["*.json"] }]
            });
            try
            {
                if (ReferenceEquals(DataContext, model) && files.Count == 1 && files[0].TryGetLocalPath() is { } path)
                    await model.ImportReportAsync(path);
            }
            finally { foreach (var file in files) file.Dispose(); }
        }
        catch (Exception)
        { if (ReferenceEquals(DataContext, model)) model.ErrorMessage = "无法选择报告文件，请重试。"; }
    }

    private async void ExportSummaryClick(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not PluginStatusWindowViewModel model || !model.HasSelection) return;
        var text = model.CreateDiagnosticText();
        try
        {
            using var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            { Title = "导出插件诊断摘要", SuggestedFileName = "plugin-diagnostics.txt", DefaultExtension = "txt" });
            if (file is null || !ReferenceEquals(DataContext, model)) return;
            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(text);
            if (ReferenceEquals(DataContext, model)) model.CopyFeedback = "诊断摘要已导出。";
        }
        catch (Exception)
        { if (ReferenceEquals(DataContext, model)) model.CopyFeedback = "导出失败，请重试。"; }
    }
}
