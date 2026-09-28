using ExifLibrary;

namespace ExifBatchEditor.Models;

public enum TagEditorKind
{
    Text,
    LatitudeRef,
    LongitudeRef,
    AltitudeRef,
}

public class TagRow
{
    public required ExifTag Tag { get; init; }
    public required IFD Ifd { get; init; }
    public required string Name { get; init; }
    public required string TypeName { get; init; }
    public required string ValueText { get; set; }
    public TagEditorKind EditorKind { get; init; } = TagEditorKind.Text;

    // Grayed-out example text shown in the grid when ValueText is blank (e.g. an unfilled
    // GPS field). Empty string means no example is shown.
    public string Example { get; init; } = string.Empty;

    public static TagEditorKind GetEditorKind(ExifTag tag) => tag switch
    {
        ExifTag.GPSLatitudeRef => TagEditorKind.LatitudeRef,
        ExifTag.GPSLongitudeRef => TagEditorKind.LongitudeRef,
        ExifTag.GPSAltitudeRef => TagEditorKind.AltitudeRef,
        _ => TagEditorKind.Text,
    };
}
