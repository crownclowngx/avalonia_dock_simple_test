using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using MyAvaloniaManagement.Business.Constants;
using MyAvaloniaManagement.PluginSdk;

namespace MyAvaloniaManagement.ViewModels.Hello;

internal sealed partial class WelcomeViewModel : ObservableObject, IPluginDocument
{
    private const string DefaultIntroduction =
        "MyAvaloniaManagement 是基于 Avalonia 与 Dock 构建的插件化桌面框架，" +
        "用可停靠布局组织工具，用独立插件扩展业务能力。";

    private readonly Action<ToolTypeId>? _showTool;
    private string _text = DefaultIntroduction;
    private string _title = "欢迎";

    public WelcomeViewModel()
    {
    }

    public WelcomeViewModel(Action<ToolTypeId> showTool)
    {
        _showTool = showTool ?? throw new ArgumentNullException(nameof(showTool));
    }

    /// <summary>获取声明式贡献使用的只读展示状态。</summary>
    public DocumentPresentationState Presentation => new(_title);

    /// <summary>
    /// 标题投影变化通知；Dock Adapter 在 G6 后仍只通过普通模型契约连接该通知，
    /// Welcome 不知道 Session、Factory 或 Dock 类型。
    /// </summary>
    public event EventHandler? PresentationChanged;

    /// <summary>应用宿主已经校验的初始标题；Welcome 没有异步业务初始化。</summary>
    public ValueTask InitializeAsync(
        DocumentActivation activation,
        CancellationToken cancellationToken)
    {
        if (activation is not NewDocumentActivation newActivation)
        {
            throw new NotSupportedException("Host Welcome 只支持新建激活。");
        }
        InitializeHost(newActivation, cancellationToken);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// 由 Host Workspace 激活路径同步应用 Welcome 的初始展示状态。
    /// </summary>
    /// <remarks>
    /// Host 默认布局本身是同步 Dock 协议，且 Welcome 没有外部 I/O。把真实逻辑集中在此方法，
    /// 可让 Host 直接完成初始化，同时保留 IPluginDocument 的契约实现供统一 Adapter 使用，避免
    /// 维护两套标题规则。
    /// </remarks>
    internal void InitializeHost(
        NewDocumentActivation activation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(activation);
        cancellationToken.ThrowIfCancellationRequested();
        _title = activation.Title;
        PresentationChanged?.Invoke(this, EventArgs.Empty);
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
            }
        }
    }

    [RelayCommand]
    private void OpenPluginMenu() =>
        _showTool?.Invoke(HostExtensionIds.PluginMenu);

    [RelayCommand]
    private void OpenToolManagement() =>
        _showTool?.Invoke(HostExtensionIds.ToolManagement);

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
