using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace BiliDownloader.Converters;

/// <summary>
/// 多值转换器：当 OriginalTitle != Title 时显示 "原标题 → 新标题"，否则只显示 Title
/// </summary>
public class RenameDisplayConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count >= 2 && values[0] is string original && values[1] is string current)
        {
            return original != current ? $"{original} → {current}" : current;
        }
        return values.Count >= 2 ? values[1]?.ToString() ?? "" : "";
    }
}
