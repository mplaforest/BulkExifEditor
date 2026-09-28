using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ExifLibrary;

namespace ExifBatchEditor.Models;

public class FileEntry : INotifyPropertyChanged
{
    private const int ThumbnailPixelWidth = 48;

    private bool _isDirty;

    public string FilePath { get; }
    public string FileName => Path.GetFileName(FilePath);
    public ImageFile Image { get; private set; }
    public BitmapSource? Thumbnail { get; }

    public bool IsDirty
    {
        get => _isDirty;
        set
        {
            if (_isDirty == value) return;
            _isDirty = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayName));
        }
    }

    public string DisplayName => IsDirty ? FileName + " *" : FileName;

    public FileEntry(string path)
    {
        FilePath = path;
        Image = ImageFile.FromFile(path);
        Thumbnail = TryLoadThumbnail(path, GetOrientation(Image));
    }

    private static int GetOrientation(ImageFile image)
    {
        try
        {
            if (!image.Properties.Contains(ExifTag.Orientation)) return 1;
            object? value = image.Properties.Get(ExifTag.Orientation).Value;
            return value == null ? 1 : Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return 1;
        }
    }

    private static BitmapSource? TryLoadThumbnail(string path, int orientation)
    {
        try
        {
            var bitmap = new BitmapImage();
            // Read into memory (via a FileStream + OnLoad caching) rather than pointing
            // UriSource at the file directly - otherwise WPF keeps a handle open and
            // saving changes back to the same path later fails with a sharing violation.
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = ThumbnailPixelWidth;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
            }

            BitmapSource result = ApplyOrientation(bitmap, orientation);
            result.Freeze();
            return result;
        }
        catch
        {
            return null;
        }
    }

    // Standard EXIF orientation values 1-8: apply the matching flip/rotate so the
    // thumbnail displays right-side up regardless of how the camera held the sensor.
    private static BitmapSource ApplyOrientation(BitmapSource source, int orientation)
    {
        Transform? transform = orientation switch
        {
            2 => new ScaleTransform(-1, 1),
            3 => new RotateTransform(180),
            4 => new ScaleTransform(1, -1),
            5 => Group(new ScaleTransform(-1, 1), new RotateTransform(270)),
            6 => new RotateTransform(90),
            7 => Group(new ScaleTransform(-1, 1), new RotateTransform(90)),
            8 => new RotateTransform(270),
            _ => null,
        };

        if (transform == null) return source;

        var transformed = new TransformedBitmap(source, transform);
        transformed.Freeze();
        return transformed;
    }

    private static Transform Group(params Transform[] transforms)
    {
        var group = new TransformGroup();
        foreach (var t in transforms) group.Children.Add(t);
        return group;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
