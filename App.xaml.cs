using System.Windows;
using System.Windows.Threading;

namespace ExifBatchEditor;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += App_DispatcherUnhandledException;
    }

    // Last-resort safety net: show what went wrong and keep the app running (and any
    // unsaved edits intact) instead of a hard crash with no explanation.
    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"An unexpected error occurred:\n\n{e.Exception.Message}\n\nThe application will continue running, " +
            "but if this keeps happening please note what you were doing when it occurred.",
            "Unexpected Error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
