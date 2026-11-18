using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using CodeWalker.GameFiles;

namespace FooxClothStudio;

public sealed record ClothingModelReuse(ClothItem Incoming, Guid TargetId);

/// <summary>Only incoming items are considered. Published drawables are never consolidated.</summary>
public sealed class ClothingModelMatcher(Func<string, string> resolve)
{
    public const int TextureCapacity = 26;
    private readonly Dictionary<string, string> hashes = new(StringComparer.OrdinalIgnoreCase);

    private string Hash(string path)
    {
        path = Path.GetFullPath(resolve(path));
        if (!hashes.TryGetValue(path, out var hash))
        {
            using var stream = File.OpenRead(path);
            hashes[path] = hash = Convert.ToHexString(SHA256.HashData(stream));
        }
        return hash;
    }

    public bool Identical(ClothItem existing, ClothItem incoming)
    {
        static bool IsRigid(ClothItem item) => (item.OriginalModelFileName ?? item.ModelPath).EndsWith("_r.ydd", StringComparison.OrdinalIgnoreCase);
        if (!incoming.AllowModelReuse || existing.IsDummy || incoming.IsDummy || existing.Gender != incoming.Gender || existing.Category != incoming.Category
            || incoming.InsertBeforeItemId is not null || existing.AlternateModelPaths.Count != incoming.AlternateModelPaths.Count
            || (existing.PhysicsPath is null) != (incoming.PhysicsPath is null) || IsRigid(existing) != IsRigid(incoming)) return false;
        // The same mesh with different runtime behaviour is a different clothing item.
        string Metadata(ClothItem item) => JsonSerializer.Serialize(new { item.HairOption, item.HoodieHairs, item.HairCutValue,
            item.HeelHeight, item.FirstPersonMaskModel, item.HasFirstPersonModel, item.MaskOffsetX, item.MaskOffsetY,
            item.MaskOffsetZ, item.TextureEffects, item.AudioPreset, item.BehaviourFlags });
        if (Metadata(existing) != Metadata(incoming) || Hash(existing.ModelPath) != Hash(incoming.ModelPath)) return false;
        for (var i = 0; i < existing.AlternateModelPaths.Count; i++)
            if (Hash(existing.AlternateModelPaths[i]) != Hash(incoming.AlternateModelPaths[i])) return false;
        return existing.PhysicsPath is null || Hash(existing.PhysicsPath) == Hash(incoming.PhysicsPath!);
    }
}

public static class ResourceTextureReuse
{
    public static List<ClothingModelReuse> Plan(ResourceUpdatePlan plan, string sourceRoot, Func<string, string> resolve)
    {
        var matcher = new ClothingModelMatcher(resolve);
        var used = new Dictionary<Guid, int>();
        var result = new List<ClothingModelReuse>();
        var metadata = new Dictionary<string, XDocument>();
        var pendingTargets = new List<ClothItem>();
        foreach (var incoming in plan.AddedItems)
        {
            if (CollectionIndices.ComponentSlot(incoming.Category) < 0 || CollectionInsertion.HasCompanionMetadata(incoming)) continue;
            foreach (var entry in plan.OriginalIdentities.Entries.Where(entry => entry.Available && entry.AliasOf is null
                && entry.Kind == "component" && entry.Gender == incoming.Gender && entry.Slot == CollectionIndices.ComponentSlot(incoming.Category)))
            {
                var target = plan.Original.Items.Single(item => item.Id == entry.Id);
                if (!matcher.Identical(target, incoming)) continue;
                var name = entry.Model + "_" + entry.Collection + ".ymt";
                if (!metadata.TryGetValue(name, out var document))
                {
                    var paths = Directory.EnumerateFiles(Path.Combine(sourceRoot, "stream"), name, SearchOption.AllDirectories).ToArray();
                    if (paths.Length != 1) continue;
                    metadata[name] = document = CollectionInsertion.ReadYmt(paths[0]);
                }
                var drawable = CollectionInsertion.Drawables(document, entry.Slot).Elements("Item").ElementAt(entry.Index);
                var count = drawable.Element("aTexData")!.Elements("Item").Count();
                var occupied = Math.Max(count, entry.Textures.Select(texture => texture.Index + 1).DefaultIfEmpty().Max());
                if (occupied != count) continue; // An inconsistent map is not a safe append target.
                if (occupied + used.GetValueOrDefault(entry.Id) + incoming.TexturePaths.Count > ClothingModelMatcher.TextureCapacity) continue;
                // Unsupported companion/selection/outfit remapping must never be guessed.
                if (document.Root!.Element("aSelectionSets")?.Elements().Any() == true || !Compatible(document, drawable, entry, incoming)) continue;
                var shops = Directory.EnumerateFiles(sourceRoot, "*_shop.meta", SearchOption.AllDirectories)
                    .Select(XDocument.Load).Where(shop => shop.Root?.Element("fullDlcName")?.Value == entry.Model + "_" + entry.Collection).ToArray();
                if (shops.Length != 1 || shops[0].Root!.Element("pedOutfits")?.Elements().Any() == true) continue;
                used[entry.Id] = used.GetValueOrDefault(entry.Id) + incoming.TexturePaths.Count;
                result.Add(new(incoming, entry.Id));
                break;
            }
            if (result.Any(reuse => reuse.Incoming.Id == incoming.Id)) continue;
            var pendingTarget = pendingTargets.FirstOrDefault(target =>
                target.TexturePaths.Count + used.GetValueOrDefault(target.Id) + incoming.TexturePaths.Count <= ClothingModelMatcher.TextureCapacity
                && matcher.Identical(target, incoming));
            if (pendingTarget is null) pendingTargets.Add(incoming);
            else
            {
                used[pendingTarget.Id] = used.GetValueOrDefault(pendingTarget.Id) + incoming.TexturePaths.Count;
                result.Add(new(incoming, pendingTarget.Id));
            }
        }
        return result;
    }

    private static bool Compatible(XDocument document, XElement drawable, ClothingIdentityEntry entry, ClothItem incoming)
    {
        static string? Value(XElement element, string name) => element.Element(name)?.Attribute("value")?.Value;
        var rigid = (incoming.OriginalModelFileName ?? incoming.ModelPath).EndsWith("_r.ydd", StringComparison.OrdinalIgnoreCase);
        var expectedMask = rigid ? 17 : entry.Slot switch { 2 or 7 => 11, 8 => 65, 10 => 5, _ => 1 };
        var infos = document.Root!.Element("compInfos")!.Elements("Item")
            .Where(info => Value(info, "pedXml_compIdx") == entry.Slot.ToString() && Value(info, "pedXml_drawblIdx") == entry.Index.ToString()).ToArray();
        return infos.Length == 1 && Value(drawable, "propMask") == expectedMask.ToString()
            && Value(drawable, "numAlternatives") == incoming.AlternateModelPaths.Count.ToString()
            && Value(drawable.Element("clothData")!, "ownsCloth") == (incoming.PhysicsPath is null ? "false" : "true")
            && Value(infos[0], "flags") == (incoming.BehaviourFlags ?? 0).ToString()
            && infos[0].Element("pedXml_audioID")?.Value == incoming.AudioPreset
            && string.IsNullOrEmpty(infos[0].Element("pedXml_audioID2")?.Value)
            && infos[0].Element("inclusions")?.Value == "0" && infos[0].Element("exclusions")?.Value == "0"
            && infos[0].Element("pedXml_vfxComps")?.Value == "PV_COMP_HEAD" && Value(infos[0], "pedXml_flags") == "0"
            && drawable.Element("aTexData")!.Elements("Item").All(texture => Value(texture, "texId") == (rigid ? "1" : "0"))
            && infos[0].Element("pedXml_expressionMods")?.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).All(value => value == "0") == true;
    }

    public static void Apply(string output, Dictionary<Guid, ClothingIdentityEntry> identities, IReadOnlyList<ClothingModelReuse> reuses,
        Func<string, string> resolve)
    {
        foreach (var group in reuses.GroupBy(reuse => (identities[reuse.TargetId].Model, identities[reuse.TargetId].Collection)))
        {
            var fullName = group.Key.Model + "_" + group.Key.Collection;
            var ymtPath = Directory.EnumerateFiles(Path.Combine(output, "stream"), fullName + ".ymt", SearchOption.AllDirectories).Single();
            var document = CollectionInsertion.ReadYmt(ymtPath);
            var shopPath = Directory.EnumerateFiles(output, fullName + "_shop.meta", SearchOption.AllDirectories).Single();
            var shop = XDocument.Load(shopPath);
            foreach (var reuse in group)
            {
                var entry = identities[reuse.TargetId];
                var drawables = CollectionInsertion.Drawables(document, entry.Slot);
                var texData = drawables.Elements("Item").ElementAt(entry.Index).Element("aTexData")!;
                var start = texData.Elements("Item").Count();
                if (start + reuse.Incoming.TexturePaths.Count > ClothingModelMatcher.TextureCapacity)
                    throw new InvalidDataException("Texture reuse exceeds A-Z capacity.");
                var code = CollectionIndices.CodeOf(reuse.Incoming.Category);
                var templates = shop.Root!.Element("pedComponents")!.Elements("Item").Where(item =>
                    item.Element("eCompType")?.Value == "PV_COMP_" + code.ToUpperInvariant()
                    && item.Element("localDrawableIndex")?.Attribute("value")?.Value == entry.Index.ToString()).ToArray();
                if (templates.Length == 0 || !texData.Elements("Item").Any()) throw new InvalidDataException("Reuse target has no texture metadata.");
                var textures = entry.Textures.ToList();
                for (var i = 0; i < reuse.Incoming.TexturePaths.Count; i++)
                {
                    var index = start + i;
                    // The published target's skin mode determines the native dictionary name.
                    var suffix = entry.ModelFile?.EndsWith("_r.ydd", StringComparison.OrdinalIgnoreCase) == true ? "whi" : "uni";
                    var name = fullName + "^" + code + "_diff_" + entry.Index.ToString("D3") + "_" + (char)('a' + index) + "_" + suffix + ".ytd";
                    var path = Path.Combine(Path.GetDirectoryName(ymtPath)!, name);
                    if (Directory.EnumerateFiles(output, name, SearchOption.AllDirectories).Any()) throw new IOException("Texture filename already occupied: " + name);
                    File.Copy(resolve(reuse.Incoming.TexturePaths[i]), path);
                    if (!AssetContent.Equivalent(resolve(reuse.Incoming.TexturePaths[i]), path)) throw new IOException("Texture copy failed: " + name);
                    var nativeTexture = new XElement(texData.Elements("Item").First());
                    nativeTexture.Element("distribution")!.SetAttributeValue("value", 255);
                    texData.Add(nativeTexture);
                    var component = new XElement(templates[0]);
                    component.Element("textureIndex")!.SetAttributeValue("value", index);
                    component.Element("uniqueNameHash")!.Value = "PCS_" + entry.Id.ToString("N") + "_" + reuse.Incoming.TextureIds[i].ToString("N");
                    component.Element("cost")?.SetAttributeValue("value", reuse.Incoming.ShopCost);
                    if (component.Element("textLabel") is { } label) label.Value = reuse.Incoming.ShopTextLabel;
                    if (component.Element("lockHash") is { } lockHash) lockHash.Value = reuse.Incoming.ShopLockHash;
                    shop.Root.Element("pedComponents")!.Add(component);
                    textures.Add(new(reuse.Incoming.TextureIds[i], index, name, true));
                }
                drawables.Parent!.Element("numAvailTex")!.SetAttributeValue("value",
                    drawables.Elements("Item").Sum(item => item.Element("aTexData")!.Elements("Item").Count()) & 255);
                identities[entry.Id] = entry with { Textures = textures };
            }
            var xml = new XmlDocument(); xml.LoadXml(document.ToString());
            File.WriteAllBytes(ymtPath, ResourceBuilder.Build(XmlMeta.GetMeta(xml), 2));
            if (!XNode.DeepEquals(document.Root, CollectionInsertion.ReadYmt(ymtPath).Root))
                throw new InvalidDataException("Reused texture YMT failed round-trip verification.");
            shop.Save(shopPath);
        }
    }
}
