using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using MySmallTools.Business.SecretVideoPlayer.Decryption;
using MySmallTools.Business.SecretVideoPlayer.Operations;

namespace MySmallTools.ViewModels.SecretVideoPlayer;

/// <summary>
/// 批量解密 Document：管理页面队列、预检展示、命令状态和任务生命周期。
/// </summary>
public partial class VideoDecryptorViewModel : Document, IDisposable
{
    private readonly IVideoDecryptionService _decryptionService;
    private readonly ObservableCollection<DecryptionQueueItemViewModel> _items = [];
    private readonly ObservableCollection<VideoPreflightIssue> _preflightIssues = [];
    private CancellationTokenSource? _operationCancellation;
    private int _operationGeneration;
    private bool _disposed;

    public VideoDecryptorViewModel(IVideoDecryptionService decryptionService)
    {
        _decryptionService = decryptionService ?? throw new ArgumentNullException(nameof(decryptionService));
        Items = new ReadOnlyObservableCollection<DecryptionQueueItemViewModel>(_items);
        PreflightIssues = new ReadOnlyObservableCollection<VideoPreflightIssue>(_preflightIssues);
    }

    public ReadOnlyObservableCollection<DecryptionQueueItemViewModel> Items { get; }
    public ReadOnlyObservableCollection<VideoPreflightIssue> PreflightIssues { get; }
    public int ItemCount => _items.Count;
    public bool HasItems => _items.Count > 0;
    public bool HasPreflightIssues => _preflightIssues.Count > 0;
    public bool IsBusy => IsInspecting || IsPreflighting || IsRunning;

    [ObservableProperty] private DecryptionQueueItemViewModel? _selectedItem;
    [ObservableProperty] private string _outputDirectory = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private bool _showPassword;
    [ObservableProperty] private bool _isInspecting;
    [ObservableProperty] private bool _isPreflighting;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private double _overallProgress;
    [ObservableProperty] private string _currentFile = string.Empty;
    [ObservableProperty] private string _statusMessage = "请添加要解密的 SECVID03 视频";
    [ObservableProperty] private int _succeededCount;
    [ObservableProperty] private int _failedCount;
    [ObservableProperty] private int _cancelledCount;

    partial void OnSelectedItemChanged(DecryptionQueueItemViewModel? value) =>
        RemoveSelectedCommand.NotifyCanExecuteChanged();

    partial void OnOutputDirectoryChanged(string value) => NotifyCommandStates();
    partial void OnPasswordChanged(string value) => NotifyCommandStates();
    partial void OnIsInspectingChanged(bool value) => OnBusyStateChanged();
    partial void OnIsPreflightingChanged(bool value) => OnBusyStateChanged();
    partial void OnIsRunningChanged(bool value) => OnBusyStateChanged();

    public async Task AddFilesAsync(IReadOnlyList<string> paths)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsBusy || paths.Count == 0)
            return;

        var existingPaths = _items
            .Select(item => item.InputPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newPaths = paths
            .Select(path =>
            {
                try { return Path.GetFullPath(path); }
                catch { return path; }
            })
            .Where(path => !existingPaths.Contains(path))
            .ToArray();
        if (newPaths.Length == 0)
        {
            StatusMessage = "所选文件已经在队列中";
            return;
        }

        var generation = Interlocked.Increment(ref _operationGeneration);
        var cancellation = ReplaceCancellation();
        IsInspecting = true;
        StatusMessage = "正在读取视频公开信息...";
        try
        {
            var candidates = await _decryptionService.InspectAsync(newPaths, cancellation.Token);
            if (!IsCurrent(generation))
                return;

            foreach (var candidate in candidates)
                _items.Add(new DecryptionQueueItemViewModel(candidate));
            OnQueueChanged();
            StatusMessage = $"已添加 {candidates.Count} 个文件，共 {_items.Count} 个";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (IsCurrent(generation))
                StatusMessage = "文件预检已取消";
        }
        catch
        {
            if (IsCurrent(generation))
                StatusMessage = "添加文件失败：无法读取所选文件。";
        }
        finally
        {
            ReleaseCancellation(cancellation);
            if (IsCurrent(generation))
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
        StatusMessage = _items.Count == 0
            ? "请添加要解密的 SECVID03 视频"
            : $"队列中剩余 {_items.Count} 个文件";
    }

    private bool CanRemoveSelected() => !_disposed && !IsBusy && SelectedItem is not null;

    [RelayCommand(CanExecute = nameof(CanClear))]
    private void Clear()
    {
        _items.Clear();
        _preflightIssues.Clear();
        SelectedItem = null;
        OverallProgress = 0;
        CurrentFile = string.Empty;
        SucceededCount = 0;
        FailedCount = 0;
        CancelledCount = 0;
        OnPropertyChanged(nameof(HasPreflightIssues));
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
            .Where(item => item.State != VideoTaskState.Succeeded)
            .ToArray();
        if (workItems.Length == 0)
            return;

        foreach (var item in workItems)
            item.ResetForRetry();

        var generation = Interlocked.Increment(ref _operationGeneration);
        var cancellation = ReplaceCancellation();
        OverallProgress = 0;
        CurrentFile = string.Empty;
        _preflightIssues.Clear();
        OnPropertyChanged(nameof(HasPreflightIssues));

        try
        {
            IsPreflighting = true;
            StatusMessage = $"正在重新检查 {workItems.Length} 个视频和输出环境...";

            var refreshed = await _decryptionService.InspectAsync(
                workItems.Select(item => item.InputPath).ToArray(),
                cancellation.Token);
            if (!IsCurrent(generation))
                return;

            var refreshedByPath = refreshed.ToDictionary(
                candidate => candidate.InputPath,
                StringComparer.OrdinalIgnoreCase);
            foreach (var item in workItems)
            {
                if (refreshedByPath.TryGetValue(item.InputPath, out var candidate))
                    item.ApplyInspection(candidate);
            }

            var preflight = await _decryptionService.PreflightAsync(
                workItems.Select(item => item.Candidate).ToArray(),
                OutputDirectory,
                cancellation.Token);
            if (!IsCurrent(generation))
                return;

            foreach (var issue in preflight.Overall.Issues)
                _preflightIssues.Add(issue);
            OnPropertyChanged(nameof(HasPreflightIssues));

            var itemByPath = workItems.ToDictionary(item => item.InputPath, StringComparer.OrdinalIgnoreCase);
            foreach (var itemPreflight in preflight.Items)
            {
                if (itemByPath.TryGetValue(itemPreflight.Candidate.InputPath, out var item))
                    item.ApplyPreflight(itemPreflight);
            }
            RecalculateCounts();

            var globalBlocker = preflight.Overall.Issues.FirstOrDefault(issue =>
                issue.Severity == PreflightSeverity.Blocking);
            if (globalBlocker is not null)
            {
                StatusMessage = $"{globalBlocker.Message} {globalBlocker.SuggestedAction}";
                return;
            }
            if (!preflight.HasRunnableItems)
            {
                StatusMessage = "没有通过预检的文件，请处理队列中的失败项后重试。";
                return;
            }

            IsPreflighting = false;
            IsRunning = true;
            StatusMessage = $"准备解密 {workItems.Length} 个视频...";
            var progress = new Progress<BatchDecryptionProgress>(value =>
            {
                if (IsCurrent(generation))
                    ApplyProgress(value);
            });
            var result = await _decryptionService.DecryptBatchAsync(
                workItems.Select(item => item.Candidate).ToArray(),
                OutputDirectory,
                Password,
                progress,
                cancellation.Token);

            if (!IsCurrent(generation))
                return;

            RecalculateCounts();
            OverallProgress = 100;
            CurrentFile = string.Empty;
            StatusMessage = $"批次完成：成功 {result.SucceededCount}，失败 {result.FailedCount}";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (IsCurrent(generation))
            {
                foreach (var item in workItems.Where(item =>
                             item.State is VideoTaskState.Pending or VideoTaskState.Preflighting or
                                 VideoTaskState.Ready or VideoTaskState.Running))
                {
                    item.State = VideoTaskState.Cancelled;
                    item.FailureCode = VideoTaskFailureCode.Cancelled;
                    item.Message = "批次已取消";
                }

                RecalculateCounts();
                CurrentFile = string.Empty;
                StatusMessage = "批量解密已取消，已成功提交的文件予以保留。";
            }
        }
        catch (VideoTaskException ex)
        {
            if (IsCurrent(generation))
                StatusMessage = $"{ex.Message}（错误代码：{ex.FailureCode}）";
        }
        catch
        {
            if (IsCurrent(generation))
                StatusMessage = "批量解密失败：发生未预期错误，请检查输入和输出环境。";
        }
        finally
        {
            ReleaseCancellation(cancellation);
            if (IsCurrent(generation))
            {
                IsPreflighting = false;
                IsRunning = false;
            }
        }
    }

    private bool CanStartDecryption() =>
        !_disposed &&
        !IsBusy &&
        !string.IsNullOrWhiteSpace(Password) &&
        !string.IsNullOrWhiteSpace(OutputDirectory) &&
        _items.Any(item => item.State != VideoTaskState.Succeeded);

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        try
        {
            Volatile.Read(ref _operationCancellation)?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 操作恰好结束时无需再次处理。
        }
    }

    private bool CanCancel() => !_disposed && IsBusy;

    private void ApplyProgress(BatchDecryptionProgress progress)
    {
        var item = _items.FirstOrDefault(candidate =>
            string.Equals(candidate.InputPath, progress.InputPath, StringComparison.OrdinalIgnoreCase));
        if (item is null)
            return;

        item.State = progress.State;
        item.Progress = progress.FilePercentage;
        item.Message = progress.Message;
        item.OutputPath = progress.OutputPath;
        item.FailureCode = progress.FailureCode;
        OverallProgress = progress.OverallPercentage;
        CurrentFile = progress.State == VideoTaskState.Running ? item.EncryptedFileName : string.Empty;
        StatusMessage = progress.FailureCode is null
            ? progress.Message
            : $"{progress.Message}（错误代码：{progress.FailureCode}）";
        RecalculateCounts();
    }

    private void RecalculateCounts()
    {
        SucceededCount = _items.Count(item => item.State == VideoTaskState.Succeeded);
        FailedCount = _items.Count(item => item.State == VideoTaskState.Failed);
        CancelledCount = _items.Count(item => item.State == VideoTaskState.Cancelled);
    }

    private void OnQueueChanged()
    {
        OnPropertyChanged(nameof(ItemCount));
        OnPropertyChanged(nameof(HasItems));
        RecalculateCounts();
        NotifyCommandStates();
    }

    private void OnBusyStateChanged()
    {
        OnPropertyChanged(nameof(IsBusy));
        NotifyCommandStates();
    }

    private void NotifyCommandStates()
    {
        StartDecryptionCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        RemoveSelectedCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
    }

    private CancellationTokenSource ReplaceCancellation()
    {
        var cancellation = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _operationCancellation, cancellation);
        previous?.Cancel();
        previous?.Dispose();
        return cancellation;
    }

    private void ReleaseCancellation(CancellationTokenSource cancellation)
    {
        Interlocked.CompareExchange(ref _operationCancellation, null, cancellation);
        cancellation.Dispose();
    }

    private bool IsCurrent(int generation) =>
        !_disposed && generation == Volatile.Read(ref _operationGeneration);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Interlocked.Increment(ref _operationGeneration);
        Password = string.Empty;
        Interlocked.Exchange(ref _operationCancellation, null)?.Cancel();
        GC.SuppressFinalize(this);
    }
}
