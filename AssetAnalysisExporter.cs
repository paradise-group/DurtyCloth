using System.IO;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using CodeWalker.GameFiles;
using CodeWalker.Utils;

namespace FooxClothStudio;

public static class AssetAnalysisExporter
{
    public static void ValidateTextureRoundtrip(IEnumerable<Texture> expected, IEnumerable<Texture> actual)
    {
        static string Signature(Texture texture)
        {
            if (texture.Data?.FullData is not { Length: > 0 } pixels)
                throw new InvalidDataException("Texture image data is missing: " + texture.Name);
            return string.Join("|", texture.Name, texture.NameHash, texture.Width, texture.Height,
                texture.Format, texture.Levels, Convert.ToHexString(SHA256.HashData(pixels)));
        }
        var before = expected.Select(Signature).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var after = actual.Select(Signature).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        if (!before.SequenceEqual(after)) throw new InvalidDataException("XML reimport changed texture names, dimensions, format, mipmaps or image data.");
    }
    public static string Export(string source, string parentFolder, string format)
    {
        var parent = Path.GetFullPath(parentFolder);
        var stem = Path.GetFileNameWithoutExtension(source);
        var folder = Path.Combine(parent, "foox_" + format.ToLowerInvariant() + "_" + stem + "_" + Guid.NewGuid().ToString("N"));
        if (!Path.GetFullPath(folder).StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Invalid export folder.");
        if (Directory.Exists(folder)) throw new IOException("Export output already exists.");
        var textures = new List<Texture>(); YddFile? ydd = null; YtdFile? ytd = null;
        if (Path.GetExtension(source).Equals(".ydd", StringComparison.OrdinalIgnoreCase))
        {
            ydd = new YddFile(); ydd.Load(File.ReadAllBytes(source));
            textures.AddRange((ydd.Drawables ?? []).SelectMany(drawable => drawable?.ShaderGroup?.TextureDictionary?.Textures?.data_items ?? []));
        }
        else if (Path.GetExtension(source).Equals(".ytd", StringComparison.OrdinalIgnoreCase))
        {
            ytd = new YtdFile(); ytd.Load(File.ReadAllBytes(source)); textures.AddRange(ytd.TextureDict?.Textures?.data_items ?? []);
        }
        else throw new InvalidDataException("Select a YDD or YTD asset.");
        foreach (var texture in textures)
            if (string.IsNullOrWhiteSpace(texture.Name) || texture.Name != Path.GetFileName(texture.Name) || texture.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || texture.Name is "." or "..")
                throw new InvalidDataException("Texture name is not a valid output filename.");
        var groups = textures.GroupBy(texture => texture.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var group in groups)
            if (group.Select(texture => Convert.ToHexString(SHA256.HashData(DDSIO.GetDDSFile(texture)))).Distinct().Count() > 1)
                throw new InvalidDataException("Conflicting embedded textures use the same name: " + group.Key);
        Directory.CreateDirectory(folder);
        try
        {
            if (format == "XML")
            {
                var xml = ydd is not null ? YddXml.GetXml(ydd, folder) : YtdXml.GetXml(ytd, folder);
                var document = XDocument.Parse(xml);
                if (document.Root is null) throw new InvalidDataException("XML root is missing.");
                if (ydd is not null)
                {
                    var reopened = XmlYdd.GetYdd(xml, folder);
                    if ((reopened.DrawableDict?.Drawables?.data_items?.Length ?? 0) != (ydd.Drawables?.Length ?? 0))
                        throw new InvalidDataException("XML lost drawables during reimport.");
                    ValidateTextureRoundtrip(textures, (reopened.DrawableDict?.Drawables?.data_items ?? [])
                        .SelectMany(drawable => drawable?.ShaderGroup?.TextureDictionary?.Textures?.data_items ?? []));
                }
                else
                {
                    var reopened = XmlYtd.GetYtd(xml, folder);
                    if ((reopened.TextureDict?.Textures?.data_items?.Length ?? 0) != textures.Count) throw new InvalidDataException("XML lost textures during reimport.");
                    ValidateTextureRoundtrip(textures, reopened.TextureDict?.Textures?.data_items ?? []);
                }
                File.WriteAllText(Path.Combine(folder, Path.GetFileName(source) + ".xml"), xml);
            }
            else
            {
                if (groups.Length == 0) throw new InvalidDataException("No embedded texture images found in this asset.");
                foreach (var group in groups)
                {
                    var texture = group.First();
                    if (format == "DDS")
                    {
                        var bytes = DDSIO.GetDDSFile(texture); var reopened = DDSIO.GetTexture(bytes);
                        if (reopened.Levels != texture.Levels || !SHA256.HashData(reopened.Data.FullData).SequenceEqual(SHA256.HashData(texture.Data.FullData)))
                            throw new InvalidDataException("DDS did not preserve texture mipmaps.");
                        File.WriteAllBytes(Path.Combine(folder, texture.Name + ".dds"), bytes);
                    }
                    else if (format == "PNG")
                    {
                        var pixels = DDSIO.GetPixels(texture, 0);
                        var bitmap = BitmapSource.Create(texture.Width, texture.Height, 96, 96, PixelFormats.Bgra32, null, pixels, texture.Width * 4);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using var file = File.Create(Path.Combine(folder, texture.Name + ".png")); encoder.Save(file);
                    }
                    else throw new InvalidDataException("Unknown export format.");
                }
            }
            return folder;
        }
        catch { Directory.Delete(folder, true); throw; }
    }
}
