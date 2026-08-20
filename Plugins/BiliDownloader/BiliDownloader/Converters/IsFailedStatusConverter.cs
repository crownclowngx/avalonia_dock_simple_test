using System;
using System.Globalization;
using Avalonia.Data.Converters;
using BiliDownloader.Models;

namespace BiliDownloader.Converters;

/// <summary>
/// 将 Status 字符串转换为 bool：仅当值为 Failed 时返回 true
/// </summary>
public class IsFailedStatusConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is string s && DownloadTaskStatusMapper.FromStorageString(s) == DownloadTaskStatus.Failed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("失败状态转换器只支持单向显示绑定。");
    }
}
