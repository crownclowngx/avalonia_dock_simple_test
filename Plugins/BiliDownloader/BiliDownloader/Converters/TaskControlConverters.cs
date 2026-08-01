using System;
using System.Globalization;
using Avalonia.Data.Converters;
using BiliDownloader.Models;

namespace BiliDownloader.Converters;

/// <summary>
/// G2: 运行中状态可见性（暂停/取消按钮显示条件）。
/// 当状态为 downloading_video/downloading_audio/merging/fetching_metadata 时返回 true。
/// </summary>
public class IsRunningStatusConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string s) return false;
        return DownloadTaskStatusMapper.IsRunning(DownloadTaskStatusMapper.FromStorageString(s));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// G2: 暂停/等待登录状态可见性（恢复按钮显示条件）。
/// 当状态为 paused 或 waiting_for_login 时返回 true。
/// </summary>
public class IsPausedStatusConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string s) return false;
        var status = DownloadTaskStatusMapper.FromStorageString(s);
        return status is DownloadTaskStatus.Paused or DownloadTaskStatus.WaitingForLogin;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// G2: 可取消状态可见性（非终态均可取消）。
/// 排除 done/canceled 终态。
/// </summary>
public class IsCancelableStatusConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string s) return false;
        var status = DownloadTaskStatusMapper.FromStorageString(s);
        return status is not (DownloadTaskStatus.Completed or DownloadTaskStatus.Canceled);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// G2: 可重新开始状态（失败/中断/取消）。
/// </summary>
public class IsRestartableStatusConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string s) return false;
        var status = DownloadTaskStatusMapper.FromStorageString(s);
        return status is DownloadTaskStatus.Failed
            or DownloadTaskStatus.Interrupted
            or DownloadTaskStatus.Canceled;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
