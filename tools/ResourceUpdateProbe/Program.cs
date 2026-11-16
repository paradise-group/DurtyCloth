using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using FooxClothStudio;
using CodeWalker.GameFiles;
using System.Xml.Linq;

try
{
    if (args.Length == 1 && args[0] == "--eta-test")
    {
        ProgressEstimateTests.Run();
        return;
    }
    if (args.Length == 3 && args[0] == "--verify-insertion-metadata")
    {
        InsertionReal.CheckMetadata(args[1], args[2]);
        return;
    }
    if (args.Length == 5 && args[0] is "--insert-real" or "--prepare-insertion")
    {
        InsertionReal.Run(args[1], args[2], args[3], args[4], args[0] == "--prepare-insertion");
        return;
    }
    if (args.Length == 2 && args[0] == "--insert-test")
    {
        InsertionTests.Run(Path.GetFullPath(args[1]));
        return;
    }

    if (args.Length == 3 && args[0] == "--inspect-ymt")
    {
        var ymt = new YmtFile();
        ymt.Load(File.ReadAllBytes(args[1]));
        var xml = MetaXml.GetXml(ymt.Meta);
        File.WriteAllText(args[2], xml);
        var metadataRoot = XDocument.Parse(xml).Root!;
        Console.WriteLine(metadataRoot.Name + " " + metadataRoot.Attribute("name"));
        Console.WriteLine("availComp: " + metadataRoot.Element("availComp")?.Value);
        var ordinal = 0;
        foreach (var component in metadataRoot.Element("aComponentData3")!.Elements("Item"))
            Console.WriteLine($"Group {ordinal++}: {component.Element("aDrawblData3")!.Elements("Item").Count()} drawables");
        Console.WriteLine(metadataRoot.Element("aSelectionSets"));
        return;
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    ClothProject Import(string source)
    {
        var items = ClothCatalog.Import(Directory.EnumerateFiles(Path.Combine(source, "stream"), "*", SearchOption.AllDirectories));
        ClothingIdentityMap.Restore(source, items);
        return new() { Name = Path.GetFileName(source), SourceResourceRoot = source, Items = items };
    }

    if (args.Length == 3 && args[0] == "--prepare-identities")
    {
        var source = Path.GetFullPath(args[1]);
        var destination = Path.GetFullPath(args[2]);
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
            throw new IOException("The preparation folder must be empty.");
        var project = Import(source);
        var document = OriginalResourceExporter.Validate(project, Path.GetFullPath, source);
        Directory.CreateDirectory(destination);
        ClothingIdentityMap.Write(document, destination);
        var manifest = File.ReadAllText(Path.Combine(source, "fxmanifest.lua"));
        if (!manifest.Contains("clothing-identities.json", StringComparison.Ordinal))
            manifest += "\nfiles { 'clothing-identities.json' }\n";
        if (!manifest.Contains("clothing-ids.client.lua", StringComparison.Ordinal))
            manifest += "client_script 'clothing-ids.client.lua'\n";
        File.WriteAllText(Path.Combine(destination, "fxmanifest.lua"), manifest);
        var reread = ClothingIdentityMap.Read(Path.Combine(destination, ClothingIdentityMap.FileName));
        Check(JsonSerializer.Serialize(document) == JsonSerializer.Serialize(reread), "Prepared identities did not round-trip.");
        Console.WriteLine($"PASS: {document.Entries.Count} items, {document.Entries.Sum(entry => entry.Textures.Count)} texture identities, " +
            $"{document.Entries.Count(entry => entry.Kind == "component" && entry.Slot == 2)} hair items; source assets and addresses preserved.");
        return;
    }

    void Verify(string source, string output, ClothingIdentityDocument original, int added)
    {
        foreach (var path in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, path);
            if (relative.Split(Path.DirectorySeparatorChar)[0].Equals(".git", StringComparison.OrdinalIgnoreCase)) continue;
            if (relative is "fxmanifest.lua" or "clothing-identities.json" or "clothing-ids.client.lua" or "CLOTHING-IDS.md" or "ORIGINAL-LAYOUT.txt") continue;
            Check(AssetContent.Equivalent(path, Path.Combine(output, relative)), "Original file changed: " + relative);
        }
        var map = ClothingIdentityMap.Read(Path.Combine(output, ClothingIdentityMap.FileName));
        Check(map.Entries.Count == original.Entries.Count + added, "Combined identity count incorrect");
        foreach (var entry in original.Entries)
            Check(JsonSerializer.Serialize(map.Entries.Single(current => current.Id == entry.Id)) == JsonSerializer.Serialize(entry),
                "Original native address or texture UUID changed: " + entry.Name);
        var nativeFiles = Directory.EnumerateFiles(Path.Combine(output, "stream"), "*", SearchOption.AllDirectories)
            .Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in map.Entries)
        {
            Check(entry.ModelFile is null || nativeFiles.Contains(entry.ModelFile), "Model declared by map missing");
            foreach (var texture in entry.Textures)
                Check(texture.File is null || nativeFiles.Contains(texture.File), "Texture declared by map missing");
        }
        var manifest = File.ReadAllText(Path.Combine(output, "fxmanifest.lua"));
        foreach (Match declaration in Regex.Matches(manifest, @"(?m)^data_file\s+'([^']+)'\s+'([^']+)'\s*$"))
            Check(File.Exists(Path.Combine(output, declaration.Groups[2].Value)), "Declared metadata missing");
    }

    var root = args.Length == 0 ? Path.Combine(Path.GetTempPath(), "ResourceUpdateProbe", Guid.NewGuid().ToString("N")) : Path.GetFullPath(args[^1]);
    Directory.CreateDirectory(root);
    if (args.Length is 4 or 5 && args[0] == "--real")
    {
        var source = Path.GetFullPath(args[1]);
        var project = args.Length == 5
            ? JsonSerializer.Deserialize<ClothProject>(File.ReadAllText(args[3]))!
            : Import(source);
        Console.WriteLine($"Loaded {project.Items.Count} original items; preparing the update.");
        var original = OriginalResourceExporter.Describe(project);
        var incoming = ClothCatalog.Import(Directory.EnumerateFiles(Path.GetFullPath(args[2]), "*", SearchOption.AllDirectories));
        var additions = incoming.Where(item => item.Gender == "Masculino" && CollectionIndices.CodeOf(item.Category) == "jbib").ToArray();
        Check(additions.Length == 1, "Expected one male test top");
        additions[0].ExportPosition = 0;
        project.Items.AddRange(additions);
        var report = new StageProgress();
        var output = ResourceUpdateExporter.Export(project, Path.GetFullPath, source, root, report, GamePedAssets.FindGameFolder());
        Verify(source, output, original, additions.Length);
        var map = ClothingIdentityMap.Read(Path.Combine(output, ClothingIdentityMap.FileName));
        var old = map.Entries.Single(entry => entry.Id == Guid.Parse("d6c55f77-12a7-8d35-68dc-d7385fb5309b"));
        Check(old.Textures.Any(texture => texture.Id == Guid.Parse("c25ecaa7-fdbb-d86b-b422-5699197bd9d6")), "Persisted texture UUID lost");
        File.WriteAllText(Path.Combine(root, "project-with-addition.fcsproj"), JsonSerializer.Serialize(project, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS: actual resource retained {original.Entries.Count} identities and all original asset bytes; added {additions.Length} top with {additions[0].TexturePaths.Count} textures. {output}");
        return;
    }

    Check(args.Length <= 1, "Use an empty fixture folder, or --real resourceRoot incomingFolder outputParent");
    var sourceRoot = Path.Combine(root, "original");
    var stream = Path.Combine(sourceRoot, "stream");
    Directory.CreateDirectory(stream);
    Directory.CreateDirectory(Path.Combine(sourceRoot, ".git"));
    File.WriteAllText(Path.Combine(sourceRoot, ".git", "private-history"), "repository data is not a FiveM asset");
    File.WriteAllText(Path.Combine(sourceRoot, "fxmanifest.lua"), "fx_version 'cerulean'\ngame 'gta5'\n");
    File.WriteAllBytes(Path.Combine(stream, "original.ymt"), [1, 2, 3]);
    File.WriteAllBytes(Path.Combine(stream, "tattoo.ytd"), [4, 5, 6]);

    ClothItem Item(string folder, string collection, int index)
    {
        Directory.CreateDirectory(folder);
        var model = Path.Combine(folder, $"mp_m_freemode_01_{collection}^jbib_{index:D3}_u.ydd");
        var texture = Path.Combine(folder, $"mp_m_freemode_01_{collection}^jbib_diff_{index:D3}_a_uni.ytd");
        File.WriteAllBytes(model, [1, 2, 3]);
        File.WriteAllBytes(texture, [4, 5, 6]);
        return ClothCatalog.Import([model, texture]).Single();
    }

    var oldItem = Item(stream, "original", 4);
    var fixture = new ClothProject { Name = "original", Items = [oldItem], SourceResourceRoot = sourceRoot };
    var baseline = OriginalResourceExporter.Describe(fixture);
    ClothingIdentityMap.Write(baseline, sourceRoot);
    File.AppendAllText(Path.Combine(sourceRoot, "fxmanifest.lua"), "files { 'clothing-identities.json' }\nclient_script 'clothing-ids.client.lua'\n");
    var unchanged = new ClothProject { Name = "original", Items = [oldItem] };
    var unchangedOutput = ResourceUpdateExporter.Export(unchanged, Path.GetFullPath, sourceRoot, Path.Combine(root, "unchanged"));
    Check(unchanged.SourceResourceRoot == sourceRoot, "Unchanged export did not retain its source folder");
    Verify(sourceRoot, unchangedOutput, baseline, 0);
    var addedItem = Item(Path.Combine(root, "incoming"), "incoming", 0);
    addedItem.ExportPosition = 0;
    fixture.Items.Add(addedItem);
    var before = JsonSerializer.Serialize(fixture);
    var updated = ResourceUpdateExporter.Export(fixture, Path.GetFullPath, sourceRoot, Path.Combine(root, "first"));
    Verify(sourceRoot, updated, baseline, 1);
    Check(!Directory.Exists(Path.Combine(updated, ".git")), "Repository metadata was copied into the game resource");
    Check(JsonSerializer.Serialize(fixture) == before, "Export modified project item identities or assets");
    var imported = Import(updated);
    Check(imported.Items.Select(item => item.Id).ToHashSet().SetEquals(fixture.Items.Select(item => item.Id)), "Reimport lost IDs");
    var addedAgain = Item(Path.Combine(root, "next-incoming"), "second", 0);
    imported.Items.Add(addedAgain);
    var importedMap = OriginalResourceExporter.Describe(new() { Items = imported.Items.Where(item => item.Id != addedAgain.Id).ToList() });
    var second = ResourceUpdateExporter.Export(imported, Path.GetFullPath, updated, Path.Combine(root, "second"));
    Verify(updated, second, importedMap, 1);

    var rejected = JsonSerializer.Deserialize<ClothProject>(before)!;
    rejected.Items.RemoveAt(0);
    try { ResourceUpdateExporter.Plan(rejected, Path.GetFullPath, sourceRoot); throw new Exception("Removed source item accepted"); }
    catch (InvalidDataException) { }
    rejected = JsonSerializer.Deserialize<ClothProject>(before)!;
    rejected.Items[0].Id = Guid.NewGuid();
    try { ResourceUpdateExporter.Plan(rejected, Path.GetFullPath, sourceRoot); throw new Exception("Changed published UUID accepted"); }
    catch (InvalidDataException) { }
    rejected = JsonSerializer.Deserialize<ClothProject>(before)!;
    rejected.Items[1].Id = rejected.Items[0].Id;
    try { ResourceUpdateExporter.Plan(rejected, Path.GetFullPath, sourceRoot); throw new Exception("Duplicate UUID accepted"); }
    catch (InvalidDataException) { }
    var assetGuard = Import(updated);
    assetGuard.Items.Single(item => item.Id == oldItem.Id).ModelPath = oldItem.ModelPath;
    var safeBytes = File.ReadAllBytes(oldItem.ModelPath);
    File.WriteAllBytes(oldItem.ModelPath, [99]);
    try { ResourceUpdateExporter.Plan(assetGuard, Path.GetFullPath, updated); throw new Exception("Changed original asset accepted"); }
    catch (InvalidDataException error) { Check(error.Message.Contains("Project asset differs"), "Wrong asset guard failed"); }
    File.WriteAllBytes(oldItem.ModelPath, safeBytes);

    var beforeExistingDestination = File.ReadAllText(Path.Combine(updated, "fxmanifest.lua"));
    try { ResourceUpdateExporter.Export(fixture, Path.GetFullPath, sourceRoot, Path.GetDirectoryName(updated)!); throw new Exception("Existing destination overwritten"); }
    catch (IOException) { }
    Check(File.ReadAllText(Path.Combine(updated, "fxmanifest.lua")) == beforeExistingDestination, "Existing destination changed on rejection");

    var orderFirst = Item(Path.Combine(root, "ordered-incoming"), "ordered", 0);
    var orderSecond = Item(Path.Combine(root, "ordered-incoming"), "ordered", 1);
    orderFirst.ExportPosition = 0;
    orderSecond.ExportPosition = 1;
    var orderedProject = new ClothProject { Name = "original", Items = [oldItem, orderFirst, orderSecond] };
    var orderedBefore = ResourceUpdateExporter.Export(orderedProject, Path.GetFullPath, sourceRoot, Path.Combine(root, "order-before"));
    orderSecond.ExportPosition = 0;
    orderFirst.ExportPosition = 1;
    orderFirst.TexturePaths.Add(orderFirst.TexturePaths[0]);
    orderFirst.OriginalTextureFileNames.Add(orderFirst.OriginalTextureFileNames[0]);
    orderFirst.TextureIds.Add(Guid.NewGuid());
    orderFirst.TexturePaths.Reverse();
    orderFirst.OriginalTextureFileNames.Reverse();
    orderFirst.TextureIds.Reverse();
    var orderedAfter = ResourceUpdateExporter.Export(orderedProject, Path.GetFullPath, sourceRoot, Path.Combine(root, "order-after"));
    var mapBefore = ClothingIdentityMap.Read(Path.Combine(orderedBefore, ClothingIdentityMap.FileName));
    var mapAfter = ClothingIdentityMap.Read(Path.Combine(orderedAfter, ClothingIdentityMap.FileName));
    var entryBefore = mapBefore.Entries.Single(entry => entry.Id == orderFirst.Id);
    var entryAfter = mapAfter.Entries.Single(entry => entry.Id == orderFirst.Id);
    Check(entryBefore.Collection == entryAfter.Collection && entryBefore.Index == 0 && entryAfter.Index == 1,
        "Added clothes cannot be reordered within their stable generated collection");
    Check(entryAfter.Textures.Single(texture => texture.Id == entryBefore.Textures[0].Id).Index == 1,
        "Reordered added textures lost their UUIDs");

    ClothItem RawItem(string folder, string gender, string code, int index)
    {
        Directory.CreateDirectory(folder);
        var model = Path.Combine(folder, $"{code}_{index:D3}_u.ydd");
        var texture = Path.Combine(folder, $"{code}_diff_{index:D3}_a_uni.ytd");
        File.WriteAllBytes(model, [1, 2, 3]);
        File.WriteAllBytes(texture, [4, 5, 6]);
        var item = ClothCatalog.Import([model, texture]).Single();
        item.Gender = gender;
        item.Id = Guid.NewGuid();
        item.TextureIds.Clear();
        ClothingIdentityMap.EnsureTextures(item);
        return item;
    }

    var raw = new ClothProject { Name = "original", Items = [oldItem] };
    foreach (var gender in new[] { "Masculino", "Feminino" })
    {
        var folder = Path.Combine(root, "raw-incoming", gender);
        foreach (var code in new[] { "jbib", "lowr", "p_head" })
        for (var index = 0; index < 45; index++)
            raw.Items.Add(RawItem(folder, gender, code, index));
        raw.Items.Add(RawItem(Path.Combine(folder, "department"), gender, "jbib", 0));
    }
    var rawOutput = ResourceUpdateExporter.Export(raw, Path.GetFullPath, sourceRoot, Path.Combine(root, "raw"));
    var rawMap = ClothingIdentityMap.Read(Path.Combine(rawOutput, ClothingIdentityMap.FileName));
    var rawAdded = rawMap.Entries.Where(entry => entry.Id != oldItem.Id).ToArray();
    Check(rawAdded.GroupBy(entry => entry.Collection).Count() == 2
        && rawAdded.GroupBy(entry => entry.Gender).All(group => group.Select(entry => entry.Collection).Distinct().Count() == 1),
        "Raw models must share one collection per gender across slots, filenames and folders");
    const string metadataDeclaration = "files { 'data/pcs/*' }";
    Check(File.ReadAllLines(Path.Combine(rawOutput, "fxmanifest.lua")).Count(line => line == metadataDeclaration) == 1,
        "Added metadata glob must be declared exactly once");
    Check(File.ReadAllLines(Path.Combine(second, "fxmanifest.lua")).Count(line => line == metadataDeclaration) == 1,
        "Existing metadata glob must not be duplicated");
    var rawReversed = new ClothProject { Name = "original", Items = raw.Items.AsEnumerable().Reverse().ToList() };
    var rawRepeated = ResourceUpdateExporter.Export(rawReversed, Path.GetFullPath, sourceRoot, Path.Combine(root, "raw-repeated"));
    var repeatedMap = ClothingIdentityMap.Read(Path.Combine(rawRepeated, ClothingIdentityMap.FileName));
    foreach (var entry in rawAdded)
    {
        var repeated = repeatedMap.Entries.Single(item => item.Id == entry.Id);
        Check(entry.Collection == repeated.Collection && entry.Index == repeated.Index, "Input order changed raw collection addresses");
    }
    Verify(sourceRoot, rawOutput, baseline, raw.Items.Count - 1);

    var paged = new ClothProject { Name = "original", Items = [oldItem] };
    for (var index = 0; index <= CollectionIndices.DrawablesPerSlot; index++)
        paged.Items.Add(Item(Path.Combine(root, "many-incoming"), "paged", index));
    var pagedOutput = ResourceUpdateExporter.Export(paged, Path.GetFullPath, sourceRoot, Path.Combine(root, "paged"));
    var pagedMap = ClothingIdentityMap.Read(Path.Combine(pagedOutput, ClothingIdentityMap.FileName));
    Check(pagedMap.Entries.Where(entry => entry.Id != oldItem.Id).GroupBy(entry => entry.Collection).Count() == 2, "Large additions not split into native-capacity collections");
    Check(pagedMap.Entries.Where(entry => entry.Id != oldItem.Id).GroupBy(entry => (entry.Collection, entry.Kind, entry.Slot))
        .All(group => group.Count() <= CollectionIndices.DrawablesPerSlot), "Collection exceeds per-slot drawable capacity");
    Verify(sourceRoot, pagedOutput, baseline, CollectionIndices.DrawablesPerSlot + 1);
    Console.WriteLine("PASS: incremental additions, metadata declarations, original bytes and UUIDs, repeated update/reimport, removal/asset/ID guards and collection capacity paging.");

}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    Environment.ExitCode = 1;
}

sealed class StageProgress : IProgress<string>
{
    private int count;
    public void Report(string message)
    {
        if (!message.StartsWith("Verifying original assets:") || ++count % 500 == 0) Console.WriteLine(message);
    }
}
