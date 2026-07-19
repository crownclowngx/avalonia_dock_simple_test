using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace BiliDownloader.Converters;

/// <summary>
/// 将视频状态文本转换为左侧色条颜色
/// 排队中=灰, 下载中/合并中=蓝, 完成=绿, 失败=红
/// </summary>
public class StatusToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string status) return Brushes.Transparent;

        return status switch
        {
            "完成" => new SolidColorBrush(Color.Parse("#4CAF50")),
            "失败" or "已中断" => new SolidColorBrush(Color.Parse("#F44336")),
            "下载视频" or "下载音频" or "合并中" or "获取信息" => new SolidColorBrush(Color.Parse("#00A1D6")),
            "排队中" or "等待中" => new SolidColorBrush(Color.Parse("#9E9E9E")),
            _ => Brushes.Transparent,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
