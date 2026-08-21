using Avalonia.Platform.Storage;
using MyAvaloniaManagement.PluginSdk.UI;

namespace DaTangAccountingHelpPlug.Business;

/// <summary>定义发票导入流程所需的两个文件选择动作。</summary>
/// <remarks>
/// ViewModel 只表达“选择输入工作簿”和“选择输出工作簿”，不拥有窗口、文件类型或主窗口定位规则。
/// 原生选择器不能可靠强制关闭，因此取消令牌表示结果是否仍允许提交，而不是强制关闭系统窗口。
/// </remarks>
public interface IInvoiceFileDialogService
{
    /// <summary>选择一个输入工作簿；取消或关闭后返回 <see langword="null"/>。</summary>
    Task<string?> PickInputWorkbookAsync(string title, CancellationToken cancellationToken = default);

    /// <summary>选择发票汇总表输出路径；取消或关闭后返回 <see langword="null"/>。</summary>
    Task<string?> PickOutputWorkbookAsync(CancellationToken cancellationToken = default);
}

/// <summary>定义银行余额调节流程所需的文件选择动作。</summary>
/// <remarks>
/// 接口只包含同一业务流程的来源、配置与报告路径选择。配置实际读写、Excel 读取和报告生成仍由
/// 原有业务服务负责，窗口适配器不解释文件正文，也不把路径保存为自身状态。
/// </remarks>
public interface IReconciliationFileDialogService
{
    /// <summary>选择企业账、银行账或到款表。</summary>
    Task<string?> PickSourceWorkbookAsync(string title, CancellationToken cancellationToken = default);

    /// <summary>选择需要导入的对账配置。</summary>
    Task<string?> PickConfigurationImportAsync(CancellationToken cancellationToken = default);

    /// <summary>选择对账配置导出路径。</summary>
    Task<string?> PickConfigurationExportAsync(CancellationToken cancellationToken = default);

    /// <summary>选择银行余额调节报告输出路径。</summary>
    Task<string?> PickReportOutputAsync(
        string suggestedFileName,
        CancellationToken cancellationToken = default);
}

/// <summary>定义发票日志复制所需的最小剪贴板能力。</summary>
public interface IPluginClipboardService
{
    /// <summary>尝试写入文本；没有可用剪贴板时返回 <see langword="false"/>。</summary>
    Task<bool> TrySetTextAsync(string text, CancellationToken cancellationToken = default);
}

/// <summary>把 DaTang 的业务交互翻译为最终 UI SDK 的窄窗口 Host Port。</summary>
/// <remarks>
/// 本类无状态并由插件 Provider 作为 singleton 持有。三个小接口让各 ViewModel 只看到自己需要的
/// 用例，而唯一实现集中维护文件类型和建议文件名，避免选择器参数散落在多个 scoped 模型中。
/// </remarks>
public sealed class DaTangWindowInteractionService(
    IPluginWindowInteraction windowInteraction) :
    IInvoiceFileDialogService,
    IReconciliationFileDialogService,
    IPluginClipboardService
{
    private readonly IPluginWindowInteraction _windowInteraction =
        windowInteraction ?? throw new ArgumentNullException(nameof(windowInteraction));

    /// <inheritdoc />
    public async Task<string?> PickInputWorkbookAsync(
        string title,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var files = await _windowInteraction.PickOpenFilesAsync(
            CreateOpenOptions(title, "Excel 文件", "*.xlsx"),
            cancellationToken);
        return files.Count == 0 ? null : files[0];
    }

    /// <inheritdoc />
    public Task<string?> PickOutputWorkbookAsync(CancellationToken cancellationToken = default) =>
        _windowInteraction.PickSaveFileAsync(new FilePickerSaveOptions
        {
            Title = "保存发票汇总表",
            DefaultExtension = "xlsx",
            SuggestedFileName = "发票汇总表",
            FileTypeChoices = [new FilePickerFileType("Excel 文件 (.xlsx)") { Patterns = ["*.xlsx"] }],
        }, cancellationToken);

    /// <inheritdoc />
    public async Task<string?> PickSourceWorkbookAsync(
        string title,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var files = await _windowInteraction.PickOpenFilesAsync(
            CreateOpenOptions(title, "Excel/CSV 文件", "*.xlsx", "*.xlsm", "*.csv"),
            cancellationToken);
        return files.Count == 0 ? null : files[0];
    }

    /// <inheritdoc />
    public async Task<string?> PickConfigurationImportAsync(
        CancellationToken cancellationToken = default)
    {
        var files = await _windowInteraction.PickOpenFilesAsync(
            CreateOpenOptions("导入银行余额调节配置", "JSON 配置", "*.json"),
            cancellationToken);
        return files.Count == 0 ? null : files[0];
    }

    /// <inheritdoc />
    public Task<string?> PickConfigurationExportAsync(
        CancellationToken cancellationToken = default) =>
        _windowInteraction.PickSaveFileAsync(new FilePickerSaveOptions
        {
            Title = "导出银行余额调节配置",
            SuggestedFileName = "reconciliation-profiles.json",
            DefaultExtension = "json",
            FileTypeChoices = [new FilePickerFileType("JSON 配置") { Patterns = ["*.json"] }],
        }, cancellationToken);

    /// <inheritdoc />
    public Task<string?> PickReportOutputAsync(
        string suggestedFileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suggestedFileName);
        return _windowInteraction.PickSaveFileAsync(new FilePickerSaveOptions
        {
            Title = "保存银行余额调节表",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "xlsx",
            FileTypeChoices = [new FilePickerFileType("Excel 工作簿") { Patterns = ["*.xlsx"] }],
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> TrySetTextAsync(
        string text,
        CancellationToken cancellationToken = default) =>
        _windowInteraction.TrySetClipboardTextAsync(text, cancellationToken);

    private static FilePickerOpenOptions CreateOpenOptions(
        string title,
        string typeName,
        params string[] patterns) => new()
    {
        Title = title,
        AllowMultiple = false,
        FileTypeFilter = [new FilePickerFileType(typeName) { Patterns = patterns }],
    };
}
