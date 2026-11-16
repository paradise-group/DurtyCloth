using System.Windows.Media.Imaging;

namespace FooxClothStudio;

public static class PhotoView
{
    private sealed class Pixels(BitmapSource source)
    {
        public int Width { get; } = source.PixelWidth;
        public int Height { get; } = source.PixelHeight;
        public byte[] Data { get; } = Read(source);
        private static byte[] Read(BitmapSource source)
        {
            var data = new byte[checked(source.PixelWidth * source.PixelHeight * 4)];
            source.CopyPixels(data, source.PixelWidth * 4, 0);
            return data;
        }
    }

    public static bool IsDecorationTemplate(BitmapSource image)
    {
        var pixels = new Pixels(image);
        if (pixels.Width == 64 && pixels.Height == 64 && Enumerable.Range(0, pixels.Width * pixels.Height)
            .All(index => pixels.Data[index * 4] == 255 && pixels.Data[index * 4 + 1] == 255 && pixels.Data[index * 4 + 2] == 255))
            return true;
        var cornerWidth = Math.Max(1, pixels.Width / 16);
        var cornerHeight = Math.Max(1, pixels.Height / 16);
        if (cornerWidth == pixels.Width && cornerHeight == pixels.Height)
            return pixels.Data[0] == 0 && pixels.Data[1] == 0 && pixels.Data[2] == 0;
        for (var y = 0; y < pixels.Height; y++)
            for (var x = 0; x < pixels.Width; x++)
            {
                if (x < cornerWidth && y < cornerHeight) continue;
                var offset = (y * pixels.Width + x) * 4;
                if (pixels.Data[offset] != 0 || pixels.Data[offset + 1] != 0 || pixels.Data[offset + 2] != 0) return false;
            }
        return true;
    }

    public static bool ShowBack(string slot) => slot == "hand";
}
