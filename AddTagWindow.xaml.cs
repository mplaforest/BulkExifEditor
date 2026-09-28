using System.Globalization;
using System.Linq;
using System.Windows;
using ExifLibrary;

namespace ExifBatchEditor;

public partial class AddTagWindow : Window
{
    public ExifProperty? Result { get; private set; }

    // Exposed alongside Result so callers can rebuild a fresh property instance per
    // target file (e.g. when applying to multiple selected files) instead of sharing
    // one ExifProperty object across unrelated ImageFile.Properties collections.
    public ExifTag ResultTag { get; private set; }
    public string ResultTypeName { get; private set; } = string.Empty;
    public string ResultValueText { get; private set; } = string.Empty;

    public AddTagWindow()
    {
        InitializeComponent();

        KnownTagCombo.ItemsSource = Enum.GetNames(typeof(ExifTag)).OrderBy(n => n).ToList();

        IfdCombo.ItemsSource = new[] { IFD.Zeroth, IFD.EXIF, IFD.GPS, IFD.Interop, IFD.First };
        IfdCombo.SelectedIndex = 1; // EXIF

        TypeCombo.ItemsSource = PropertyBuilder.SupportedTypes;
        TypeCombo.SelectedIndex = 0;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ExifTag tag;
            if (UseKnownTag.IsChecked == true)
            {
                string text = KnownTagCombo.Text?.Trim() ?? string.Empty;
                if (!Enum.TryParse(text, out tag))
                    throw new FormatException($"Unknown tag name '{text}'.");
            }
            else
            {
                if (IfdCombo.SelectedItem is not IFD ifd)
                    throw new FormatException("Choose an IFD section.");

                string idText = CustomIdBox.Text.Trim();
                int id = idText.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    ? Convert.ToInt32(idText, 16)
                    : int.Parse(idText, CultureInfo.InvariantCulture);
                tag = (ExifTag)((int)ifd + id);
            }

            string typeName = (string)TypeCombo.SelectedItem;
            Result = PropertyBuilder.Build(tag, typeName, ValueBox.Text);
            ResultTag = tag;
            ResultTypeName = typeName;
            ResultValueText = ValueBox.Text;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not add tag: {ex.Message}", "Invalid tag", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
