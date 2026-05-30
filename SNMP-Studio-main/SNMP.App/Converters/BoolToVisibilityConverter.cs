using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SNMP.App.Converters;

/// <summary>
/// Converts bool → Visibility.
/// ConverterParameter="Inverse" reverses the mapping (false→Visible, true→Collapsed).
/// Used for showing/hiding panels based on IsV3, WriteModeEnabled, ChartModel != null, etc.
/// </summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var b      = value is bool b2 && b2;
        var invert = parameter is string s && s.Equals("Inverse", StringComparison.OrdinalIgnoreCase);
        if (invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var vis    = value is Visibility v && v == Visibility.Visible;
        var invert = parameter is string s && s.Equals("Inverse", StringComparison.OrdinalIgnoreCase);
        return invert ? !vis : vis;
    }
}

/// <summary>
/// Converts any object → Visibility. Null/empty = Collapsed, non-null = Visible.
/// Used for ChartModel null-check in the Live Poll page.
/// ConverterParameter="Inverse" reverses this.
/// </summary>
[ValueConversion(typeof(object), typeof(Visibility))]
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var hasValue = value != null;
        var invert   = parameter is string s && s.Equals("Inverse", StringComparison.OrdinalIgnoreCase);
        if (invert) hasValue = !hasValue;
        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
/// <summary>
/// Converts int → Visibility. Zero (or negative) = Collapsed, positive = Visible.
/// ConverterParameter="Inverse" reverses this (zero→Visible, positive→Collapsed).
/// Used for showing count-based chips only when count > 0.
/// </summary>
[ValueConversion(typeof(int), typeof(Visibility))]
public sealed class IntToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var count  = value is int i ? i : 0;
        var show   = count > 0;
        var invert = parameter is string s && s.Equals("Inverse", StringComparison.OrdinalIgnoreCase);
        if (invert) show = !show;
        return show ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
