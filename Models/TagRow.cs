using System.ComponentModel;
using ExifLibrary;

namespace ExifBatchEditor.Models;

public enum TagEditorKind
{
    Text,
    AltitudeRef,
}

public class TagRow : INotifyPropertyChanged
{
    public required ExifTag Tag { get; init; }
    public required IFD Ifd { get; init; }
    public required string Name { get; init; }
    public required string TypeName { get; init; }

    private string _valueText = string.Empty;
    // INotifyPropertyChanged so reverting this back to its last valid value after a
    // failed commit actually updates the on-screen control, not just the model.
    public required string ValueText
    {
        get => _valueText;
        set
        {
            if (_valueText == value) return;
            _valueText = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ValueText)));
        }
    }

    public TagEditorKind EditorKind { get; init; } = TagEditorKind.Text;

    // What ValueText was when this row was built from the actual file (or "" for a blank
    // placeholder). Lets a commit be skipped when nothing actually changed, e.g. a cell
    // merely gaining and losing focus without being edited.
    public string OriginalValueText { get; set; } = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    // Grayed-out example text shown in the grid when ValueText is blank (e.g. an unfilled
    // GPS field). Empty string means no example is shown.
    public string Example { get; init; } = string.Empty;

    public static TagEditorKind GetEditorKind(ExifTag tag) => tag switch
    {
        ExifTag.GPSAltitudeRef => TagEditorKind.AltitudeRef,
        _ => TagEditorKind.Text,
    };
}
