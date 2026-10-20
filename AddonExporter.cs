using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using CodeWalker.GameFiles;

namespace FooxClothStudio;

public static class AddonExporter
{
    private static readonly string[] SlotCodes =
        ["head", "berd", "hair", "uppr", "lowr", "hand", "feet", "teef", "accs", "task", "decl", "jbib"];
    private static readonly string[] PropCodes =
        ["p_head", "p_eyes", "p_ears", "p_mouth", "p_lwrist", "p_rwrist"];
    private static readonly string[] AnchorNames =
        ["ANCHOR_HEAD", "ANCHOR_EYES", "ANCHOR_EARS", "ANCHOR_MOUTH",
            "ANCHOR_LEFT_WRIST", "ANCHOR_RIGHT_WRIST"];
    private static readonly int[] AnchorIds = [0, 1, 2, 3, 6, 7];

    private static readonly Encoding Utf8 = new UTF8Encoding(false);

    public static string Export(ClothProject project, Func<string, string> resolvePath, string parentFolder)
    {
        var items = project.Items.ToArray();
        if (items.Length == 0 && project.Tattoos.Count == 0) throw new InvalidDataException("O projeto não contém peças ou tatuagens.");
        if (items.Any(x => x.Gender is not ("Masculino" or "Feminino")))
            throw new InvalidDataException("Defina o gênero de todas as peças antes de gerar o addon.");
        if (items.Any(x => !SlotCodes.Contains(CodeOf(x.Category)) && !PropCodes.Contains(CodeOf(x.Category))))
            throw new InvalidDataException("Esta versão do gerador aceita componentes de roupa e props conhecidos; a categoria 'Outros' ainda precisa de suporte próprio.");

        var shortName = new string(project.Name.ToLowerInvariant()
            .Where(char.IsAsciiLetterOrDigit).Take(14).ToArray());
        if (shortName.Length == 0) shortName = "roupas";
        if (!string.IsNullOrWhiteSpace(project.DlcName))
        {
            if (!Regex.IsMatch(project.DlcName, "^[a-z0-9_]{1,24}$"))
                throw new InvalidDataException("DLC Name must use 1–24 lowercase letters, numbers or underscores.");
            shortName = project.DlcName;
        }
        var resourceName = project.ReplaceDlcCollection ? project.ReplacementResource : shortName;
        if (!Regex.IsMatch(resourceName, "^[a-z0-9_]{1,64}$")) throw new InvalidDataException("Invalid replacement resource name.");
        var output = Path.Combine(Path.GetFullPath(parentFolder), resourceName);
        if (Directory.Exists(output) || File.Exists(output))
            throw new IOException("A saída já existe: " + output);

        var collectionByGender = new Dictionary<string, string>
        {
            ["Masculino"] = (project.RemoveDefaultCollectionPrefix ? "" : "mp_m_") + resourceName,
            ["Feminino"] = (project.RemoveDefaultCollectionPrefix ? "" : "mp_f_") + resourceName
        };
        var manifestFiles = new List<string>();
        if (project.ReplaceDlcCollection)
        {
            foreach (var (gender, replacement) in new[] { ("Masculino", project.ReplacementMaleCollection), ("Feminino", project.ReplacementFemaleCollection) })
            {
                if (!Regex.IsMatch(replacement, "^[a-z0-9_]{1,64}$")) throw new InvalidDataException("Invalid replacement collection name.");
                collectionByGender[gender] = replacement;
            }
        }
        var alternateMaps = new Dictionary<string, Dictionary<(int Slot, int OriginalIndex), int>>();
        Directory.CreateDirectory(output);
        try
        {
            foreach (var (gender, collection) in collectionByGender)
            {
                var genderItems = items.Where(x => x.Gender == gender).ToArray();
                if (genderItems.Length == 0) continue;
                var ped = gender == "Masculino" ? "mp_m_freemode_01" : "mp_f_freemode_01";
                var fullCollection = ped + "_" + collection;
                var folder = Path.Combine(output, "stream", fullCollection);
                Directory.CreateDirectory(folder);
                var groups = genderItems.Where(x => SlotCodes.Contains(CodeOf(x.Category)))
                    .GroupBy(x => Array.IndexOf(SlotCodes, CodeOf(x.Category)))
                    .OrderBy(g => g.Key).ToArray();
                var metadata = new List<(int Slot, int Drawable, int Texture)>();
                var propMetadata = new List<(int Anchor, int Drawable, int Texture)>();
                var indexMap = new Dictionary<(int Slot, int OriginalIndex), int>();
                var drawables = new Dictionary<int, List<(ClothItem Item, int TextureCount)>>();
                var props = new Dictionary<int, List<(ClothItem Item, int TextureCount)>>();
                foreach (var group in groups)
                {
                    var ordered = group.OrderBy(x => x.ExportPosition ?? x.SourceIndex).ThenBy(x => x.Name).ToArray();
                    var entries = new List<(ClothItem, int)>();
                    for (var index = 0; index < ordered.Length; index++)
                    {
                        var item = ordered[index];
                        var itemFolder = project.CreateDrawableSubfolders ? Path.Combine(folder, SlotCodes[group.Key]) : folder;
                        Directory.CreateDirectory(itemFolder);
                        if (item.SourceIndex.HasValue)
                            indexMap[(group.Key, item.SourceIndex.Value)] = index;
                        if (item.IsDummy) { entries.Add((item, 0)); continue; }
                        if (item.TexturePaths.Count is < 1 or > 26)
                            throw new InvalidDataException(item.Name + ": exige de 1 a 26 texturas YTD para o addon.");
                        var number = index.ToString("D3");
                        var modelName = item.OriginalModelFileName ?? item.ModelPath;
                        var suffix = Path.GetFileNameWithoutExtension(modelName).EndsWith("_r", StringComparison.OrdinalIgnoreCase) ? "r" : "u";
                        var stem = fullCollection + "^" + SlotCodes[group.Key] + "_" + number;
                        CopyAsset(resolvePath(item.ModelPath), Path.Combine(itemFolder, stem + "_" + suffix + ".ydd"));
                        for (var alternate = 0; alternate < item.AlternateModelPaths.Count; alternate++)
                            CopyAsset(resolvePath(item.AlternateModelPaths[alternate]),
                                Path.Combine(itemFolder, stem + "_" + suffix + "_" + (alternate + 1) + ".ydd"));
                        for (var texture = 0; texture < item.TexturePaths.Count; texture++)
                        {
                            if (string.IsNullOrWhiteSpace(item.TexturePaths[texture])) continue;
                            var originalTexture = Path.GetFileNameWithoutExtension(
                                item.OriginalTextureFileNames.ElementAtOrDefault(texture) ?? item.TexturePaths[texture]);
                            var suffixMatch = Regex.Match(originalTexture, "_([a-z]{3})$", RegexOptions.IgnoreCase);
                            var textureSuffix = suffixMatch.Success ? suffixMatch.Groups[1].Value.ToLowerInvariant() :
                                suffix == "r" ? "whi" : "uni";
                            var textureName = fullCollection + "^" + SlotCodes[group.Key] + "_diff_" +
                                number + "_" + (char)('a' + texture) + "_" + textureSuffix + ".ytd";
                            CopyAsset(resolvePath(item.TexturePaths[texture]), Path.Combine(itemFolder, textureName));
                            metadata.Add((group.Key, index, texture));
                        }
                        if (item.PhysicsPath is not null)
                            CopyAsset(resolvePath(item.PhysicsPath), Path.Combine(itemFolder, stem + ".yld"));
                        entries.Add((item, item.TexturePaths.Count));
                    }
                    drawables[group.Key] = entries;
                }
                alternateMaps[gender] = indexMap;

                var propCollection = ped + "_p_" + collection;
                var propFolder = Path.Combine(output, "stream", propCollection);
                foreach (var group in genderItems.Where(x => PropCodes.Contains(CodeOf(x.Category)))
                    .GroupBy(x => Array.IndexOf(PropCodes, CodeOf(x.Category))).OrderBy(g => g.Key))
                {
                    Directory.CreateDirectory(propFolder);
                    var ordered = group.OrderBy(x => x.ExportPosition ?? x.SourceIndex).ThenBy(x => x.Name).ToArray();
                    var entries = new List<(ClothItem, int)>();
                    for (var index = 0; index < ordered.Length; index++)
                    {
                        var item = ordered[index];
                        if (item.IsDummy) { entries.Add((item, 0)); continue; }
                        if (item.TexturePaths.Count is < 1 or > 26)
                            throw new InvalidDataException(item.Name + ": exige de 1 a 26 texturas YTD para o addon.");
                        var stem = propCollection + "^" + PropCodes[group.Key] + "_" + index.ToString("D3");
                        var propItemFolder = project.CreateDrawableSubfolders ? Path.Combine(propFolder, PropCodes[group.Key]) : propFolder;
                        Directory.CreateDirectory(propItemFolder);
                        CopyAsset(resolvePath(item.ModelPath), Path.Combine(propItemFolder, stem + ".ydd"));
                        for (var texture = 0; texture < item.TexturePaths.Count; texture++)
                        {
                            if (string.IsNullOrWhiteSpace(item.TexturePaths[texture])) continue;
                            var textureName = propCollection + "^" + PropCodes[group.Key] + "_diff_" +
                                index.ToString("D3") + "_" + (char)('a' + texture) + ".ytd";
                            CopyAsset(resolvePath(item.TexturePaths[texture]), Path.Combine(propItemFolder, textureName));
                            propMetadata.Add((group.Key, index, texture));
                        }
                        entries.Add((item, item.TexturePaths.Count));
                    }
                    props[group.Key] = entries;
                }

                var ymtXml = CreateYmtXml(collection, drawables, props, project.DisableExtraYmtFiles);
                var doc = new XmlDocument();
                doc.LoadXml(ymtXml);
                var ymt = ResourceBuilder.Build(XmlMeta.GetMeta(doc), 2);
                var ymtPath = Path.Combine(output, "stream", fullCollection + ".ymt");
                File.WriteAllBytes(ymtPath, ymt);
                var parsed = new YmtFile();
                parsed.Load(File.ReadAllBytes(ymtPath));
                if (parsed.Meta is null || !MetaXml.GetXml(parsed.Meta).Contains("<CPedVariationInfo"))
                    throw new InvalidDataException("O YMT gerado não passou na releitura.");

                var metaName = fullCollection + "_shop.meta";
                var creatureName = "mp_creaturemetadata_" + (project.RemoveDefaultCollectionPrefix || project.ReplaceDlcCollection ? ped + "_" : "") + collection;
                var hats = props.GetValueOrDefault(0, []).Select((x, index) => (index, x.Item));
                var shoes = drawables.GetValueOrDefault(6, []).Select((x, index) => (index, x.Item));
                if (project.DisableExtraYmtFiles || !CreatureMetadata.Write(Path.Combine(output, "stream", creatureName + ".ymt"), hats, shoes))
                    creatureName = "";
                File.WriteAllText(Path.Combine(output, metaName),
                    CreateShopMeta(ped, collection, fullCollection, resourceName, metadata, propMetadata,
                        drawables, props, project.UseTextureNameAsUniqueHash, creatureName), Utf8);
                manifestFiles.Add(metaName);
                manifestFiles.AddRange(FirstPersonMetadata.Write(output, ped, collection, fullCollection, drawables, props));
            }
            var alternateMetaWritten = false;
            if (!project.DisableExtraAlternateVariationFeatures && project.SourceDlcName is not null && project.SourceAlternateVariationsPath is not null)
            {
                var sourceMeta = resolvePath(project.SourceAlternateVariationsPath);
                alternateMetaWritten = WriteAlternateVariations(sourceMeta,
                    Path.Combine(output, "pedalternatevariations.meta"),
                    project.SourceDlcName, collectionByGender, alternateMaps);
                if (alternateMetaWritten) manifestFiles.Add("pedalternatevariations.meta");
            }
            if (!project.DisableExtraAlternateVariationFeatures && HairVariations.Write(Path.Combine(output, "pedalternatevariations.meta"), project, collectionByGender)
                && !manifestFiles.Contains("pedalternatevariations.meta")) manifestFiles.Add("pedalternatevariations.meta");
            manifestFiles.AddRange(TattooExporter.Write(project, resolvePath, output, shortName));
            var manifest = new StringBuilder("fx_version 'cerulean'\ngame 'gta5'\n\nfiles {\n");
            foreach (var name in manifestFiles) manifest.AppendLine("  '" + name + "',");
            manifest.AppendLine("}\n");
            static string Lua(string value) => "'" + value.Replace("\\", "\\\\").Replace("'", "\\'")
                .Replace("\r", "\\r").Replace("\n", "\\n") + "'";
            if (!string.IsNullOrWhiteSpace(project.Author)) manifest.AppendLine("author " + Lua(project.Author));
            if (!string.IsNullOrWhiteSpace(project.Version)) manifest.AppendLine("version " + Lua(project.Version));
            if (!string.IsNullOrWhiteSpace(project.Description)) manifest.AppendLine("description " + Lua(project.Description));
            if (project.TargetGameBuild > 0) manifest.AppendLine("dependency '/gameBuild:" + project.TargetGameBuild + "'");
            foreach (var name in manifestFiles)
                manifest.AppendLine("data_file '" + FirstPersonMetadata.TypeOf(name) + "' '" + name + "'");
            File.WriteAllText(Path.Combine(output, "fxmanifest.lua"), manifest.ToString(), Utf8);
            if (project.CreateClothesInfoDump)
                File.WriteAllText(Path.Combine(output, "clothes-info.json"), JsonSerializer.Serialize(
                    items.Select(item => new { item.Name, item.Gender, item.Category, item.SourceIndex,
                        Textures = item.TexturePaths.Count, item.Creator, item.CreatorWebsite, item.CreatorDescription }),
                    new JsonSerializerOptions { WriteIndented = true }), Utf8);
            var validation = FiveMResourceValidator.Validate(output);
            if (validation.Count > 0)
                throw new InvalidDataException("Addon gerado com inconsistências: " +
                    string.Join("; ", validation.Take(3)));
            return output;
        }
        catch
        {
            Directory.Delete(output, true);
            throw;
        }
    }

    private static string CodeOf(string category)
    {
        var start = category.LastIndexOf('[');
        return start < 0 ? "" : category[(start + 1)..].TrimEnd(']');
    }

    private static void CopyAsset(string source, string destination)
    {
        if (!File.Exists(source)) throw new FileNotFoundException("Arquivo ausente", source);
        File.Copy(source, destination);
    }

    private static XElement Value(string name, object value) => new(name, new XAttribute("value", value));
    private static XElement Typed(string name, string type, IEnumerable<XElement> items) =>
        new(name, new XAttribute("itemType", type), items);

    private static string CreateYmtXml(string collection,
        Dictionary<int, List<(ClothItem Item, int TextureCount)>> groups,
        Dictionary<int, List<(ClothItem Item, int TextureCount)>> props, bool disableExtraYmt)
    {
        var slots = Enumerable.Repeat(255, 12).ToArray();
        var components = new List<XElement>();
        var infos = new List<XElement>();
        foreach (var (slot, entries) in groups.OrderBy(x => x.Key))
        {
            slots[slot] = components.Count;
            var drawableElements = new List<XElement>();
            for (var index = 0; index < entries.Count; index++)
            {
                var (item, textureCount) = entries[index];
                drawableElements.Add(new XElement("Item",
                    Value("propMask", (item.OriginalModelFileName ?? item.ModelPath).EndsWith("_r.ydd", StringComparison.OrdinalIgnoreCase) ? 17 :
                        slot switch { 2 or 7 => 11, 8 => 65, 10 => 5, _ => 1 }),
                    Value("numAlternatives", item.AlternateModelPaths.Count),
                    Typed("aTexData", "CPVTextureData", Enumerable.Range(0, textureCount)
                        .Select(texture => new XElement("Item", Value("texId",
                            (item.OriginalModelFileName ?? item.ModelPath).EndsWith("_r.ydd", StringComparison.OrdinalIgnoreCase) ? 1 : 0),
                            Value("distribution", string.IsNullOrWhiteSpace(item.TexturePaths[texture]) ? 0 : 255)))),
                    new XElement("clothData", Value("ownsCloth", item.PhysicsPath is not null ? "true" : "false"))));
                infos.Add(new XElement("Item",
                    new XElement("pedXml_audioID", item.AudioPreset), new XElement("pedXml_audioID2"),
                    new XElement("pedXml_expressionMods", "0 0 0 0 " + (disableExtraYmt ? 0 : item.HeelHeight).ToString(System.Globalization.CultureInfo.InvariantCulture)), Value("flags", item.BehaviourFlags ?? 0),
                    new XElement("inclusions", "0"), new XElement("exclusions", "0"),
                    new XElement("pedXml_vfxComps", "PV_COMP_HEAD"), Value("pedXml_flags", 0),
                    Value("pedXml_compIdx", slot), Value("pedXml_drawblIdx", index)));
            }
            components.Add(new XElement("Item", Value("numAvailTex", entries.Sum(x => x.TextureCount) & 0xFF),
                Typed("aDrawblData3", "CPVDrawblData", drawableElements)));
        }
        var propItems = new List<XElement>();
        var anchors = new List<XElement>();
        foreach (var (anchor, entries) in props.OrderBy(x => x.Key))
        {
            anchors.Add(new XElement("Item", new XElement("props",
                string.Join(" ", entries.Select(x => x.TextureCount))),
                new XElement("anchor", AnchorNames[anchor])));
            for (var index = 0; index < entries.Count; index++)
            {
                var textures = Enumerable.Range(0, entries[index].TextureCount).Select(texture =>
                    new XElement("Item", new XElement("inclusions", "0"),
                        new XElement("exclusions", "0"), Value("texId", texture),
                        Value("inclusionId", 0), Value("exclusionId", 0),
                        Value("distribution", string.IsNullOrWhiteSpace(entries[index].Item.TexturePaths[texture]) ? 0 : 255)));
                propItems.Add(new XElement("Item", new XElement("audioId", entries[index].Item.AudioPreset),
                    new XElement("expressionMods", (anchor == 0 && !disableExtraYmt ? CreatureMetadata.HairValue(entries[index].Item) : 0)
                        .ToString(System.Globalization.CultureInfo.InvariantCulture) + " 0 0 0 0"),
                    Typed("texData", "CPedPropTexData", textures),
                    new XElement("renderFlags"), Value("propFlags", entries[index].Item.BehaviourFlags ?? 0), Value("flags", 0),
                    Value("anchorId", AnchorIds[anchor]), Value("propId", index), Value("stickyness", 0)));
            }
        }
        var root = new XElement("CPedVariationInfo", new XAttribute("name", collection),
            Value("bHasTexVariations", "true"), Value("bHasDrawblVariations", "true"),
            Value("bHasLowLODs", "false"), Value("bIsSuperLOD", "false"),
            new XElement("availComp", string.Join(" ", slots)),
            Typed("aComponentData3", "CPVComponentData", components),
            Typed("aSelectionSets", "CPedSelectionSet", []),
            Typed("compInfos", "CComponentInfo", infos),
            new XElement("propInfo", Value("numAvailProps", propItems.Count),
                Typed("aPropMetaData", "CPedPropMetaData", propItems),
                Typed("aAnchors", "CAnchorProps", anchors)),
            new XElement("dlcName", collection));
        return new XDocument(new XDeclaration("1.0", "UTF-8", null), root).ToString();
    }

    private static string CreateShopMeta(string ped, string collection, string fullCollection, string resourceName,
        IEnumerable<(int Slot, int Drawable, int Texture)> items,
        IEnumerable<(int Anchor, int Drawable, int Texture)> props,
        Dictionary<int, List<(ClothItem Item, int TextureCount)>> drawables,
        Dictionary<int, List<(ClothItem Item, int TextureCount)>> propDrawables, bool useTextureName, string creatureName)
    {
        string Unique(ClothItem item, int texture, string fallback) => useTextureName
            ? Path.GetFileNameWithoutExtension(item.OriginalTextureFileNames.ElementAtOrDefault(texture) ?? item.TexturePaths[texture])
            : fallback;
        var components = items.Select(x => new XElement("Item",
            new XElement("lockHash", drawables[x.Slot][x.Drawable].Item.ShopLockHash), Value("cost", drawables[x.Slot][x.Drawable].Item.ShopCost), new XElement("textLabel", drawables[x.Slot][x.Drawable].Item.ShopTextLabel),
            new XElement("uniqueNameHash", Unique(drawables[x.Slot][x.Drawable].Item, x.Texture, $"FOOX_{collection.ToUpperInvariant()}_{x.Slot}_{x.Drawable}_{x.Texture}")),
            new XElement("eShopEnum", "CLO_SHOP_NONE"), Value("locate", -99),
            Value("scriptSaveData", 0), new XElement("restrictionTags"),
            new XElement("forcedComponents"), new XElement("variantComponents"),
            Value("drawableIndex", 0), Value("localDrawableIndex", x.Drawable),
            new XElement("eCompType", "PV_COMP_" + SlotCodes[x.Slot].ToUpperInvariant()),
            Value("textureIndex", x.Texture), Value("isInOutfit", "false")));
        var propEntries = props.Select(x => new XElement("Item",
            new XElement("lockHash", propDrawables[x.Anchor][x.Drawable].Item.ShopLockHash), Value("cost", propDrawables[x.Anchor][x.Drawable].Item.ShopCost), new XElement("textLabel", propDrawables[x.Anchor][x.Drawable].Item.ShopTextLabel),
            new XElement("uniqueNameHash", Unique(propDrawables[x.Anchor][x.Drawable].Item, x.Texture, $"FOOX_{collection.ToUpperInvariant()}_P{x.Anchor}_{x.Drawable}_{x.Texture}")),
            new XElement("eShopEnum", "CLO_SHOP_NONE"), Value("locate", -99),
            Value("scriptSaveData", 0), new XElement("restrictionTags"),
            new XElement("forcedComponents"), new XElement("forcedProps"),
            new XElement("variantComponents"), new XElement("variantProps"),
            Value("propIndex", 0), Value("localPropIndex", x.Drawable),
            new XElement("eAnchorPoint", AnchorNames[x.Anchor]),
            Value("textureIndex", x.Texture), Value("isInOutfit", "false")));
        var root = new XElement("ShopPedApparel", new XElement("pedName", ped),
            new XElement("dlcName", collection), new XElement("fullDlcName", fullCollection),
            new XElement("eCharacter", ped == "mp_f_freemode_01" ?
                "SCR_CHAR_MULTIPLAYER_F" : "SCR_CHAR_MULTIPLAYER"),
            new XElement("creatureMetaData", creatureName),
            new XElement("pedOutfits"), new XElement("pedComponents", components),
            new XElement("pedProps", propEntries));
        return new XDocument(new XDeclaration("1.0", "UTF-8", null), root).ToString();
    }

    private static bool WriteAlternateVariations(string source, string target, string sourceDlc,
        IReadOnlyDictionary<string, string> outputCollections,
        IReadOnlyDictionary<string, Dictionary<(int Slot, int OriginalIndex), int>> maps)
    {
        var document = XDocument.Load(source);
        var peds = document.Root?.Element("peds") ??
            throw new InvalidDataException("pedalternatevariations.meta sem lista de peds.");
        foreach (var ped in peds.Elements("Item").ToArray())
        {
            var gender = ped.Element("name")?.Value switch
            {
                "mp_m_freemode_01" => "Masculino",
                "mp_f_freemode_01" => "Feminino",
                _ => null
            };
            if (gender is null || !maps.TryGetValue(gender, out var indexMap))
            {
                ped.Remove();
                continue;
            }
            var oldCollection = (gender == "Masculino" ? "mp_m_" : "mp_f_") + sourceDlc;
            var newCollection = outputCollections[gender];
            var switches = ped.Element("switches");
            if (switches is null) continue;
            foreach (var rule in switches.Elements("Item").ToArray())
            {
                var targetName = rule.Element("dlcNameHash");
                if (targetName?.Value == oldCollection)
                {
                    if (!RemapIndex(rule, indexMap)) { rule.Remove(); continue; }
                    targetName.Value = newCollection;
                }
                var assets = rule.Element("sourceAssets");
                var before = assets?.Elements("Item").Count() ?? 0;
                if (assets is not null)
                    foreach (var asset in assets.Elements("Item").ToArray())
                    {
                        var name = asset.Element("dlcNameHash");
                        if (name?.Value != oldCollection) continue;
                        if (!RemapIndex(asset, indexMap)) asset.Remove();
                        else name.Value = newCollection;
                    }
                if (targetName?.Value != newCollection && before > 0 &&
                    !(assets?.Elements("Item").Any() ?? false)) rule.Remove();
            }
            if (!switches.Elements("Item").Any()) ped.Remove();
        }
        if (!peds.Elements("Item").Any()) return false;
        document.Save(target);
        return true;
    }

    private static bool RemapIndex(XElement item,
        IReadOnlyDictionary<(int Slot, int OriginalIndex), int> indexMap)
    {
        var component = item.Element("component")?.Attribute("value");
        var index = item.Element("index")?.Attribute("value");
        if (!int.TryParse(component?.Value, out var slot) ||
            !int.TryParse(index?.Value, out var original) ||
            !indexMap.TryGetValue((slot, original), out var updated)) return false;
        index!.Value = updated.ToString();
        return true;
    }
}
