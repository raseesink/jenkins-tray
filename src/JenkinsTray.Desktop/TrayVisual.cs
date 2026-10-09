using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using JenkinsTray.Core;

namespace JenkinsTray.Desktop;

internal static class TrayVisual
{
    public static WindowIcon Create(AggregateStatus status)
    {
        using var bitmap = Render(status);
        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;
        return new WindowIcon(stream);
    }
    internal static WriteableBitmap Render(AggregateStatus status)
    {
        var bitmap = new WriteableBitmap(new PixelSize(32, 32), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        var color = status.Health switch
        {
            Health.Success => (R: 34, G: 165, B: 75), Health.Failure => (R: 215, G: 45, B: 45),
            Health.Unstable => (R: 235, G: 165, B: 20), Health.Incomplete => (R: 105, G: 110, B: 125),
            _ => (R: 70, G: 120, B: 180)
        };
        using (var buffer = bitmap.Lock())
        {
            var bytes = new byte[buffer.RowBytes * 32];
            for (var y = 0; y < 32; y++) for (var x = 0; x < 32; x++)
            {
                var distance = Math.Pow(x - 15.5, 2) + Math.Pow(y - 15.5, 2);
                var index = y * buffer.RowBytes + x * 4;
                if (distance > 210) continue;
                var (r, g, b) = distance > 175 ? (255, 255, 255) : distance > 140 ? (20, 20, 20) : color;
                // Glyphs complement color; the dual outline stays visible on light/dark desktops.
                var glyph = status.Health switch
                {
                    Health.Success => (x is >= 8 and <= 14 && Math.Abs(y - x - 4) < 2) || (x is >= 14 and <= 24 && Math.Abs(y + x - 32) < 2),
                    Health.Failure => x is >= 9 and <= 22 && (Math.Abs(y - x) < 2 || Math.Abs(y + x - 31) < 2),
                    Health.Unstable or Health.Incomplete => x is >= 14 and <= 17 && (y is >= 8 and <= 18 or >= 22 and <= 24),
                    _ => y is >= 14 and <= 17 && x is >= 8 and <= 24
                };
                if (glyph) (r, g, b) = (255, 255, 255);
                if (status.Building && x is >= 21 and <= 29 && y is >= 21 and <= 29)
                    (r, g, b) = x is 21 or 29 || y is 21 or 29 ? (255, 255, 255) : (40, 105, 245);
                bytes[index] = (byte)b; bytes[index + 1] = (byte)g; bytes[index + 2] = (byte)r; bytes[index + 3] = 255;
            }
            Marshal.Copy(bytes, 0, buffer.Address, bytes.Length);
        }
        return bitmap;
    }
}
