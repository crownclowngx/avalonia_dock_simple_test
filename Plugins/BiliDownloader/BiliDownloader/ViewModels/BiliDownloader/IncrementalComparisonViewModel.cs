using System.Collections.ObjectModel;
using BiliDownloader.Models;
using BiliDownloader.Models.ContentSources;
using BiliDownloader.Services.ContentSources;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BiliDownloader.ViewModels.BiliDownloader;

public sealed record ComparisonStatusFilterOption(string Label, ContentComparisonStatus? Status);

/// <summary>增量结果行；只有 New 且具有解析结果时才允许勾选。</summary>
public sealed partial class ContentComparisonItemViewModel : ObservableObject
{
    public ContentComparisonItemViewModel(ContentComparisonResult result)
    {
        Result = result;
        _isSelected = result.IsSelectedByDefault && result.CanSubmit;
    }

    public ContentComparisonResult Result { get; }
    public string Title => Result.Title;
    public string StatusText => Result.Status switch
    {
        ContentComparisonStatus.New => "新增",
        ContentComparisonStatus.Downloaded => "已下载",
        ContentComparisonStatus.InProgress => "下载中",
        ContentComparisonStatus.Invalid => "已失效",
        _ => "规则排除",
    };
    public string Detail => Result.Warnings.Count == 0
        ? $"来源证据 {Result.SourceKeys.Count} 项"
        : string.Join("；", Result.Warnings.Select(warning => warning.Message));
    public bool CanSelect => Result.CanSubmit;
    [ObservableProperty] private bool _isSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        if (value && !CanSelect) IsSelected = false;
    }
}

/// <summary>
/// 增量检查的会话级 ViewModel。它只持有内存扫描快照和选择状态；Document 基线通过回调写回，
/// 下载任务仍必须进入现有预检与 Coordinator，避免 UI 成为第二个提交入口。
/// </summary>
public sealed partial class IncrementalComparisonViewModel : ObservableObject, IDisposable
{
    private readonly IIncrementalComparisonService? _service;
    private readonly Func<ContentSourceDescriptor?> _descriptorProvider;
    private readonly Func<SourceFilterRules> _rulesProvider;
    private readonly Func<IncrementalBaselineSaveData> _baselineProvider;
    private readonly Action<IncrementalBaselineSaveData> _baselineWriter;
    private readonly Func<RenditionSpecification?> _renditionProvider;
    private CancellationTokenSource? _checkCts;
    private IncrementalComparisonSnapshot? _snapshot;
    // 增量比较会同时读取缓存基线、解析详情并重建 UI 分类，持续时间可能明显长于一次点击。
    // Document 令牌负责标签关闭，本地 CTS 负责对象释放，_checkCts 负责用新比较替代旧比较；
    // 三层令牌分工后，局部重试不会误取消整个页面，页面关闭又能覆盖所有在途比较阶段。
    private readonly CancellationToken _documentToken;
    private readonly CancellationTokenSource _disposeCts = new();
    private int _disposed;

    public IncrementalComparisonViewModel(
        IIncrementalComparisonService? service,
        Func<ContentSourceDescriptor?> descriptorProvider,
        Func<SourceFilterRules> rulesProvider,
        Func<IncrementalBaselineSaveData> baselineProvider,
        Action<IncrementalBaselineSaveData> baselineWriter,
        Func<RenditionSpecification?> renditionProvider,
        CancellationToken documentToken = default)
    {
        _service = service;
        _descriptorProvider = descriptorProvider;
        _rulesProvider = rulesProvider;
        _baselineProvider = baselineProvider;
        _baselineWriter = baselineWriter;
        _renditionProvider = renditionProvider;
        _documentToken = documentToken;
        StatusFilters =
        [
            new("全部", null),
            new("新增", ContentComparisonStatus.New),
            new("已下载", ContentComparisonStatus.Downloaded),
            new("下载中", ContentComparisonStatus.InProgress),
            new("已失效", ContentComparisonStatus.Invalid),
            new("规则排除", ContentComparisonStatus.RuleExcluded),
        ];
        _selectedStatusFilter = StatusFilters[0];
    }

    public event Action<IReadOnlyList<BiliVideoItem>, IncrementalSubmissionExpectation>? ItemsAccepted;
    public ObservableCollection<ContentComparisonItemViewModel> AllItems { get; } = [];
    public ObservableCollection<ContentComparisonItemViewModel> Items { get; } = [];
    public IReadOnlyList<ComparisonStatusFilterOption> StatusFilters { get; }

    [ObservableProperty] private ComparisonStatusFilterOption _selectedStatusFilter;
    [ObservableProperty] private bool _isSupported;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasResults;
    [ObservableProperty] private bool _isPartial;
    [ObservableProperty] private bool _isStale;
    [ObservableProperty] private string _status = "增量检查只会生成预览，不会创建下载任务。";
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private string _lastCompletedCheckText = "尚未完成检查";

    public bool CanCheck => IsSupported && !IsBusy;
    public bool CanCancel => IsBusy;
    public bool CanUseSelected => !IsBusy && !IsStale && AllItems.Any(item => item.CanSelect && item.IsSelected);
    public bool CanReclassify => !IsBusy && _snapshot?.SourceSnapshot is not null;

    public void RefreshCapability()
    {
        var descriptor = _descriptorProvider();
        IsSupported = _service is not null && descriptor is not null &&
            descriptor.Kind is not (ContentSourceKind.DirectLink or ContentSourceKind.Course);
        if (!IsSupported) Status = descriptor?.Kind == ContentSourceKind.Course
            ? "课程来源当前仅支持浏览，不提供增量提交。"
            : "当前来源不支持增量检查。";
        NotifyCommandState();
    }

    public void ResetForSourceChange()
    {
        _checkCts?.Cancel();
        _snapshot = null;
        AllItems.Clear();
        Items.Clear();
        HasResults = false;
        IsPartial = false;
        IsStale = false;
        Summary = string.Empty;
        RefreshCapability();
    }

    /// <summary>筛选或输出身份变化后保留扫描快照，但旧分类和用户勾选不得继续提交。</summary>
    public void MarkStale(string reason)
    {
        if (_snapshot is null) return;
        IsStale = true;
        foreach (var item in AllItems) item.IsSelected = false;
        Status = reason;
        NotifyCommandState();
    }

    /// <summary>只有真正改变指纹的输出设置才使结果过期，避免解析结果回填同一画质时产生假警告。</summary>
    public void MarkStaleIfRenditionChanged()
    {
        var rendition = _renditionProvider();
        var sample = _snapshot?.Results.FirstOrDefault(result =>
            result.MediaUnitKey.HasValue && result.RenditionFingerprint.HasValue);
        if (rendition is null || sample?.MediaUnitKey is null || sample.RenditionFingerprint is null)
        {
            MarkStale("输出设置已变化，请使用内存扫描结果重新分类后再提交。");
            return;
        }
        var current = RenditionFingerprint.Create(sample.MediaUnitKey.Value, rendition);
        if (current != sample.RenditionFingerprint.Value)
            MarkStale("输出设置已变化，请使用内存扫描结果重新分类后再提交。");
    }

    [RelayCommand]
    private async Task CheckAsync()
    {
        if (_service is null || _descriptorProvider() is not { } descriptor || !CanCheck) return;
        var rendition = _renditionProvider();
        if (rendition is null)
        {
            Status = "请先解析内容、选择有效画质并保存方案，再执行增量检查。";
            return;
        }

        _checkCts?.Cancel();
        _checkCts?.Dispose();
        _checkCts = CancellationTokenSource.CreateLinkedTokenSource(
            _documentToken,
            _disposeCts.Token);
        try
        {
            IsBusy = true;
            IsStale = false;
            Status = "正在递归检查整个来源…";
            var progress = new Progress<IncrementalScanProgress>(value =>
                Status = $"已扫描 {value.ScopeCount} 个层级、{value.PageCount} 页、{value.LeafCount} 个项目，解析 {value.ResolvedCount} 个媒体单元…");
            var snapshot = await _service.CheckAsync(
                descriptor, _rulesProvider(), _baselineProvider(), rendition,
                progress, _checkCts.Token);
            ApplySnapshot(snapshot);
            if (snapshot.IsComplete && snapshot.ProposedBaseline is not null)
            {
                _baselineWriter(snapshot.ProposedBaseline);
                LastCompletedCheckText = snapshot.ProposedBaseline.LastCompletedCheckAtUtc?.ToLocalTime()
                    .ToString("yyyy-MM-dd HH:mm:ss") ?? "尚未完成检查";
            }
        }
        finally
        {
            if (!IsDisposed) IsBusy = false;
            _checkCts?.Dispose();
            _checkCts = null;
            NotifyCommandState();
        }
    }

    [RelayCommand] private void Cancel() => _checkCts?.Cancel();

    [RelayCommand]
    public async Task RefreshFromCacheAsync(CancellationToken cancellationToken = default)
    {
        if (_service is null || _snapshot?.SourceSnapshot is not { } source || IsBusy) return;
        var rendition = _renditionProvider();
        if (rendition is null)
        {
            Status = "当前输出身份不完整，无法重新分类。";
            return;
        }
        IsBusy = true;
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _documentToken,
                _disposeCts.Token);
            var snapshot = await _service.ReclassifyAsync(
                source, _baselineProvider(), rendition, linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            if (!IsDisposed) ApplySnapshot(snapshot);
        }
        finally
        {
            if (!IsDisposed)
            {
                IsBusy = false;
                NotifyCommandState();
            }
        }
    }

    private bool IsDisposed => Volatile.Read(ref _disposed) != 0 || _documentToken.IsCancellationRequested;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _checkCts?.Cancel();
        _disposeCts.Cancel();
        _checkCts?.Dispose();
        _disposeCts.Dispose();
    }

    [RelayCommand]
    private void UseSelected()
    {
        if (_snapshot is null || !CanUseSelected) return;
        var selected = AllItems.Where(item => item.CanSelect && item.IsSelected).ToArray();
        var media = selected.Select(item => item.Result.ResolvedItem!).ToArray();
        foreach (var item in media) item.IsSelected = true;
        var fingerprints = selected.Select(item => item.Result.RenditionFingerprint!.Value.Value).ToArray();
        ItemsAccepted?.Invoke(media, new IncrementalSubmissionExpectation(_snapshot.ComparisonToken, fingerprints));
        Status = $"已将 {media.Length} 个新增媒体单元送入下载配置；仍需预检和用户确认。";
    }

    partial void OnSelectedStatusFilterChanged(ComparisonStatusFilterOption value) => RebuildVisibleItems();
    partial void OnIsBusyChanged(bool value) => NotifyCommandState();

    private void ApplySnapshot(IncrementalComparisonSnapshot snapshot)
    {
        _snapshot = snapshot;
        AllItems.Clear();
        foreach (var result in snapshot.Results)
        {
            var item = new ContentComparisonItemViewModel(result);
            item.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(ContentComparisonItemViewModel.IsSelected))
                    NotifyCommandState();
            };
            AllItems.Add(item);
        }
        HasResults = AllItems.Count > 0;
        IsPartial = !snapshot.IsComplete;
        IsStale = false;
        Summary = string.Join(" · ", Enum.GetValues<ContentComparisonStatus>().Select(status =>
            $"{StatusLabel(status)} {AllItems.Count(item => item.Result.Status == status)}"));
        Status = snapshot.IsComplete
            ? "检查完成；仅新增项已默认勾选，尚未创建任何任务。"
            : "检查未完整完成；已保留部分预览，不判定基线缺失项且不更新基线。";
        RebuildVisibleItems();
        NotifyCommandState();
    }

    private void RebuildVisibleItems()
    {
        Items.Clear();
        foreach (var item in AllItems.Where(item =>
                     SelectedStatusFilter.Status is null || item.Result.Status == SelectedStatusFilter.Status))
            Items.Add(item);
    }

    private void NotifyCommandState()
    {
        OnPropertyChanged(nameof(CanCheck));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanUseSelected));
        OnPropertyChanged(nameof(CanReclassify));
        CheckCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        UseSelectedCommand.NotifyCanExecuteChanged();
        RefreshFromCacheCommand.NotifyCanExecuteChanged();
    }

    private static string StatusLabel(ContentComparisonStatus status) => status switch
    {
        ContentComparisonStatus.New => "新增",
        ContentComparisonStatus.Downloaded => "已下载",
        ContentComparisonStatus.InProgress => "下载中",
        ContentComparisonStatus.Invalid => "已失效",
        _ => "规则排除",
    };
}
