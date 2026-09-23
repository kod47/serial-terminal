using System.Globalization;
using System.IO.Ports;
using System.Windows.Data;

namespace SerialTerminal;

/// <summary>Shows StopBits as "1", "1.5", "2".</summary>
public sealed class StopBitsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        StopBits.One => "1",
        StopBits.OnePointFive => "1.5",
        StopBits.Two => "2",
        _ => value?.ToString() ?? "",
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
