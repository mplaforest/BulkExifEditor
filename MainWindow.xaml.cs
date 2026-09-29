using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ExifBatchEditor.Models;
using ExifLibrary;

namespace ExifBatchEditor;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<FileEntry> Files = new();
    private readonly ObservableCollection<TagRow> CurrentTags = new();
    private FileEntry? _currentEntry;

    public MainWindow()
    {
        InitializeComponent();

        FileListBox.ItemsSource = Files;
        TagGrid.ItemsSource = CurrentTags;
    }

    // All currently selected files in the list, in list order (first selected = template
    // shown in the tag grid). Tag edits/adds/removals apply to every entry in this set.
    private List<FileEntry> SelectedEntries =>
        FileListBox.SelectedItems.Cast<FileEntry>().OrderBy(f => Files.IndexOf(f)).ToList();

    // --- File list management ---

    private void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select JPEG files",
            Multiselect = true,
            Filter = "JPEG Images|*.jpg;*.jpeg",
        };
        if (dlg.ShowDialog(this) == true)
            LoadFiles(dlg.FileNames);
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select folder containing JPEG files",
        };

        bool? shown;
        try
        {
            shown = dlg.ShowDialog(this);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open the folder browser: {ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (shown != true) return;

        List<string> files;
        try
        {
            files = Directory.EnumerateFiles(dlg.FolderName, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not read folder:\n{dlg.FolderName}\n\n{ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (files.Count == 0)
        {
            MessageBox.Show(this, $"No .jpg/.jpeg files found directly in:\n{dlg.FolderName}\n\n" +
                "(Subfolders are not searched.)", "No files found", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        LoadFiles(files);
    }

    private void FileListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FileListBox.SelectedItem is not FileEntry entry) return;

        try
        {
            // UseShellExecute = true hands this to Windows, which opens it with
            // whatever program is set as the default for .jpg/.jpeg - same as
            // double-clicking the file in File Explorer.
            Process.Start(new ProcessStartInfo(entry.FilePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open {entry.FileName}: {ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadFiles(IEnumerable<string> paths)
    {
        var errors = new List<string>();
        foreach (var path in paths)
        {
            try
            {
                Files.Add(new FileEntry(path));
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }

        if (errors.Count > 0)
            MessageBox.Show(this, "Some files could not be loaded:\n" + string.Join("\n", errors),
                "Load errors", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void RemoveFiles_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in FileListBox.SelectedItems.Cast<FileEntry>().ToList())
            Files.Remove(item);

        _currentEntry = null;
        CurrentTags.Clear();
    }

    // --- Save ---

    private void SaveChanges_Click(object sender, RoutedEventArgs e)
    {
        var dirty = Files.Where(f => f.IsDirty).ToList();

        // Editing with multiple files selected already auto-saves as you go, so by the
        // time this button is clicked there's often genuinely nothing left dirty - say so
        // explicitly rather than leaving a "Saved 0 file(s)" status line that looks the
        // same whether nothing needed saving or something went silently wrong.
        if (dirty.Count == 0)
        {
            MessageBox.Show(this, "No unsaved changes - everything is already saved.",
                "Save Changes", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SaveEntries(dirty);
    }

    private void SaveEntries(IEnumerable<FileEntry> entries)
    {
        int saved = 0;
        var errors = new List<string>();
        var list = entries.ToList();
        foreach (var entry in list)
        {
            try
            {
                entry.Image.Save(entry.FilePath);
                entry.IsDirty = false;
                saved++;
            }
            catch (Exception ex)
            {
                errors.Add($"{entry.FileName}: {ex.Message}");
            }
        }

        StatusText.Text = $"Saved {saved} file(s).";

        if (saved > 0)
            MessageBox.Show(this, "All files have been updated!",
                "Save Changes", MessageBoxButton.OK, MessageBoxImage.Information);

        if (errors.Count > 0)
            MessageBox.Show(this, "Some files failed to save:\n" + string.Join("\n", errors),
                "Save errors", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    // --- Per-file raw tag grid ---

    private void FileListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // The grid displays the first selected file (in list order) as an editable
        // template. If more than one file is selected, edits made in the grid are applied
        // (and saved) to every selected file, not just this one.
        _currentEntry = SelectedEntries.FirstOrDefault();
        RefreshCurrentTags();
    }

    // The six core GPS position tags, always shown in this fixed order (each value tag
    // immediately followed by its Ref tag) regardless of the order the file stores them
    // in, using the real saved value if present or a blank editable placeholder if not.
    // Also doubles as the name-override table below.
    private static readonly (ExifTag Tag, IFD Ifd, string Name, string TypeName, string Example)[] GpsPositionTags =
    {
        (ExifTag.GPSLatitude, IFD.GPS, "GPSLatitude", "ExifURationalArray", "40 26 46.3"),
        (ExifTag.GPSLatitudeRef, IFD.GPS, "GPSLatitudeRef", "ExifAscii", ""),
        (ExifTag.GPSLongitude, IFD.GPS, "GPSLongitude", "ExifURationalArray", "79 58 56.0"),
        (ExifTag.GPSLongitudeRef, IFD.GPS, "GPSLongitudeRef", "ExifAscii", ""),
        (ExifTag.GPSAltitude, IFD.GPS, "GPSAltitude", "ExifURational", "120.5"),
        (ExifTag.GPSAltitudeRef, IFD.GPS, "GPSAltitudeRef", "ExifByte", ""),
    };

    private static readonly HashSet<ExifTag> GpsPositionTagSet = GpsPositionTags.Select(p => p.Tag).ToHashSet();

    private void RefreshCurrentTags()
    {
        CurrentTags.Clear();
        if (_currentEntry == null) return;

        // Everything except the six GPS position tags, in the file's natural order.
        // "Unknown"-named tags are unrecognized/manufacturer-specific entries (often
        // MakerNote sub-tags) with no useful friendly meaning - skip them to declutter.
        foreach (var prop in _currentEntry.Image.Properties)
        {
            if (GpsPositionTagSet.Contains(prop.Tag)) continue;
            if (prop.Name == "Unknown") continue;
            CurrentTags.Add(ToRow(prop, prop.Name));
        }

        // The GPS position tags, always in the same fixed order. The library's own
        // friendly-name lookup reports "Unknown" for some of these once they're real
        // saved values (observed with GPSLatitudeRef/GPSLongitudeRef) - use our own
        // name instead of prop.Name so that doesn't hide them.
        foreach (var gps in GpsPositionTags)
        {
            if (_currentEntry.Image.Properties.Contains(gps.Tag))
            {
                var prop = _currentEntry.Image.Properties.Get(gps.Tag);
                CurrentTags.Add(ToRow(prop, gps.Name));
            }
            else
            {
                CurrentTags.Add(new TagRow
                {
                    Tag = gps.Tag,
                    Ifd = gps.Ifd,
                    Name = gps.Name,
                    TypeName = gps.TypeName,
                    ValueText = string.Empty,
                    EditorKind = TagRow.GetEditorKind(gps.Tag),
                    Example = gps.Example,
                });
            }
        }
    }

    private static TagRow ToRow(ExifProperty prop, string name)
    {
        string typeName = prop.GetType().Name;
        return new TagRow
        {
            Tag = prop.Tag,
            Ifd = prop.IFD,
            Name = name,
            TypeName = typeName,
            EditorKind = TagRow.GetEditorKind(prop.Tag),
            ValueText = FormatValueText(prop, typeName),
        };
    }

    // GPS coordinates are stored as a 3-value rational array; the library's own ToString()
    // for that raw type looks like "[44/1 66/1 641/7]", which isn't how people read
    // coordinates. Re-render it as "44° 6' 40.1\"" - still round-trips through
    // PropertyBuilder.ParseThreeNumbers if edited.
    private static string FormatValueText(ExifProperty prop, string typeName)
    {
        string raw = prop.ToString() ?? string.Empty;

        if (typeName == "ExifURationalArray" || typeName == "GPSLatitudeLongitude")
        {
            try
            {
                var dms = PropertyBuilder.ParseThreeNumbers(raw);
                return $"{dms[0]:0.###}° {dms[1]:0.###}' {dms[2]:0.###}\"";
            }
            catch
            {
                return raw;
            }
        }

        // Single rational-valued tags (e.g. GPSAltitude, FNumber, ExposureTime, FocalLength)
        // print as a raw "numerator/denominator" fraction by default (e.g. "110051/50"),
        // which reads as a huge, meaningless number. Show the computed decimal instead -
        // still round-trips through PropertyBuilder.ParseFraction if edited.
        if (typeName == "ExifURational" || typeName == "ExifSRational")
        {
            try
            {
                var (n, d) = PropertyBuilder.ParseFraction(raw);
                if (d == 0) return raw;
                return ((double)n / d).ToString("0.####", CultureInfo.InvariantCulture);
            }
            catch
            {
                return raw;
            }
        }

        return raw;
    }

    private void TagGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return;
        if (e.Row.Item is not TagRow row) return;

        // The Value column is a template (plain TextBox for most tags, a ComboBox for the
        // GPS ref tags) bound TwoWay with UpdateSourceTrigger=PropertyChanged, so row.ValueText
        // already reflects whatever was typed/selected by the time this event fires.
        string newText = row.ValueText;

        // A blank placeholder row (e.g. an unfilled GPS field) clicked into and left empty
        // shouldn't nag with a parse error - just leave it blank.
        if (string.IsNullOrWhiteSpace(newText)) return;

        var targets = SelectedEntries;
        if (targets.Count == 0) return;
        bool multi = targets.Count > 1;

        var errors = new List<string>();
        int applied = 0;

        foreach (var entry in targets)
        {
            try
            {
                // Build a fresh property instance per file rather than sharing one object
                // across unrelated ImageFile.Properties collections.
                var newProp = PropertyBuilder.Build(row.Tag, row.TypeName, newText);
                entry.Image.Properties.Set(newProp);
                entry.IsDirty = true;

                if (multi)
                {
                    entry.Image.Save(entry.FilePath);
                    entry.IsDirty = false;
                }

                applied++;
            }
            catch (Exception ex)
            {
                errors.Add($"{entry.FileName}: {ex.Message}");
            }
        }

        if (applied == 0)
        {
            MessageBox.Show(this, $"Could not parse value: {errors.FirstOrDefault()}", "Invalid value",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Cancel = true;
            return;
        }

        row.ValueText = newText;

        if (multi)
        {
            StatusText.Text = $"Applied \"{row.Name}\" to {applied} of {targets.Count} file(s).";
            if (errors.Count > 0)
                MessageBox.Show(this, "Some files had problems:\n" + string.Join("\n", errors),
                    "Apply errors", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void AddTag_Click(object sender, RoutedEventArgs e)
    {
        var targets = SelectedEntries;
        if (targets.Count == 0)
        {
            MessageBox.Show(this, "Select a file first.", "No file selected");
            return;
        }

        var dlg = new AddTagWindow { Owner = this };
        if (dlg.ShowDialog() != true || dlg.Result == null) return;

        bool multi = targets.Count > 1;
        var errors = new List<string>();
        int applied = 0;

        foreach (var entry in targets)
        {
            try
            {
                // Set() replaces any existing entry for this tag and adds it if not
                // present - safe for both "add new" and "overwrite existing" in one call.
                // Built fresh per file rather than reusing dlg.Result across files.
                var prop = PropertyBuilder.Build(dlg.ResultTag, dlg.ResultTypeName, dlg.ResultValueText);
                entry.Image.Properties.Set(prop);
                entry.IsDirty = true;

                if (multi)
                {
                    entry.Image.Save(entry.FilePath);
                    entry.IsDirty = false;
                }

                applied++;
            }
            catch (Exception ex)
            {
                errors.Add($"{entry.FileName}: {ex.Message}");
            }
        }

        RefreshCurrentTags();

        if (multi)
        {
            StatusText.Text = $"Added tag to {applied} of {targets.Count} file(s).";
            if (errors.Count > 0)
                MessageBox.Show(this, "Some files had problems:\n" + string.Join("\n", errors),
                    "Add tag errors", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RemoveTag_Click(object sender, RoutedEventArgs e)
    {
        var targets = SelectedEntries;
        if (targets.Count == 0) return;

        var selectedRows = TagGrid.SelectedItems.Cast<TagRow>().ToList();
        if (selectedRows.Count == 0) return;

        bool multi = targets.Count > 1;
        var errors = new List<string>();
        int applied = 0;

        foreach (var entry in targets)
        {
            try
            {
                foreach (var row in selectedRows)
                    entry.Image.Properties.Remove(row.Tag);

                entry.IsDirty = true;

                if (multi)
                {
                    entry.Image.Save(entry.FilePath);
                    entry.IsDirty = false;
                }

                applied++;
            }
            catch (Exception ex)
            {
                errors.Add($"{entry.FileName}: {ex.Message}");
            }
        }

        RefreshCurrentTags(); // restores a blank placeholder row for GPS tags, if applicable

        if (multi)
        {
            StatusText.Text = $"Removed tag(s) from {applied} of {targets.Count} file(s).";
            if (errors.Count > 0)
                MessageBox.Show(this, "Some files had problems:\n" + string.Join("\n", errors),
                    "Remove tag errors", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
