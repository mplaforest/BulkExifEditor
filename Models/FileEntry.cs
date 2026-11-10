using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ExifBatchEditor.Imaging;
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

    // Edit panel slider state - see PhotoEditState for why this is in-memory-only until
    // SaveEdit() bakes it into the actual file.
    public PhotoEditState Edit { get; } = new();

    private float[]? _cachedGrainNoise;
    private int _cachedGrainNoisePixelCount = -1;

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

    private static BitmapSource? TryLoadThumbnail(string path, int orientation) =>
        TryLoadBitmap(path, ThumbnailPixelWidth, orientation);

    // Decoded on demand (not precomputed/cached like Thumbnail) since it's only needed
    // while the file list's hover-preview panel is showing this entry - re-reads
    // orientation fresh in case the EXIF tag was edited since this entry was loaded.
    public BitmapSource? LoadPreview(int pixelWidth) =>
        TryLoadBitmap(FilePath, pixelWidth, GetOrientation(Image));

    // Full native resolution, no downscaling - used by the Edit panel's Save pipeline so
    // the baked-in adjustments apply to the real image, not the small preview.
    public BitmapSource? LoadFullResolutionBitmap() =>
        TryLoadBitmap(FilePath, 0, GetOrientation(Image));

    private static BitmapSource? TryLoadBitmap(string path, int pixelWidth, int orientation)
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
                if (pixelWidth > 0) bitmap.DecodePixelWidth = pixelWidth;
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

    // Stable (doesn't "swim" between unrelated slider tweaks) grain noise for the live
    // preview, regenerated only if a different pixel count is requested (e.g. the
    // preview panel was resized). The actual save pipeline generates its own full-
    // resolution noise separately (see SaveEdit) since the pixel counts differ.
    public float[] GetOrCreatePreviewGrainNoise(int pixelCount)
    {
        if (_cachedGrainNoise == null || _cachedGrainNoisePixelCount != pixelCount)
        {
            _cachedGrainNoise = PhotoAdjustments.GenerateGrainNoise(pixelCount, FilePath.GetHashCode());
            _cachedGrainNoisePixelCount = pixelCount;
        }
        return _cachedGrainNoise;
    }

    // Bakes the current Edit slider values into the actual file's pixels at full
    // resolution, re-attaches the existing EXIF metadata (the pixel encode step below
    // doesn't carry it), overwrites the file, then resets Edit back to neutral - once
    // baked in, the adjusted pixels ARE the new "original," so there's nothing left to
    // represent as a pending edit. Returns an error message on failure, or null on
    // success. No-ops (returns null immediately) if nothing was actually changed.
    public string? SaveEdit()
    {
        if (!Edit.HasAnyEdit) return null;

        string? tempPath = null;
        try
        {
            BitmapSource? original = LoadFullResolutionBitmap();
            if (original == null) return "Could not re-read the original image to apply edits to.";

            float[] grainNoise = PhotoAdjustments.GenerateGrainNoise(
                original.PixelWidth * original.PixelHeight, FilePath.GetHashCode());
            WriteableBitmap adjusted = PhotoAdjustments.Apply(original, Edit, grainNoise);

            var encoder = new JpegBitmapEncoder { QualityLevel = 92 };
            encoder.Frames.Add(BitmapFrame.Create(adjusted));

            // Encode to a temp file first (not directly over FilePath) so a mid-write
            // failure can't corrupt the original, and so ImageFile.FromFile below has a
            // normal file to open rather than needing a from-stream overload.
            tempPath = FilePath + ".newpixels.tmp";
            using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
                encoder.Save(fs);

            var newImage = ImageFile.FromFile(tempPath);
            foreach (var prop in Image.Properties)
                newImage.Properties.Set(prop);

            SaveImageWithRetry(newImage, FilePath);

            Image = newImage;
            Edit.Reset();
            IsDirty = false;
            OnPropertyChanged(nameof(Image));
            return null;
        }
        catch (Exception ex)
        {
            return $"Could not save edited image: {ex.Message}";
        }
        finally
        {
            if (tempPath != null && File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* best-effort cleanup only */ }
            }
        }
    }

    // Mirrors MainWindow.xaml.cs's SaveWithRetry pattern (transient IOException from
    // antivirus/cloud-sync briefly holding the file).
    private static void SaveImageWithRetry(ImageFile image, string path)
    {
        const int maxAttempts = 4;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                image.Save(path);
                return;
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                Thread.Sleep(150 * attempt);
            }
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
