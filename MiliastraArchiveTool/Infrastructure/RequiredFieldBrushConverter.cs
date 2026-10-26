using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace MiliastraArchiveTool.Infrastructure;

public sealed class RequiredFieldBrushConverter : IValueConverter
{
    private static readonly Brush ValidBrush = new SolidColorBrush(Color.FromRgb(22, 163, 74));
    private static readonly Brush InvalidBrush = new SolidColorBrush(Color.FromRgb(220, 38, 38));

    static RequiredFieldBrushConverter()
    {
        ValidBrush.Freeze();
        InvalidBrush.Freeze();
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return string.IsNullOrWhiteSpace(value?.ToString()) ? InvalidBrush : ValidBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
