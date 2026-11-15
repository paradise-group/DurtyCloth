using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FooxClothStudio;
using SkiaSharp;

internal static class WebPExportTests
{
    private static int _checks;

    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        var source = CreateFixture();
        foreach (var size in new[] { 64, 256, 512, 768 })
        {
            var options = new PhotoImageOptions(size, "WebP", 85);
            var path = Path.Combine(output, $"transparent_{size}{options.Extension}");
            options.Write(path, source);
            var bytes = File.ReadAllBytes(path);
            Check(bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8), "Actual WebP container");
            using var decoded = SKBitmap.Decode(bytes);
            Check(decoded is not null && decoded.Width == size && decoded.Height == size, "WebP dimensions");
            Check(decoded!.GetPixel(0, 0).Alpha == 0, "Fully transparent background");
            var translucent = decoded.GetPixel(size / 4, size / 4);
            Check(Math.Abs(translucent.Alpha - 128) <= 1, "Partial alpha survives premultiplication and WebP encoding");
            Check(Math.Abs(translucent.Red - 50) < 20 && Math.Abs(translucent.Green - 120) < 20
                && Math.Abs(translucent.Blue - 210) < 20, "Translucent colors are not darkened by premultiplied alpha");
            var center = decoded.GetPixel(size / 2, size / 2);
            Check(center.Alpha == 255 && Math.Abs(center.Red - 220) < 20 && Math.Abs(center.Green - 40) < 20
                && Math.Abs(center.Blue - 30) < 20, "BGRA channel order and opaque color");
        }

        var lowerQuality = Path.Combine(output, "quality_40.webp");
        var higherQuality = Path.Combine(output, "quality_95.webp");
        new PhotoImageOptions(512, "WebP", 40).Write(lowerQuality, source);
        new PhotoImageOptions(512, "WebP", 95).Write(higherQuality, source);
        Check(new FileInfo(lowerQuality).Length < new FileInfo(higherQuality).Length, "Selected quality changes actual compression");

        var opaque = new DrawingVisual();
        using (var drawing = opaque.RenderOpen())
        {
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x62, 0x5F, 0x68)), null, new Rect(0, 0, 320, 320));
            drawing.DrawImage(source, new Rect(0, 0, 320, 320));
        }
        var solid = new RenderTargetBitmap(320, 320, 96, 96, PixelFormats.Pbgra32);
        solid.Render(opaque);
        var solidPath = Path.Combine(output, "solid.webp");
        new PhotoImageOptions(512, "WebP", 85).Write(solidPath, solid);
        using (var decoded = SKBitmap.Decode(solidPath))
        {
            var corner = decoded.GetPixel(0, 0);
            Check(corner.Alpha == 255 && Math.Abs(corner.Red - 0x62) < 8 && Math.Abs(corner.Green - 0x5F) < 8
                && Math.Abs(corner.Blue - 0x68) < 8, "Solid background preserved");
        }

        foreach (var format in new[] { "PNG", "JPEG" })
        {
            var options = new PhotoImageOptions(512, format, 85);
            var path = Path.Combine(output, "legacy" + options.Extension);
            options.Write(path, source);
            var decoded = BitmapDecoder.Create(new Uri(Path.GetFullPath(path)), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            var converted = new FormatConvertedBitmap(decoded, PixelFormats.Bgra32, null, 0);
            var corner = new byte[4];
            converted.CopyPixels(new Int32Rect(0, 0, 1, 1), corner, 4, 0);
            Check(decoded.PixelWidth == 512 && decoded.PixelHeight == 512 && corner[3] == (format == "PNG" ? 0 : 255), "PNG/JPEG compatibility");
        }

        var existingBytes = File.ReadAllBytes(solidPath);
        try
        {
            new PhotoImageOptions(512, "WebP").Write(solidPath, source);
            throw new Exception("Existing output was overwritten.");
        }
        catch (IOException) { Check(File.ReadAllBytes(solidPath).SequenceEqual(existingBytes), "Existing images are not overwritten"); }
        var invalidPath = Path.Combine(output, "invalid.webp");
        foreach (var options in new[] { new PhotoImageOptions(32, "WebP"), new PhotoImageOptions(512, "GIF"), new PhotoImageOptions(512, "WebP", 101) })
        {
            try { options.Write(invalidPath, source); throw new Exception("Invalid options accepted."); }
            catch (ArgumentException) { Check(!File.Exists(invalidPath), "Invalid options do not create output"); }
        }
        Check(new PhotoImageOptions().Extension == ".png", "Existing renderer defaults remain PNG");
        CheckDialog();
        Console.WriteLine($"PASS: {_checks} WebP export checks, including alpha, colors, compression, sizes, legacy formats, output guards and dialog transitions.");
        Console.WriteLine($"512px: PNG {new FileInfo(Path.Combine(output, "legacy.png")).Length:N0} bytes; WebP quality 85 {new FileInfo(Path.Combine(output, "transparent_512.webp")).Length:N0} bytes.");
    }

    private static BitmapSource CreateFixture()
    {
        const int size = 320;
        var pixels = new byte[size * size * 4];
        var random = new Random(97);
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var offset = (y * size + x) * 4;
            if (x < 32 || y < 32 || x >= 288 || y >= 288) continue;
            pixels[offset] = (byte)(30 + random.Next(60));
            pixels[offset + 1] = (byte)(40 + random.Next(100));
            pixels[offset + 2] = (byte)(80 + random.Next(130));
            pixels[offset + 3] = 255;
            if (x >= 64 && x < 96 && y >= 64 && y < 96)
            {
                pixels[offset] = 210;
                pixels[offset + 1] = 120;
                pixels[offset + 2] = 50;
                pixels[offset + 3] = 128;
            }
            if (x >= 144 && x < 176 && y >= 144 && y < 176)
            {
                pixels[offset] = 30;
                pixels[offset + 1] = 40;
                pixels[offset + 2] = 220;
            }
        }
        var source = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        source.Freeze();
        return source;
    }

    private static void CheckDialog()
    {
        var type = typeof(MainWindow).Assembly.GetType("FooxClothStudio.PhotoExportOptionsWindow")!;
        var window = (Window)Activator.CreateInstance(type)!;
        T Field<T>(string name) => (T)type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        var format = Field<ComboBox>("_format");
        var size = Field<ComboBox>("_size");
        var quality = Field<Slider>("_quality");
        var transparent = Field<CheckBox>("_transparent");
        var background = Field<TextBox>("_background");
        Check((string)format.SelectedItem == "WebP" && (int)size.SelectedItem == 512 && quality.Value == 85, "Recommended export defaults");
        Check(quality.IsEnabled && transparent.IsEnabled && background.Text == "#625F68", "WebP quality and background controls");
        transparent.IsChecked = true;
        Check(!background.IsEnabled, "Transparent WebP disables solid background input");
        format.SelectedItem = "JPEG";
        Check(transparent.IsChecked == false && !transparent.IsEnabled && background.IsEnabled && quality.IsEnabled, "JPEG forces opaque output");
        format.SelectedItem = "PNG";
        Check(transparent.IsEnabled && !quality.IsEnabled, "PNG preserves lossless quality and optional alpha");
        format.SelectedItem = "WebP";
        Check(transparent.IsEnabled && quality.IsEnabled, "Switching back restores WebP controls");
        window.Close();
    }

    private static void Check(bool success, string message)
    {
        if (!success) throw new Exception(message);
        _checks++;
    }
}
