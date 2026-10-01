using System.Globalization;
using System.Windows.Data;

namespace ExifBatchEditor.Converters;

// Shows the grayed-out example overlay only when the cell's actual value is blank
// AND an example string exists for that row (some rows, like the GPS ref dropdowns,
// have no example - StringFormat alone can't tell "no example" apart from "blank value").
//
// Drives Opacity rather than Visibility: a Visibility change (Visible<->Collapsed) takes
// the element in/out of layout entirely, forcing a measure/arrange pass on the whole grid
// cell - which, on the very first keystroke into a previously-blank field (exactly when
// this toggles for the first time), was found to disrupt the sibling TextBox's focus and
// cause a premature commit of just the first character typed. Opacity only affects
// rendering, not layout, so toggling it is inert as far as the TextBox is concerned.
public class PlaceholderVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        string valueText = values.Length > 0 ? values[0] as string ?? string.Empty : string.Empty;
        string example = values.Length > 1 ? values[1] as string ?? string.Empty : string.Empty;
        return string.IsNullOrEmpty(valueText) && !string.IsNullOrEmpty(example) ? 1.0 : 0.0;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
