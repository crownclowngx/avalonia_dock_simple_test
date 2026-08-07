using BiliDownloader.Models;
using BiliDownloader.Models.ContentSources;
using BiliDownloader.Services.ContentSources;
using BiliDownloader.Services.Persistence;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BiliDownloader.ViewModels.BiliDownloader;

public enum DownloadCreationMode { QuickUrl, PersonalSource }

/// <summary>来源阶段的轻量编排 VM；不负责分页细节，也不负责下载配置与提交。</summary>
public partial class DownloadSourceWorkflowViewModel : ObservableObject
{
    private readonly IContentSourceProviderRegistry _registry;
    private SourceDescriptorSaveData? _persistedSource;
    private SourceFilterRulesSaveData _persistedFilters = new();
    private IncrementalBaselineSaveData _baseline = new();
    private IncrementalSubmissionExpectation? _submissionExpectation;

    /// <summary>来源、筛选或增量基线发生持久变化时触发。</summary>
    public event Action? PersistentStateChanged;
    public event Action<IReadOnlyList<BiliVideoItem>, IncrementalSubmissionExpectation>? IncrementalItemsAccepted;

    public DownloadSourceWorkflowViewModel(
        VideoParseViewModel quickUrl,
        IContentSourceProviderRegistry registry,
        IFavoriteSourceDiscoveryService favorites,
        VideoParseResultFactory resultFactory,
        Action<VideoParseResult> onResolved,
        IIncrementalComparisonService? incrementalComparisonService = null,
        Func<RenditionSpecification?>? renditionProvider = null)
    {
        _registry = registry;
        QuickUrl = quickUrl;
        Browser = new ContentSourceBrowserViewModel(registry, resultFactory, result =>
        {
            _submissionExpectation = null;
            onResolved(result);
        });
        Comparison = new IncrementalComparisonViewModel(
            incrementalComparisonService,
            () => Browser.CurrentDescriptor,
            () => Browser.CaptureFilters(),
            () => BiliDownloaderDocumentStateMapper.CloneBaseline(_baseline),
            SetIncrementalBaseline,
            renditionProvider ?? (() => null));
        Comparison.ItemsAccepted += (items, expectation) =>
        {
            _submissionExpectation = expectation;
            IncrementalItemsAccepted?.Invoke(items, expectation);
        };
        Picker = new ContentSourcePickerViewModel(registry, favorites, OpenDescriptorAsync);
        Browser.PersistentStateChanged += OnBrowserPersistentStateChanged;
        QuickUrl.PersistentSourceChanged += OnQuickUrlPersistentSourceChanged;
    }

    public VideoParseViewModel QuickUrl { get; }
    public ContentSourcePickerViewModel Picker { get; }
    public ContentSourceBrowserViewModel Browser { get; }
    public IncrementalComparisonViewModel Comparison { get; }
    [ObservableProperty] private DownloadCreationMode _mode;
    [ObservableProperty] private bool _isBrowsing;
    [ObservableProperty] private bool _isRestoredSourceUnsupported;
    [ObservableProperty] private string _unsupportedSourceSummary = string.Empty;
    public bool IsQuickUrl => Mode == DownloadCreationMode.QuickUrl;
    public bool IsPersonalSource => Mode == DownloadCreationMode.PersonalSource;
    public bool HasUnsupportedSource => IsRestoredSourceUnsupported;

    public void SetInitialMode(DownloadCreationMode mode)
    {
        Mode = mode;
        IsBrowsing = false;
    }

    partial void OnModeChanged(DownloadCreationMode value)
    {
        OnPropertyChanged(nameof(IsQuickUrl));
        OnPropertyChanged(nameof(IsPersonalSource));
        IsBrowsing = false;
    }

    [RelayCommand]
    private void UseQuickUrl()
    {
        _submissionExpectation = null;
        Mode = DownloadCreationMode.QuickUrl;
    }
    [RelayCommand] private void UsePersonalSource() => Mode = DownloadCreationMode.PersonalSource;
    [RelayCommand] private void BackToSources() => IsBrowsing = false;

    private async Task OpenDescriptorAsync(ContentSourceDescriptor descriptor)
    {
        _persistedSource = ContentSourceSaveDataMapper.FromRuntime(descriptor);
        _persistedFilters = new SourceFilterRulesSaveData();
        _baseline = new IncrementalBaselineSaveData();
        IsRestoredSourceUnsupported = false;
        UnsupportedSourceSummary = string.Empty;
        IsBrowsing = true;
        _submissionExpectation = null;
        Comparison.ResetForSourceChange();
        PersistentStateChanged?.Invoke();
        await Browser.OpenAsync(descriptor);
        Comparison.RefreshCapability();
    }

    /// <summary>
    /// 截取 V3 所需的来源状态。若 Provider 当前缺失，则沿用加载时保存的白名单 DTO，
    /// 保证用户查看并另存时不会丢失未来或可选插件来源。
    /// </summary>
    public BiliDownloaderDocumentSourceState CapturePersistentState()
    {
        SourceDescriptorSaveData? source = _persistedSource;
        if (Mode == DownloadCreationMode.QuickUrl && QuickUrl.CurrentSourceDescriptor is not null)
            source = ContentSourceSaveDataMapper.FromRuntime(QuickUrl.CurrentSourceDescriptor);
        // 已恢复的未知来源必须始终以白名单 DTO 为准。浏览器可能仍保留上一次会话的描述符，
        // 若在不支持状态下读取它，会在再次保存时悄然丢失未知 Provider 的稳定身份。
        else if (!IsRestoredSourceUnsupported && Browser.CurrentDescriptor is not null)
            source = ContentSourceSaveDataMapper.FromRuntime(Browser.CurrentDescriptor);

        var filters = Browser.CurrentDescriptor is null || IsRestoredSourceUnsupported
            ? _persistedFilters
            : ContentSourceSaveDataMapper.FromRuntime(Browser.CaptureFilters());
        return new BiliDownloaderDocumentSourceState(
            BiliDownloaderDocumentStateMapper.CloneSource(source),
            BiliDownloaderDocumentStateMapper.CloneFilters(filters),
            BiliDownloaderDocumentStateMapper.CloneBaseline(_baseline));
    }

    /// <summary>
    /// 从 V3 恢复来源意图，整个过程不调用 Provider 方法。
    /// 已注册 Provider 仅用于读取本地能力声明和构造空浏览状态。
    /// </summary>
    public void RestorePersistentState(BiliDownloaderDocumentSourceState state, string? legacyUrl)
    {
        ArgumentNullException.ThrowIfNull(state);
        _persistedSource = BiliDownloaderDocumentStateMapper.CloneSource(state.Source);
        _persistedFilters = BiliDownloaderDocumentStateMapper.CloneFilters(state.Filters);
        _baseline = BiliDownloaderDocumentStateMapper.CloneBaseline(state.Baseline);
        IsRestoredSourceUnsupported = false;
        UnsupportedSourceSummary = string.Empty;
        _submissionExpectation = null;
        Comparison.ResetForSourceChange();

        if (_persistedSource is null) return;
        if (!ContentSourceSaveDataMapper.TryToRuntime(_persistedSource, out var descriptor)
            || descriptor is null
            || !_registry.TryGet(descriptor.Kind, out _))
        {
            Mode = DownloadCreationMode.PersonalSource;
            IsBrowsing = false;
            IsRestoredSourceUnsupported = true;
            UnsupportedSourceSummary = $"{_persistedSource.DisplayName} · {_persistedSource.Kind} · {_persistedSource.StableSourceId}";
            return;
        }

        if (descriptor.Kind == ContentSourceKind.DirectLink)
        {
            Mode = DownloadCreationMode.QuickUrl;
            IsBrowsing = false;
            QuickUrl.RestoreSource(descriptor, legacyUrl);
            return;
        }

        Mode = DownloadCreationMode.PersonalSource;
        IsBrowsing = true;
        Browser.RestoreOffline(descriptor, ContentSourceSaveDataMapper.ToRuntime(state.Filters));
        Comparison.RefreshCapability();
    }

    /// <summary>P1-G5 写入新的完整检查基线时复用；P1-G4 不主动调用。</summary>
    internal void SetIncrementalBaseline(IncrementalBaselineSaveData baseline)
    {
        _baseline = BiliDownloaderDocumentStateMapper.CloneBaseline(baseline);
        PersistentStateChanged?.Invoke();
    }

    /// <summary>当前进入工作区的增量选择期望；普通解析返回 null，不改变原有提交流程。</summary>
    public IncrementalSubmissionExpectation? CreateSubmissionExpectation() => _submissionExpectation;

    public Task RefreshComparisonFromCacheAsync() => Comparison.RefreshFromCacheAsync();

    /// <summary>画质、编码、容器或输出模式变化时，旧比较不得继续作为 New 的提交依据。</summary>
    public void MarkOutputIdentityChanged() => Comparison.MarkStaleIfRenditionChanged();

    private void OnBrowserPersistentStateChanged()
    {
        if (Browser.CurrentDescriptor is not null)
        {
            _persistedSource = ContentSourceSaveDataMapper.FromRuntime(Browser.CurrentDescriptor);
            _persistedFilters = ContentSourceSaveDataMapper.FromRuntime(Browser.CaptureFilters());
        }
        Comparison.MarkStale("来源筛选已变化，请重新执行检查或使用最新来源重新分类。");
        PersistentStateChanged?.Invoke();
    }

    private void OnQuickUrlPersistentSourceChanged()
    {
        _persistedSource = QuickUrl.CurrentSourceDescriptor is null
            ? null
            : ContentSourceSaveDataMapper.FromRuntime(QuickUrl.CurrentSourceDescriptor);
        PersistentStateChanged?.Invoke();
    }

    partial void OnIsRestoredSourceUnsupportedChanged(bool value) =>
        OnPropertyChanged(nameof(HasUnsupportedSource));
}

internal sealed class UnavailableFavoriteSourceDiscoveryService : IFavoriteSourceDiscoveryService
{
    public Task<IReadOnlyList<ContentSourceDescriptor>> GetMyFoldersAsync(CancellationToken cancellationToken) =>
        throw new ContentSourceException(ContentSourceErrorCode.UnknownProvider, "收藏夹来源尚未注册。");
}
