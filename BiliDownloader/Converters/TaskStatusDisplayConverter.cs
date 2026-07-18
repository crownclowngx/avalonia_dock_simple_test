using Avalonia.Data.Converters;
using System.Globalization;
using BiliDownloader.Models;

namespace BiliDownloader.Converters;

/// <summary>
/// 统一的任务状态显示转换器（委托给 DownloadTaskStatusMapper）
/// </summary>
public static class TaskStatusDisplay
{
    /// <summary>
    /// 将存储状态字符串转换为中文显示文本
    /// </summary>
    public static string ToDisplayText(string status)
        => DownloadTaskStatusMapper.ToDisplayText(DownloadTaskStatusMapper.FromStorageString(status));

    /// <summary>
    /// 将存储状态字符串转换为阶段文本（与 ToDisplayText 相同，用于 UI 列显示）
    /// </summary>
    public static string ToStageText(string status) => ToDisplayText(status);
}

/// <summary>
/// Avalonia ValueConverter：用于 XAML 绑定中的状态文本转换
/// </summary>
public class TaskStatusDisplayConverter : IValueConverter
{
    public static readonly TaskStatusDisplayConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string status)
            return TaskStatusDisplay.ToDisplayText(status);
        return value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
