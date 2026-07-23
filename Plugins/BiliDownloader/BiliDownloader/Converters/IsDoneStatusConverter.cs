using System;
using System.Globalization;
using Avalonia.Data.Converters;
using BiliDownloader.Models;

namespace BiliDownloader.Converters;

/// <summary>
/// 将 Status 字符串转换为 bool：仅当值为 Completed(done) 时返回 true
/// </summary>
public class IsDoneStatusConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is string s && s == "done";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
