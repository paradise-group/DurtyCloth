using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using System.Globalization;
using System.Text.RegularExpressions;
using CodeWalker.GameFiles;
using CodeWalker.Utils;

namespace FooxClothStudio;

public sealed class GtaPhotoCatalog
{
    private sealed record Source(string Archive, RpfFileEntry Entry);
    private readonly Dictionary<string, Source> _sources = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, XDocument> _decorationCollections = new(StringComparer.OrdinalIgnoreCase);
    private XDocument? _facialOverlays;
    public static readonly Dictionary<string, string> AppearanceKinds = new()
    {
        ["blemishes"] = "Manchas", ["facialHair"] = "Barba", ["eyebrow"] = "Sobrancelhas",
        ["aging"] = "Envelhecimento", ["makeup"] = "Maquilhagem", ["blusher"] = "Blush",
        ["damage"] = "Imperfeições", ["baseDetail"] = "Danos do sol", ["skinDetail1"] = "Lábios",
        ["skinDetail2"] = "Sardas", ["bodyOverlay1"] = "Pelos corporais", ["bodyOverlay2"] = "Manchas corporais",
        ["bodyOverlay3"] = "Manchas corporais adicionais"
    };
    private readonly string _cache;
    public ClothProject Project { get; } = new() { Name = "Installed GTA V photos" };
    public List<string> Warnings { get; } = [];

    private GtaPhotoCatalog(string cache) => _cache = Path.GetFullPath(cache);

    public static GtaPhotoCatalog Scan(string gameFolder, string? cacheFolder = null,
        IProgress<(int Done, int Total, string Stage)>? progress = null)
    {
        var root = Path.GetFullPath(gameFolder);
        if (!File.Exists(Path.Combine(root, "x64v.rpf")))
            throw new InvalidDataException("A pasta selecionada não contém x64v.rpf. Selecione a pasta do GTA V.");
        var catalog = new GtaPhotoCatalog(cacheFolder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ParadiseClothStudio", "gta-photo-cache"));
        GTA5Keys.LoadFromPath(root);
        var archives = Directory.EnumerateFiles(root, "*.rpf", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar)
                .Any(part => part.Equals("mods", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(ArchivePriority).ThenBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        for (var index = 0; index < archives.Length; index++)
        {
            progress?.Report((index, archives.Length, $"A ler roupas do GTA V: {Path.GetFileName(Path.GetDirectoryName(archives[index]))}"));
            catalog.ReadArchive(archives[index]);
        }
        catalog.BuildProject();
        if (catalog.Project.Items.Count == 0)
            throw new InvalidDataException("Não foram encontrados modelos de roupas freemode nos arquivos do GTA V.");
        progress?.Report((archives.Length, archives.Length, $"{catalog.Project.Items.Count} peças encontradas; {catalog.Warnings.Count} avisos."));
        return catalog;
    }

    private static int ArchivePriority(string path)
    {
        if (Path.GetFileName(path).Equals("update2.rpf", StringComparison.OrdinalIgnoreCase)) return 20002;
        if (Path.GetFileName(path).Equals("update.rpf", StringComparison.OrdinalIgnoreCase)) return 20001;
        var patch = Regex.Match(path, @"patchday(\d+)", RegexOptions.IgnoreCase);
        if (patch.Success) return 10000 + int.Parse(patch.Groups[1].Value);
        return path.Contains("dlcpacks", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
    }

    private static IEnumerable<RpfFile> Archives(RpfFile archive) =>
        new[] { archive }.Concat((archive.Children ?? []).SelectMany(Archives));

    private void ReadArchive(string path)
    {
        try
        {
            var archive = new RpfFile(path, Path.GetFileName(path));
            archive.ScanStructure(null, message => Warnings.Add(Path.GetFileName(path) + ": " + message));
            foreach (var entry in Archives(archive).SelectMany(child => child.AllEntries ?? []).OfType<RpfFileEntry>())
            {
                if (entry.Name.EndsWith("_overlays.xml", StringComparison.OrdinalIgnoreCase))
                {
                    using var stream = new MemoryStream(entry.File.ExtractFile(entry));
                    var document = XDocument.Load(stream);
                    if (document.Root?.Name.LocalName == "PedDecorationCollection") _decorationCollections[entry.Name] = document;
                }
                if (entry.Name.Equals("facial_overlays.meta", StringComparison.OrdinalIgnoreCase))
                {
                    using var stream = new MemoryStream(entry.File.ExtractFile(entry));
                    _facialOverlays = XDocument.Load(stream);
                }
                if (!entry.Name.EndsWith(".ydd", StringComparison.OrdinalIgnoreCase) && !entry.Name.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase)) continue;
                if (entry.Name.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase) && !entry.Name.Contains('^'))
                    _sources[entry.Name] = new Source(path, entry);
                if (entry.Name.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase) && (entry.Name.Contains("faov", StringComparison.OrdinalIgnoreCase)
                    || entry.Name.StartsWith("mp_fm_body_", StringComparison.OrdinalIgnoreCase)))
                    _sources[entry.Name] = new Source(path, entry);
                var name = entry.Name.Contains('^') ? entry.Name : entry.Parent.Name + "^" + entry.Name;
                if (!name.StartsWith("mp_m_freemode_01", StringComparison.OrdinalIgnoreCase)
                    && !name.StartsWith("mp_f_freemode_01", StringComparison.OrdinalIgnoreCase)) continue;
                _sources[name] = new Source(path, entry);
            }
        }
        catch (Exception error)
        {
            Warnings.Add(Path.GetFileName(path) + ": " + error.Message);
        }
    }

    private void BuildProject()
    {
        var supported = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "berd", "hair", "uppr", "lowr", "hand", "feet", "teef", "accs", "task", "decl", "jbib",
            "p_head", "p_eyes", "p_ears", "p_lwrist", "p_rwrist"
        };
        var textures = _sources.Keys.Where(name => name.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase))
            .GroupBy(TextureGroup, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.OrdinalIgnoreCase);
        foreach (var name in _sources.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            var match = Regex.Match(name, @"\^(p_[a-z]+|[a-z]{4})_(\d+)(?:_[ur])?\.ydd$", RegexOptions.IgnoreCase);
            if (!match.Success || !supported.Contains(match.Groups[1].Value)) continue;
            var gender = ClothCatalog.ResolveGender(name);
            var item = new ClothItem
            {
                Id = ClothingIdentityMap.SourceId(name, gender), Name = Path.GetFileNameWithoutExtension(name),
                Gender = gender, Category = ClothCatalog.ResolveType(name).Category, SourceIndex = int.Parse(match.Groups[2].Value),
                ModelPath = name, OriginalModelFileName = name
            };
            item.TexturePaths = textures.GetValueOrDefault(TextureGroup(name)) ?? [];
            item.OriginalTextureFileNames = item.TexturePaths.ToList();
            ClothingIdentityMap.EnsureTextures(item);
            Project.Items.Add(item);
        }
        foreach (var group in _facialOverlays?.Root?.Elements() ?? [])
        {
            if (!AppearanceKinds.TryGetValue(group.Name.LocalName, out var kind)) continue;
            foreach (var (definition, index) in group.Elements().Select((definition, index) => (definition, index)))
            {
                var dictionary = definition.Value.Trim();
                if (!_sources.ContainsKey(dictionary + ".ytd")) continue;
                Project.AppearanceAssets.Add(new AppearanceAsset
                {
                    Id = ClothingIdentityMap.SourceId(dictionary, "Both"), Name = dictionary, Gender = "Both", Kind = kind,
                    TexturePath = dictionary + ".ytd", OverlayCategory = AppearanceCategory(group.Name.LocalName), OverlayIndex = index
                });
            }
        }
        foreach (var (collection, document) in _decorationCollections)
            foreach (var definition in document.Root?.Element("presets")?.Elements("Item") ?? [])
            {
                if (definition.Element("type")?.Value != "TYPE_TATTOO") continue;
                if (definition.Element("faction")?.Value is string faction && !faction.Equals("FM", StringComparison.OrdinalIgnoreCase)) continue;
                if (!TattooExporter.Zones.Contains(definition.Element("zone")?.Value)) continue;
                var dictionary = definition.Element("txdHash")?.Value ?? "";
                if (!_sources.ContainsKey(dictionary + ".ytd")) continue;
                var overlay = definition.Element("nameHash")?.Value ?? "";
                var gender = definition.Element("gender")?.Value switch
                {
                    "GENDER_MALE" => "Male", "GENDER_FEMALE" => "Female", "GENDER_DONTCARE" => "Both", _ => ""
                };
                if (gender.Length == 0) continue;
                var tattoo = ReadTattoo(definition, new TattooItem
                {
                    Id = ClothingIdentityMap.SourceId(collection + "|" + overlay, gender), Name = overlay,
                    ImagePath = dictionary + ".ytd", SourceTextureName = definition.Element("txtHash")?.Value ?? "",
                    Collection = Path.GetFileNameWithoutExtension(collection), Overlay = overlay
                }, gender);
                Project.Tattoos.Add(tattoo);
            }
    }

    public static string AppearanceCategory(string group) => group switch
    {
        "eyebrow" => "eyebrows", "aging" => "ageing", "blusher" => "blush", "damage" => "complexion",
        "baseDetail" => "sunDamage", "skinDetail1" => "lipstick", "skinDetail2" => "freckles",
        "bodyOverlay1" => "chestHair", "bodyOverlay2" => "bodyBlemishes", "bodyOverlay3" => "addBodyBlemishes", _ => group
    };

    public TattooItem? ResolveTattoo(TattooItem source, string gender, Func<string, string>? resolveAsset = null)
    {
        var dictionary = Path.GetFileNameWithoutExtension(source.ImagePath);
        var hasDictionaryName = !Regex.IsMatch(dictionary, @"^tattoo(?:_\d+)?$", RegexOptions.IgnoreCase);
        if (!hasDictionaryName && source.Label.Contains(" / ", StringComparison.Ordinal))
        {
            dictionary = source.Label.Split(" / ", StringSplitOptions.None)[0];
            hasDictionaryName = true;
        }
        var hashes = new HashSet<uint> { JenkHash.GenHash(source.SourceTextureName.ToLowerInvariant()) };
        if (resolveAsset is not null)
        {
            try
            {
                var texture = new YtdFile(); texture.Load(File.ReadAllBytes(resolveAsset(source.ImagePath)));
                var entries = texture.TextureDict?.Textures?.data_items ?? [];
                var keys = texture.TextureDict?.TextureNameHashes?.data_items ?? [];
                for (var index = 0; index < Math.Min(entries.Length, keys.Length); index++)
                    if (string.Equals(entries[index].Name, source.SourceTextureName, StringComparison.OrdinalIgnoreCase)) hashes.Add(keys[index]);
            }
            catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException)
            {
                Warnings.Add(source.Name + ": " + error.Message);
            }
        }
        var expected = gender == "Feminino" ? "GENDER_FEMALE" : "GENDER_MALE";
        var matches = _decorationCollections.Values.SelectMany(document => document.Root?.Element("presets")?.Elements("Item") ?? [])
            .Where(entry => (!hasDictionaryName || string.Equals(entry.Element("txdHash")?.Value, dictionary, StringComparison.OrdinalIgnoreCase))
                && hashes.Contains(JenkHash.GenHash((entry.Element("txtHash")?.Value ?? "").ToLowerInvariant()))
                && (entry.Element("gender")?.Value == expected || entry.Element("gender")?.Value == "GENDER_DONTCARE"))
            .ToArray();
        if (matches.Length != 1) return null;
        var resolved = ReadTattoo(matches[0], source, gender == "Feminino" ? "Female" : "Male");
        resolved.Collection = Path.GetFileNameWithoutExtension(_decorationCollections.First(pair =>
            pair.Value.Root?.Element("presets")?.Elements("Item").Contains(matches[0]) == true).Key);
        return resolved;
    }

    private static TattooItem ReadTattoo(XElement definition, TattooItem source, string gender)
    {
        float Value(string name, string attribute) => float.Parse(definition.Element(name)?.Attribute(attribute)?.Value
            ?? throw new InvalidDataException("Metadados de tatuagem incompletos."), CultureInfo.InvariantCulture);
        return new TattooItem
        {
            Id = source.Id, Name = source.Name, Label = source.Label, ImagePath = source.ImagePath,
            SourceTextureName = source.SourceTextureName, Gender = gender, Zone = definition.Element("zone")?.Value ?? "",
            Collection = source.Collection, Overlay = source.Overlay ?? definition.Element("nameHash")?.Value,
            UseTintColour = string.Equals(definition.Element("useTintColour")?.Attribute("value")?.Value, "true", StringComparison.OrdinalIgnoreCase),
            U = Value("uvPos", "x"), V = Value("uvPos", "y"), ScaleX = Value("scale", "x"),
            ScaleY = Value("scale", "y"), Rotation = Value("rotation", "value"), Cost = source.Cost
        };
    }

    private static string TextureGroup(string name) =>
        Regex.Replace(name, @"\^((?:p_[a-z]+|[a-z]{4}))_(?:diff_)?(\d+).*\.(?:ytd|ydd)$", "^$1_$2", RegexOptions.IgnoreCase);

    public string Resolve(string name)
    {
        if (!_sources.TryGetValue(name, out var source)) throw new FileNotFoundException("Asset do GTA não encontrado", name);
        var archive = new FileInfo(source.Archive);
        var stamp = source.Archive + "|" + archive.Length + "|" + archive.LastWriteTimeUtc.Ticks + "|" + source.Entry.Path;
        var version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stamp)))[..24];
        var folder = Path.Combine(_cache, version);
        var target = Path.Combine(folder, Path.GetFileName(name));
        if (File.Exists(target)) return target;
        Directory.CreateDirectory(folder);
        var bytes = source.Entry.File.ExtractFile(source.Entry) ?? throw new InvalidDataException("Não foi possível ler " + name);
        if (name.EndsWith(".ydd", StringComparison.OrdinalIgnoreCase))
        {
            var model = new YddFile();
            model.Load(bytes, source.Entry);
            bytes = model.Save();
        }
        else
        {
            var texture = new YtdFile();
            texture.Load(bytes, source.Entry);
            bytes = texture.Save();
        }
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, target, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return target;
    }

    public void WriteIdentityMap(string folder)
    {
        var map = OriginalResourceExporter.Describe(Project);
        var target = Path.Combine(folder, "gta-clothing-identities.json");
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(map, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true
            }), new UTF8Encoding(false));
            File.Move(temporary, target, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
