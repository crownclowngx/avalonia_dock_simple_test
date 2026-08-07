using BiliDownloader.Models.ContentSources;
using BiliDownloader.Services.ContentSources;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BiliDownloader.ViewModels.BiliDownloader;

/// <summary>把稳定权限状态转换为中文展示；业务层不依赖任何 UI 文案。</summary>
public static class ContentAccessPresentationPolicy
{
    public static string GetLabel(ContentAccessState state, bool supportsResolution) => state switch
    {
        ContentAccessState.Available when supportsResolution => "可下载",
        ContentAccessState.Available => "仅浏览",
        ContentAccessState.LoginRequired => "需要登录",
        ContentAccessState.PurchaseRequired => "需要购买",
        ContentAccessState.RegionRestricted => "区域限制",
        ContentAccessState.Expired => "已失效",
        ContentAccessState.NotReleased => "尚未发布",
        ContentAccessState.DrmProtected => "DRM 保护",
        _ => "权限未知",
    };
}

public sealed class ContentSourceItemViewModel : ObservableObject
{
    private readonly ContentSelectionState _selection;
    private readonly Func<FilterFingerprint> _fingerprint;
    private readonly Action? _selectionChanged;

    public ContentSourceItemViewModel(
        ContentSourceItem item,
        bool supportsResolution = true,
        Func<ContentSourceItemViewModel, Task>? onOpen = null,
        ContentSelectionState? selection = null,
        Func<FilterFingerprint>? fingerprint = null,
        Action? selectionChanged = null)
    {
        Item = item;
        SupportsResolution = supportsResolution;
        _selection = selection ?? new ContentSelectionState();
        _fingerprint = fingerprint ?? (() => ContentFilterPlanBuilder.CreateFingerprint(SourceFilterRules.Empty));
        _selectionChanged = selectionChanged;
        OpenCommand = new AsyncRelayCommand(
            () => onOpen is null ? Task.CompletedTask : onOpen(this),
            () => CanOpen);
    }

    public ContentSourceItem Item { get; }
    public bool SupportsResolution { get; }
    public string Title => Item.Title;
    public string Detail => string.Join(" · ", new[]
        {
            Item.Author,
            Item.PublishedAt?.LocalDateTime.ToString("yyyy-MM-dd"),
            Item.ChildCount.HasValue ? $"共 {Item.ChildCount.Value} 项" : null,
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
    public string AccessText => ContentAccessPresentationPolicy.GetLabel(Item.AccessState, SupportsResolution);
    public bool IsContainer => Item.NodeKind == ContentSourceNodeKind.Container;
    public bool CanOpen => IsContainer && Item.AccessState == ContentAccessState.Available;
    public bool CanSelect => !IsContainer && SupportsResolution && Item.AccessState == ContentAccessState.Available;
    public bool ShowCheckBox => CanSelect;
    public bool ShowStateIcon => !CanSelect;
    public string StateIcon => IsContainer ? "›" : Item.AccessState switch
    {
        ContentAccessState.PurchaseRequired => "锁",
        ContentAccessState.RegionRestricted or ContentAccessState.DrmProtected => "禁",
        ContentAccessState.Expired => "失",
        ContentAccessState.NotReleased => "待",
        _ => "·",
    };
    public IAsyncRelayCommand OpenCommand { get; }

    public bool IsSelected
    {
        get => CanSelect && _selection.IsSelected(Item.Key, _fingerprint());
        set
        {
            var normalized = CanSelect && value;
            if (IsSelected == normalized) return;
            _selection.SetSelected(Item.Key, normalized, _fingerprint());
            OnPropertyChanged();
            _selectionChanged?.Invoke();
        }
    }

    public void RefreshSelection() => OnPropertyChanged(nameof(IsSelected));
}
