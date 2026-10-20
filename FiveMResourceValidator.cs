using System.IO;
using System.Xml.Linq;
using System.Text.RegularExpressions;
using CodeWalker.GameFiles;

namespace FooxClothStudio;

public static class FiveMResourceValidator
{
    private static readonly string[] Slots =
        ["HEAD", "BERD", "HAIR", "UPPR", "LOWR", "HAND", "FEET", "TEEF", "ACCS", "TASK", "DECL", "JBIB"];
    private static readonly string[] Anchors =
        ["ANCHOR_HEAD", "ANCHOR_EYES", "ANCHOR_EARS", "ANCHOR_MOUTH",
            "ANCHOR_LEFT_WRIST", "ANCHOR_RIGHT_WRIST"];
    private static readonly string[] PropCodes =
        ["p_head", "p_eyes", "p_ears", "p_mouth", "p_lwrist", "p_rwrist"];
    private static readonly int[] AnchorIds = [0, 1, 2, 3, 6, 7];

    public static List<string> Validate(string resourceRoot)
    {
        var errors = new List<string>();
        var stream = Path.Combine(resourceRoot, "stream");
        if (!Directory.Exists(stream) || !File.Exists(Path.Combine(resourceRoot, "fxmanifest.lua")))
            return ["A pasta não contém stream/ e fxmanifest.lua."];
        var manifest = File.ReadAllText(Path.Combine(resourceRoot, "fxmanifest.lua"));
        foreach (Match declaration in Regex.Matches(manifest, @"data_file\s+['""](PED_OVERLAY_FILE|TATTOO_SHOP_DLC_FILE)['""]\s+['""]([^'""]+)['""]", RegexOptions.IgnoreCase))
            if (!File.Exists(Path.Combine(resourceRoot, declaration.Groups[2].Value)))
                errors.Add("Metadata de tatuagem declarado ausente: " + declaration.Groups[2].Value);
        foreach (var (suffix, type) in new[] {
            ("_first_person.meta", "PED_FIRST_PERSON_ASSET_DATA"),
            ("_first_person_alternates.meta", "PED_FIRST_PERSON_ALTERNATE_DATA") })
        {
            var declarations = Regex.Matches(manifest,
                @"data_file\s+['"" ]*" + type + @"['""]\s+['""]([^'""]+)['""]",
                RegexOptions.IgnoreCase).Select(match => match.Groups[1].Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var path in Directory.EnumerateFiles(resourceRoot, "*" + suffix))
                if (!declarations.Contains(Path.GetFileName(path)))
                    errors.Add("Registro " + type + " ausente: " + Path.GetFileName(path));
            foreach (var path in declarations)
                if (!File.Exists(Path.Combine(resourceRoot, path)))
                    errors.Add("Metadata declarado ausente: " + path);
        }

        foreach (var shopPath in Directory.EnumerateFiles(resourceRoot, "*_shop.meta"))
        {
            if (!manifest.Contains(Path.GetFileName(shopPath), StringComparison.OrdinalIgnoreCase))
                errors.Add("Shop meta ausente do manifesto: " + Path.GetFileName(shopPath));
            try { ValidateShop(shopPath, stream, errors); }
            catch (Exception ex) { errors.Add(Path.GetFileName(shopPath) + ": " + ex.Message); }
        }
        if (!Directory.EnumerateFiles(resourceRoot, "*_shop.meta").Any() && !Directory.EnumerateFiles(resourceRoot, "*_overlays.xml").Any())
            errors.Add("Nenhum arquivo *_shop.meta encontrado.");
        foreach (var overlayPath in Directory.EnumerateFiles(resourceRoot, "*_overlays.xml"))
        {
            try
            {
                if (!manifest.Contains("data_file 'PED_OVERLAY_FILE' '" + Path.GetFileName(overlayPath) + "'", StringComparison.OrdinalIgnoreCase))
                    errors.Add("Overlay de tatuagem ausente do manifesto: " + Path.GetFileName(overlayPath));
                var overlay = XDocument.Load(overlayPath).Root;
                if (overlay?.Name.LocalName != "PedDecorationCollection") throw new InvalidDataException("Raiz PedDecorationCollection ausente.");
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in overlay.Element("presets")?.Elements("Item") ?? [])
                {
                    var name = entry.Element("nameHash")?.Value ?? "";
                    if (name.Length == 0 || !names.Add(name)) errors.Add("Nome de tatuagem ausente/duplicado: " + name);
                    var dictionary = entry.Element("txdHash")?.Value ?? "";
                    if (Path.GetFileName(dictionary) != dictionary || dictionary.Length == 0) throw new InvalidDataException("txdHash inválido.");
                    var ytdPath = Directory.EnumerateFiles(stream, dictionary + ".ytd", SearchOption.AllDirectories).FirstOrDefault();
                    if (ytdPath is null) { errors.Add("YTD de tatuagem ausente: " + dictionary); continue; }
                    var ytd = new YtdFile(); ytd.Load(File.ReadAllBytes(ytdPath));
                    if (!(ytd.TextureDict?.Textures?.data_items?.Any(x => x.Name.Equals(entry.Element("txtHash")?.Value, StringComparison.OrdinalIgnoreCase)) ?? false))
                        errors.Add("Textura interna de tatuagem ausente: " + entry.Element("txtHash")?.Value);
                    if (!TattooExporter.Zones.Contains(entry.Element("zone")?.Value ?? "")) errors.Add("Zona de tatuagem inválida: " + name);
                }
                foreach (var shopPath in Directory.EnumerateFiles(resourceRoot, "*_tattoo.meta"))
                {
                    var shop = XDocument.Load(shopPath).Root;
                    if (shop?.Name.LocalName != "TattooShopItemArray") throw new InvalidDataException("Raiz TattooShopItemArray ausente.");
                    if (!manifest.Contains("data_file 'TATTOO_SHOP_DLC_FILE' '" + Path.GetFileName(shopPath) + "'", StringComparison.OrdinalIgnoreCase))
                        errors.Add("Shop tattoo ausente do manifesto: " + Path.GetFileName(shopPath));
                    foreach (var entry in shop.Descendants("TattooShopItems").Elements("Item").Where(x => x.Element("collection")?.Value == Path.GetFileNameWithoutExtension(overlayPath)))
                        if (!names.Contains(entry.Element("preset")?.Value ?? "")) errors.Add("Preset de tatuagem ausente: " + entry.Element("preset")?.Value);
                }
            }
            catch (Exception ex) { errors.Add(Path.GetFileName(overlayPath) + ": " + ex.Message); }
        }
        var alternate = Path.Combine(resourceRoot, "pedalternatevariations.meta");
        if (File.Exists(alternate))
        {
            if (!manifest.Contains("ALTERNATE_VARIATIONS_FILE", StringComparison.OrdinalIgnoreCase) ||
                !manifest.Contains("pedalternatevariations.meta", StringComparison.OrdinalIgnoreCase))
                errors.Add("pedalternatevariations.meta ausente das declarações do manifesto.");
            try { XDocument.Load(alternate); }
            catch (Exception ex) { errors.Add("pedalternatevariations.meta: " + ex.Message); }
        }
        return errors;
    }

    private static void ValidateShop(string shopPath, string stream, List<string> errors)
    {
        var shop = XDocument.Load(shopPath).Root;
        if (shop?.Name.LocalName != "ShopPedApparel")
            throw new InvalidDataException("Raiz ShopPedApparel ausente.");
        var fullName = shop.Element("fullDlcName")?.Value;
        if (string.IsNullOrWhiteSpace(fullName) || Path.GetFileName(fullName) != fullName)
            throw new InvalidDataException("fullDlcName inválido.");
        var ymtPath = Path.Combine(stream, fullName + ".ymt");
        if (!File.Exists(ymtPath)) throw new FileNotFoundException("YMT ausente", ymtPath);
        var ymt = new YmtFile();
        ymt.Load(File.ReadAllBytes(ymtPath));
        if (ymt.Meta is null) throw new InvalidDataException("YMT não pôde ser lido.");
        var root = XDocument.Parse(MetaXml.GetXml(ymt.Meta)).Root!;
        if (root.Name.LocalName != "CPedVariationInfo")
            throw new InvalidDataException("YMT não contém CPedVariationInfo.");
        var creatureName = shop.Element("creatureMetaData")?.Value;
        if (!string.IsNullOrWhiteSpace(creatureName))
        {
            if (Path.GetFileName(creatureName) != creatureName)
                throw new InvalidDataException("creatureMetaData inválido.");
            var creaturePath = Path.Combine(stream, creatureName + ".ymt");
            if (!File.Exists(creaturePath)) errors.Add("Creature metadata ausente: " + creatureName + ".ymt");
            else
            {
                try
                {
                    var creature = new YmtFile(); creature.Load(File.ReadAllBytes(creaturePath));
                    if (creature.Meta is null || XDocument.Parse(MetaXml.GetXml(creature.Meta)).Root?.Name.LocalName != "CCreatureMetaData")
                        throw new InvalidDataException("Raiz CCreatureMetaData ausente.");
                }
                catch (Exception ex) { errors.Add("Creature metadata inválido: " + creatureName + " (" + ex.Message + ")"); }
            }
        }
        var availability = root.Element("availComp")?.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(int.Parse).ToArray() ?? [];
        var groups = root.Element("aComponentData3")?.Elements("Item").ToArray() ?? [];
        var props = root.Element("propInfo")?.Element("aPropMetaData")?.Elements("Item").ToArray() ?? [];
        var fileNames = Directory.EnumerateFiles(stream, "*", SearchOption.AllDirectories)
            .Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var validatedYlds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var textureNames = fileNames.Where(name => name is not null &&
                name.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase))
            .Select(name => Regex.Replace(name!, @"_[a-z]{3}(?=\.ytd$)", "",
                RegexOptions.IgnoreCase)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var slot = 0; slot < availability.Length && slot < Slots.Length; slot++)
        {
            if (availability[slot] == 255 || availability[slot] >= groups.Length) continue;
            var group = groups[availability[slot]];
            var drawables = group.Element("aDrawblData3")?.Elements("Item").ToArray() ?? [];
            var actualTextures = drawables.Sum(drawable =>
                drawable.Element("aTexData")?.Elements("Item").Count() ?? 0);
            if (Value(group, "numAvailTex") != (actualTextures & 0xFF))
                errors.Add(Path.GetFileName(ymtPath) + ": contador de texturas divergente em " + Slots[slot]);
            for (var drawable = 0; drawable < drawables.Length; drawable++)
            {
                var declaredTextures = drawables[drawable].Element("aTexData")?.Elements("Item").ToArray() ?? [];
                if (declaredTextures.Length == 0) continue; // Reserved drawable slot.
                var stem = fullName + "^" + Slots[slot].ToLowerInvariant() + "_" + drawable.ToString("D3");
                var suffix = fileNames.Contains(stem + "_u.ydd") ? "_u" :
                    fileNames.Contains(stem + "_r.ydd") ? "_r" : null;
                if (suffix is null)
                    errors.Add("YDD ausente: " + stem + "_u.ydd ou _r.ydd");
                var alternateCount = Value(drawables[drawable], "numAlternatives");
                for (var alternate = 1; alternate <= alternateCount && suffix is not null; alternate++)
                    if (!fileNames.Contains(stem + suffix + "_" + alternate + ".ydd"))
                        errors.Add("YDD alternativo ausente: " + stem + suffix + "_" + alternate + ".ydd");
                var textureCount = drawables[drawable].Element("aTexData")?.Elements("Item").Count() ?? 0;
                var ownsCloth = bool.TryParse(drawables[drawable].Element("clothData")?
                    .Element("ownsCloth")?.Attribute("value")?.Value, out var cloth) && cloth;
                if (ownsCloth)
                {
                    var yldName = stem + ".yld";
                    var yldPath = Directory.EnumerateFiles(stream, yldName, SearchOption.AllDirectories)
                        .FirstOrDefault();
                    if (yldPath is null)
                        errors.Add("YLD ausente para drawable com ownsCloth=true: " + yldName);
                    else if (validatedYlds.Add(yldPath))
                    {
                        try { YldInspector.CountClothes(yldPath); }
                        catch (Exception ex)
                        {
                            errors.Add("YLD inválido para drawable com ownsCloth=true: " + yldName +
                                " (" + ex.Message + ")");
                        }
                    }
                }
                for (var texture = 0; texture < textureCount && texture < 26; texture++)
                {
                    if (Value(declaredTextures[texture], "distribution") == 0) continue;
                    var expected = fullName + "^" + Slots[slot].ToLowerInvariant() + "_diff_" +
                        drawable.ToString("D3") + "_" + (char)('a' + texture) + ".ytd";
                    if (!textureNames.Contains(expected)) errors.Add("YTD ausente: " + expected);
                }
            }
        }
        var propCollection = shop.Element("pedName")?.Value + "_p_" + shop.Element("dlcName")?.Value;
        foreach (var prop in props)
        {
            var declaredTextures = prop.Element("texData")?.Elements("Item").ToArray() ?? [];
            if (declaredTextures.Length == 0) continue;
            var anchor = Value(prop, "anchorId");
            var anchorOrdinal = Array.IndexOf(AnchorIds, anchor);
            var index = Value(prop, "propId");
            if (anchorOrdinal < 0 || index < 0) continue;
            var stem = propCollection + "^" + PropCodes[anchorOrdinal] +
                "_" + index.ToString("D3");
            if (!fileNames.Contains(stem + ".ydd")) errors.Add("YDD prop ausente: " + stem + ".ydd");
            var textureCount = prop.Element("texData")?.Elements("Item").Count() ?? 0;
            for (var texture = 0; texture < textureCount && texture < 26; texture++)
            {
                if (Value(declaredTextures[texture], "distribution") == 0) continue;
                var expected = propCollection + "^" + PropCodes[anchorOrdinal] + "_diff_" +
                    index.ToString("D3") + "_" + (char)('a' + texture) + ".ytd";
                if (!textureNames.Contains(expected)) errors.Add("YTD prop ausente: " + expected);
            }
        }
        var propMap = props.ToDictionary(x => (Value(x, "anchorId"), Value(x, "propId")));
        var firstPersonPath = Path.Combine(Path.GetDirectoryName(shopPath)!, fullName + "_first_person.meta");
        if (File.Exists(firstPersonPath))
        {
            var manager = XDocument.Load(firstPersonPath).Root;
            if (manager?.Name.LocalName != "FirstPersonAssetDataManager") errors.Add("Invalid first person metadata root.");
            foreach (var obj in manager?.Descendants("objects").Elements("Item") ?? [])
            {
                var index = Value(obj, "localIndex");
                var component = Array.IndexOf(Slots, obj.Element("eCompType")?.Value.Replace("PV_COMP_", ""));
                var anchor = Array.IndexOf(Anchors, obj.Element("eAnchorPoint")?.Value);
                XElement? entry = null;
                if (component >= 0 && component < availability.Length && availability[component] < groups.Length)
                    entry = groups[availability[component]].Element("aDrawblData3")?.Elements("Item").ElementAtOrDefault(index);
                else if (anchor >= 0) propMap.TryGetValue((AnchorIds[anchor], index), out entry);
                var textures = entry?.Element(component >= 0 ? "aTexData" : "texData")?.Elements("Item").Count() ?? 0;
                if (entry is null || obj.Element("dlcHash")?.Value != shop.Element("dlcName")?.Value)
                    errors.Add("First person metadata references an invalid drawable or collection.");
                foreach (var effect in obj.Element("timeCycleMods")?.Elements("Item") ?? [])
                {
                    var texture = Value(effect, "textureId");
                    if (texture < 0 || texture >= textures) errors.Add("First person TimeCycle texture index is invalid.");
                    if (!float.TryParse(effect.Element("intensity")?.Attribute("value")?.Value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var intensity) || !float.IsFinite(intensity) || intensity is < 0.1f or > 1)
                        errors.Add("First person TimeCycle intensity is invalid.");
                }
            }
        }
        var alternatePath = Path.Combine(Path.GetDirectoryName(shopPath)!, fullName + "_first_person_alternates.meta");
        if (File.Exists(alternatePath))
            foreach (var alternateItem in XDocument.Load(alternatePath).Descendants("alternates").Elements("Item"))
            {
                var stem = alternateItem.Element("assetName")?.Value;
                var alternate = Value(alternateItem, "alternate");
                if (string.IsNullOrWhiteSpace(stem) || alternate < 1 || !fileNames.Contains(stem + "_" + alternate + ".ydd"))
                    errors.Add("First person alternate YDD is missing: " + stem);
            }
        var components = shop.Element("pedComponents")?.Elements("Item") ?? [];
        foreach (var item in components)
        {
            var code = item.Element("eCompType")?.Value.Replace("PV_COMP_", "", StringComparison.Ordinal);
            var slot = Array.IndexOf(Slots, code);
            var drawable = Value(item, "localDrawableIndex");
            var texture = Value(item, "textureIndex");
            if (slot < 0 || slot >= availability.Length || availability[slot] == 255 ||
                availability[slot] >= groups.Length)
            {
                errors.Add(Path.GetFileName(shopPath) + ": componente inválido " + code);
                continue;
            }
            var drawables = groups[availability[slot]].Element("aDrawblData3")?.Elements("Item").ToArray() ?? [];
            if (drawable < 0 || drawable >= drawables.Length || texture < 0 ||
                texture >= (drawables[drawable].Element("aTexData")?.Elements("Item").Count() ?? 0))
                errors.Add(Path.GetFileName(shopPath) + $": índice inválido {code}/{drawable}/{texture}");
        }
        var shopProps = shop.Element("pedProps")?.Elements("Item") ?? [];
        foreach (var item in shopProps)
        {
            var anchor = Array.IndexOf(Anchors, item.Element("eAnchorPoint")?.Value);
            if (anchor >= 0) anchor = AnchorIds[anchor];
            var prop = Value(item, "localPropIndex");
            var texture = Value(item, "textureIndex");
            if (anchor < 0 || !propMap.TryGetValue((anchor, prop), out var propEntry) ||
                texture < 0 || texture >= (propEntry.Element("texData")?.Elements("Item").Count() ?? 0))
                errors.Add(Path.GetFileName(shopPath) + $": prop inválido {anchor}/{prop}/{texture}");
        }
    }

    private static int Value(XElement item, string name) =>
        int.TryParse(item.Element(name)?.Attribute("value")?.Value, out var value) ? value : -1;
}
