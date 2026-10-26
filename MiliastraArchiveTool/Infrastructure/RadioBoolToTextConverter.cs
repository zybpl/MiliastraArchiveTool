using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace MiliastraArchiveTool.Infrastructure;

[ValueConversion(typeof(string), typeof(bool))]
public sealed class RadioBoolToTextConverter : MarkupExtension, IValueConverter
{
    private static readonly RadioBoolToTextConverter Instance = new();

    public override object ProvideValue(IServiceProvider serviceProvider) => Instance;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? parameter?.ToString() ?? string.Empty : Binding.DoNothing;
}
