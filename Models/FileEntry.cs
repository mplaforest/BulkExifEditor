using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ExifLibrary;

namespace ExifBatchEditor.Models;

public class FileEntry : INotifyPropertyChanged
{
    private const int ThumbnailPixelWidth = 48;

    private bool _isDirty;

    public string FilePath { get; private set; }
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

    // Renames the file on disk and updates FilePath/FileName to match. Returns an
    // error message on failure (invalid name, collision, locked file) or null on success.
    public string? Rename(string newName)
    {
        newName = newName.Trim();
        if (newName.Length == 0)
            return "Filename cannot be empty.";

        if (!Path.HasExtension(newName))
            newName += Path.GetExtension(FilePath);

        if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return "Filename contains invalid characters.";

        if (string.Equals(newName, FileName, StringComparison.OrdinalIgnoreCase))
            return null;

        string dir = Path.GetDirectoryName(FilePath) ?? "";
        string newPath = Path.Combine(dir, newName);

        if (File.Exists(newPath))
            return $"A file named \"{newName}\" already exists in this folder.";

        try
        {
            MoveWithRetry(FilePath, newPath);
        }
        catch (Exception ex)
        {
            return $"Could not rename: {ex.Message}";
        }

        FilePath = newPath;
        OnPropertyChanged(nameof(FilePath));
        OnPropertyChanged(nameof(FileName));
        OnPropertyChanged(nameof(DisplayName));
        return null;
    }

    // Mirrors the save-retry pattern elsewhere in this app: a rename can transiently
    // fail with IOException if antivirus/cloud-sync briefly holds the file right after
    // it was loaded or saved.
    private static void MoveWithRetry(string oldPath, string newPath)
    {
        const int maxAttempts = 4;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                File.Move(oldPath, newPath);
                return;
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                Thread.Sleep(150 * attempt);
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
