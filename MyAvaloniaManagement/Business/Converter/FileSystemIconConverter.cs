using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace MyAvaloniaManagement.Business.Converter;

/// <summary>
/// 用于在文件系统树中显示图标
/// </summary>
public class FileSystemIconConverter : IValueConverter
{
    public static readonly FileSystemIconConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isDirectory)
        {
            // 使用Unicode表情符号作为图标
            return isDirectory ? "📁" : "📄";
        }
        return "📄";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}