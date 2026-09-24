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

    /// <summary>View 只适配文件选择；ZIP 的解包、校验和路径规则全部由安装服务处理。</summary>
    private async void InstallZipClick(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not PluginStatusWindowViewModel model || !model.CanInspectPackage) return;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            { Title = "选择插件 ZIP 安装包", AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("插件 ZIP") { Patterns = ["*.zip"] }] });
            try
            {
                if (ReferenceEquals(DataContext, model) && files.Count == 1 && files[0].TryGetLocalPath() is { } path)
                    await model.InspectPackageAsync(path);
            }
            finally { foreach (var file in files) file.Dispose(); }
        }
        catch (Exception) { if (ReferenceEquals(DataContext, model)) model.InstallationFeedback = "无法选择插件安装包，请重试。"; }
    }

    private async void SelectPackageManifestClick(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not PluginStatusWindowViewModel model || !model.CanChoosePackageManifest) return;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            { Title = "选择配套发布清单", AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("发布清单 JSON") { Patterns = ["*.manifest.json", "*.json"] }] });
            try
            {
                if (ReferenceEquals(DataContext, model) && files.Count == 1 && files[0].TryGetLocalPath() is { } path)
                    await model.SelectPackageManifestAsync(path);
            }
            finally { foreach (var file in files) file.Dispose(); }
        }
        catch (Exception) { if (ReferenceEquals(DataContext, model)) model.InstallationFeedback = "无法选择配套清单，请重试。"; }
    }
}
