using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SideDock.Converters;

public sealed class ColorBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            return new BrushConverter().ConvertFromString(value as string ?? "#334155") as Brush
                ?? Brushes.SlateGray;
        }
        catch
        {
            return Brushes.SlateGray;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
