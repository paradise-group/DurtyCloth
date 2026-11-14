using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using FooxClothStudio;

internal static class InsertionTests
{
    public static void Run(string root)
    {
        Directory.CreateDirectory(root);
        ClothItem Item(string name, int sourceIndex)
        {
            var directory = Path.Combine(root, "input", name);
            Directory.CreateDirectory(directory);
            var model = Path.Combine(directory, $"mp_m_freemode_01_input^jbib_{sourceIndex:D3}_u.ydd");
            var texture = Path.Combine(directory, $"mp_m_freemode_01_input^jbib_diff_{sourceIndex:D3}_a_uni.ytd");
            File.WriteAllBytes(model, [(byte)sourceIndex, 2, 3]);
            File.WriteAllBytes(texture, [(byte)sourceIndex, 5, 6]);
            return ClothCatalog.Import([model, texture]).Single();
        }
        var baseline = new ClothProject { Name = "insert_fixture", DlcName = "insert_fixture", UsePermanentClothingIds = true,
            Items = [Item("old0", 0), Item("old1", 1), Item("old2", 2)] };
        var source = AddonExporter.Export(baseline, Path.GetFullPath, Path.Combine(root, "baseline"));
        var originalMap = ClothingIdentityMap.Read(Path.Combine(source, ClothingIdentityMap.FileName));
        var project = Import(source);
        var target = project.Items.Single(item => item.Id == baseline.Items[1].Id);
        var added = Item("new", 5);
        added.InsertBeforeItemId = target.Id;
        project.Items.Add(added);
        project.UsePermanentClothingIds = true;
        var inputSnapshot = JsonSerializer.Serialize(project);
        var output = ResourceUpdateExporter.Export(project, Path.GetFullPath, source, Path.Combine(root, "after"));
        Check(JsonSerializer.Serialize(project) == inputSnapshot, "Insertion mutated the source project");
        Verify(source, output, originalMap, project, target.Id, added.Id, 1);
        var reimport = Import(output);
        Check(reimport.Items.Single(item => item.Id == target.Id).SourceIndex == 2, "Reimport lost shifted original identity");
        Check(reimport.Items.Single(item => item.Id == added.Id).SourceIndex == 1, "Reimport lost inserted identity");
        var third = Item("third", 6);
        third.InsertBeforeItemId = target.Id;
        reimport.Items.Add(third);
        reimport.UsePermanentClothingIds = true;
        var secondOutput = ResourceUpdateExporter.Export(reimport, Path.GetFullPath, output, Path.Combine(root, "after-second"));
        var secondMap = ClothingIdentityMap.Read(Path.Combine(secondOutput, ClothingIdentityMap.FileName));
        Check(secondMap.Entries.Single(entry => entry.Id == target.Id).Index == 3, "Repeated insertion did not advance the same UUID again");
        Check(secondMap.Entries.Single(entry => entry.Id == added.Id).Index == 1, "Repeated insertion moved an item before the requested index");

        var invalid = JsonSerializer.Deserialize<ClothProject>(inputSnapshot)!;
        invalid.UsePermanentClothingIds = false;
        Reject(() => ResourceUpdateExporter.Export(invalid, Path.GetFullPath, source, Path.Combine(root, "legacy-rejected")), "permanent IDs");
        invalid.UsePermanentClothingIds = true;
        invalid.Items.Single(item => item.Id == added.Id).InsertBeforeItemId = Guid.NewGuid();
        Reject(() => ResourceUpdateExporter.Export(invalid, Path.GetFullPath, source, Path.Combine(root, "target-rejected")), "Insertion target");
        invalid.Items.Single(item => item.Id == added.Id).InsertBeforeItemId = target.Id;
        invalid.Items.Single(item => item.Id == added.Id).Gender = "Feminino";
        Reject(() => ResourceUpdateExporter.Export(invalid, Path.GetFullPath, source, Path.Combine(root, "gender-rejected")), "same gender");
        var ymt = Directory.EnumerateFiles(Path.Combine(source, "stream"), "*.ymt").Single();
        var savedYmt = File.ReadAllBytes(ymt);
        File.Delete(ymt);
        Reject(() => ResourceUpdateExporter.Export(project, Path.GetFullPath, source, Path.Combine(root, "ymt-rejected")), "collection YMT");
        File.WriteAllBytes(ymt, savedYmt);
        Check(!Directory.Exists(Path.Combine(root, "legacy-rejected", "insert_fixture")), "Rejected insertion left a resource behind");
        var firstMapBytes = File.ReadAllBytes(Path.Combine(output, ClothingIdentityMap.FileName));
        try { ResourceUpdateExporter.Export(project, Path.GetFullPath, source, Path.GetDirectoryName(output)!); throw new Exception("Existing insertion output overwritten"); }
        catch (IOException) { }
        Check(firstMapBytes.SequenceEqual(File.ReadAllBytes(Path.Combine(output, ClothingIdentityMap.FileName))), "Existing output changed after rejection");
        var simultaneous = Import(source);
        var additional = Item("simultaneous", 7);
        additional.InsertBeforeItemId = target.Id;
        additional.ExportPosition = 1;
        var firstAdded = Item("simultaneous-first", 8);
        firstAdded.InsertBeforeItemId = target.Id;
        firstAdded.ExportPosition = 0;
        simultaneous.Items.AddRange([firstAdded, additional]);
        var multipleOutput = ResourceUpdateExporter.Export(simultaneous, Path.GetFullPath, source, Path.Combine(root, "multiple"));
        var multipleMap = ClothingIdentityMap.Read(Path.Combine(multipleOutput, ClothingIdentityMap.FileName));
        Check(multipleMap.Entries.Single(entry => entry.Id == firstAdded.Id).Index == 1
            && multipleMap.Entries.Single(entry => entry.Id == additional.Id).Index == 2
            && multipleMap.Entries.Single(entry => entry.Id == target.Id).Index == 3, "Simultaneous native insertions lost their requested order");
        var full = new ClothProject { Name = "full_fixture", DlcName = "full_fixture", UsePermanentClothingIds = true };
        for (var index = 0; index < CollectionIndices.DrawablesPerSlot; index++) full.Items.Add(Item("full" + index, index));
        var fullSource = AddonExporter.Export(full, Path.GetFullPath, Path.Combine(root, "full-baseline"));
        var fullProject = Import(fullSource);
        var overCapacity = Item("over-capacity", 200);
        overCapacity.InsertBeforeItemId = fullProject.Items[0].Id;
        fullProject.Items.Add(overCapacity);
        Reject(() => ResourceUpdateExporter.Export(fullProject, Path.GetFullPath, fullSource, Path.Combine(root, "capacity-rejected")), "capacity");
        Console.WriteLine("PASS: real native insertion shifts indices, model/texture bytes, YMT and shop references while retaining UUIDs; reimport, repeated insertions and unsafe-target guards.");
    }

    public static ClothProject Import(string source)
    {
        var items = ClothCatalog.Import(Directory.EnumerateFiles(Path.Combine(source, "stream"), "*", SearchOption.AllDirectories));
        ClothingIdentityMap.Restore(source, items);
        return new() { Name = Path.GetFileName(source), SourceResourceRoot = source, UsePermanentClothingIds = true, Items = items };
    }

    public static void Verify(string source, string output, ClothingIdentityDocument before, ClothProject project,
        Guid targetId, Guid addedId, int insertionIndex)
    {
        var map = ClothingIdentityMap.Read(Path.Combine(output, ClothingIdentityMap.FileName));
        var old = before.Entries.Single(entry => entry.Id == targetId);
        var shifted = map.Entries.Single(entry => entry.Id == targetId);
        var inserted = map.Entries.Single(entry => entry.Id == addedId);
        Check(shifted.Index == insertionIndex + 1 && shifted.Collection == old.Collection, "Target was not shifted in the same collection");
        Check(inserted.Index == insertionIndex && inserted.Collection == old.Collection, "New item did not take the target's previous native index");
        Check(shifted.Textures.Select(texture => texture.Id).SequenceEqual(old.Textures.Select(texture => texture.Id)), "Saved texture UUIDs changed");
        var sourceFiles = Directory.EnumerateFiles(Path.Combine(source, "stream"), "*", SearchOption.AllDirectories).GroupBy(path => Path.GetFileName(path)).ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var outputFiles = Directory.EnumerateFiles(Path.Combine(output, "stream"), "*", SearchOption.AllDirectories).GroupBy(path => Path.GetFileName(path)).ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var entry in before.Entries)
        {
            var current = map.Entries.Single(candidate => candidate.Id == entry.Id);
            Check(current.Index == (entry.Model == old.Model && entry.Collection == old.Collection && entry.Slot == old.Slot && entry.Kind == old.Kind && entry.Index >= insertionIndex ? entry.Index + 1 : entry.Index), "Unexpected native index shift");
            if (entry.ModelFile is not null) Check(AssetContent.Equivalent(sourceFiles[entry.ModelFile], outputFiles[current.ModelFile!]), "Original model bytes were not retained at the new index");
            foreach (var texture in entry.Textures.Where(texture => texture.File is not null))
                Check(AssetContent.Equivalent(sourceFiles[texture.File!], outputFiles[current.Textures.Single(candidate => candidate.Id == texture.Id).File!]), "Original texture bytes lost their UUID association");
        }
        var sourceYmt = Directory.EnumerateFiles(Path.Combine(source, "stream"), old.Model + "_" + old.Collection + ".ymt", SearchOption.AllDirectories).Single();
        var outputYmt = outputFiles[Path.GetFileName(sourceYmt)];
        var originalXml = CollectionInsertion.ReadYmt(sourceYmt);
        var outputXml = CollectionInsertion.ReadYmt(outputYmt);
        var previous = CollectionInsertion.Drawables(originalXml, old.Slot).Elements("Item").ToArray();
        var next = CollectionInsertion.Drawables(outputXml, old.Slot).Elements("Item").ToArray();
        Check(next.Length == previous.Length + 1, "Native drawable count did not increase");
        for (var index = 0; index < previous.Length; index++)
            Check(XNode.DeepEquals(previous[index], next[index >= insertionIndex ? index + 1 : index]), "Original drawable metadata changed beyond insertion");
        var insertedItem = project.Items.Single(item => item.Id == addedId);
        Check(next[insertionIndex].Element("aTexData")!.Elements("Item").Count() == insertedItem.TexturePaths.Count, "Inserted texture count is incorrect");
        var shop = Directory.EnumerateFiles(output, "*.meta", SearchOption.AllDirectories).Select(XDocument.Load)
            .Single(document => document.Root?.Name == "ShopPedApparel" && document.Root.Element("dlcName")?.Value == old.Collection);
        Check(shop.Root!.Element("pedComponents")!.Elements("Item").Any(item => item.Element("localDrawableIndex")?.Attribute("value")?.Value == insertionIndex.ToString()), "Shop metadata does not expose the inserted index");
        var reimport = Import(output);
        Check(map.Entries.Count == before.Entries.Count + 1 && reimport.Items.Select(item => item.Id).ToHashSet().SetEquals(project.Items.Select(item => item.Id)), "Insertion/reimport lost or duplicated UUIDs");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Reject(Action action, string message)
    {
        try { action(); throw new Exception("Unsafe insertion was accepted"); }
        catch (InvalidDataException error) { Check(error.Message.Contains(message, StringComparison.OrdinalIgnoreCase), "Unexpected rejection: " + error.Message); }
    }
}
