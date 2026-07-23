using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MySmallTools.Business.SecretVideoPlayer.Library;
using MySmallTools.Models.SecretVideoPlayer;

namespace MySmallTools.ViewModels.SecretVideoPlayer;

/// <summary>
/// 管理文件夹扫描、筛选和选择状态；不持有密码，也不操作播放器。
/// </summary>
public partial class VideoLibraryBrowserViewModel : ObservableObject, IDisposable
{
    private static readonly IComparer<VideoLibraryItemViewModel> ItemComparer =
        Comparer<VideoLibraryItemViewModel>.Create(CompareItems);

    private readonly IVideoLibraryScanner _scanner;
    private readonly List<VideoLibraryItemViewModel> _allItems = [];
    private readonly ObservableCollection<VideoLibraryItemViewModel> _visibleItems = [];
    private CancellationTokenSource? _scanCancellation;
    private CancellationTokenSource? _filterCancellation;
    private long _scanGeneration;
    private bool _scanFaulted;
    private bool _disposed;

    [ObservableProperty] private string _folderPath = string.Empty;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private VideoLibraryItemViewModel? _selectedItem;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private int _processedCount;
    [ObservableProperty] private int _failedCount;
    [ObservableProperty] private int _visibleItemCount;
    [ObservableProperty] private string _statusMessage = "请选择包含 .secvid 文件的文件夹";

    public ReadOnlyObservableCollection<VideoLibraryItemViewModel> VisibleItems { get; }
    public bool HasFolder => !string.IsNullOrWhiteSpace(FolderPath);
    public bool HasVisibleItems => VisibleItemCount > 0;

    public VideoLibraryBrowserViewModel(IVideoLibraryScanner scanner)
    {
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        VisibleItems = new ReadOnlyObservableCollection<VideoLibraryItemViewModel>(_visibleItems);
    }

    partial void OnSearchTextChanged(string value) => ScheduleFilter();

    partial void OnFolderPathChanged(string value)
    {
        OnPropertyChanged(nameof(HasFolder));
        RefreshCommand.NotifyCanExecuteChanged();
    }

    partial void OnVisibleItemCountChanged(int value) => OnPropertyChanged(nameof(HasVisibleItems));

    public Task LoadFolderAsync(string folderPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(folderPath))
            throw new ArgumentException("视频文件夹不能为空。", nameof(folderPath));

        FolderPath = Path.GetFullPath(folderPath);
        return ScanCurrentFolderAsync();
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private Task RefreshAsync() => ScanCurrentFolderAsync();

    private bool CanRefresh() => !_disposed && !string.IsNullOrWhiteSpace(FolderPath);

    private async Task ScanCurrentFolderAsync()
    {
        var generation = Interlocked.Increment(ref _scanGeneration);
        ReplaceCancellation(ref _scanCancellation, out var cancellation);
        var token = cancellation.Token;

        _allItems.Clear();
        _visibleItems.Clear();
        SelectedItem = null;
        ProcessedCount = 0;
        FailedCount = 0;
        VisibleItemCount = 0;
        _scanFaulted = false;
        IsScanning = true;
        StatusMessage = "正在扫描，已读取 0 个";

        try
        {
            await foreach (var result in _scanner.ScanAsync(FolderPath, token).WithCancellation(token))
            {
                if (_disposed || generation != Volatile.Read(ref _scanGeneration))
                    return;

                var item = new VideoLibraryItemViewModel(result);
                InsertSorted(_allItems, item);
                if (MatchesCurrentSearch(item))
                    InsertSorted(_visibleItems, item);

                ProcessedCount++;
                if (item.HasError)
                    FailedCount++;
                VisibleItemCount = _visibleItems.Count;
                UpdateStatus();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // 切换目录、重新扫描或关闭文档属于正常取消；旧代次不得再更新 UI 状态。
        }
        catch (Exception ex)
        {
            if (_disposed || generation != Volatile.Read(ref _scanGeneration))
                return;

            _scanFaulted = true;
            StatusMessage = MapDirectoryError(ex);
        }
        finally
        {
            if (!_disposed && generation == Volatile.Read(ref _scanGeneration))
            {
                IsScanning = false;
                if (!_scanFaulted)
                    UpdateStatus();
            }

            if (ReferenceEquals(_scanCancellation, cancellation))
                _scanCancellation = null;
            cancellation.Dispose();
        }
    }

    private void ScheduleFilter()
    {
        if (_disposed)
            return;

        ReplaceCancellation(ref _filterCancellation, out var cancellation);
        _ = ApplyFilterAfterDelayAsync(cancellation);
    }

    private async Task ApplyFilterAfterDelayAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(150), cancellation.Token);
            if (_disposed || cancellation.IsCancellationRequested)
                return;

            ApplyFilter();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_filterCancellation, cancellation))
                _filterCancellation = null;
            cancellation.Dispose();
        }
    }

    private void ApplyFilter()
    {
        var selected = SelectedItem;
        _visibleItems.Clear();
        foreach (var item in _allItems)
        {
            if (MatchesCurrentSearch(item))
                _visibleItems.Add(item);
        }

        if (selected is not null && !_visibleItems.Contains(selected))
            SelectedItem = null;

        VisibleItemCount = _visibleItems.Count;
        if (!_scanFaulted)
            UpdateStatus();
    }

    private bool MatchesCurrentSearch(VideoLibraryItemViewModel item)
    {
        var query = SearchText.Trim();
        if (query.Length == 0)
            return true;

        return item.FileNameWithoutExtension.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               item.PublicTitle.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               item.PublicDescription.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateStatus()
    {
        StatusMessage = IsScanning
            ? $"正在扫描，已读取 {ProcessedCount} 个，当前筛选显示 {VisibleItemCount} 个"
            : $"扫描完成，共 {ProcessedCount} 个，失败 {FailedCount} 个，当前筛选显示 {VisibleItemCount} 个";
    }

    private static string MapDirectoryError(Exception ex) => ex switch
    {
        DirectoryNotFoundException => "文件夹不存在或已被删除",
        UnauthorizedAccessException => "没有访问该文件夹的权限",
        IOException => "读取文件夹失败，请检查磁盘或文件夹状态",
        _ => $"扫描文件夹失败: {ex.Message}"
    };

    private static int CompareItems(VideoLibraryItemViewModel left, VideoLibraryItemViewModel right)
    {
        var byName = StringComparer.OrdinalIgnoreCase.Compare(
            left.FileNameWithoutExtension,
            right.FileNameWithoutExtension);
        return byName != 0
            ? byName
            : StringComparer.OrdinalIgnoreCase.Compare(left.FilePath, right.FilePath);
    }

    private static void InsertSorted(
        List<VideoLibraryItemViewModel> items,
        VideoLibraryItemViewModel item)
    {
        var index = items.BinarySearch(item, ItemComparer);
        items.Insert(index < 0 ? ~index : index, item);
    }

    private static void InsertSorted(
        ObservableCollection<VideoLibraryItemViewModel> items,
        VideoLibraryItemViewModel item)
    {
        var low = 0;
        var high = items.Count;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (CompareItems(items[middle], item) <= 0)
                low = middle + 1;
            else
                high = middle;
        }
        items.Insert(low, item);
    }

    private static void ReplaceCancellation(
        ref CancellationTokenSource? field,
        out CancellationTokenSource replacement)
    {
        var previous = field;
        replacement = new CancellationTokenSource();
        field = replacement;
        if (previous is not null)
        {
            previous.Cancel();
            // Previous operation owns disposal in its finally block.
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Interlocked.Increment(ref _scanGeneration);
        _scanCancellation?.Cancel();
        _filterCancellation?.Cancel();
        _scanCancellation = null;
        _filterCancellation = null;
        GC.SuppressFinalize(this);
    }
}
