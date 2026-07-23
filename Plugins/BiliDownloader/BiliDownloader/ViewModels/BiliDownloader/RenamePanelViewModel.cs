using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BiliDownloader.Models;

namespace BiliDownloader.ViewModels.BiliDownloader;

/// <summary>
/// 批量重命名子 ViewModel：负责原标题/新标题双栏 + 应用重命名
/// </summary>
public partial class RenamePanelViewModel : ObservableObject
{
    private readonly Action<List<string>>? _onRenameApplied;
    private readonly Func<int> _getVideoCount;

    [ObservableProperty]
    private bool _showRenamePanel;

    [ObservableProperty]
    private string _originalTitlesText = "";

    [ObservableProperty]
    private string _newTitlesText = "";

    /// <summary>
    /// 应用重命名后的提示信息
    /// </summary>
    public string? StatusMessage { get; private set; }

    public IRelayCommand ToggleRenamePanelCommand { get; }
    public IRelayCommand ApplyRenameCommand { get; }

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="onRenameApplied">应用重命名后的回调，传入新标题列表</param>
    /// <param name="getVideoCount">获取当前视频数量的函数</param>
    public RenamePanelViewModel(Action<List<string>>? onRenameApplied, Func<int> getVideoCount)
    {
        _onRenameApplied = onRenameApplied;
        _getVideoCount = getVideoCount;
        ToggleRenamePanelCommand = new RelayCommand(() => ShowRenamePanel = !ShowRenamePanel);
        ApplyRenameCommand = new RelayCommand(ApplyRename);
    }

    /// <summary>
    /// 由主 VM 在解析成功后调用，生成初始文本
    /// </summary>
    public void InitTitles(List<BiliVideoItem> videoItems)
    {
        var titlesLines = string.Join(Environment.NewLine, videoItems.Select(i => i.Title));
        OriginalTitlesText = titlesLines;
        NewTitlesText = titlesLines;
    }

    private void ApplyRename()
    {
        var videoCount = _getVideoCount();
        if (videoCount == 0) return;

        var newTitles = NewTitlesText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        // 行数必须与视频数量一致
        if (newTitles.Length != videoCount)
        {
            StatusMessage = $"重命名失败：新标题行数({newTitles.Length})与视频数量({videoCount})不一致";
            return;
        }

        var trimmedTitles = newTitles.Select(t => t.Trim()).ToList();
        _onRenameApplied?.Invoke(trimmedTitles);
        StatusMessage = $"已应用批量重命名（{videoCount} 个视频）";
    }
}
