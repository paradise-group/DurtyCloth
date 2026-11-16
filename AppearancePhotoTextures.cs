using System.IO;
using System.Text.RegularExpressions;
using CodeWalker.GameFiles;
using CodeWalker.Utils;

namespace FooxClothStudio;

public static class AppearancePhotoTextures
{
    public static Texture[] Select(YtdFile dictionary, string name, uint? diffuseHash = null, bool paletteMask = false)
    {
        var bound = dictionary.TextureDict?.Lookup(diffuseHash ?? JenkHash.GenHash(name.ToLowerInvariant()));
        if (bound is not null) return [bound];
        var candidates = (dictionary.TextureDict?.Textures?.data_items ?? [])
            .Where(texture => texture is not null && texture.Usage is not (TextureUsage.NORMAL or TextureUsage.SPECULAR or TextureUsage.DETAIL)
                && !Regex.IsMatch(texture.Name ?? "", @"(?:_n|_s|_normal|_norm|_spec)$", RegexOptions.IgnoreCase)).ToArray();
        var exact = candidates.Where(texture => string.Equals(texture.Name, name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (exact.Length == 1) return exact;
        var diffuse = candidates.Where(texture => texture.Usage is TextureUsage.DIFFUSE or TextureUsage.DIFFUSEDARK
            or TextureUsage.DIFFUSEALPHAOPAQUE or TextureUsage.DIFFUSEMIPSHARPEN).ToArray();
        if (diffuse.Length == 1) return diffuse;
        if (candidates.Length == 1) return candidates;
        if (paletteMask)
        {
            var masks = candidates.Where(IsPaletteMask).ToArray();
            if (masks.Length == 1) return masks;
        }
        throw new InvalidDataException("A textura de cor da aparência está ausente ou ambígua: " + name);
    }

    private static bool IsPaletteMask(Texture texture)
    {
        var pixels = texture.Format == TextureFormat.D3DFMT_BC7
            ? TexturePreviewDecoder.DecodeBc7(texture, 0) : DDSIO.GetPixels(texture, 0);
        var hasGreen = false;
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            if (pixels[offset + 3] == 0) continue;
            if (pixels[offset] > pixels[offset + 1] || pixels[offset + 2] > pixels[offset + 1]) return false;
            hasGreen |= pixels[offset + 1] > pixels[offset] && pixels[offset + 1] > pixels[offset + 2];
        }
        return hasGreen;
    }
}
