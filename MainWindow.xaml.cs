using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
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

    // Shows a status message with a brief colored flash that fades out, so an
    // auto-save notification actually catches the eye instead of blending into
    // plain text at the bottom of the window.
    private void ShowStatus(string message)
    {
        StatusText.Text = message;

        var flashBrush = new SolidColorBrush(Color.FromArgb(255, 180, 235, 150));
        StatusText.Background = flashBrush;

        var fade = new ColorAnimation
        {
            From = Color.FromArgb(255, 180, 235, 150),
            To = Colors.Transparent,
            Duration = TimeSpan.FromSeconds(1.5),
            BeginTime = TimeSpan.FromSeconds(0.3)
        };
        flashBrush.BeginAnimation(SolidColorBrush.ColorProperty, fade);
    }

    // All currently selected files in the list, in list order (first selected = template
    // shown in the tag grid). Tag edits/adds/removals apply to every entry in this set.
    private List<FileEntry> SelectedEntries =>
        FileListBox.SelectedItems.Cast<FileEntry>().OrderBy(f => Files.IndexOf(f)).ToList();

    // --- File list management ---

    private async void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select JPEG files",
            Multiselect = true,
            Filter = "JPEG Images|*.jpg;*.jpeg",
        };
        if (dlg.ShowDialog(this) == true)
            await LoadFilesAsync(dlg.FileNames);
    }

    private async void AddFolder_Click(object sender, RoutedEventArgs e)
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

        await LoadFilesAsync(files);
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

    private void FileListBox_DragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void FileListBox_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var dropped = (string[])e.Data.GetData(DataFormats.FileDrop);

        bool IsJpeg(string p) => p.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                  p.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase);

        var files = new List<string>();
        foreach (var path in dropped)
        {
            if (Directory.Exists(path))
                files.AddRange(Directory.EnumerateFiles(path, "*.*", SearchOption.TopDirectoryOnly).Where(IsJpeg));
            else if (IsJpeg(path))
                files.Add(path);
        }

        if (files.Count == 0)
        {
            MessageBox.Show(this, "No .jpg/.jpeg files found in what was dropped.\n\n(Subfolders are not searched.)",
                "Nothing to load", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await LoadFilesAsync(files);
    }

    private async Task LoadFilesAsync(IEnumerable<string> paths)
    {
        var pathList = paths.ToList();
        if (pathList.Count == 0) return;

        AddFilesButton.IsEnabled = false;
        AddFolderButton.IsEnabled = false;
        LoadingPanel.Visibility = Visibility.Visible;
        LoadingProgressBar.Maximum = pathList.Count;
        LoadingProgressBar.Value = 0;

        var errors = new List<string>();
        int completed = 0;

        try
        {
            foreach (var path in pathList)
            {
                LoadingStatusText.Text = $"Loading {completed + 1} of {pathList.Count}: {Path.GetFileName(path)}";

                try
                {
                    // Parsing EXIF and decoding/resizing the thumbnail are the slow parts -
                    // run each off the UI thread so the window stays responsive and the
                    // progress bar can actually repaint between files.
                    var entry = await Task.Run(() => new FileEntry(path));
                    Files.Add(entry);
                }
                catch (Exception ex)
                {
                    errors.Add($"{Path.GetFileName(path)}: {ex.Message}");
                }

                completed++;
                LoadingProgressBar.Value = completed;
            }
        }
        finally
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            AddFilesButton.IsEnabled = true;
            AddFolderButton.IsEnabled = true;
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
        var saved = new List<FileEntry>();
        var errors = new List<string>();
        var list = entries.ToList();
        foreach (var entry in list)
        {
            try
            {
                SaveWithRetry(entry);
                entry.IsDirty = false;
                saved.Add(entry);
            }
            catch (Exception ex)
            {
                errors.Add($"{entry.FileName}: {ex.Message}");
            }
        }

        ShowStatus($"Saved {saved.Count} file(s).");

        if (saved.Count == 1)
            MessageBox.Show(this, $"{saved[0].FileName} has been updated!",
                "Save Changes", MessageBoxButton.OK, MessageBoxImage.Information);
        else if (saved.Count > 1)
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

    // Attribution tags shown first, always present as a real value or a blank editable
    // placeholder - so bulk-filling Artist/Copyright across several selected files is as
    // easy as typing into the row (same mechanism as the GPS fields below).
    private static readonly (ExifTag Tag, IFD Ifd, string Name, string TypeName, string Example)[] AttributionTags =
    {
        (ExifTag.Artist, IFD.Zeroth, "Artist", "ExifAscii", "Jane Doe"),
        (ExifTag.Copyright, IFD.Zeroth, "Copyright", "ExifAscii", "© 2026 Jane Doe"),
    };

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

    private static readonly HashSet<ExifTag> PinnedTagSet =
        AttributionTags.Select(p => p.Tag).Concat(GpsPositionTags.Select(p => p.Tag)).ToHashSet();

    private void RefreshCurrentTags()
    {
        CurrentTags.Clear();
        if (_currentEntry == null) return;

        // Attribution tags first, always shown (real value or blank placeholder).
        foreach (var attr in AttributionTags)
            CurrentTags.Add(GetOrPlaceholderRow(attr));

        // Everything except the pinned attribution/GPS tags above, in the file's natural
        // order. "Unknown"-named tags are unrecognized/manufacturer-specific entries
        // (often MakerNote sub-tags) with no useful friendly meaning - skip them to declutter.
        foreach (var prop in _currentEntry.Image.Properties)
        {
            if (PinnedTagSet.Contains(prop.Tag)) continue;
            if (prop.Name == "Unknown") continue;
            CurrentTags.Add(ToRow(prop, prop.Name));
        }

        // The GPS position tags, always in the same fixed order, at the end.
        foreach (var gps in GpsPositionTags)
            CurrentTags.Add(GetOrPlaceholderRow(gps));
    }

    // The library's own friendly-name lookup reports "Unknown" for some pinned tags once
    // they're real saved values (observed with GPSLatitudeRef/GPSLongitudeRef) - use our
    // own name instead of prop.Name so that doesn't hide them.
    private TagRow GetOrPlaceholderRow((ExifTag Tag, IFD Ifd, string Name, string TypeName, string Example) def)
    {
        if (_currentEntry!.Image.Properties.Contains(def.Tag))
        {
            var prop = _currentEntry.Image.Properties.Get(def.Tag);
            return ToRow(prop, def.Name);
        }

        return new TagRow
        {
            Tag = def.Tag,
            Ifd = def.Ifd,
            Name = def.Name,
            TypeName = def.TypeName,
            ValueText = string.Empty,
            OriginalValueText = string.Empty,
            EditorKind = TagRow.GetEditorKind(def.Tag),
            Example = def.Example,
        };
    }

    private static TagRow ToRow(ExifProperty prop, string name)
    {
        string typeName = prop.GetType().Name;
        var editorKind = TagRow.GetEditorKind(prop.Tag);
        string valueText;

        if (editorKind != TagEditorKind.Text)
        {
            // Ref/dropdown fields (GPSLatitudeRef/LongitudeRef/AltitudeRef): once saved,
            // the library re-parses these as an ExifEnumProperty<T> wrapper with a
            // friendly name like "North"/"AboveSeaLevel" - a type PropertyBuilder can't
            // reconstruct, which broke editing a field a second time. Always write these
            // back using our own known-good type instead of trusting prop.GetType(), and
            // normalize the display text to the raw code the dropdown's items use.
            typeName = GpsPositionTags.First(g => g.Tag == prop.Tag).TypeName;
            valueText = NormalizeRefValue(prop.Tag, prop.ToString() ?? string.Empty);
        }
        else
        {
            valueText = FormatValueText(prop, typeName);
        }

        return new TagRow
        {
            Tag = prop.Tag,
            Ifd = prop.IFD,
            Name = name,
            TypeName = typeName,
            EditorKind = editorKind,
            ValueText = valueText,
            OriginalValueText = valueText,
        };
    }

    private static string NormalizeRefValue(ExifTag tag, string raw) => (tag, raw) switch
    {
        (ExifTag.GPSLatitudeRef, "North") => "N",
        (ExifTag.GPSLatitudeRef, "South") => "S",
        (ExifTag.GPSLongitudeRef, "East") => "E",
        (ExifTag.GPSLongitudeRef, "West") => "W",
        (ExifTag.GPSAltitudeRef, "AboveSeaLevel") => "0",
        (ExifTag.GPSAltitudeRef, "BelowSeaLevel") => "1",
        _ => raw, // already a raw code (e.g. freshly-set "N") or unrecognized - pass through
    };

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

    // The Value column's TextBox/ComboBox editors are always live (see MainWindow.xaml) -
    // there's no separate DataGrid "edit mode" to juggle. A text field commits when it
    // loses focus; a dropdown commits as soon as a selection is made.
    // Temporary diagnostics: timestamp when a field gains focus, per-row, so LostFocus
    // can report how long it actually had focus. If it fires within ~100ms of GotFocus,
    // that's proof it was triggered by something other than the user clicking/tabbing
    // away (no one reacts that fast) - most likely WPF recycling the cell's container
    // mid-edit. Shown directly in the error dialog if the commit fails.
    private readonly Dictionary<TagRow, DateTime> _editorFocusedAt = new();

    private void ValueEditor_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TagRow row })
            _editorFocusedAt[row] = DateTime.Now;
    }

    private void ValueEditor_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TagRow row }) return;

        string newFocusTarget = Keyboard.FocusedElement?.GetType().Name ?? "(none)";
        string elapsed = _editorFocusedAt.TryGetValue(row, out var focusedAt)
            ? $"{(DateTime.Now - focusedAt).TotalMilliseconds:F0}ms since focused"
            : "no GotFocus recorded";
        CommitRowEdit(row, $"LostFocus -> {newFocusTarget} ({elapsed})");
    }

    private void RefComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0) return;
        if (sender is not FrameworkElement { DataContext: TagRow row } editor) return;

        // All three ref dropdowns show the same row's ValueText (see MainWindow.xaml), so
        // a character typed into the text box that happens to match one of a *different*,
        // currently-invisible dropdown's item Tag (e.g. a stray "1" matching AltRefEditor's
        // "Below Sea Level") silently reselects it there too and fires this same event -
        // even though it's hidden and the user never touched it. Only treat this as a real
        // selection if it came from the dropdown actually shown for this row's EditorKind.
        if (editor.Tag is not string editorTag || editorTag != row.EditorKind.ToString()) return;

        // SelectedValue binds OneWay now (display only) - write the pick back to the row
        // explicitly here instead of relying on a TwoWay binding's reverse direction. With
        // all three dropdowns TwoWay-bound to the same ValueText, a dropdown whose items
        // didn't match the *other* field's value (e.g. AltRefEditor's "0"/"1" against a
        // typed "120.5") would fail to resolve and push that failure back, silently
        // clearing ValueText - which was quietly discarding GPSAltitude's typed value
        // with no error, since the commit saw a blank value and just skipped it.
        if (sender is ComboBox { SelectedValue: string selected })
            row.ValueText = selected;

        CommitRowEdit(row, "ComboBox SelectionChanged");
    }

    // Saving can transiently fail with a sharing violation if something else briefly
    // has the file open right after a write (antivirus real-time scanning, cloud sync,
    // Windows Search indexing - all common on files outside the local disk). Retry a
    // few times with short pauses before giving up, rather than failing on the first hit.
    private static void SaveWithRetry(FileEntry entry)
    {
        const int maxAttempts = 4;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                entry.Image.Save(entry.FilePath);
                return;
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                System.Threading.Thread.Sleep(150 * attempt);
            }
        }
    }

    private void CommitRowEdit(TagRow row, string triggeredBy = "")
    {
        string newText = row.ValueText;

        // Nothing actually changed (e.g. the box merely gained and lost focus) - skip
        // the parse/save work entirely rather than re-saving an unchanged value.
        if (newText == row.OriginalValueText) return;

        // A blank placeholder row (e.g. an unfilled GPS field) clicked into and left empty
        // shouldn't nag with a parse error - just leave it blank.
        if (string.IsNullOrWhiteSpace(newText)) return;

        var targets = SelectedEntries;
        if (targets.Count == 0) return;
        bool multi = targets.Count > 1;

        // Tracked separately so the error dialog says the right thing - a save failure
        // (e.g. a transient file lock) was previously mislabeled as "Could not parse
        // value" just because both stages shared one error list.
        var parseErrors = new List<string>();
        var saveErrors = new List<string>();
        int applied = 0;

        foreach (var entry in targets)
        {
            ExifProperty newProp;
            try
            {
                // Build a fresh property instance per file rather than sharing one object
                // across unrelated ImageFile.Properties collections.
                newProp = PropertyBuilder.Build(row.Tag, row.TypeName, newText);
            }
            catch (Exception ex)
            {
                parseErrors.Add($"{entry.FileName}: {ex.Message}");
                continue;
            }

            try
            {
                entry.Image.Properties.Set(newProp);
                entry.IsDirty = true;

                // Always save immediately on commit, whether editing one file or many -
                // previously only multi-file edits auto-saved, which meant a single-file
                // edit just sat as an unsaved "*" until a separate Save Changes click.
                // That inconsistency repeatedly looked like "this field isn't saving".
                SaveWithRetry(entry);
                entry.IsDirty = false;

                applied++;
            }
            catch (Exception ex)
            {
                saveErrors.Add($"{entry.FileName}: {ex.Message}");
            }
        }

        if (applied == 0)
        {
            // Leave the typed text in place rather than rebuilding the grid (which tears
            // down and regenerates every row's controls) - the user can just fix it and
            // the field will lose focus again to retry.
            string diag = string.IsNullOrEmpty(triggeredBy) ? "" : $"\n\n[debug: triggered by {triggeredBy}, typed text was '{newText}']";
            string message = parseErrors.Count > 0
                ? $"Could not parse value: {parseErrors.FirstOrDefault()}"
                : $"Could not save: {saveErrors.FirstOrDefault()}";
            MessageBox.Show(this, $"{message}{diag}", parseErrors.Count > 0 ? "Invalid value" : "Save failed",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var errors = parseErrors.Concat(saveErrors).ToList();

        // Re-format the display value (e.g. normalizing GPS "D M S" input) for single-file
        // edits; multi-file edits get a fresh row from the next RefreshCurrentTags instead.
        if (!multi)
        {
            row.ValueText = newText;
            row.OriginalValueText = newText;
        }

        ShowStatus(multi
            ? $"{row.Name} saved to {applied} of {targets.Count} file(s)."
            : $"{row.Name} saved.");

        if (errors.Count > 0)
            MessageBox.Show(this, "Some files had problems:\n" + string.Join("\n", errors),
                "Apply errors", MessageBoxButton.OK, MessageBoxImage.Warning);
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

                // Always save immediately, same as CommitRowEdit - see its comment for why.
                SaveWithRetry(entry);
                entry.IsDirty = false;

                applied++;
            }
            catch (Exception ex)
            {
                errors.Add($"{entry.FileName}: {ex.Message}");
            }
        }

        RefreshCurrentTags();

        ShowStatus(multi
            ? $"Added tag to {applied} of {targets.Count} file(s) and saved."
            : applied > 0 ? $"Added tag to {targets[0].FileName} and saved." : "");
        if (errors.Count > 0)
            MessageBox.Show(this, "Some files had problems:\n" + string.Join("\n", errors),
                "Add tag errors", MessageBoxButton.OK, MessageBoxImage.Warning);
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

                // Always save immediately, same as CommitRowEdit - see its comment for why.
                SaveWithRetry(entry);
                entry.IsDirty = false;

                applied++;
            }
            catch (Exception ex)
            {
                errors.Add($"{entry.FileName}: {ex.Message}");
            }
        }

        RefreshCurrentTags(); // restores a blank placeholder row for GPS tags, if applicable

        ShowStatus(multi
            ? $"Removed tag(s) from {applied} of {targets.Count} file(s) and saved."
            : applied > 0 ? $"Removed tag(s) from {targets[0].FileName} and saved." : "");
        if (errors.Count > 0)
            MessageBox.Show(this, "Some files had problems:\n" + string.Join("\n", errors),
                "Remove tag errors", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
