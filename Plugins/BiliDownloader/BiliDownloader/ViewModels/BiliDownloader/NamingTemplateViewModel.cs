using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using BiliDownloader.Services.Naming;

namespace BiliDownloader.ViewModels.BiliDownloader;

/// <summary>
/// 命名模板子 ViewModel：管理命名模板的编辑、验证和实时预览。
/// <para>
/// 设计思考：独立 VM 管理命名模板（SRP），不修改 RenamePanelViewModel。
/// 两者并列共存，职责分离：
/// - NamingTemplateViewModel = 自动模式（提交时按模板生成标题）
/// - RenamePanelViewModel = 手动覆盖模式（用户逐行编辑标题）
/// 手动重命名优先级高于模板（IsRenamed 的项不使用模板渲染）。
/// 
/// 验证和预览在 Template 变更时实时触发，用户所见即所得。
/// 预览只取前 3 个选中项的 NamingContext，不遍历全部 100 项，性能开销可忽略。
/// </para>
/// </summary>
public partial class NamingTemplateViewModel : ObservableObject
{
    /// <summary>当前模板文本（绑定到 TextBox）</summary>
    [ObservableProperty]
    private string _template = NamingTemplateEngine.DefaultTemplate;

    /// <summary>验证错误信息（合法时为 null，UI 显示红色提示）</summary>
    [ObservableProperty]
    private string? _validationError;

    /// <summary>模板是否合法（控制提交按钮可用性）</summary>
    [ObservableProperty]
    private bool _isValid = true;

    /// <summary>预览结果列表（最多 3 项，实时展示命名效果）</summary>
    public ObservableCollection<string> PreviewItems { get; } = new();

    /// <summary>可用变量列表（供 UI 展示变量选择提示）</summary>
    public IReadOnlyList<TemplateVariableInfo> AvailableVariables { get; }
        = NamingTemplateEngine.GetSupportedVariables();

    /// <summary>缓存的命名上下文列表（由外部在解析完成后设置）</summary>
    private IReadOnlyList<NamingContext> _cachedContexts = Array.Empty<NamingContext>();

    /// <summary>
    /// Template 属性变更时自动触发验证和预览刷新。
    /// 设计思考：使用 CommunityToolkit.Mvvm 的 partial 方法，
    /// 无需手动订阅 PropertyChanged，代码更简洁。
    /// </summary>
    partial void OnTemplateChanged(string value)
    {
        RefreshValidationAndPreview();
    }

    /// <summary>
    /// 更新命名上下文缓存并刷新预览。
    /// 由外部在解析完成后、视频选择变更时调用。
    /// </summary>
    /// <param name="contexts">当前选中视频的命名上下文列表</param>
    public void UpdatePreview(IReadOnlyList<NamingContext> contexts)
    {
        _cachedContexts = contexts;
        RefreshValidationAndPreview();
    }

    /// <summary>
    /// 刷新验证状态和预览列表。
    /// <para>
    /// 设计思考：验证和预览合并为一个方法，因为两者都依赖 Template 和 Contexts，
    /// 且总是一起刷新。先验证再预览——模板非法时预览无意义。
    /// </para>
    /// </summary>
    private void RefreshValidationAndPreview()
    {
        // 验证模板合法性
        var validation = NamingTemplateEngine.Validate(Template);
        IsValid = validation.IsValid;
        ValidationError = validation.ErrorMessage;

        // 模板非法时清空预览
        if (!validation.IsValid)
        {
            PreviewItems.Clear();
            return;
        }

        // 渲染预览（前 3 项）
        var previews = NamingTemplateEngine.Preview(Template, _cachedContexts);

        // 重填 ObservableCollection（Clear + Add 策略，与 G4 一致）
        PreviewItems.Clear();
        foreach (var preview in previews)
        {
            PreviewItems.Add(preview);
        }
    }
}
