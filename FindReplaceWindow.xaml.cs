using System.Windows;

namespace ExifBatchEditor;

public partial class FindReplaceWindow : Window
{
    public string ResultFind { get; private set; } = string.Empty;
    public string ResultReplace { get; private set; } = string.Empty;
    public bool ResultMatchCase { get; private set; }

    public FindReplaceWindow()
    {
        InitializeComponent();
        FindBox.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(FindBox.Text))
        {
            MessageBox.Show(this, "Enter text to find.", "Find and Replace", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ResultFind = FindBox.Text;
        ResultReplace = ReplaceBox.Text;
        ResultMatchCase = MatchCaseBox.IsChecked == true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
