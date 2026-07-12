using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using MyAvaloniaManagementCommon.DocumentCreation;
using MyAvaloniaManagementCommon.Message;
using MyAvaloniaManagementCommon.Save;
using BiliDownloader.Constants;
using BiliDownloader.Messages;
using BiliDownloader.Models;
using BiliDownloader.Services;
using BiliDownloader.ViewModels.BiliDownloader;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BiliDownloader.ViewModels;

/// <summary>
/// BiliDownloader Document ViewModel：负责子 VM 组合、任务提交、进度接收、持久化
/// </summary>
public class BiliDownloaderViewModel : Document, ISavableDocument
{
    public string SaveDocumentTypeId => SaveDocumentTypeIdConstant.BiliDownloaderDocumentId;
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// 本 Document 实例的唯一标识（持久化到 SaveData，跨重启不丢）
    /// </summary>
    public string DocumentId { get; private set; } = Guid.NewGuid().ToString("N");

    private readonly IMessengerService? _messengerService;

    #region 子 ViewModel

    public LoginBarViewModel LoginBar { get; }
    public VideoParseViewModel VideoParse { get; }
    public DownloadConfigViewModel DownloadConfig { get; }
    public RenamePanelViewModel RenamePanel { get; }

    #endregion

    #region 属性

    public ObservableCollection<BiliVideoItem> VideoItems { get; } = new();

    private BiliVideoCollection? _videoCollection;

    private bool _isParsed;
    public bool IsParsed
    {
        get => _isParsed;
        set => SetProperty(ref _isParsed, value);
    }

    private double _totalProgress;
    public double TotalProgress
    {
        get => _totalProgress;
        set => SetProperty(ref _totalProgress, value);
    }

    private string _downloadInfo = "";
    public string DownloadInfo
    {
        get => _downloadInfo;
        set => SetProperty(ref _downloadInfo, value);
    }

    #endregion

    #region Commands

    public IRelayCommand SubmitDownloadCommand { get; }
    public IRelayCommand SelectAllCommand { get; }
    public IRelayCommand DeselectAllCommand { get; }

    #endregion

    public BiliDownloaderViewModel()
    {
        // 初始化子 ViewModel（通过回调通信）
        LoginBar = new LoginBarViewModel();

        VideoParse = new VideoParseViewModel(
            onParsed: HandleParseResult,
            isLoggedInCheck: () => LoginBar.IsLoggedIn);

        DownloadConfig = new DownloadConfigViewModel();

        RenamePanel = new RenamePanelViewModel(
            onRenameApplied: ApplyRenameToVideoItems,
            getVideoCount: () => VideoItems.Count);

        SubmitDownloadCommand = new RelayCommand(SubmitDownload);
        SelectAllCommand = new RelayCommand(() => { foreach (var v in VideoItems) v.IsSelected = true; });
        DeselectAllCommand = new RelayCommand(() => { foreach (var v in VideoItems) v.IsSelected = false; });

        // 注册消息总线
        try
        {
            _messengerService = new MessengerService();

            // 登录状态变更 -> 同步到 LoginBar 子 VM
            _messengerService.Register<BiliDownloaderViewModel, LoginStateChangedMessage>(
                this, (vm, msg) =>
                {
                    vm.LoginBar.IsLoggedIn = msg.IsLoggedIn;
                    vm.LoginBar.UserName = msg.UserName;
                });

            // 下载进度回传（按 DocumentId 过滤）
            _messengerService.Register<BiliDownloaderViewModel, DownloadTaskProgressMessage>(
                this, (vm, msg) =>
                {
                    if (msg.TargetDocumentId != vm.DocumentId) return;
                    vm.HandleProgressMessage(msg);
                });

            // 任务被删除通知（从 VideoItems 中移除）
            _messengerService.Register<BiliDownloaderViewModel, DownloadTaskDeletedMessage>(
                this, (vm, msg) =>
                {
                    if (msg.TargetDocumentId != vm.DocumentId) return;
                    var item = vm.VideoItems.FirstOrDefault(v => v.ItemId == msg.TaskId);
                    if (item != null)
                        vm.VideoItems.Remove(item);
                });

            // 调度器自主状态变更通知（重试、自动恢复等）
            _messengerService.Register<BiliDownloaderViewModel, DownloadTaskStatusChangedMessage>(
                this, (vm, msg) =>
                {
                    if (msg.TargetDocumentId != vm.DocumentId) return;
                    var item = vm.VideoItems.FirstOrDefault(v => v.ItemId == msg.TaskId);
                    if (item == null) return;
                    item.Status = MapStatusToDisplay(msg.NewStatus);
                    item.StageText = MapStageToDisplay(msg.NewStatus);
                    item.Progress = msg.Progress;
                    item.VideoProgress = msg.VideoProgress;
                    item.AudioProgress = msg.AudioProgress;
                    item.MergeProgress = msg.MergeProgress;
                    item.SpeedText = msg.SpeedText;
                });
        }
        catch
        {
            // ServiceProvider 尚未初始化时忽略
        }
    }

    #region 子 VM 回调处理

    /// <summary>
    /// 解析成功后的回调：填充 VideoItems、分发清晰度到 DownloadConfig、初始化重命名面板
    /// </summary>
    private void HandleParseResult(VideoParseResult result)
    {
        _videoCollection = result.Collection;

        // 填充视频列表
        VideoItems.Clear();
        foreach (var item in result.VideoItems)
            VideoItems.Add(item);

        // 分发清晰度到 DownloadConfig
        DownloadConfig.PopulateQualities(
            result.QualityOptions,
            result.SelectedQuality,
            result.AudioQualityOptions,
            result.SelectedAudioQuality,
            result.IsMultiVideo);

        // 初始化重命名面板
        RenamePanel.InitTitles(result.VideoItems);

        IsParsed = true;
        IsModified = true;

        // 同步解析状态到 VideoParse 子 VM
        VideoParse.IsParsed = true;
    }

    /// <summary>
    /// 重命名应用后的回调：将新标题写入 VideoItems
    /// </summary>
    private void ApplyRenameToVideoItems(List<string> newTitles)
    {
        for (int i = 0; i < VideoItems.Count && i < newTitles.Count; i++)
        {
            if (!string.IsNullOrEmpty(newTitles[i]))
                VideoItems[i].Title = newTitles[i];
        }

        DownloadInfo = $"已应用批量重命名（{VideoItems.Count} 个视频）";
    }

    #endregion

    /// <summary>
    /// 确保登录状态已初始化（由 View 的 OnAttachedToVisualTree 调用）
    /// </summary>
    public async Task EnsureLoggedInAsync()
    {
        await LoginBar.EnsureLoggedInAsync();
    }

    /// <summary>
    /// 从 SQLite 恢复本 Document 的未完成任务状态（由 View 首次加载时调用）
    /// </summary>
    public async Task RecoverTasksFromStoreAsync()
    {
        try
        {
            var store = new DownloadTaskStore();
            await store.InitAsync();
            var records = await store.GetByDocumentIdAsync(DocumentId);

            int idx = VideoItems.Count + 1;
            foreach (var record in records)
            {
                // 查找已有的 VideoItem（按 ItemId 匹配），没有则创建
                var item = VideoItems.FirstOrDefault(v => v.ItemId == record.TaskId);
                if (item == null)
                {
                    item = new BiliVideoItem
                    {
                        Index = idx++,
                        ItemId = record.TaskId,
                        OriginalTitle = record.ItemTitle,
                        Title = record.ItemTitle,
                        Aid = record.Aid,
                        Bvid = record.Bvid,
                        Cid = record.Cid,
                        IsSelected = false, // 已提交的任务不再勾选
                    };
                    VideoItems.Add(item);
                }

                item.Status = MapStatusToDisplay(record.Status);
                item.StageText = MapStageToDisplay(record.Status);
                item.Progress = record.Progress;
                item.VideoProgress = record.VideoProgress;
                item.AudioProgress = record.AudioProgress;
                item.MergeProgress = record.MergeProgress;
                item.SpeedText = record.SpeedText;
            }
        }
        catch (Exception ex)
        {
            DownloadInfo = $"恢复任务状态失败: {ex.Message}";
        }
    }

    #region 任务提交

    private void SubmitDownload()
    {
        if (!IsParsed || VideoItems.Count == 0)
        {
            DownloadInfo = "请先解析视频";
            return;
        }

        var selectedItems = VideoItems.Where(v => v.IsSelected).ToList();
        if (selectedItems.Count == 0)
        {
            DownloadInfo = "请至少勾选一个视频";
            return;
        }

        if (DownloadConfig.SelectedQuality == null)
        {
            DownloadInfo = "请选择清晰度";
            return;
        }

        // 检查 ffmpeg 是否就绪
        if (!FfmpegService.IsReady)
        {
            DownloadInfo = "ffmpeg 未就绪，请在调度器工具中等待下载完成或手动配置路径";
            return;
        }

        // 构造消息
        var downloadItems = selectedItems.Select(v => new DownloadItemInfo
        {
            ItemId = v.ItemId,
            Title = DownloadConfig.AddIndexToTitle ? $"{v.Index}.{v.Title}" : v.Title,
            Aid = v.Aid,
            Bvid = v.Bvid,
            Cid = v.Cid,
            Duration = v.Duration,
        }).ToList();

        var message = new SubmitDownloadTaskMessage(
            sourceDocumentId: DocumentId,
            seriesTitle: _videoCollection?.SeriesTitle ?? "下载",
            items: downloadItems,
            qualityId: DownloadConfig.SelectedQuality.QualityId,
            audioQualityId: DownloadConfig.SelectedAudioQuality?.QualityId ?? 0,
            outputDirectory: DownloadConfig.OutputDirectory,
            cookie: BiliLoginStateService.Instance.CookieHeader,
            useGroupFolder: DownloadConfig.UseGroupFolder);

        // 通过消息总线发送给调度器
        try
        {
            _messengerService?.Send(message);
            DownloadInfo = $"已提交 {selectedItems.Count} 个下载任务到调度器";

            // 标记为已提交
            foreach (var item in selectedItems)
            {
                item.Status = "排队中";
                item.IsSelected = false;
            }
        }
        catch (Exception ex)
        {
            DownloadInfo = $"提交任务失败: {ex.Message}";
        }
    }

    #endregion

    #region 进度接收

    private void HandleProgressMessage(DownloadTaskProgressMessage msg)
    {
        var item = VideoItems.FirstOrDefault(v => v.ItemId == msg.TaskId);
        if (item == null) return;

        item.Status = MapStatusToDisplay(msg.Status);
        item.StageText = MapStageToDisplay(msg.Status);
        item.Progress = msg.Progress;
        item.VideoProgress = msg.VideoProgress;
        item.AudioProgress = msg.AudioProgress;
        item.MergeProgress = msg.MergeProgress;
        item.SpeedText = msg.SpeedText;

        if (msg.Status == "failed" && !string.IsNullOrEmpty(msg.ErrorMessage))
        {
            item.Status = $"失败: {msg.ErrorMessage}";
            item.StageText = "失败";
        }

        // 更新总进度
        if (VideoItems.Count > 0)
        {
            TotalProgress = VideoItems.Average(v => v.Progress);
        }
    }

    private static string MapStatusToDisplay(string status) => status switch
    {
        "pending" => "排队中",
        "downloading_video" => "下载视频",
        "downloading_audio" => "下载音频",
        "merging" => "合并中",
        "done" => "完成",
        "failed" => "失败",
        _ => status,
    };

    private static string MapStageToDisplay(string status) => status switch
    {
        "pending" => "排队中",
        "downloading_video" => "下载视频",
        "downloading_audio" => "下载音频",
        "merging" => "合并中",
        "done" => "完成",
        "failed" => "失败",
        _ => status,
    };

    #endregion

    #region 持久化

    public DocumentSaveData CreateSaveDocumentMetaData(string filePath)
    {
        var saveDataObject = new
        {
            DocumentId,
            Url = VideoParse.Url,
            DownloadInfo = _downloadInfo,
            OutputDirectory = DownloadConfig.OutputDirectory,
            UseGroupFolder = DownloadConfig.UseGroupFolder,
            AddIndexToTitle = DownloadConfig.AddIndexToTitle,
        };

        var saveData = new DocumentSaveData
        {
            DocumentTypeId = SaveDocumentTypeId,
            Title = Title,
            SaveTime = DateTime.Now,
            Content = JsonConvert.SerializeObject(saveDataObject),
            PluginMetadata = JsonConvert.SerializeObject(new { Version = "1.0" })
        };

        IsModified = false;
        return saveData;
    }

    public void LoadDocumentByMetaData(DocumentSaveData saveData)
    {
        try
        {
            if (saveData == null) return;
            var data = JsonConvert.DeserializeObject<dynamic>(saveData.Content);
            if (data == null) return;

            var url = data.Url?.ToString() ?? "";
            var downloadInfo = data.DownloadInfo?.ToString() ?? "";
            var outputDirectory = data.OutputDirectory?.ToString() ?? DownloadConfig.OutputDirectory;

            VideoParse.Url = url;
            DownloadInfo = downloadInfo;
            DownloadConfig.OutputDirectory = outputDirectory;

            // 恢复 UseGroupFolder
            var useGroupFolderVal = data.UseGroupFolder;
            if (useGroupFolderVal != null && useGroupFolderVal.Type != JTokenType.Null)
                DownloadConfig.UseGroupFolder = (bool)useGroupFolderVal;

            // 恢复 AddIndexToTitle
            var addIndexVal = data.AddIndexToTitle;
            if (addIndexVal != null && addIndexVal.Type != JTokenType.Null)
                DownloadConfig.AddIndexToTitle = (bool)addIndexVal;

            // 恢复 DocumentId
            var savedDocId = data.DocumentId?.ToString();
            if (!string.IsNullOrEmpty(savedDocId))
                DocumentId = savedDocId;

            OnPropertyChanged(nameof(DocumentId));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"加载文档错误: {ex.Message}");
        }
    }

    #endregion
}
