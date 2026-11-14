using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using CodeWalker.GameFiles;
using FooxClothStudio;

internal static class InsertionReal
{
    private static readonly Guid TargetId = Guid.Parse("1caae2da-d370-7042-d403-5f2273a6c8bb");
    private static readonly Guid TextureId = Guid.Parse("0f83f7f3-26c3-37c7-dac1-bb7d14b5a6a8");

    public static void CheckMetadata(string source, string output)
    {
        var filename = "mp_m_freemode_01_mp_m_meta_02.ymt";
        var before = CollectionInsertion.ReadYmt(Directory.EnumerateFiles(Path.Combine(source, "stream"), filename, SearchOption.AllDirectories).Single());
        var after = CollectionInsertion.ReadYmt(Directory.EnumerateFiles(Path.Combine(output, "stream"), filename, SearchOption.AllDirectories).Single());
        foreach (var element in before.Root!.Elements().Where(element => element.Name != "aComponentData3" && element.Name != "compInfos"))
            if (!XNode.DeepEquals(element, after.Root!.Element(element.Name))) throw new Exception("Unrelated YMT structure changed: " + element.Name);
        var beforeGroups = before.Root.Element("aComponentData3")!.Elements("Item").ToArray();
        var afterGroups = after.Root!.Element("aComponentData3")!.Elements("Item").ToArray();
        var availability = before.Root.Element("availComp")!.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
        for (var group = 0; group < beforeGroups.Length; group++)
        {
            if (group != availability[11])
            {
                if (!XNode.DeepEquals(beforeGroups[group], afterGroups[group])) throw new Exception("Unrelated component metadata changed");
                continue;
            }
            var previous = beforeGroups[group].Element("aDrawblData3")!.Elements("Item").ToArray();
            var current = afterGroups[group].Element("aDrawblData3")!.Elements("Item").ToArray();
            if (current.Length != previous.Length + 1) throw new Exception("Incorrect native component count");
            for (var index = 0; index < previous.Length; index++)
                if (!XNode.DeepEquals(previous[index], current[index < 101 ? index : index + 1])) throw new Exception("Original drawable metadata not preserved at shifted index");
        }
        foreach (var info in before.Root.Element("compInfos")!.Elements("Item"))
        {
            var expected = new XElement(info);
            var slot = int.Parse(expected.Element("pedXml_compIdx")!.Attribute("value")!.Value);
            var index = int.Parse(expected.Element("pedXml_drawblIdx")!.Attribute("value")!.Value);
            if (slot == 11 && index >= 101) expected.Element("pedXml_drawblIdx")!.SetAttributeValue("value", index + 1);
            if (!after.Root.Element("compInfos")!.Elements("Item").Any(current => XNode.DeepEquals(expected, current)))
                throw new Exception("Original component flags/audio/metadata lost after index shift");
        }
        var map = ClothingIdentityMap.Read(Path.Combine(output, ClothingIdentityMap.FileName));
        var added = map.Entries.Single(entry => entry.Id == Guid.Parse("af3c706b-f571-7b0b-15cf-99349b42dfaf"));
        var files = Directory.EnumerateFiles(Path.Combine(output, "stream"), "*", SearchOption.AllDirectories).ToDictionary(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase);
        var model = new YddFile();
        model.Load(File.ReadAllBytes(files[added.ModelFile!]));
        if (model.Drawables is not { Length: > 0 }) throw new Exception("Inserted YDD is invalid");
        foreach (var texture in added.Textures)
        {
            var dictionary = new YtdFile();
            dictionary.Load(File.ReadAllBytes(files[texture.File!]));
            if (dictionary.TextureDict?.Textures?.data_items is not { Length: > 0 }) throw new Exception("Inserted YTD is invalid");
        }
        Console.WriteLine("PASS: actual YMT preserves unrelated components, props, every shifted drawable's metadata and original component flags/audio; inserted YDD and five YTD files parse successfully.");
    }

    public static void Run(string source, string snapshot, string incoming, string output, bool prepareOnly)
    {
        source = Path.GetFullPath(source);
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(output);
        var project = JsonSerializer.Deserialize<ClothProject>(File.ReadAllText(snapshot))!;
        project.UsePermanentClothingIds = true;
        var original = OriginalResourceExporter.Describe(project);
        var target = original.Entries.Single(entry => entry.Id == TargetId);
        if (target.Collection != "mp_m_meta_02" || target.Index != 101 || !target.Textures.Any(texture => texture.Id == TextureId))
            throw new Exception("Source snapshot does not match the player's confirmed addon top");
        var added = ClothCatalog.Import(Directory.EnumerateFiles(Path.GetFullPath(incoming), "*", SearchOption.AllDirectories))
            .Single(item => item.Gender == "Masculino" && CollectionIndices.CodeOf(item.Category) == "jbib");
        added.InsertBeforeItemId = TargetId;
        added.ExportPosition = target.Index;
        project.Items.Add(added);
        var plan = ResourceUpdateExporter.Plan(project, Path.GetFullPath, source);
        if (prepareOnly)
        {
            var prepared = CollectionInsertion.Prepare(project, plan, source, Path.GetFullPath, output, GamePedAssets.FindGameFolder());
            var shifted = prepared.Single().Entries.Single(entry => entry.Id == TargetId);
            var inserted = prepared.Single().Entries.Single(entry => entry.Id == added.Id);
            if (shifted.Index != 102 || inserted.Index != 101 || shifted.Collection != target.Collection || shifted.Textures[0].Id != TextureId)
                throw new Exception("Prepared native insertion lost the saved clothing reference");
            File.WriteAllText(Path.Combine(output, "prepared.json"), JsonSerializer.Serialize(prepared, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("PASS: actual addon insertion prepared: old UUID 101 -> 102; new clothing -> 101, same mp_m_meta_02 collection and texture UUIDs.");
            return;
        }
        Console.WriteLine("Preparing actual insert-before export: mp_m_meta_02:101 -> 102.");
        var progress = new InsertionProgress();
        var exported = ResourceUpdateExporter.Export(project, Path.GetFullPath, source, output, progress, GamePedAssets.FindGameFolder());
        var map = ClothingIdentityMap.Read(Path.Combine(exported, ClothingIdentityMap.FileName));
        var sourceFiles = Directory.EnumerateFiles(Path.Combine(source, "stream"), "*", SearchOption.AllDirectories)
            .GroupBy(path => Path.GetFileName(path)).ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var outputFiles = Directory.EnumerateFiles(Path.Combine(exported, "stream"), "*", SearchOption.AllDirectories)
            .GroupBy(path => Path.GetFileName(path)).ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var entry in original.Entries)
        {
            var current = map.Entries.Single(candidate => candidate.Id == entry.Id);
            var expected = entry.Model == target.Model && entry.Collection == target.Collection && entry.Kind == target.Kind && entry.Slot == target.Slot && entry.Index >= target.Index ? entry.Index + 1 : entry.Index;
            if (current.Index != expected || current.Collection != entry.Collection) throw new Exception("Unexpected native address change: " + entry.Name);
            if (entry.ModelFile is not null && !AssetContent.Equivalent(sourceFiles[entry.ModelFile], outputFiles[current.ModelFile!]))
                throw new Exception("Model bytes changed or were not moved correctly: " + entry.Name);
            foreach (var texture in entry.Textures.Where(texture => texture.File is not null))
                if (!AssetContent.Equivalent(sourceFiles[texture.File!], outputFiles[current.Textures.Single(candidate => candidate.Id == texture.Id).File!]))
                    throw new Exception("Texture bytes lost their saved UUID association: " + entry.Name);
        }
        var oldEntry = map.Entries.Single(entry => entry.Id == TargetId);
        var newEntry = map.Entries.Single(entry => entry.Id == added.Id);
        if (oldEntry.Index != 102 || newEntry.Index != 101 || oldEntry.Textures[0].Id != TextureId || map.Entries.Count != original.Entries.Count + 1)
            throw new Exception("Final native insertion failed identity validation");
        var ymt = outputFiles[target.Model + "_" + target.Collection + ".ymt"];
        if (CollectionInsertion.Drawables(CollectionInsertion.ReadYmt(ymt), target.Slot).Elements("Item").Count() != 121)
            throw new Exception("Output YMT does not declare 121 native tops");
        File.WriteAllText(Path.Combine(output, "project-insert-before.fcsproj"), JsonSerializer.Serialize(project, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(output, "verified-insertion.json"), JsonSerializer.Serialize(new
        {
            resource = exported, collection = target.Collection, oldItemId = TargetId, textureId = TextureId,
            before = 101, after = 102, newItemId = added.Id, newIndex = 101, originalItems = original.Entries.Count, totalItems = map.Entries.Count
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS: actual pack exported and all original model/texture bytes verified by UUID. New shirt is mp_m_meta_02:101; player's shirt is mp_m_meta_02:102 with its saved UUID pair unchanged.");
    }

    private sealed class InsertionProgress : IProgress<string>
    {
        private int count;
        public void Report(string message)
        {
            if (!message.StartsWith("Verifying original assets:") || ++count % 500 == 0) Console.WriteLine(message);
        }
    }
}
