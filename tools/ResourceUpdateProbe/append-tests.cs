using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using FooxClothStudio;

internal static class AppendTests
{
    private const int UpperSlot = 3;
    private const int LowerSlot = 4;
    private const int AccessorySlot = 8;
    private const int TopSlot = 11;

    public static void Run(string root)
    {
        Directory.CreateDirectory(root);
        ClothItem Item(string name, string code, int sourceIndex, string gender = "m")
        {
            var directory = Path.Combine(root, "input", name);
            Directory.CreateDirectory(directory);
            var model = Path.Combine(directory, $"mp_{gender}_freemode_01_input^{code}_{sourceIndex:D3}_u.ydd");
            var texture = Path.Combine(directory, $"mp_{gender}_freemode_01_input^{code}_diff_{sourceIndex:D3}_a_uni.ytd");
            File.WriteAllBytes(model, [(byte)sourceIndex, (byte)name.Length, 3]);
            File.WriteAllBytes(texture, [(byte)sourceIndex, (byte)name.Length, 6]);
            return ClothCatalog.Import([model, texture]).Single();
        }
        string Update(string source, string output, bool append, params ClothItem[] added)
        {
            var project = InsertionTests.Import(source);
            project.Items.AddRange(added);
            return ResourceUpdateExporter.Export(project, Path.GetFullPath, source, Path.Combine(root, output), appendToLatestCollection: append);
        }

        var baseline = new ClothProject { Name = "append_fixture", DlcName = "append_fixture", UsePermanentClothingIds = true,
            Items = [Item("base", "jbib", 0)] };
        var addon = AddonExporter.Export(baseline, Path.GetFullPath, Path.Combine(root, "baseline"));

        var firstTop = Item("first-top", "jbib", 5);
        var firstLower = Item("first-lower", "lowr", 5);
        var first = Update(addon, "first", true, firstTop, firstLower);
        var firstMap = ClothingIdentityMap.Read(Path.Combine(first, ClothingIdentityMap.FileName));
        var collection = firstMap.Entries.Single(entry => entry.Id == firstTop.Id).Collection;
        Check(collection.StartsWith("mp_m_pcs_", StringComparison.Ordinal)
            && firstMap.Entries.Single(entry => entry.Id == firstLower.Id).Collection == collection,
            "Without a pcs collection the update must create one collection for the gender");

        var top = Item("top", "jbib", 6);
        var accessory = Item("accessory", "accs", 6);
        var upper = Item("upper", "uppr", 6);
        var femaleTop = Item("female-top", "jbib", 6, "f");
        var second = Update(first, "second", true, top, accessory, upper, femaleTop);
        var secondMap = ClothingIdentityMap.Read(Path.Combine(second, ClothingIdentityMap.FileName));
        string Address(ClothingIdentityEntry entry) => JsonSerializer.Serialize(new { entry.Model, entry.Kind, entry.Slot, entry.Collection, entry.Index,
            entry.ModelFile, Textures = entry.Textures.Select(texture => new { texture.Id, texture.Index, texture.File }) });
        foreach (var entry in firstMap.Entries)
            Check(Address(secondMap.Entries.Single(current => current.Id == entry.Id)) == Address(entry), "Append changed an existing identity or native address");
        ClothingIdentityEntry Entry(ClothingIdentityDocument map, ClothItem item) => map.Entries.Single(entry => entry.Id == item.Id);
        Check(Entry(secondMap, top) is { Index: 1, Slot: TopSlot } appendedTop && appendedTop.Collection == collection, "Top was not appended after the last native index");
        Check(Entry(secondMap, accessory) is { Index: 0, Slot: AccessorySlot } addedAccessory && addedAccessory.Collection == collection, "A missing component was not added to the collection");
        Check(Entry(secondMap, upper) is { Index: 0, Slot: UpperSlot } addedUpper && addedUpper.Collection == collection, "A missing leading component was not added to the collection");
        var femaleCollection = Entry(secondMap, femaleTop).Collection;
        Check(femaleCollection.StartsWith("mp_f_pcs_", StringComparison.Ordinal), "A gender without a pcs collection must receive a new one");
        Check(secondMap.Entries.Count == firstMap.Entries.Count + 4, "Append lost or duplicated identities");

        var ymtName = "mp_m_freemode_01_" + collection + ".ymt";
        var before = CollectionInsertion.ReadYmt(Directory.EnumerateFiles(first, ymtName, SearchOption.AllDirectories).Single());
        var after = CollectionInsertion.ReadYmt(Directory.EnumerateFiles(second, ymtName, SearchOption.AllDirectories).Single());
        Check(Directory.EnumerateFiles(second, "mp_m_freemode_01_mp_m_pcs_*.ymt", SearchOption.AllDirectories).Count() == 1, "Append created another male collection");
        Check(after.Root!.Element("availComp")!.Value == "255 255 255 0 1 255 255 255 2 255 255 3", "Component availability was not rebuilt in slot order: " + after.Root.Element("availComp")!.Value);
        int Count(XDocument document, int slot) => CollectionInsertion.Drawables(document, slot).Elements("Item").Count();
        Check(Count(after, TopSlot) == 2 && Count(after, LowerSlot) == 1 && Count(after, AccessorySlot) == 1 && Count(after, UpperSlot) == 1, "Drawable counts are incorrect after append");
        foreach (var slot in new[] { TopSlot, LowerSlot })
        {
            var previous = CollectionInsertion.Drawables(before, slot).Elements("Item").ToArray();
            var next = CollectionInsertion.Drawables(after, slot).Elements("Item").ToArray();
            for (var index = 0; index < previous.Length; index++)
                Check(XNode.DeepEquals(previous[index], next[index]), "Existing drawable metadata changed during append");
        }
        var infos = after.Root.Element("compInfos")!.Elements("Item")
            .Select(info => (Slot: int.Parse(info.Element("pedXml_compIdx")!.Attribute("value")!.Value), Index: int.Parse(info.Element("pedXml_drawblIdx")!.Attribute("value")!.Value))).ToArray();
        Check(infos.SequenceEqual(infos.OrderBy(info => info.Slot).ThenBy(info => info.Index)) && infos.Distinct().Count() == 5, "Component infos are not unique and ordered");

        var shop = Directory.EnumerateFiles(second, "*.meta", SearchOption.AllDirectories).Select(XDocument.Load)
            .Single(document => document.Root?.Name == "ShopPedApparel" && document.Root.Element("dlcName")?.Value == collection);
        var shopItems = shop.Root!.Element("pedComponents")!.Elements("Item").ToArray();
        Check(shopItems.Length == 5 && shopItems.Select(item => item.Element("uniqueNameHash")!.Value).Distinct().Count() == 5, "Shop metadata does not list each drawable once");
        Check(shopItems.Count(item => item.Element("eCompType")!.Value == "PV_COMP_JBIB" && item.Element("localDrawableIndex")!.Attribute("value")!.Value == "1") == 1, "Shop metadata does not expose the appended index");

        var files = Directory.EnumerateFiles(Path.Combine(second, "stream"), "*", SearchOption.AllDirectories).ToArray();
        foreach (var entry in secondMap.Entries)
        {
            Check(files.Count(file => Path.GetFileName(file).Equals(entry.ModelFile, StringComparison.OrdinalIgnoreCase)) == 1, "Mapped model is missing or duplicated: " + entry.ModelFile);
            foreach (var texture in entry.Textures.Where(texture => texture.File is not null))
                Check(files.Count(file => Path.GetFileName(file).Equals(texture.File, StringComparison.OrdinalIgnoreCase)) == 1, "Mapped texture is missing or duplicated: " + texture.File);
        }
        var firstFiles = Directory.EnumerateFiles(Path.Combine(first, "stream"), "*", SearchOption.AllDirectories).Where(file => !file.EndsWith(ymtName, StringComparison.OrdinalIgnoreCase));
        foreach (var file in firstFiles)
            Check(File.ReadAllBytes(file).SequenceEqual(File.ReadAllBytes(Path.Combine(second, Path.GetRelativePath(first, file)))), "Append changed an existing stream file: " + Path.GetFileName(file));
        Check(Path.GetDirectoryName(files.Single(file => Path.GetFileName(file).Equals(Entry(secondMap, top).ModelFile, StringComparison.OrdinalIgnoreCase)))
            == Path.GetDirectoryName(Directory.EnumerateFiles(second, ymtName, SearchOption.AllDirectories).Single()), "Appended files must sit beside the collection YMT");
        var manifest = File.ReadAllText(Path.Combine(second, "fxmanifest.lua"));
        Check(manifest.Split(collection + "_shop.meta").Length == 2 && manifest.Split("files { 'data/pcs/*' }").Length == 2, "Manifest declarations were duplicated");

        var reimport = InsertionTests.Import(second);
        Check(reimport.Items.Select(item => item.Id).ToHashSet().SetEquals(secondMap.Entries.Select(entry => entry.Id)), "Reimport lost or duplicated UUIDs");
        Check(reimport.Items.Single(item => item.Id == top.Id).SourceIndex == 1, "Reimport lost the appended native index");

        var thirdTop = Item("third-top", "jbib", 7);
        var thirdMap = ClothingIdentityMap.Read(Path.Combine(Update(second, "third", true, thirdTop), ClothingIdentityMap.FileName));
        Check(Entry(thirdMap, thirdTop) is { Index: 2 } repeated && repeated.Collection == collection, "A second append did not continue after the previous one");

        var separate = Item("separate", "jbib", 8);
        var separateMap = ClothingIdentityMap.Read(Path.Combine(Update(second, "separate", false, separate), ClothingIdentityMap.FileName));
        Check(Entry(separateMap, separate) is { Index: 0 } isolated && isolated.Collection != collection, "Without append the update must keep creating a new collection");

        var filling = Enumerable.Range(0, CollectionIndices.DrawablesPerSlot - 2).Select(index => Item("fill" + index, "jbib", 20 + index)).ToArray();
        var full = Update(second, "full", true, filling);
        var fullMap = ClothingIdentityMap.Read(Path.Combine(full, ClothingIdentityMap.FileName));
        Check(filling.All(item => Entry(fullMap, item).Collection == collection) && fullMap.Entries.Count(entry => entry.Collection == collection && entry.Slot == TopSlot) == CollectionIndices.DrawablesPerSlot,
            "Append must fill the collection up to its native capacity");
        var overflowTop = Item("overflow-top", "jbib", 200);
        var overflowLower = Item("overflow-lower", "lowr", 200);
        var overflowMap = ClothingIdentityMap.Read(Path.Combine(Update(full, "overflow", true, overflowTop, overflowLower), ClothingIdentityMap.FileName));
        var overflowCollection = Entry(overflowMap, overflowTop).Collection;
        Check(overflowCollection != collection && overflowCollection.StartsWith("mp_m_pcs_", StringComparison.Ordinal)
            && Entry(overflowMap, overflowLower).Collection == overflowCollection && Entry(overflowMap, overflowTop).Index == 0,
            "Exceeding 128 drawables in a slot must open one new collection for the gender");
        Console.WriteLine("PASS: append adds components after the last native index of the latest pcs collection, creates missing components, keeps existing identities, files and drawable metadata, and opens a new collection only without a target or beyond capacity.");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
