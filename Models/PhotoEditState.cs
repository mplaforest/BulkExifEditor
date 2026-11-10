using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ExifBatchEditor.Models;

// Per-file, in-memory-only slider state for the Edit panel. Nothing here is persisted to
// disk on its own - it only ever reaches the file when the user clicks Save (see
// PhotoAdjustments.Apply + SaveEdit_Click in MainWindow.xaml.cs), which bakes the current
// values into the actual JPEG pixels and then resets this back to neutral (there's
// nothing meaningful left to "undo" once the original pixels are gone). Revert just
// resets these values without touching the file.
public class PhotoEditState : INotifyPropertyChanged
{
    private double _exposure, _contrast, _highlights, _shadows, _whites, _blacks;
    private double _temperature, _tint, _vibrance, _saturation;
    private double _texture, _clarity, _dehaze, _grain, _glow;

    // Light
    public double Exposure { get => _exposure; set => Set(ref _exposure, value); }
    public double Contrast { get => _contrast; set => Set(ref _contrast, value); }
    public double Highlights { get => _highlights; set => Set(ref _highlights, value); }
    public double Shadows { get => _shadows; set => Set(ref _shadows, value); }
    public double Whites { get => _whites; set => Set(ref _whites, value); }
    public double Blacks { get => _blacks; set => Set(ref _blacks, value); }

    // Color
    public double Temperature { get => _temperature; set => Set(ref _temperature, value); }
    public double Tint { get => _tint; set => Set(ref _tint, value); }
    public double Vibrance { get => _vibrance; set => Set(ref _vibrance, value); }
    public double Saturation { get => _saturation; set => Set(ref _saturation, value); }

    // Effects
    public double Texture { get => _texture; set => Set(ref _texture, value); }
    public double Clarity { get => _clarity; set => Set(ref _clarity, value); }
    public double Dehaze { get => _dehaze; set => Set(ref _dehaze, value); }
    public double Grain { get => _grain; set => Set(ref _grain, value); }
    public double Glow { get => _glow; set => Set(ref _glow, value); }

    public bool HasAnyEdit =>
        Exposure != 0 || Contrast != 0 || Highlights != 0 || Shadows != 0 || Whites != 0 || Blacks != 0 ||
        Temperature != 0 || Tint != 0 || Vibrance != 0 || Saturation != 0 ||
        Texture != 0 || Clarity != 0 || Dehaze != 0 || Grain != 0 || Glow != 0;

    public void Reset()
    {
        Exposure = Contrast = Highlights = Shadows = Whites = Blacks = 0;
        Temperature = Tint = Vibrance = Saturation = 0;
        Texture = Clarity = Dehaze = Grain = Glow = 0;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
