using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ExifBatchEditor.Converters;

// Shows the grayed-out example overlay only when the cell's actual value is blank
// AND an example string exists for that row (some rows, like the GPS ref dropdowns,
// have no example - StringFormat alone can't tell "no example" apart from "blank value").
public class PlaceholderVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        string valueText = values.Length > 0 ? values[0] as string ?? string.Empty : string.Empty;
        string example = values.Length > 1 ? values[1] as string ?? string.Empty : string.Empty;
        return string.IsNullOrEmpty(valueText) && !string.IsNullOrEmpty(example)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
