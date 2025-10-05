using System.ComponentModel;
using System.Runtime.Serialization;
using Avalonia;
using Avalonia.Controls;
using LibVLCSharp.Shared;
using MySmallTools.Business.SecretVideoPlayer;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Model.Mvvm.Controls;
// 添加 CommunityToolkit.Mvvm 命名空间
using CommunityToolkit.Mvvm.Input;
using MySmallTools.Constants.SecretVideoPlayer;
using Ursa.Controls;
using TimeChangedEventArgs = MySmallTools.Business.SecretVideoPlayer.TimeChangedEventArgs;

namespace MySmallTools.ViewModels.SecretVideoPlayer;

/// <summary>
/// 加密视频播放器视图模型
/// 现在主要负责文件选择和加载，播放功能委托给 VideoPlayerControlViewModel
/// </summary>
public partial class SecretVideoPlayerViewModel : Document, IDisposable
{
    private bool _disposed = false;

    #region Properties

    /// <summary>
    /// 文件路径
    /// </summary>
    [ObservableProperty] 
    private string _filePath = string.Empty;

    /// <summary>
    /// 解密密码
    /// </summary>
    [ObservableProperty] 
    private string _password = string.Empty;

    /// <summary>
    /// 状态消息
    /// </summary>
    [ObservableProperty] 
    private string _statusMessage = "请选择加密视频文件";
    
    /// <summary>
    /// 是否正在加载
    /// </summary>
    [ObservableProperty] 
    private bool _isLoading = false;

    /// <summary>
    /// 播放器控件的 ViewModel
    /// </summary>
    [ObservableProperty]
    private VideoPlayerControlViewModel _playerViewModel;

    #endregion

    #region Constructor

    public SecretVideoPlayerViewModel()
    {
        PlayerViewModel = new VideoPlayerControlViewModel();
    }

    #endregion
    
    #region Commands

    /// <summary>
    /// 加载视频命令
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanLoadVideo))]
    private async Task LoadVideoAsync()
    {
        if (string.IsNullOrEmpty(FilePath) || string.IsNullOrEmpty(Password))
        {
            StatusMessage = "请输入文件路径和密码";
            return;
        }

        if (!File.Exists(FilePath))
        {
            StatusMessage = "文件不存在";
            return;
        }

        IsLoading = true;
        StatusMessage = "正在加载视频文件...";

        try
        {
            var success = await PlayerViewModel.LoadMediaAsync(FilePath, Password);
            GC.Collect(GC.MaxGeneration);
            
            if (success)
            {
                StatusMessage = "视频文件加载成功，可以开始播放";
            }
            else
            {
                StatusMessage = "加载失败，请检查文件和密码";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            // 更新加载命令状态
            LoadVideoCommand.NotifyCanExecuteChanged();
        }
    }

    #endregion

    #region Methods

    /// <summary>
    /// 检查是否可以加载视频
    /// </summary>
    private bool CanLoadVideo()
    {
        return !IsLoading;
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // 释放播放器控件资源
                PlayerViewModel?.Dispose();
            }

            _disposed = true;
        }
    }
    
    public override bool OnClose()
    {
        PlayerViewModel?.CleanupMedia();
        return base.OnClose();
    }

    #endregion
}