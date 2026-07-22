using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using MySmallTools.Business.SecretVideoPlayer;

namespace MySmallTools.ViewModels.SecretVideoPlayer;

/// <summary>
/// 批量解密 Document：只管理页面队列、命令状态和任务生命周期。
/// </summary>
public partial class VideoDecryptorViewModel : Document, IDisposable
{
    private readonly IVideoDecryptionService _decryptionService;
    private readonly ObservableCollection<DecryptionQueueItemViewModel> _items = [];
    private CancellationTokenSource? _operationCancellation;
    private bool _disposed;

    public VideoDecryptorViewModel(IVideoDecryptionService decryptionService)
    {
        _decryptionService = decryptionService ?? throw new ArgumentNullException(nameof(decryptionService));
        Items = new ReadOnlyObservableCollection<DecryptionQueueItemViewModel>(_items);
    }

    public ReadOnlyObservableCollection<DecryptionQueueItemViewModel> Items { get; }
    public int ItemCount => _items.Count;
    public bool IsBusy => IsInspecting || IsRunning;

    [ObservableProperty] private DecryptionQueueItemViewModel? _selectedItem;
    [ObservableProperty] private string _outputDirectory = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private bool _showPassword;
    [ObservableProperty] private bool _isInspecting;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private double _overallProgress;
    [ObservableProperty] private string _currentFile = string.Empty;
    [ObservableProperty] private string _statusMessage = "请添加要解密的 SECVID03 视频";
    [ObservableProperty] private int _succeededCount;
    [ObservableProperty] private int _failedCount;
    [ObservableProperty] private int _cancelledCount;

    partial void OnSelectedItemChanged(DecryptionQueueItemViewModel? value) =>
        RemoveSelectedCommand.NotifyCanExecuteChanged();

    partial void OnOutputDirectoryChanged(string value) => StartDecryptionCommand.NotifyCanExecuteChanged();
    partial void OnPasswordChanged(string value) => StartDecryptionCommand.NotifyCanExecuteChanged();

    partial void OnIsInspectingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsBusy));
        NotifyCommandStates();
    }

    partial void OnIsRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(IsBusy));
        NotifyCommandStates();
    }

    public async Task AddFilesAsync(IReadOnlyList<string> paths)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsBusy || paths.Count == 0)
            return;

        var existingPaths = _items
            .Select(item => item.InputPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newPaths = paths.Where(path => !existingPaths.Contains(Path.GetFullPath(path))).ToArray();
        if (newPaths.Length == 0)
        {
            StatusMessage = "所选文件已经在队列中";
            return;
        }

        IsInspecting = true;
        StatusMessage = "正在读取视频公开信息...";
        try
        {
            var candidates = await _decryptionService.InspectAsync(newPaths);
            if (_disposed)
                return;

            foreach (var candidate in candidates)
                _items.Add(new DecryptionQueueItemViewModel(candidate));

            OnQueueChanged();
            StatusMessage = $"已添加 {candidates.Count} 个文件，共 {_items.Count} 个";
        }
        catch (OperationCanceledException)
        {
            if (!_disposed)
                StatusMessage = "文件预检已取消";
        }
        catch (Exception ex)
        {
            if (!_disposed)
                StatusMessage = $"添加文件失败：{ex.Message}";
        }
        finally
        {
            if (!_disposed)
                IsInspecting = false;
        }
    }

    public void SetOutputDirectory(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsBusy && !string.IsNullOrWhiteSpace(path))
            OutputDirectory = Path.GetFullPath(path);
    }

    [RelayCommand(CanExecute = nameof(CanRemoveSelected))]
    private void RemoveSelected()
    {
        if (SelectedItem is not { } selected || !_items.Remove(selected))
            return;

        SelectedItem = null;
        OnQueueChanged();
        StatusMessage = _items.Count == 0 ? "请添加要解密的 SECVID03 视频" : $"队列中剩余 {_items.Count} 个文件";
    }

    private bool CanRemoveSelected() => !_disposed && !IsBusy && SelectedItem is not null;

    [RelayCommand(CanExecute = nameof(CanClear))]
    private void Clear()
    {
        _items.Clear();
        SelectedItem = null;
        OverallProgress = 0;
        CurrentFile = string.Empty;
        SucceededCount = 0;
        FailedCount = 0;
        CancelledCount = 0;
        OnQueueChanged();
        StatusMessage = "请添加要解密的 SECVID03 视频";
    }

    private bool CanClear() => !_disposed && !IsBusy && _items.Count > 0;

    [RelayCommand]
    private void TogglePasswordVisibility() => ShowPassword = !ShowPassword;

    [RelayCommand(CanExecute = nameof(CanStartDecryption))]
    private async Task StartDecryptionAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var workItems = _items
            .Where(item => item.Candidate.IsValid && item.State != DecryptionItemState.Succeeded)
            .ToArray();
        if (workItems.Length == 0)
            return;

        foreach (var item in workItems)
            item.ResetForRetry();

        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _operationCancellation, cancellation);
        previous?.Cancel();
        previous?.Dispose();

        IsRunning = true;
        OverallProgress = 0;
        CurrentFile = string.Empty;
        StatusMessage = $"准备解密 {workItems.Length} 个视频...";
        RecalculateCounts();

        try
        {
            var progress = new Progress<BatchDecryptionProgress>(ApplyProgress);
            var result = await _decryptionService.DecryptBatchAsync(
                workItems.Select(item => item.Candidate).ToArray(),
                OutputDirectory,
                Password,
                progress,
                cancellation.Token);

            if (_disposed)
                return;

            RecalculateCounts();
            OverallProgress = 100;
            CurrentFile = string.Empty;
            StatusMessage = $"批次完成：成功 {result.SucceededCount}，失败 {result.FailedCount}";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (!_disposed)
            {
                RecalculateCounts();
                CurrentFile = string.Empty;
                StatusMessage = "批量解密已取消，已完成的文件予以保留";
            }
        }
        catch (VideoDecryptionException ex)
        {
            if (!_disposed)
                StatusMessage = $"无法开始批量解密：{ex.Message}";
        }
        catch (Exception ex)
        {
            if (!_disposed)
                StatusMessage = $"批量解密失败：{ex.Message}";
        }
        finally
        {
            Interlocked.CompareExchange(ref _operationCancellation, null, cancellation);
            cancellation.Dispose();
            if (!_disposed)
                IsRunning = false;
        }
    }

    private bool CanStartDecryption() =>
        !_disposed &&
        !IsBusy &&
        !string.IsNullOrWhiteSpace(Password) &&
        Directory.Exists(OutputDirectory) &&
        _items.Any(item => item.Candidate.IsValid && item.State != DecryptionItemState.Succeeded);

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _operationCancellation?.Cancel();

    private bool CanCancel() => !_disposed && IsRunning;

    private void ApplyProgress(BatchDecryptionProgress progress)
    {
        if (_disposed)
            return;

        var item = _items.FirstOrDefault(candidate =>
            string.Equals(candidate.InputPath, progress.InputPath, StringComparison.OrdinalIgnoreCase));
        if (item is null)
            return;

        item.State = progress.State;
        item.Progress = progress.FilePercentage;
        item.Message = progress.Message;
        item.OutputPath = progress.OutputPath;
        OverallProgress = progress.OverallPercentage;
        CurrentFile = progress.State == DecryptionItemState.Running ? item.EncryptedFileName : string.Empty;
        StatusMessage = progress.Message;
        RecalculateCounts();
    }

    private void RecalculateCounts()
    {
        SucceededCount = _items.Count(item => item.State == DecryptionItemState.Succeeded);
        FailedCount = _items.Count(item => item.State == DecryptionItemState.Failed);
        CancelledCount = _items.Count(item => item.State == DecryptionItemState.Cancelled);
    }

    private void OnQueueChanged()
    {
        OnPropertyChanged(nameof(ItemCount));
        RecalculateCounts();
        NotifyCommandStates();
    }

    private void NotifyCommandStates()
    {
        StartDecryptionCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        RemoveSelectedCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Password = string.Empty;
        Interlocked.Exchange(ref _operationCancellation, null)?.Cancel();
        GC.SuppressFinalize(this);
    }
}
