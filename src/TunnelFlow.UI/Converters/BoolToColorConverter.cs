using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace TunnelFlow.UI.Converters;

[ValueConversion(typeof(bool), typeof(Brush))]
public sealed class BoolToColorConverter : IValueConverter
{
    private static readonly SolidColorBrush GreenBrush =
        new(Color.FromRgb(0x22, 0xC5, 0x5E));

    private static readonly SolidColorBrush RedBrush =
        new(Color.FromRgb(0xEF, 0x44, 0x44));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? GreenBrush : RedBrush;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
