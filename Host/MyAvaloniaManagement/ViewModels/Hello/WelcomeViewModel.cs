using System;
using System.Reflection;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagement.Business.Constants;

namespace MyAvaloniaManagement.ViewModels.Hello;

internal sealed partial class WelcomeViewModel : Document
{
    private const string DefaultIntroduction =
        "MyAvaloniaManagement 是基于 Avalonia 与 Dock 构建的插件化桌面框架，" +
        "用可停靠布局组织工具，用独立插件扩展业务能力。";

    private readonly Action<string>? _showTool;
    private string _text = DefaultIntroduction;

    public WelcomeViewModel()
    {
    }

    internal WelcomeViewModel(Action<string> showTool)
    {
        _showTool = showTool ?? throw new ArgumentNullException(nameof(showTool));
    }

    /// <summary>
    /// 获取欢迎页显示的运行时版本。
    /// </summary>
    public string VersionText => $"版本 {GetVersion()}";

    public string Text
    {
        get => _text;
        set
        {
            if (value != _text)
            {
                SetProperty(ref _text, value);
                IsModified = false;
            }
        }
    }

    [RelayCommand]
    private void OpenPluginMenu() =>
        _showTool?.Invoke(DockNameConstant.PlugGroupMenu);

    [RelayCommand]
    private void OpenToolManagement() =>
        _showTool?.Invoke(DockNameConstant.ToolManagement);

    private static string GetVersion()
    {
        // 设计意图：产品版本由宿主程序集拥有。若读取 EntryAssembly，单元测试、Harness
        // 或未来引导器会把自己的版本误显示为产品版本，造成发布信息与实际宿主不一致。
        var assembly = typeof(WelcomeViewModel).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            var metadataSeparator = informationalVersion.IndexOf('+');
            return metadataSeparator >= 0
                ? informationalVersion[..metadataSeparator]
                : informationalVersion;
        }

        return assembly.GetName().Version?.ToString(3) ?? "1.0.0";
    }
}
