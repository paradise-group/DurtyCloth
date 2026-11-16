using System.IO;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CodeWalker.GameFiles;
using CodeWalker.Utils;

namespace FooxClothStudio;

public static class TattooExporter
{
    public static readonly string[] Zones = ["ZONE_TORSO", "ZONE_HEAD", "ZONE_LEFT_ARM", "ZONE_RIGHT_ARM", "ZONE_LEFT_LEG", "ZONE_RIGHT_LEG"];
    public static void Validate(TattooItem item)
    {
        if (!Regex.IsMatch(item.Name, "^[a-zA-Z0-9_]{1,48}$")) throw new InvalidDataException("Tattoo name requires 1–48 letters, numbers or underscores.");
        if (!Zones.Contains(item.Zone) || item.Gender is not ("Male" or "Female" or "Both")) throw new InvalidDataException("Select a valid tattoo gender and zone.");
        if (new[] { item.U, item.V, item.ScaleX, item.ScaleY, item.Rotation, item.CameraX, item.CameraY, item.CameraZ, item.CameraHeading }.Any(x => !float.IsFinite(x)) || item.U is < 0 or > 1 || item.V is < 0 or > 1 || item.ScaleX <= 0 || item.ScaleY <= 0 || item.Cost < 0)
            throw new InvalidDataException("Use finite UV coordinates (0–1), positive scales and a nonnegative cost.");
    }
    public static Texture ReadImage(TattooItem item, string path)
    {
        var texture = ReadImageForPreview(item, path);
        if (texture.Width != texture.Height || texture.Width > 4096 || (texture.Width & (texture.Width - 1)) != 0)
            throw new InvalidDataException("Tattoo image must be square, power of two and at most 4096 px.");
        return texture;
    }

    public static Texture ReadImageForPreview(TattooItem item, string path)
    {
        Texture texture;
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".png": texture = PngTextureReader.Read(path); break;
            case ".dds": texture = DDSIO.GetTexture(File.ReadAllBytes(path)) ?? throw new InvalidDataException("Invalid DDS."); break;
            case ".ytd":
                var ytd = new YtdFile(); ytd.Load(File.ReadAllBytes(path));
                var textures = ytd.TextureDict?.Textures?.data_items ?? [];
                texture = string.IsNullOrWhiteSpace(item.SourceTextureName) && textures.Length == 1 ? textures[0]
                    : textures.SingleOrDefault(x => x.Name.Equals(item.SourceTextureName, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidDataException("Select the internal tattoo texture name in the YTD.");
                break;
            default: throw new InvalidDataException("Select a PNG, DDS or YTD tattoo image.");
        }
        if (texture.Width == 0 || texture.Height == 0 || texture.Data?.FullData is not { Length: > 0 })
            throw new InvalidDataException("Tattoo image has no pixel data.");
        return texture;
    }
    public static string[] Write(ClothProject project, Func<string, string> resolve, string output, string resource)
    {
        if (project.Tattoos.Count == 0) return [];
        var presets = new List<XElement>(); var shops = new List<XElement>();
        var info = new List<object>(); var names = new HashSet<uint>();
        var collection = resource + "_overlays";
        var imageFolder = Path.Combine(output, "stream", "tattoos"); Directory.CreateDirectory(imageFolder);
        static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
        static XElement Value(string key, object value) => new(key, new XAttribute("value", value));
        foreach (var item in project.Tattoos)
        {
            Validate(item);
            var stem = resource + "_" + item.Name.ToLowerInvariant();
            if (!names.Add(JenkHash.GenHash(stem))) throw new InvalidDataException("Duplicate tattoo name/hash: " + item.Name);
            var texture = ReadImage(item, resolve(item.ImagePath));
            texture.Name = stem; texture.NameHash = JenkHash.GenHash(stem);
            var ytd = new YtdFile { TextureDict = new TextureDictionary() };
            ytd.TextureDict.BuildFromTextureList([texture]);
            var bytes = ytd.Save(); var reopened = new YtdFile(); reopened.Load(bytes);
            var actual = reopened.TextureDict?.Textures?.data_items?.SingleOrDefault();
            if (actual is null || actual.NameHash != texture.NameHash || actual.Width != texture.Width || actual.Height != texture.Height || actual.Levels != texture.Levels || !SHA256.HashData(actual.Data.FullData).SequenceEqual(SHA256.HashData(texture.Data.FullData)))
                throw new InvalidDataException("Tattoo YTD failed roundtrip validation.");
            File.WriteAllBytes(Path.Combine(imageFolder, stem + ".ytd"), bytes);
            foreach (var gender in item.Gender == "Both" ? new[] { "Male", "Female" } : new[] { item.Gender })
            {
                var overlay = stem + (gender == "Male" ? "_M" : "_F");
                presets.Add(new XElement("Item", new XElement("uvPos", new XAttribute("x", F(item.U)), new XAttribute("y", F(item.V))),
                    new XElement("scale", new XAttribute("x", F(item.ScaleX)), new XAttribute("y", F(item.ScaleY))), Value("rotation", F(item.Rotation)),
                    new XElement("nameHash", overlay), new XElement("txdHash", stem), new XElement("txtHash", stem), new XElement("zone", item.Zone),
                    new XElement("type", "TYPE_TATTOO"), new XElement("faction", "FM"), new XElement("garment", "All"),
                    new XElement("gender", "GENDER_" + gender.ToUpperInvariant()), new XElement("award"), new XElement("awardLevel")));
                shops.Add(new XElement("Item", Value("id", JenkHash.GenHash(overlay.ToLowerInvariant())), Value("cost", item.Cost),
                    new XElement("textLabel", item.Name), new XElement("collection", collection), new XElement("preset", overlay),
                    new XElement("zone", item.Zone.Replace("ZONE_", "PDZ_")), new XElement("updateGroup", "ARM_LEFT_1"),
                    new XElement("eFaction", "TATTOO_MP_FM"), new XElement("eFacing", "TATTOO_FRONT"), new XElement("lockHash")));
                info.Add(new { item.Name, item.Label, Collection = collection, Overlay = overlay, Gender = gender, item.Zone, item.Cost, item.U, item.V, item.ScaleX, item.ScaleY, item.Rotation });
            }
        }
        var overlayFile = collection + ".xml"; var shopFile = resource + "_tattoo.meta";
        new XDocument(new XElement("PedDecorationCollection", Value("bRequiredForSync", "true"), new XElement("presets", presets))).Save(Path.Combine(output, overlayFile));
        new XDocument(new XElement("TattooShopItemArray", new XElement("TattooShopItems", shops))).Save(Path.Combine(output, shopFile));
        if (project.CreateTattoosInfoDump) File.WriteAllText(Path.Combine(output, "tattoos-info.json"), JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));
        if (project.CreateIlleniumConfig)
        {
            var folder = Path.Combine(output, "configs"); Directory.CreateDirectory(folder);
            static string Lua(string text) => "'" + text.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\r", "\\r").Replace("\n", "\\n") + "'";
            var config = new StringBuilder("-- Merge these entries into Config.Tattoos in illenium-appearance.\nreturn {\n");
            foreach (var zone in project.Tattoos.GroupBy(x => x.Zone))
            {
                config.AppendLine("  [" + Lua(zone.Key) + "] = {");
                foreach (var item in zone)
                {
                    var stem = resource + "_" + item.Name.ToLowerInvariant();
                    config.AppendLine("    { name = " + Lua(stem) + ", label = " + Lua(item.Label) + ", hashMale = " + Lua(item.Gender == "Female" ? "" : stem + "_M") + ", hashFemale = " + Lua(item.Gender == "Male" ? "" : stem + "_F") + ", zone = " + Lua(item.Zone) + ", collection = " + Lua(collection) + ", price = " + item.Cost + " },");
                }
                config.AppendLine("  },");
            }
            File.WriteAllText(Path.Combine(folder, "illenium-appearance.lua"), config.AppendLine("}").ToString(), new UTF8Encoding(false));
        }
        if (project.CreateMenyooTattooConfig)
        {
            var folder = Path.Combine(output, "configs"); Directory.CreateDirectory(folder);
            var root = new XElement("PedDecalOverlays");
            foreach (var gender in new[] { "Male", "Female" })
            {
                var pedName = gender == "Male" ? "mp_m_freemode_01" : "mp_f_freemode_01";
                root.Add(new XElement("Ped", new XAttribute("hash", "0x" + JenkHash.GenHash(pedName).ToString("x8")), new XAttribute("name", pedName),
                    new XElement("TYPE_TATTOO", project.Tattoos.Where(item => item.Gender == "Both" || item.Gender == gender).GroupBy(item => item.Zone)
                        .Select(group => new XElement(group.Key, group.Select(item => new XElement("OVERLAY", new XAttribute("collection", collection),
                            new XAttribute("name", resource + "_" + item.Name.ToLowerInvariant() + (gender == "Male" ? "_M" : "_F")), new XAttribute("caption", item.Label))))))));
            }
            new XDocument(root).Save(Path.Combine(folder, "PedDecalOverlays.xml"));
        }
        if (project.CreateEsxTattooConfig)
        {
            var folder = Path.Combine(output, "configs"); Directory.CreateDirectory(folder);
            static string Lua(string text) => "'" + text.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\r", "\\r").Replace("\n", "\\n") + "'";
            var config = new StringBuilder("-- Merge this collection into Config.TattooList (esx-community/esx_tattooshop).\nreturn {\n  [" + Lua(collection) + "] = {\n");
            foreach (var item in project.Tattoos)
                foreach (var gender in item.Gender == "Both" ? new[] { "Male", "Female" } : new[] { item.Gender })
                    config.AppendLine("    { name = " + Lua(item.Label) + ", nameHash = " + Lua(resource + "_" + item.Name.ToLowerInvariant() + (gender == "Male" ? "_M" : "_F")) + ", price = " + item.Cost + ", addedX = " + F(item.CameraX) + ", addedY = " + F(item.CameraY) + ", addedZ = " + F(item.CameraZ) + ", rotZ = " + F(item.CameraHeading) + " },");
            File.WriteAllText(Path.Combine(folder, "esx-tattooshop.lua"), config.AppendLine("  }\n}").ToString(), new UTF8Encoding(false));
        }
        return [overlayFile, shopFile];
    }
}
