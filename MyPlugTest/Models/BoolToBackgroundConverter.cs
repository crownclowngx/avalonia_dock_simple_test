using Avalonia.Data.Converters;
using Avalonia.Media;

namespace MyPlugTest.Models;

public class BoolToBackgroundConverter : IValueConverter
{
    public static readonly BoolToBackgroundConverter Instance = new();
        
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is bool isRead && isRead)
        {
            return new SolidColorBrush(Color.Parse("#0000FF"));
        }
        return new SolidColorBrush(Color.Parse("#000000"));
    }
        
    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}