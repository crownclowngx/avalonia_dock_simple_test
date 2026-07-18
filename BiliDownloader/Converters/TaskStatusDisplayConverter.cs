using Avalonia.Data.Converters;
using System.Globalization;

namespace BiliDownloader.Converters;

/// <summary>
/// 统一的任务状态显示转换器（替代分散在各 ViewModel 中的 MapStatusToDisplay）
/// </summary>
public static class TaskStatusDisplay
{
    /// <summary>
    /// 将存储状态字符串转换为中文显示文本
    /// </summary>
    public static string ToDisplayText(string status) => status switch
    {
        "pending" => "排队中",
        "fetching_metadata" => "获取信息",
        "downloading_video" => "下载视频",
        "video_ready" => "视频就绪",
        "downloading_audio" => "下载音频",
        "audio_ready" => "音频就绪",
        "merging" => "合并中",
        "done" => "完成",
        "failed" => "失败",
        "interrupted" => "已中断",
        "paused" => "已暂停",
        "canceled" => "已取消",
        "waiting_for_login" => "等待登录",
        _ => status,
    };

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
