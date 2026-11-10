using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ExifBatchEditor.Models;

namespace ExifBatchEditor.Imaging;

// Applies the Edit panel's sliders to a decoded bitmap. None of this claims to be
// pixel-identical to any particular commercial editor's algorithms (those are
// proprietary and undocumented) - these are standard, reasonable approximations of the
// same named concepts (exposure = stops of brightness, clarity/texture = local contrast
// at different blur radii, dehaze = contrast+haze-tinted-color correction, etc.), good
// enough to give an obviously-correct-direction, usably-real-time live preview.
public static class PhotoAdjustments
{
    // Per-entry grain noise is cached by the caller (keyed off the entry) and passed in
    // here so the speckle pattern stays stable across recomputes triggered by *other*
    // sliders - regenerating it from a fresh Random every call would make the grain
    // visibly "swim" every time any unrelated slider moved.
    //
    // applyCrop=false is used for the live on-screen preview while editing: cropping the
    // actual displayed bitmap on every drag tick while the user is still positioning the
    // crop rectangle made the image itself shrink mid-drag, which threw off the overlay's
    // own size/position calculations (they're based on PreviewImage.Source's dimensions)
    // and broke the whole interaction. The live preview always shows the full, uncropped,
    // adjusted image - only FileEntry.SaveEdit() (which calls this with the default
    // applyCrop=true) actually crops the real output.
    public static WriteableBitmap Apply(BitmapSource source, PhotoEditState state, float[]? grainNoise, bool applyCrop = true)
    {
        int width = source.PixelWidth;
        int height = source.PixelHeight;

        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int stride = width * 4;
        var pixels = new byte[height * stride];
        converted.CopyPixels(pixels, stride, 0);

        // Work in float RGB (0..1) throughout - simpler and more numerically forgiving
        // than juggling byte clamping at every single step of a 10+ stage pipeline.
        int pixelCount = width * height;
        var r = new float[pixelCount];
        var g = new float[pixelCount];
        var b = new float[pixelCount];
        for (int i = 0, p = 0; i < pixelCount; i++, p += 4)
        {
            b[i] = pixels[p] / 255f;
            g[i] = pixels[p + 1] / 255f;
            r[i] = pixels[p + 2] / 255f;
        }

        ApplyToneAndColor(r, g, b, state);

        if (state.Texture != 0 || state.Clarity != 0)
            ApplyLocalContrast(r, g, b, width, height, state);

        if (state.Glow != 0)
            ApplyGlow(r, g, b, width, height, state.Glow);

        if (state.Grain != 0 && grainNoise != null)
            ApplyGrain(r, g, b, grainNoise, state.Grain);

        for (int i = 0, p = 0; i < pixelCount; i++, p += 4)
        {
            pixels[p] = ToByte(b[i]);
            pixels[p + 1] = ToByte(g[i]);
            pixels[p + 2] = ToByte(r[i]);
            // alpha (p+3) untouched - stays opaque
        }

        var result = applyCrop
            ? CropIfNeeded(pixels, width, height, stride, state.Crop)
            : (Pixels: pixels, Width: width, Height: height, Stride: stride);
        var bmp = new WriteableBitmap(result.Width, result.Height, source.DpiX, source.DpiY, PixelFormats.Bgra32, null);
        bmp.WritePixels(new Int32Rect(0, 0, result.Width, result.Height), result.Pixels, result.Stride, 0);
        bmp.Freeze();
        return bmp;
    }

    // Everything here is "pointwise" (each pixel computed independently of its
    // neighbors) - white balance, tone curve, and color presence, all folded into one
    // pass per pixel for speed.
    private static void ApplyToneAndColor(float[] r, float[] g, float[] b, PhotoEditState state)
    {
        float tempFactor = (float)(state.Temperature / 100.0);
        float tintFactor = (float)(state.Tint / 100.0);
        float exposureScale = MathF.Pow(2f, (float)(state.Exposure));
        float contrastFactor = 1f + (float)(state.Contrast / 100.0);
        float highlightsAmt = (float)(state.Highlights / 100.0);
        float shadowsAmt = (float)(state.Shadows / 100.0);
        float blackPoint = (float)(state.Blacks / 100.0) * 0.2f;
        float whitePoint = 1f - (float)(state.Whites / 100.0) * 0.2f;
        float whiteBlackRange = MathF.Max(0.05f, whitePoint - blackPoint);
        float satFactor = 1f + (float)(state.Saturation / 100.0);
        float vibFactor = (float)(state.Vibrance / 100.0);
        float dehazeAmt = (float)(state.Dehaze / 100.0);

        for (int i = 0; i < r.Length; i++)
        {
            float rr = r[i], gg = g[i], bb = b[i];

            // White balance: temperature shifts the red/blue axis (warmer/cooler),
            // tint shifts the green/magenta axis.
            rr += tempFactor * 0.25f + tintFactor * 0.07f;
            bb -= tempFactor * 0.25f - tintFactor * 0.07f;
            gg -= tintFactor * 0.25f;

            // Exposure: a straight multiplicative stop adjustment.
            rr *= exposureScale; gg *= exposureScale; bb *= exposureScale;

            // Highlights/Shadows: luminance-zone-weighted brightness adjustment -
            // Highlights only really affects bright pixels, Shadows only dark ones.
            float lum = 0.299f * rr + 0.587f * gg + 0.114f * bb;
            float hiWeight = SmoothStep(0.45f, 1.0f, lum);
            float shWeight = SmoothStep(0.55f, 0.0f, lum);
            float toneAdj = highlightsAmt * hiWeight * 0.5f + shadowsAmt * shWeight * 0.5f;
            rr += toneAdj; gg += toneAdj; bb += toneAdj;

            // Whites/Blacks: remap the black/white clip points (a simple linear
            // levels stretch), same remap applied to all three channels.
            rr = (rr - blackPoint) / whiteBlackRange;
            gg = (gg - blackPoint) / whiteBlackRange;
            bb = (bb - blackPoint) / whiteBlackRange;

            // Contrast: scale around the midpoint.
            rr = (rr - 0.5f) * contrastFactor + 0.5f;
            gg = (gg - 0.5f) * contrastFactor + 0.5f;
            bb = (bb - 0.5f) * contrastFactor + 0.5f;

            // Dehaze (approximate): a contrast/levels punch biased toward removing a
            // blue-grey veil, the reverse adds that veil back (simulating haze).
            float hazeContrast = 1f + dehazeAmt * 0.6f;
            rr = (rr - 0.5f) * hazeContrast + 0.5f + dehazeAmt * 0.03f;
            gg = (gg - 0.5f) * hazeContrast + 0.5f + dehazeAmt * 0.015f;
            bb = (bb - 0.5f) * hazeContrast + 0.5f - dehazeAmt * 0.04f;

            // Vibrance/Saturation via a cheap HSL-less approximation: push each
            // channel away from (vibrance) or around (saturation) the pixel's own
            // luminance, vibrance tapering off for already-saturated pixels.
            float lum2 = 0.299f * rr + 0.587f * gg + 0.114f * bb;
            float maxC = MathF.Max(rr, MathF.Max(gg, bb));
            float minC = MathF.Min(rr, MathF.Min(gg, bb));
            float currentSat = maxC - minC;
            float vibAdj = vibFactor * (1f - Clamp01(currentSat * 1.5f)) * 0.9f;
            float totalSat = satFactor + vibAdj;
            rr = lum2 + (rr - lum2) * totalSat;
            gg = lum2 + (gg - lum2) * totalSat;
            bb = lum2 + (bb - lum2) * totalSat;

            r[i] = Clamp01(rr);
            g[i] = Clamp01(gg);
            b[i] = Clamp01(bb);
        }
    }

    // Texture (fine, small-radius local contrast) and Clarity (broader, larger-radius
    // local contrast) are both unsharp-mask-style: subtract a blurred copy to get a
    // "detail" layer, then add a scaled copy of that detail back on top.
    private static void ApplyLocalContrast(float[] r, float[] g, float[] b, int width, int height, PhotoEditState state)
    {
        if (state.Texture != 0)
        {
            var (br, bg, bb2) = BoxBlurLuminancePreserving(r, g, b, width, height, radius: 2);
            float amt = (float)(state.Texture / 100.0) * 0.6f;
            AddDetail(r, g, b, br, bg, bb2, amt);
        }

        if (state.Clarity != 0)
        {
            var (br, bg, bb2) = BoxBlurLuminancePreserving(r, g, b, width, height, radius: 14);
            float amt = (float)(state.Clarity / 100.0) * 0.5f;
            AddDetail(r, g, b, br, bg, bb2, amt);
        }
    }

    private static void AddDetail(float[] r, float[] g, float[] b, float[] br, float[] bg, float[] bb, float amount)
    {
        for (int i = 0; i < r.Length; i++)
        {
            r[i] = Clamp01(r[i] + (r[i] - br[i]) * amount);
            g[i] = Clamp01(g[i] + (g[i] - bg[i]) * amount);
            b[i] = Clamp01(b[i] + (b[i] - bb[i]) * amount);
        }
    }

    // Glow: a soft bloom - blur a brightened copy of the image and screen-blend it back
    // over the original, giving the "soft glowing highlights" look.
    private static void ApplyGlow(float[] r, float[] g, float[] b, int width, int height, double glow)
    {
        int n = r.Length;
        var br = new float[n];
        var bg = new float[n];
        var bb = new float[n];
        for (int i = 0; i < n; i++)
        {
            br[i] = Clamp01(r[i] * 1.4f);
            bg[i] = Clamp01(g[i] * 1.4f);
            bb[i] = Clamp01(b[i] * 1.4f);
        }
        var (gr, gg, gb) = BoxBlur(br, bg, bb, width, height, radius: 18);

        float amt = (float)(glow / 100.0);
        for (int i = 0; i < n; i++)
        {
            float sr = 1f - (1f - r[i]) * (1f - gr[i]);
            float sg = 1f - (1f - g[i]) * (1f - gg[i]);
            float sb = 1f - (1f - b[i]) * (1f - gb[i]);
            r[i] = r[i] + (sr - r[i]) * amt;
            g[i] = g[i] + (sg - g[i]) * amt;
            b[i] = b[i] + (sb - b[i]) * amt;
        }
    }

    private static void ApplyGrain(float[] r, float[] g, float[] b, float[] noise, double grain)
    {
        float amt = (float)(grain / 100.0) * 0.12f;
        int n = Math.Min(r.Length, noise.Length);
        for (int i = 0; i < n; i++)
        {
            float d = noise[i] * amt;
            r[i] = Clamp01(r[i] + d);
            g[i] = Clamp01(g[i] + d);
            b[i] = Clamp01(b[i] + d);
        }
    }

    // Deterministic per-entry noise field for Grain, stable across recomputes (see the
    // caller - MainWindow.xaml.cs caches this per FileEntry keyed by preview size).
    public static float[] GenerateGrainNoise(int pixelCount, int seed)
    {
        var rnd = new Random(seed);
        var noise = new float[pixelCount];
        for (int i = 0; i < pixelCount; i++)
            noise[i] = (float)(rnd.NextDouble() * 2.0 - 1.0);
        return noise;
    }

    // Separable box blur (horizontal pass then vertical pass via a sliding-window sum) -
    // O(width*height) regardless of radius, which matters since this runs on every
    // slider tick for a live preview.
    private static (float[] r, float[] g, float[] b) BoxBlur(float[] r, float[] g, float[] b, int width, int height, int radius)
    {
        var tr = new float[r.Length]; var tg = new float[r.Length]; var tb = new float[r.Length];
        BoxBlur1D(r, tr, width, height, radius, horizontal: true);
        BoxBlur1D(g, tg, width, height, radius, horizontal: true);
        BoxBlur1D(b, tb, width, height, radius, horizontal: true);

        var or_ = new float[r.Length]; var og = new float[r.Length]; var ob = new float[r.Length];
        BoxBlur1D(tr, or_, width, height, radius, horizontal: false);
        BoxBlur1D(tg, og, width, height, radius, horizontal: false);
        BoxBlur1D(tb, ob, width, height, radius, horizontal: false);
        return (or_, og, ob);
    }

    // Same as BoxBlur, but renamed at the call site for clarity where it's used purely
    // as the "local average" reference for an unsharp-mask detail layer.
    private static (float[] r, float[] g, float[] b) BoxBlurLuminancePreserving(
        float[] r, float[] g, float[] b, int width, int height, int radius) =>
        BoxBlur(r, g, b, width, height, radius);

    private static void BoxBlur1D(float[] src, float[] dst, int width, int height, int radius, bool horizontal)
    {
        int window = radius * 2 + 1;
        if (horizontal)
        {
            for (int y = 0; y < height; y++)
            {
                int rowStart = y * width;
                float sum = 0;
                for (int x = -radius; x <= radius; x++)
                    sum += src[rowStart + Clamp(x, 0, width - 1)];
                for (int x = 0; x < width; x++)
                {
                    dst[rowStart + x] = sum / window;
                    int addX = Clamp(x + radius + 1, 0, width - 1);
                    int removeX = Clamp(x - radius, 0, width - 1);
                    sum += src[rowStart + addX] - src[rowStart + removeX];
                }
            }
        }
        else
        {
            for (int x = 0; x < width; x++)
            {
                float sum = 0;
                for (int y = -radius; y <= radius; y++)
                    sum += src[Clamp(y, 0, height - 1) * width + x];
                for (int y = 0; y < height; y++)
                {
                    dst[y * width + x] = sum / window;
                    int addY = Clamp(y + radius + 1, 0, height - 1);
                    int removeY = Clamp(y - radius, 0, height - 1);
                    sum += src[addY * width + x] - src[removeY * width + x];
                }
            }
        }
    }

    private static (byte[] Pixels, int Width, int Height, int Stride) CropIfNeeded(
        byte[] pixels, int width, int height, int stride, NormalizedCropRect crop)
    {
        if (crop.IsFullFrame) return (pixels, width, height, stride);

        int left = Clamp((int)Math.Round(crop.Left * width), 0, width - 1);
        int top = Clamp((int)Math.Round(crop.Top * height), 0, height - 1);
        int right = Clamp((int)Math.Round(crop.Right * width), left + 1, width);
        int bottom = Clamp((int)Math.Round(crop.Bottom * height), top + 1, height);

        int cropWidth = right - left;
        int cropHeight = bottom - top;
        int cropStride = cropWidth * 4;
        var cropped = new byte[cropHeight * cropStride];

        for (int row = 0; row < cropHeight; row++)
        {
            int srcOffset = (top + row) * stride + left * 4;
            int dstOffset = row * cropStride;
            Array.Copy(pixels, srcOffset, cropped, dstOffset, cropStride);
        }

        return (cropped, cropWidth, cropHeight, cropStride);
    }

    // Computes a 256-bucket luminance histogram (0-255) - used by the pipeline test
    // harness and anywhere only an overall tonal distribution is needed.
    public static int[] ComputeLuminanceHistogram(BitmapSource source)
    {
        var (r, g, b) = ComputeRgbHistograms(source);
        var buckets = new int[256];
        for (int i = 0; i < 256; i++) buckets[i] = (r[i] + g[i] + b[i]) / 3;
        return buckets;
    }

    // Separate per-channel histograms (0-255 each) for the Edit panel's color histogram
    // display.
    public static (int[] R, int[] G, int[] B) ComputeRgbHistograms(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int stride = source.PixelWidth * 4;
        var pixels = new byte[source.PixelHeight * stride];
        converted.CopyPixels(pixels, stride, 0);

        var r = new int[256];
        var g = new int[256];
        var b = new int[256];
        for (int p = 0; p < pixels.Length; p += 4)
        {
            b[pixels[p]]++;
            g[pixels[p + 1]]++;
            r[pixels[p + 2]]++;
        }
        return (r, g, b);
    }

    private static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = Clamp01((x - edge0) / (edge1 - edge0));
        return t * t * (3f - 2f * t);
    }

    private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    private static int Clamp(int v, int min, int max) => v < min ? min : v > max ? max : v;
    private static byte ToByte(float v) => (byte)(Clamp01(v) * 255f + 0.5f);
}
