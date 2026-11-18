using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using FooxClothStudio;

internal static class ReuseTests
{
    public static void Run(string root)
    {
        if (Directory.Exists(root)) throw new IOException("Use a fresh output folder.");
        Directory.CreateDirectory(root);
        ClothItem Item(string name, int count, byte model = 7, string gender = "Masculino")
        {
            var folder = Path.Combine(root, "inputs", name); Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "jbib_001_u.ydd"); File.WriteAllBytes(path, [model, 8, 9]);
            var item = new ClothItem { Name = name, Gender = gender, Category = "Blusas", ModelPath = path, OriginalModelFileName = Path.GetFileName(path) };
            // Use the importer's category spelling rather than relying on a display label.
            item.Category = ClothCatalog.ResolveType(path).Category;
            for (var i = 0; i < count; i++)
            {
                var texture = Path.Combine(folder, $"jbib_diff_001_{(char)('a' + i)}_uni.ytd");
                File.WriteAllBytes(texture, [(byte)i, (byte)name.Length, 6]);
                item.TexturePaths.Add(texture); item.OriginalTextureFileNames.Add(Path.GetFileName(texture));
            }
            ClothingIdentityMap.EnsureTextures(item);
            return item;
        }
        string Baseline(string name, params ClothItem[] items) => AddonExporter.Export(new ClothProject
            { Name = name, DlcName = name, UsePermanentClothingIds = true, Items = items.ToList() }, Path.GetFullPath, Path.Combine(root, name));
        string Update(string source, string name, params ClothItem[] incoming)
        {
            var project = InsertionTests.Import(source); project.Items.AddRange(incoming);
            return ResourceUpdateExporter.Export(project, Path.GetFullPath, source, Path.Combine(root, name), appendToLatestCollection: true);
        }
        ClothingIdentityDocument Map(string path) => ClothingIdentityMap.Read(Path.Combine(path, ClothingIdentityMap.FileName));
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        var originalItem = Item("original", 24);
        var uiOverflow = new ClothProject { Items = [originalItem] };
        Check(CatalogMerger.Merge(uiOverflow, [Item("ui-ten", 10)], Path.GetFullPath, _ => { }).AddedPieces == 1
            && originalItem.TexturePaths.Count == 24, "UI import must keep an overflowing batch together in a new drawable.");
        var uiTarget = Item("ui-target", 24);
        var uiReuse = new ClothProject { Items = [uiTarget] };
        var uiIdentity = uiTarget.Id;
        Check(CatalogMerger.Merge(uiReuse, [Item("ui-two", 2)], Path.GetFullPath, _ => { }).AddedPieces == 0
            && uiTarget.TexturePaths.Count == 26 && uiTarget.Id == uiIdentity, "UI import must reuse identical 3D across folders when the whole batch fits.");
        var source = Baseline("base", originalItem);
        var original = Map(source).Entries.Single() with { Name = "Published label", SourceModelFile = "provenance.ydd", SourceIndex = 123 };
        original = original with { Textures = original.Textures.AsEnumerable().Reverse().ToList() };
        ClothingIdentityMap.Write(new ClothingIdentityDocument(1, [original]), source);
        var ten = Item("ten", 10);
        var overflow = Map(Update(source, "overflow", ten));
        Check(overflow.Entries.Count == 2 && overflow.Entries.Single(entry => entry.Id == original.Id).Textures.Count == 24
            && overflow.Entries.Single(entry => entry.Id == ten.Id).Textures.Count == 10, "Two free slots must not split a ten-texture batch.");
        var two = Item("two", 2);
        var exactOutput = Update(source, "exact", two);
        var exact = Map(exactOutput).Entries.Single();
        Check(exact.Id == original.Id && exact.Index == original.Index && exact.Collection == original.Collection && exact.Textures.Count == 26,
            "Exact-fit batch must reuse the published drawable.");
        Check(original.Textures.All(exact.Textures.Contains) && JsonSerializer.Serialize(exact with { Textures = original.Textures }) == JsonSerializer.Serialize(original),
            "Published records, UUIDs and texture letters must survive reuse.");
        Check(exact.Textures[^2].Index == 24 && exact.Textures[^2].File!.Contains("_y_uni")
            && exact.Textures[^1].Index == 25 && exact.Textures[^1].File!.Contains("_z_uni"), "The last positions must be Y=24 and Z=25.");
        var skin = Item("skin", 1);
        skin.OriginalTextureFileNames[0] = "jbib_diff_001_a_whi.ytd";
        var skinMap = Map(Update(source, "skin", skin));
        Check(skinMap.Entries.Single().Textures.Last().File!.EndsWith("_y_whi.ytd"), "Preserve the incoming skin texture suffix.");
        foreach (var file in Directory.EnumerateFiles(source, "*.ydd", SearchOption.AllDirectories))
            Check(AssetContent.Equivalent(file, Path.Combine(exactOutput, Path.GetRelativePath(source, file))), "Published 3D changed.");
        var ymt = CollectionInsertion.ReadYmt(Directory.EnumerateFiles(exactOutput, "*.ymt", SearchOption.AllDirectories).Single());
        Check(CollectionInsertion.Drawables(ymt, 11).Elements("Item").Single().Element("aTexData")!.Elements("Item").Count() == 26, "Native metadata did not reach Z.");
        var full = Map(Update(exactOutput, "full", Item("another", 1)));
        Check(full.Entries.Count == 2, "Full models need a new drawable.");
        var other = Map(Update(source, "different", Item("different", 1, 77)));
        Check(other.Entries.Count == 2, "Different 3D must not be reused.");
        var female = Map(Update(source, "female", Item("female", 1, 7, "Feminino")));
        Check(female.Entries.Count == 2, "Models cannot be reused across genders.");
        var incompatible = Item("metadata", 1); incompatible.BehaviourFlags = 9;
        Check(Map(Update(source, "metadata", incompatible)).Entries.Count == 2, "Different runtime metadata needs its own drawable.");
        var spare = Item("spare", 15);
        var multiple = Baseline("multiple", originalItem, spare);
        var multipleResult = Map(Update(multiple, "find-room", Item("batch", 10)));
        Check(multipleResult.Entries.Count == 2 && multipleResult.Entries.Single(entry => entry.Id == spare.Id).Textures.Count == 25,
            "Search another identical drawable when the first cannot fit the whole batch.");
        var concurrent = Map(Update(source, "two-batches", Item("batch-a", 2), Item("batch-b", 2)));
        Check(concurrent.Entries.Count == 2 && concurrent.Entries.Single(entry => entry.Id == original.Id).Textures.Count == 26,
            "Capacity reservations must include earlier batches in the same update.");
        var pendingFirst = Item("pending-first", 2, 90);
        var pendingSecond = Item("pending-second", 3, 90);
        var pendingResult = Map(Update(source, "pending-reuse", pendingFirst, pendingSecond));
        Check(pendingResult.Entries.Count == 2 && pendingResult.Entries.Single(entry => entry.Id == pendingFirst.Id).Textures.Count == 5,
            "Identical new models in the same update must share one drawable when both whole batches fit.");
        var three = Map(Update(source, "three-batches", Item("three-a", 2), Item("three-b", 2), Item("three-c", 2)));
        Check(three.Entries.Count == 2 && three.Entries.Single(entry => entry.Id != original.Id).Textures.Count == 4,
            "After a published drawable fills, subsequent batches must reuse the new drawable.");
        var overflowFirst = Item("pending-24", 24, 91);
        var pendingOverflow = Map(Update(source, "pending-overflow", overflowFirst, Item("pending-10", 10, 91)));
        Check(pendingOverflow.Entries.Count == 3 && pendingOverflow.Entries.Single(entry => entry.Id == overflowFirst.Id).Textures.Count == 24,
            "Pending drawables must also keep a ten-texture batch together when only two positions remain.");
        var rigid = Item("rigid", 1, 90); rigid.OriginalModelFileName = "jbib_001_r.ydd";
        Check(Map(Update(source, "rigid-pending", Item("flexible", 1, 90), rigid)).Entries.Count == 3,
            "Identical bytes with different native rigid masks cannot share a drawable.");
        var pcsItem = Item("pcs", 1, 77);
        var pcsSource = Update(source, "pcs-source", pcsItem);
        var hybridNew = Item("hybrid-new", 1, 78);
        var hybrid = Map(Update(pcsSource, "hybrid", Item("hybrid-reuse", 2, 77), hybridNew));
        var pcsTarget = hybrid.Entries.Single(entry => entry.Id == pcsItem.Id);
        Check(hybrid.Entries.Count == 3 && pcsTarget.Textures.Count == 3
            && hybrid.Entries.Single(entry => entry.Id == hybridNew.Id).Collection == pcsTarget.Collection,
            "Appending a drawable and textures to the same pcs collection must compose without losing either update.");
        Console.WriteLine("PASS: exact 3D comparison, whole-batch capacity, 2+10 overflow, A-Z, gender, multiple targets, reservations, original IDs and assets.");
    }
}
