using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace MyAvaloniaManagement.Business.Converter;

/// <summary>
/// 将文件系统节点类型转换为可复用的轻量矢量图标。
/// </summary>
internal sealed class FileSystemIconConverter : IValueConverter
{
    private static readonly Lazy<StreamGeometry> FolderGeometry = new(() =>
        StreamGeometry.Parse("M2,4 H8 L10,6 H18 V16 H2 Z M2,7 H18"));

    private static readonly Lazy<StreamGeometry> FileGeometry = new(() =>
        StreamGeometry.Parse("M4,2 H12 L16,6 V18 H4 Z M12,2 V6 H16"));

    public static readonly FileSystemIconConverter Instance = new();

    public object Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) =>
        value is true ? FolderGeometry.Value : FileGeometry.Value;

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) =>
        throw new NotImplementedException();
}
