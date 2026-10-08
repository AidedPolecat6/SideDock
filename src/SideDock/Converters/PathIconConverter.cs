using System.Globalization;
using System.Windows.Data;

using SideDock.Services;

namespace SideDock.Converters;

public sealed class PathIconConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is string path ? ShellIconService.GetIcon(path) : null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
