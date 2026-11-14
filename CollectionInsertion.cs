using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using CodeWalker.GameFiles;

namespace FooxClothStudio;

public sealed record PreparedCollectionInsertion(string FilesRoot, HashSet<string> ReplacedFiles,
    List<ClothingIdentityEntry> Entries, HashSet<Guid> AddedIds);

public static class CollectionInsertion
{
    public static List<PreparedCollectionInsertion> Prepare(ClothProject profile, ResourceUpdatePlan plan,
        string sourceRoot, Func<string, string> resolve, string temporary, string? gameFolder)
    {
        var requests = plan.AddedItems.Where(item => item.InsertBeforeItemId.HasValue).Select(item =>
        {
            var target = plan.OriginalIdentities.Entries.SingleOrDefault(entry => entry.Id == item.InsertBeforeItemId)
                ?? throw new InvalidDataException("Insertion target is not an existing resource item: " + item.Name);
            if (!target.Available || target.Kind != "component" || item.Gender != target.Gender
                || CollectionIndices.CodeOf(item.Category) != CollectionIndices.CodeOf(plan.Original.Items.Single(original => original.Id == target.Id).Category))
                throw new InvalidDataException("Insertion requires an existing component of the same gender and drawable type.");
            return (Item: item, Target: target);
        }).ToArray();
        if (requests.Length > 0 && !profile.UsePermanentClothingIds)
            throw new InvalidDataException("Enable 'Use permanent IDs in scripts' before shifting existing native indices.");
        var result = new List<PreparedCollectionInsertion>();
        foreach (var collection in requests.GroupBy(request => (request.Target.Model, request.Target.Collection)))
            result.Add(PrepareCollection(profile, plan, sourceRoot, resolve, temporary, gameFolder, collection.ToArray()));
        return result;
    }

    private static PreparedCollectionInsertion PrepareCollection(ClothProject profile, ResourceUpdatePlan plan,
        string sourceRoot, Func<string, string> resolve, string temporary, string? gameFolder,
        (ClothItem Item, ClothingIdentityEntry Target)[] requests)
    {
        var target = requests[0].Target;
        var fullName = target.Model + "_" + target.Collection;
        var ymts = Directory.EnumerateFiles(Path.Combine(sourceRoot, "stream"), fullName + ".ymt", SearchOption.AllDirectories).ToArray();
        if (ymts.Length != 1)
            throw new InvalidDataException("Insertion requires the collection YMT inside this resource: " + fullName + ".ymt. Select an addon collection, not a GTA replacement without metadata.");
        var metadata = ReadYmt(ymts[0]);
        if (metadata.Root?.Name != "CPedVariationInfo" || metadata.Root.Attribute("name")?.Value != target.Collection)
            throw new InvalidDataException("The resource YMT does not identify the target collection.");
        if (metadata.Root.Element("aSelectionSets")?.Elements().Any() == true)
            throw new InvalidDataException("Insertion into a collection with native selection sets needs explicit selection-set remapping.");
        var root = Path.Combine(temporary, "insert_" + requests[0].Item.Id.ToString("N"));
        var filesRoot = Path.Combine(root, "files");
        Directory.CreateDirectory(filesRoot);
        var replaced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = plan.OriginalIdentities.Entries.Where(entry => entry.Model == target.Model && entry.Collection == target.Collection).ToList();
        var addedEntries = new List<ClothingIdentityEntry>();
        var maps = new Dictionary<int, Func<int, int>>();
        var generatedShops = new List<XElement>();
        foreach (var group in requests.GroupBy(request => request.Target.Slot))
        {
            var slot = group.Key;
            var code = CollectionIndices.CodeOf(group.First().Item.Category);
            var ordered = group.OrderBy(request => request.Target.Index).ThenBy(request => request.Item.ExportPosition ?? request.Item.SourceIndex).ThenBy(request => request.Item.Id).ToArray();
            var drawables = Drawables(metadata, slot);
            var oldCount = drawables.Elements("Item").Count();
            if (oldCount + ordered.Length > CollectionIndices.DrawablesPerSlot)
                throw new InvalidDataException($"Collection {target.Collection}, slot {slot}: insertion exceeds native drawable capacity. Choose a collection with free capacity.");
            if (ordered.Any(request => request.Target.Index < 0 || request.Target.Index >= oldCount))
                throw new InvalidDataException("Insertion target is outside the drawable count declared by its YMT.");
            int MapIndex(int index) => index == byte.MaxValue ? index : checked(index + ordered.Count(request => request.Target.Index <= index));
            maps.Add(slot, MapIndex);
            var firstIndex = ordered[0].Target.Index;
            string Rename(string name) => RenameAsset(name, fullName, code, MapIndex);
            var nativeFiles = Directory.EnumerateFiles(Path.Combine(sourceRoot, "stream"), "*", SearchOption.AllDirectories)
                .Where(path => AssetIndex(Path.GetFileName(path), fullName, code) is int index && index >= firstIndex)
                .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);
            foreach (var names in nativeFiles)
            {
                var paths = names.ToArray();
                if (paths.Any(path => !AssetContent.Equivalent(paths[0], path)))
                    throw new InvalidDataException("Conflicting source assets share the same native filename: " + names.Key);
                var nativeIndex = AssetIndex(names.Key!, fullName, code)!.Value;
                if (nativeIndex >= oldCount)
                    throw new InvalidDataException("Source asset is outside the collection YMT: " + names.Key);
                Stage(filesRoot, Path.Combine("stream", "pcs_insertions", Rename(names.Key!)), File.ReadAllBytes(paths[0]));
                foreach (var path in paths) replaced.Add(Path.GetRelativePath(sourceRoot, path));
            }
            entries = entries.Select(entry => entry.Kind == "component" && entry.Slot == slot
                ? entry with { Index = MapIndex(entry.Index), ModelFile = entry.ModelFile is null ? null : Rename(entry.ModelFile),
                    Textures = entry.Textures.Select(texture => texture with { File = texture.File is null ? null : Rename(texture.File) }).ToList() }
                : entry).ToList();
            foreach (var info in metadata.Root.Element("compInfos")?.Elements("Item") ?? [])
                if (Value(info, "pedXml_compIdx") == slot)
                    SetValue(info, "pedXml_drawblIdx", MapIndex(Value(info, "pedXml_drawblIdx")));
            for (var added = 0; added < ordered.Length; added++)
            {
                var index = ordered[added].Target.Index + added;
                var generated = Generate(profile, ordered[added].Item, target, resolve, root, gameFolder);
                var definition = ReadYmt(Path.Combine(generated, "stream", fullName + ".ymt"));
                var drawable = new XElement(Drawables(definition, slot).Elements("Item").Single());
                var current = drawables.Elements("Item").ElementAtOrDefault(index);
                if (current is null) drawables.Add(drawable); else current.AddBeforeSelf(drawable);
                foreach (var info in definition.Root!.Element("compInfos")?.Elements("Item") ?? [])
                {
                    var insertedInfo = new XElement(info);
                    SetValue(insertedInfo, "pedXml_drawblIdx", index);
                    var infos = metadata.Root.Element("compInfos")!;
                    var nextInfo = infos.Elements("Item").FirstOrDefault(existing => Value(existing, "pedXml_compIdx") == slot && Value(existing, "pedXml_drawblIdx") >= index);
                    if (nextInfo is not null) nextInfo.AddBeforeSelf(insertedInfo);
                    else
                    {
                        var previousInfo = infos.Elements("Item").LastOrDefault(existing => Value(existing, "pedXml_compIdx") == slot);
                        if (previousInfo is not null) previousInfo.AddAfterSelf(insertedInfo); else infos.Add(insertedInfo);
                    }
                }
                string AddedName(string name) => RenameAsset(name, fullName, code, _ => index);
                foreach (var file in Directory.EnumerateFiles(Path.Combine(generated, "stream"), "*", SearchOption.AllDirectories))
                {
                    if (Path.GetExtension(file).Equals(".ymt", StringComparison.OrdinalIgnoreCase)) continue;
                    Stage(filesRoot, Path.Combine("stream", "pcs_insertions", AddedName(Path.GetFileName(file))), File.ReadAllBytes(file));
                }
                var identity = ClothingIdentityMap.Read(Path.Combine(generated, ClothingIdentityMap.FileName)).Entries.Single();
                addedEntries.Add(identity with { Index = index, ModelFile = AddedName(identity.ModelFile!),
                    Textures = identity.Textures.Select(texture => texture with { File = texture.File is null ? null : AddedName(texture.File) }).ToList() });
                var shop = XDocument.Load(Path.Combine(generated, fullName + "_shop.meta"));
                foreach (var component in shop.Root!.Element("pedComponents")?.Elements("Item") ?? [])
                {
                    var insertedComponent = new XElement(component);
                    SetValue(insertedComponent, "localDrawableIndex", index);
                    var textureIndex = Value(insertedComponent, "textureIndex");
                    insertedComponent.Element("uniqueNameHash")!.Value = "PCS_" + ordered[added].Item.Id.ToString("N") + "_" + ordered[added].Item.TextureIds[textureIndex].ToString("N");
                    generatedShops.Add(insertedComponent);
                }
            }
            SetValue(drawables.Parent!, "numAvailTex", drawables.Elements("Item").Sum(item => item.Element("aTexData")?.Elements("Item").Count() ?? 0) & byte.MaxValue);
        }
        RewriteShopMetadata(sourceRoot, target, maps, generatedShops, filesRoot, replaced);
        var ymtRelative = Path.GetRelativePath(sourceRoot, ymts[0]);
        var xml = new XmlDocument();
        xml.LoadXml(metadata.ToString());
        var bytes = ResourceBuilder.Build(XmlMeta.GetMeta(xml), 2);
        Stage(filesRoot, ymtRelative, bytes);
        replaced.Add(ymtRelative);
        var reread = ReadYmt(Path.Combine(filesRoot, ymtRelative));
        foreach (var slot in maps.Keys)
            if (Drawables(reread, slot).Elements("Item").Count() != Drawables(metadata, slot).Elements("Item").Count())
                throw new InvalidDataException("Inserted YMT did not pass metadata round-trip validation.");
        return new(filesRoot, replaced, entries.Concat(addedEntries).ToList(), requests.Select(request => request.Item.Id).ToHashSet());
    }

    private static string Generate(ClothProject profile, ClothItem item, ClothingIdentityEntry target,
        Func<string, string> resolve, string temporary, string? gameFolder)
    {
        if (item.HasFirstPersonModel || item.TextureEffects.Any(effect => !string.IsNullOrWhiteSpace(effect.TimeCycleModifier))
            || !string.IsNullOrWhiteSpace(item.FirstPersonMaskModel) || item.HeelHeight != 0 || item.HairOption != "Show hairs" || item.HoodieHairs)
            throw new InvalidDataException("Native insertion of clothing with first-person, hair or heel metadata needs companion-metadata remapping.");
        var project = new ClothProject
        {
            Name = "pcs_insert", DlcName = "pcs_insert", UsePermanentClothingIds = true, Items = [item],
            ReplaceDlcCollection = true, ReplacementResource = "pcs_insert_" + item.Id.ToString("N"),
            ReplacementMaleCollection = target.Collection, ReplacementFemaleCollection = target.Collection,
            TargetGameBuild = profile.TargetGameBuild
        };
        var errors = BuildDependencies.Prepare(project, resolve, gameFolder);
        if (errors.Count != 0) throw new InvalidDataException(string.Join("\n", errors));
        return AddonExporter.Export(project, resolve, temporary);
    }

    private static void RewriteShopMetadata(string sourceRoot, ClothingIdentityEntry target,
        Dictionary<int, Func<int, int>> maps, List<XElement> additions, string filesRoot, HashSet<string> replaced)
    {
        var found = false;
        foreach (var path in Directory.EnumerateFiles(sourceRoot, "*.meta", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(path);
            if (!content.Contains(target.Collection, StringComparison.Ordinal)) continue;
            var document = XDocument.Parse(content);
            var shop = document.Root;
            if (shop?.Name != "ShopPedApparel" || shop.Element("pedName")?.Value != target.Model || shop.Element("dlcName")?.Value != target.Collection)
                throw new InvalidDataException("Insertion needs remapping of related metadata before this collection can be changed: " + Path.GetFileName(path));
            if (found || shop.Element("pedOutfits")?.Elements().Any() == true)
                throw new InvalidDataException("Insertion requires one shop metadata file without embedded outfits.");
            found = true;
            var codes = new[] { "HEAD", "BERD", "HAIR", "UPPR", "LOWR", "HAND", "FEET", "TEEF", "ACCS", "TASK", "DECL", "JBIB" };
            foreach (var component in shop.Element("pedComponents")?.Elements("Item") ?? [])
            {
                var slot = Array.IndexOf(codes, component.Element("eCompType")?.Value.Replace("PV_COMP_", ""));
                if (!maps.TryGetValue(slot, out var map)) continue;
                var globalIndex = Value(component, "drawableIndex");
                var localIndex = Value(component, "localDrawableIndex");
                if (globalIndex != 0 && globalIndex != localIndex)
                    throw new InvalidDataException("Shop metadata with different global/local indices needs explicit remapping before insertion.");
                if (component.Element("forcedComponents")?.Elements().Any() == true || component.Element("variantComponents")?.Elements().Any() == true)
                    throw new InvalidDataException("Insertion needs forced/variant component remapping for this shop metadata.");
                if (globalIndex != 0) SetValue(component, "drawableIndex", map(localIndex));
                SetValue(component, "localDrawableIndex", map(localIndex));
            }
            shop.Element("pedComponents")!.Add(additions.Select(element => new XElement(element)));
            var relative = Path.GetRelativePath(sourceRoot, path);
            Stage(filesRoot, relative, System.Text.Encoding.UTF8.GetBytes(document.ToString()));
            replaced.Add(relative);
        }
        if (!found) throw new InvalidDataException("The target addon collection has no shop metadata in this resource.");
    }

    public static void Apply(PreparedCollectionInsertion insertion, string output)
    {
        foreach (var relative in insertion.ReplacedFiles) File.Delete(Path.Combine(output, relative));
        foreach (var source in Directory.EnumerateFiles(insertion.FilesRoot, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(output, Path.GetRelativePath(insertion.FilesRoot, source));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination);
        }
    }

    public static XDocument ReadYmt(string path)
    {
        var ymt = new YmtFile();
        ymt.Load(File.ReadAllBytes(path));
        if (ymt.Meta is null) throw new InvalidDataException("Unsupported collection YMT: " + path);
        return XDocument.Parse(MetaXml.GetXml(ymt.Meta));
    }

    public static XElement Drawables(XDocument document, int slot)
    {
        var available = (document.Root!.Element("availComp")?.Value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
        if (slot < 0 || slot >= available.Length || available[slot] == byte.MaxValue)
            throw new InvalidDataException("Target component is not present in the collection YMT.");
        return document.Root.Element("aComponentData3")!.Elements("Item").ElementAt(available[slot]).Element("aDrawblData3")!;
    }

    private static int? AssetIndex(string name, string fullName, string code)
    {
        var match = Regex.Match(name, "^" + Regex.Escape(fullName + "^" + code + "_") + @"(?:diff_)?(?<index>\d+)(?=_|\.)", RegexOptions.IgnoreCase);
        return match.Success ? int.Parse(match.Groups["index"].Value) : null;
    }

    private static string RenameAsset(string name, string fullName, string code, Func<int, int> map) =>
        Regex.Replace(name, "^(" + Regex.Escape(fullName + "^" + code + "_") + @"(?:diff_)?)(\d+)(?=_|\.)",
            match => match.Groups[1].Value + map(int.Parse(match.Groups[2].Value)).ToString("D3"), RegexOptions.IgnoreCase);

    private static int Value(XElement item, string name) => int.Parse(item.Element(name)?.Attribute("value")?.Value
        ?? throw new InvalidDataException("Missing native metadata field: " + name));
    private static void SetValue(XElement item, string name, int value) => (item.Element(name)
        ?? throw new InvalidDataException("Missing native metadata field: " + name)).SetAttributeValue("value", value);
    private static void Stage(string root, string relative, byte[] bytes)
    {
        var destination = Path.GetFullPath(Path.Combine(root, relative));
        if (!destination.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Insertion output escaped its staging folder.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllBytes(destination, bytes);
    }
}
