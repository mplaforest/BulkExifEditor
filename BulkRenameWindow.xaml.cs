using System.Globalization;
using System.Windows;

namespace ExifBatchEditor;

public partial class BulkRenameWindow : Window
{
    public string ResultBaseName { get; private set; } = string.Empty;
    public int ResultStartNumber { get; private set; } = 1;

    public BulkRenameWindow()
    {
        InitializeComponent();
        BaseNameBox.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        string name = BaseNameBox.Text.Trim();
        if (name.Length == 0)
        {
            MessageBox.Show(this, "Enter a name.", "Bulk Rename", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(StartNumberBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int start) || start < 1)
        {
            MessageBox.Show(this, "Start number must be a whole number of 1 or more.", "Bulk Rename", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ResultBaseName = name;
        ResultStartNumber = start;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
