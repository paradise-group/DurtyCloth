using System.IO;
using System.Globalization;
using System.Xml.Linq;
using System.Text.RegularExpressions;
using CodeWalker.GameFiles;

namespace FooxClothStudio;

public static class TattooImporter
{
    public static bool LooksLikeTattooYtd(string path) =>
        Regex.IsMatch(Path.GetFileName(path), @"(?:tattoo|(?:^|_)tat(?:_|\d|\.))", RegexOptions.IgnoreCase);

    public static List<TattooItem> Import(string resourceRoot)
    {
        resourceRoot = Path.GetFullPath(resourceRoot);
        var images = Directory.EnumerateFiles(resourceRoot, "*.ytd", SearchOption.AllDirectories).GroupBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key!, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var results = new List<TattooItem>();
        var shopEntries = Directory.EnumerateFiles(resourceRoot, "*tattoo*.meta", SearchOption.AllDirectories)
            .SelectMany(path => XDocument.Load(path).Descendants("TattooShopItems").Elements("Item")).ToArray();
        foreach (var path in Directory.EnumerateFiles(resourceRoot, "*_overlays.xml", SearchOption.AllDirectories))
        {
            var root = XDocument.Load(path).Root;
            if (root?.Name.LocalName != "PedDecorationCollection") throw new InvalidDataException("Invalid PedDecorationCollection: " + path);
            foreach (var entry in root.Element("presets")?.Elements("Item") ?? [])
            {
                if (entry.Element("type")?.Value != "TYPE_TATTOO") continue;
                var dictionary = entry.Element("txdHash")?.Value ?? "";
                if (!images.TryGetValue(dictionary, out var matches) || matches.Length != 1) throw new InvalidDataException("Missing or ambiguous tattoo YTD: " + dictionary);
                float Float(XElement? node, string attribute) => float.Parse(node?.Attribute(attribute)?.Value ?? throw new InvalidDataException("Missing tattoo placement value."), CultureInfo.InvariantCulture);
                var item = new TattooItem { Name = entry.Element("nameHash")?.Value ?? "", Label = entry.Element("nameHash")?.Value ?? "",
                    ImagePath = matches[0], SourceTextureName = entry.Element("txtHash")?.Value ?? "", Zone = entry.Element("zone")?.Value ?? "",
                    Gender = entry.Element("gender")?.Value switch { "GENDER_MALE" => "Male", "GENDER_FEMALE" => "Female", "GENDER_DONTCARE" => "Both", _ => throw new InvalidDataException("Unknown tattoo gender.") },
                    U = Float(entry.Element("uvPos"), "x"), V = Float(entry.Element("uvPos"), "y"), ScaleX = Float(entry.Element("scale"), "x"), ScaleY = Float(entry.Element("scale"), "y"), Rotation = Float(entry.Element("rotation"), "value") };
                TattooExporter.Validate(item); TattooExporter.ReadImage(item, item.ImagePath); results.Add(item);
                var shop = shopEntries.FirstOrDefault(x => x.Element("collection")?.Value == Path.GetFileNameWithoutExtension(path) && x.Element("preset")?.Value == entry.Element("nameHash")?.Value);
                if (shop is not null && int.TryParse(shop.Element("cost")?.Attribute("value")?.Value, out var cost) && cost >= 0) item.Cost = cost;
            }
        }
        // Some resources ship only tattoo YTDs. Keep their textures visible in the
        // editor, but leave the zone unset because the YTD does not encode placement.
        var known = results.Select(item => (Path.GetFullPath(item.ImagePath), item.SourceTextureName))
            .ToHashSet();
        var usedNames = results.Select(item => item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(resourceRoot, "*.ytd", SearchOption.AllDirectories)
            .Where(LooksLikeTattooYtd))
        {
            var ytd = new YtdFile();
            try { ytd.Load(File.ReadAllBytes(path)); }
            catch (Exception) { continue; }
            foreach (var texture in ytd.TextureDict?.Textures?.data_items ?? [])
            {
                if (texture?.Name is null || known.Contains((Path.GetFullPath(path), texture.Name))) continue;
                var stem = Regex.Replace(Path.GetFileNameWithoutExtension(path) + "_" + texture.Name,
                    "[^a-zA-Z0-9_]", "_");
                stem = stem.Length > 42 ? stem[..42] : stem;
                var name = stem; var suffix = 1;
                while (!usedNames.Add(name)) name = stem[..Math.Min(stem.Length, 42)] + "_" + suffix++;
                results.Add(new TattooItem
                {
                    Name = name, Label = Path.GetFileNameWithoutExtension(path) + " / " + texture.Name,
                    ImagePath = path, SourceTextureName = texture.Name,
                    Zone = "", Gender = "Both"
                });
            }
        }
        if (results.Count == 0) throw new InvalidDataException("No tattoo overlays or tattoo textures found in this resource.");
        return results;
    }
}
