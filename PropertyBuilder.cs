using System.Globalization;
using System.Linq;
using System.Text;
using ExifLibrary;

namespace ExifBatchEditor;

public static class PropertyBuilder
{
    public static readonly string[] SupportedTypes =
    {
        "ExifAscii",
        "ExifUShort",
        "ExifSShort",
        "ExifUInt",
        "ExifSInt",
        "ExifByte",
        "ExifSByte",
        "ExifFloat",
        "ExifDouble",
        "ExifURational",
        "ExifSRational",
        "ExifURationalArray",
        "ExifUndefined",
    };

    public static ExifProperty Build(ExifTag tag, string typeName, string valueText)
    {
        switch (typeName)
        {
            case "ExifAscii":
                return new ExifAscii(tag, valueText ?? string.Empty, Encoding.UTF8);
            case "ExifUShort":
                return new ExifUShort(tag, ushort.Parse(valueText, CultureInfo.InvariantCulture));
            case "ExifSShort":
                return new ExifSShort(tag, short.Parse(valueText, CultureInfo.InvariantCulture));
            case "ExifUInt":
                return new ExifUInt(tag, uint.Parse(valueText, CultureInfo.InvariantCulture));
            case "ExifSInt":
                return new ExifSInt(tag, int.Parse(valueText, CultureInfo.InvariantCulture));
            case "ExifByte":
                return new ExifByte(tag, byte.Parse(valueText, CultureInfo.InvariantCulture));
            case "ExifSByte":
                return new ExifSByte(tag, sbyte.Parse(valueText, CultureInfo.InvariantCulture));
            case "ExifFloat":
                return new ExifFloat(tag, float.Parse(valueText, CultureInfo.InvariantCulture));
            case "ExifDouble":
                return new ExifDouble(tag, double.Parse(valueText, CultureInfo.InvariantCulture));
            case "ExifURational":
            {
                var (n, d) = ParseFraction(valueText);
                return new ExifURational(tag, (uint)Math.Max(0, n), (uint)Math.Max(1, d));
            }
            case "ExifSRational":
            {
                var (n, d) = ParseFraction(valueText);
                return new ExifSRational(tag, n, d);
            }
            // GPSLatitude/GPSLongitude (and similar) store a 3-element rational array
            // (degrees, minutes, seconds). Tags loaded from a file that already has
            // coordinates come back as the GPSLatitudeLongitude subclass; ones created
            // fresh via Properties.Set(tag, d, m, s) come back as plain ExifURationalArray.
            // Both are handled identically here.
            case "ExifURationalArray":
            case "GPSLatitudeLongitude":
            {
                var dms = ParseThreeNumbers(valueText);
                return new ExifURationalArray(tag, new[]
                {
                    new MathEx.UFraction32((float)dms[0]),
                    new MathEx.UFraction32((float)dms[1]),
                    new MathEx.UFraction32((float)dms[2]),
                });
            }
            case "ExifUndefined":
                return new ExifUndefined(tag, ParseBytes(valueText));
            default:
                throw new NotSupportedException($"Unsupported tag type '{typeName}'.");
        }
    }

    /// <summary>
    /// Parses a degrees/minutes/seconds triple, accepting plain "40 26 46.3", the
    /// library's own "[40/1 26/1 4630/1000]" ToString format, and the "40.00°26.00'46.30""
    /// GPSLatitudeLongitude ToString format, so round-tripping a value shown in the grid
    /// back through the parser always works regardless of which format produced it.
    /// </summary>
    public static double[] ParseThreeNumbers(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new FormatException("Expected three numbers: degrees minutes seconds.");

        string cleaned = text
            .Replace('[', ' ').Replace(']', ' ')
            .Replace('°', ' ').Replace('\'', ' ').Replace('"', ' ')
            .Replace(',', ' ');
        var tokens = cleaned.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length != 3)
            throw new FormatException("Expected exactly three numbers: degrees minutes seconds.");

        var result = new double[3];
        for (int i = 0; i < 3; i++)
        {
            string t = tokens[i];
            int slash = t.IndexOf('/');
            result[i] = slash >= 0
                ? double.Parse(t[..slash], CultureInfo.InvariantCulture) / double.Parse(t[(slash + 1)..], CultureInfo.InvariantCulture)
                : double.Parse(t, CultureInfo.InvariantCulture);
        }
        return result;
    }

    public static (int numerator, int denominator) ParseFraction(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return (0, 1);
        text = text.Trim();
        int slash = text.IndexOf('/');
        if (slash >= 0)
        {
            int n = int.Parse(text[..slash].Trim(), CultureInfo.InvariantCulture);
            int d = int.Parse(text[(slash + 1)..].Trim(), CultureInfo.InvariantCulture);
            return (n, d);
        }

        double value = double.Parse(text, CultureInfo.InvariantCulture);
        const int denom = 1000000;
        return ((int)Math.Round(value * denom), denom);
    }

    public static byte[] ParseBytes(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<byte>();
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            text = text[2..];

        var hexChars = text.Where(c => !char.IsWhiteSpace(c)).ToArray();
        if (hexChars.Length > 0 && hexChars.Length % 2 == 0 && hexChars.All(Uri.IsHexDigit))
        {
            var bytes = new byte[hexChars.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = Convert.ToByte(new string(hexChars, i * 2, 2), 16);
            return bytes;
        }

        return Encoding.UTF8.GetBytes(text);
    }
}
