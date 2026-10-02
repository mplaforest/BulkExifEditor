using System.Windows;

namespace ExifBatchEditor;

public partial class BulkRenameWindow : Window
{
    public string ResultBaseName { get; private set; } = string.Empty;

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

        ResultBaseName = name;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
