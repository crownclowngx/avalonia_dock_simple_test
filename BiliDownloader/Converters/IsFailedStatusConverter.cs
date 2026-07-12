using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace BiliDownloader.Converters;

/// <summary>
/// 将 Status 字符串转换为 bool：仅当值为 "failed" 时返回 true
/// </summary>
public class IsFailedStatusConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is string s && s == "failed";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
