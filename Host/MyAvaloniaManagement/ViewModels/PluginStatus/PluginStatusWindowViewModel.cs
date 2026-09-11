using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyAvaloniaManagement.Business.PluginStatus;
using MyAvaloniaManagement.Models.Plugins;

namespace MyAvaloniaManagement.ViewModels.PluginStatus;

/// <summary>拥有单个窗口的搜索、选择和刷新结果，不拥有插件运行状态。</summary>
/// <remarks>
/// 设计思路：查询通过窄接口注入，便于独立验证筛选与刷新；没有全局事件或定时器订阅。
/// 每次刷新先取得完整快照再替换展示，失败时保留上一次结果。选择使用稳定键恢复，
/// 避免用户在查看故障详情时因为刷新或重新激活窗口而跳到另一插件。
/// </remarks>
internal sealed partial class PluginStatusWindowViewModel : ObservableObject
{
    private readonly IPluginStatusQuery _query;
    private readonly TimeProvider _time;
    private IReadOnlyList<PluginStatusItem> _items = [];
    private string? _selectionKey;
    private bool _updatingSelection;

    public PluginStatusWindowViewModel(IPluginStatusQuery query, TimeProvider time)
    {
        _query = query ?? throw new ArgumentNullException(nameof(query));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _selectedFilter = "全部";
    [ObservableProperty] private IReadOnlyList<PluginStatusItem> _visibleItems = [];
    [ObservableProperty] private PluginStatusItem? _selectedItem;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _availableCount;
    [ObservableProperty] private int _problemCount;
    [ObservableProperty] private string _updatedText = "尚未刷新";
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private string _copyFeedback = string.Empty;

    public IReadOnlyList<string> Filters { get; } = ["全部", "可用", "异常"];
    public bool HasSelection => SelectedItem is not null;
    public bool HasNoResults => VisibleItems.Count == 0;
    public bool HasError => ErrorMessage.Length > 0;
    public string ResultsText => $"显示 {VisibleItems.Count} / {TotalCount} 项";
    public string EmptyText => TotalCount == 0 ? "本次会话未发现插件。" : "没有匹配的插件，请调整搜索或筛选条件。";

    [RelayCommand]
    public void Refresh()
    {
        try
        {
            var next = _query.Capture();
            _items = next;
            TotalCount = next.Count;
            AvailableCount = next.Count(item => item.IsAvailable);
            ProblemCount = next.Count(item => item.HasProblem);
            UpdatedText = $"更新于 {_time.GetLocalNow():HH:mm:ss} · 当前会话";
            ErrorMessage = string.Empty;
            ApplyFilter();
        }
        catch (Exception)
        {
            // UI 边界不展示异常正文。上一次成功快照及其更新时间保留，明确提示当前刷新失败。
            ErrorMessage = "读取插件状态失败，当前保留上一次结果，请重试。";
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedFilterChanged(string value) => ApplyFilter();
    partial void OnErrorMessageChanged(string value) => OnPropertyChanged(nameof(HasError));
    partial void OnSelectedItemChanged(PluginStatusItem? value)
    {
        if (!_updatingSelection && value is not null) _selectionKey = value.Key;
        CopyFeedback = string.Empty;
        OnPropertyChanged(nameof(HasSelection));
    }

    private void ApplyFilter()
    {
        var search = SearchText.Trim();
        var next = _items.Where(item => SelectedFilter switch
            { "可用" => item.IsAvailable, "异常" => item.HasProblem, _ => true })
            .Where(item => search.Length == 0 || item.Matches(search)).ToArray();
        // ItemsSource 更新可能让 ListBox 暂时回写 null，不能让这一瞬间覆盖稳定选择键。
        _updatingSelection = true;
        try
        {
            VisibleItems = next;
            SelectedItem = next.FirstOrDefault(item => item.Key == _selectionKey) ?? next.FirstOrDefault();
            if (SelectedItem is not null) _selectionKey = SelectedItem.Key;
        }
        finally { _updatingSelection = false; }
        OnPropertyChanged(nameof(HasNoResults));
        OnPropertyChanged(nameof(ResultsText));
        OnPropertyChanged(nameof(EmptyText));
    }

    /// <summary>生成所选插件的可复制摘要；剪贴板访问留在 View，模型无需依赖 Avalonia 平台。</summary>
    public string CreateDiagnosticText()
    {
        if (SelectedItem is not { } item) return string.Empty;
        var text = new StringBuilder().AppendLine($"插件：{item.PluginId}")
            .AppendLine($"版本：{item.VersionText}").AppendLine($"兼容：{item.CompatibilityText}")
            .AppendLine($"程序集：{item.AssemblyName}").AppendLine($"状态：{item.StatusText}")
            .AppendLine($"可用性：{item.AvailabilityText}").AppendLine($"耗时：{item.DurationText}")
            .AppendLine(item.Detail).AppendLine(UpdatedText);
        foreach (var record in item.Diagnostics)
            text.AppendLine($"{record.TimeText} [{record.SeverityText}] [{record.Code}] {record.PhaseText}：{record.Message}")
                .AppendLine(record.TechnicalDetail);
        return text.ToString();
    }
}
