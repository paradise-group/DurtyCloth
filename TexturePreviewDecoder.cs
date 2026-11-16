using System.IO;
using BCnEncoder.Decoder;
using BCnEncoder.Shared;
using CodeWalker.GameFiles;

namespace FooxClothStudio;

internal static class TexturePreviewDecoder
{
    internal static byte[] DecodeBc7(Texture texture, int mip)
    {
        if (mip < 0 || mip >= texture.Levels) throw new ArgumentOutOfRangeException(nameof(mip));
        var data = texture.Data?.FullData ?? throw new InvalidDataException("BC7 texture has no data.");
        static int Size(int width, int height) => checked(((width + 3) / 4) * ((height + 3) / 4) * 16);
        var offset = 0;
        for (var level = 0; level < mip; level++)
            offset = checked(offset + Size(Math.Max(1, texture.Width >> level), Math.Max(1, texture.Height >> level)));
        var width = Math.Max(1, texture.Width >> mip);
        var height = Math.Max(1, texture.Height >> mip);
        var length = Size(width, height);
        if (offset > data.Length || length > data.Length - offset)
            throw new InvalidDataException("BC7 texture mip data is truncated.");
        var colors = new BcDecoder().DecodeRaw(data.AsSpan(offset, length).ToArray(), width, height, CompressionFormat.Bc7);
        var pixels = new byte[checked(width * height * 4)];
        for (var index = 0; index < colors.Length; index++)
        {
            var color = colors[index];
            pixels[index * 4] = color.b;
            pixels[index * 4 + 1] = color.g;
            pixels[index * 4 + 2] = color.r;
            pixels[index * 4 + 3] = color.a;
        }
        return pixels;
    }
}
