using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodeWalker.GameFiles;
using CodeWalker.Utils;

namespace FooxClothStudio;

public static class PngTextureReader
{
    public static Texture Read(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var frame = decoder.Frames.FirstOrDefault() ??
            throw new InvalidDataException("PNG sem imagem.");
        var width = frame.PixelWidth;
        var height = frame.PixelHeight;
        if (width is < 1 or > 4096 || height is < 1 or > 4096 ||
            (width & (width - 1)) != 0 || (height & (height - 1)) != 0)
            throw new InvalidDataException("O PNG precisa ter largura e altura em potência de 2, até 4096 px.");

        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[checked(width * height * 4)];
        converted.CopyPixels(pixels, width * 4, 0);
        var mipmaps = new List<byte[]> { pixels };
        var currentWidth = width;
        var currentHeight = height;
        while (currentWidth > 1 || currentHeight > 1)
        {
            var nextWidth = Math.Max(1, currentWidth / 2);
            var nextHeight = Math.Max(1, currentHeight / 2);
            var next = new byte[nextWidth * nextHeight * 4];
            for (var y = 0; y < nextHeight; y++)
                for (var x = 0; x < nextWidth; x++)
                {
                    var sumAlpha = 0;
                    var sumBlue = 0;
                    var sumGreen = 0;
                    var sumRed = 0;
                    var samples = 0;
                    for (var dy = 0; dy < 2; dy++)
                        for (var dx = 0; dx < 2; dx++)
                        {
                            var sourceX = Math.Min(currentWidth - 1, x * 2 + dx);
                            var sourceY = Math.Min(currentHeight - 1, y * 2 + dy);
                            var source = (sourceY * currentWidth + sourceX) * 4;
                            var alpha = pixels[source + 3];
                            sumBlue += pixels[source] * alpha;
                            sumGreen += pixels[source + 1] * alpha;
                            sumRed += pixels[source + 2] * alpha;
                            sumAlpha += alpha;
                            samples++;
                        }
                    var target = (y * nextWidth + x) * 4;
                    next[target] = sumAlpha == 0 ? (byte)0 : (byte)(sumBlue / sumAlpha);
                    next[target + 1] = sumAlpha == 0 ? (byte)0 : (byte)(sumGreen / sumAlpha);
                    next[target + 2] = sumAlpha == 0 ? (byte)0 : (byte)(sumRed / sumAlpha);
                    next[target + 3] = (byte)(sumAlpha / samples);
                }
            mipmaps.Add(next);
            pixels = next;
            currentWidth = nextWidth;
            currentHeight = nextHeight;
        }

        using var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer, System.Text.Encoding.ASCII, true))
        {
            writer.Write(0x20534444u); // DDS
            writer.Write(124u);
            writer.Write(mipmaps.Count > 1 ? 0x0002100Fu : 0x0000100Fu);
            writer.Write((uint)height);
            writer.Write((uint)width);
            writer.Write((uint)(width * 4));
            writer.Write(0u);
            writer.Write((uint)mipmaps.Count);
            for (var i = 0; i < 11; i++) writer.Write(0u);
            writer.Write(32u);
            writer.Write(0x41u); // RGB and alpha
            writer.Write(0u);
            writer.Write(32u);
            writer.Write(0x00FF0000u);
            writer.Write(0x0000FF00u);
            writer.Write(0x000000FFu);
            writer.Write(0xFF000000u);
            writer.Write(mipmaps.Count > 1 ? 0x00401008u : 0x00001000u);
            for (var i = 0; i < 4; i++) writer.Write(0u);
            foreach (var mip in mipmaps) writer.Write(mip);
        }
        var texture = DDSIO.GetTexture(buffer.ToArray()) ??
            throw new InvalidDataException("Falha ao converter PNG em textura GTA.");
        if (texture.Width != width || texture.Height != height || texture.Levels != mipmaps.Count)
            throw new InvalidDataException("Os mipmaps do PNG não foram reconhecidos.");
        return texture;
    }
}
