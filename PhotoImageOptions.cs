using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FooxClothStudio;

public sealed record PhotoImageOptions(int PixelSize = 768, string Format = "PNG", int Quality = 85)
{
    public string Extension => Format == "JPEG" ? ".jpg" : ".png";

    public void Write(string destination, BitmapSource source)
    {
        if (PixelSize is < 64 or > 768 || Format is not ("PNG" or "JPEG") || Quality is < 1 or > 100)
            throw new ArgumentException("Opções de imagem inválidas.");
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            // JPEG has no alpha channel. Composite any transparency onto white.
            if (Format == "JPEG") drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, PixelSize, PixelSize));
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
            drawing.DrawImage(source, new Rect(0, 0, PixelSize, PixelSize));
        }
        var bitmap = new RenderTargetBitmap(PixelSize, PixelSize, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        BitmapEncoder encoder = Format == "JPEG"
            ? new JpegBitmapEncoder { QualityLevel = Quality }
            : new PngBitmapEncoder { Interlace = PngInterlaceOption.Off };
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
        encoder.Save(output);
    }
}
