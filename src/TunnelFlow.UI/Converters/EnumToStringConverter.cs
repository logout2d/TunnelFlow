using System.Globalization;
using System.Windows.Data;

namespace TunnelFlow.UI.Converters;

[ValueConversion(typeof(Enum), typeof(string))]
public sealed class EnumToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value?.ToString() ?? "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
