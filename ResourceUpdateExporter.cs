using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FooxClothStudio;

public sealed record ResourceUpdatePlan(ClothProject Original, ClothingIdentityDocument OriginalIdentities,
    List<ClothItem> AddedItems);

public static class ResourceUpdateExporter
{
    public static ResourceUpdatePlan Plan(ClothProject project, Func<string, string> resolve, string sourceRoot,
        IProgress<string>? progress = null)
    {
        var snapshot = JsonSerializer.Deserialize<ClothProject>(JsonSerializer.Serialize(project))!;
        ClothingIdentityMap.GenerateMissing(snapshot);
        var originalFiles = Directory.EnumerateFiles(Path.Combine(sourceRoot, "stream"), "*.ydd", SearchOption.AllDirectories)
            .Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var additions = snapshot.Items.Where(item => !originalFiles.Contains(ModelName(item))).ToList();
        snapshot.Items = snapshot.Items.Except(additions).ToList();
        var original = OriginalResourceExporter.Validate(snapshot, resolve, sourceRoot, progress);
        if (additions.Any(item => item.IsDummy))
            throw new InvalidDataException("Resource updates require real clothing assets for added items.");
        return new(snapshot, original, additions);
    }

    public static string Export(ClothProject project, Func<string, string> resolve, string sourceRoot, string parent,
        IProgress<string>? progress = null, string? gameFolder = null)
    {
        var source = FiveMResource.Open(sourceRoot);
        var plan = Plan(project, resolve, source.Root, progress);
        if (plan.AddedItems.Count == 0)
        {
            var unchanged = OriginalResourceExporter.Export(plan.Original, resolve, source.Root, parent, progress);
            project.SourceResourceRoot = source.Root;
            return unchanged;
        }

        var temporaryParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ParadiseClothStudio", "resource-updates"));
        var temporary = Path.Combine(temporaryParent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        string? createdOutput = null;
        try
        {
            progress?.Report("Preparing explicit insertions into existing addon collections...");
            var insertions = CollectionInsertion.Prepare(project, plan, source.Root, resolve, temporary, gameFolder);
            var exports = BuildAdditions(project, plan.AddedItems.Where(item => item.InsertBeforeItemId is null).ToList(), resolve, temporary, gameFolder, progress);
            var combined = plan.OriginalIdentities.Entries.ToDictionary(entry => entry.Id);
            foreach (var insertion in insertions)
                foreach (var entry in insertion.Entries) combined[entry.Id] = entry;
            foreach (var entry in exports.SelectMany(export => export.Identities.Entries)) combined.Add(entry.Id, entry);
            var identities = new ClothingIdentityDocument(1, combined.Values.ToList(), true);
            ClothingIdentityMap.Validate(identities);
            ValidateNames(source.Root, exports);
            var output = Path.GetFullPath(Path.Combine(parent, new DirectoryInfo(source.Root).Name));
            progress?.Report("Copying original collections, tattoos and metadata...");
            source.Export(output);
            createdOutput = output;
            var manifest = Path.Combine(output, "fxmanifest.lua");
            foreach (var insertion in insertions)
            {
                progress?.Report("Applying native index shifts and updated collection metadata...");
                CollectionInsertion.Apply(insertion, output);
            }
            const string metadataDeclaration = "files { 'data/pcs/*' }";
            if (exports.Count > 0 && !File.ReadAllLines(manifest).Contains(metadataDeclaration, StringComparer.Ordinal))
                File.AppendAllText(manifest, "\n" + metadataDeclaration + "\n");
            foreach (var export in exports)
            {
                progress?.Report("Adding clothing collection: " + export.Name);
                Merge(export.Root, output);
                File.AppendAllText(manifest, "\n" + export.Declarations);
            }
            ClothingIdentityMap.Write(identities, output);
            if (!File.ReadAllText(manifest).Contains("clothing-ids.client.lua", StringComparison.Ordinal))
                File.AppendAllText(manifest, "\nfiles { 'clothing-identities.json' }\nclient_script 'clothing-ids.client.lua'\n");
            File.WriteAllText(Path.Combine(output, "ORIGINAL-LAYOUT.txt"),
                "Original collections are retained. Explicit insert-before operations shift only the requested addon components and update their YMT, shop metadata, native filenames and identity map.\n" +
                "Other additions are generated in separate collections inside this same resource.\n" +
                "Existing item and texture UUIDs are retained, including shifted items.\n" +
                "clothing-identities.json combines the original and added item/texture UUIDs.\n" +
                $"Original items: {plan.OriginalIdentities.Entries.Count}; added items: {plan.AddedItems.Count}; native insertions: {insertions.Sum(insertion => insertion.AddedIds.Count)}.\n");
            project.SourceResourceRoot = source.Root;
            return output;
        }
        catch
        {
            if (createdOutput is not null && Directory.Exists(createdOutput))
                Directory.Delete(createdOutput, true);
            throw;
        }
        finally
        {
            if (Path.GetFullPath(temporary).StartsWith(temporaryParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(temporary))
                Directory.Delete(temporary, true);
        }
    }

    private sealed record AddedExport(string Name, string Root, ClothingIdentityDocument Identities, string Declarations);

    private static List<AddedExport> BuildAdditions(ClothProject profile, List<ClothItem> additions, Func<string, string> resolve,
        string temporary, string? gameFolder, IProgress<string>? progress)
    {
        var result = new List<AddedExport>();
        var groups = additions.GroupBy(item => (item.Gender, Prefix: SourceCollection(item)))
            .OrderBy(group => group.Key.Gender, StringComparer.Ordinal).ThenBy(group => group.Key.Prefix, StringComparer.Ordinal);
        foreach (var group in groups)
        foreach (var page in group.GroupBy(item => CollectionIndices.CodeOf(item.Category))
            .SelectMany(slot => slot.OrderBy(item => item.Id).Select((item, index) => new { Item = item, Page = index / CollectionIndices.DrawablesPerSlot }))
            .GroupBy(entry => entry.Page).OrderBy(page => page.Key)
            .Select(page => page.Select(entry => entry.Item).OrderBy(item => item.Id).ToArray()))
        {
            var name = "pcs_" + page[0].Id.ToString("N");
            var project = new ClothProject
            {
                Name = name, DlcName = name, UsePermanentClothingIds = true, Items = page.ToList(),
                TargetGameBuild = profile.TargetGameBuild, UseTextureNameAsUniqueHash = profile.UseTextureNameAsUniqueHash,
                DisableExtraAlternateVariationFeatures = profile.DisableExtraAlternateVariationFeatures,
                DisableExtraYmtFiles = profile.DisableExtraYmtFiles
            };
            progress?.Report($"Preparing {page.Length} added clothing item(s)...");
            var errors = BuildDependencies.Prepare(project, resolve, gameFolder);
            if (errors.Count > 0)
                throw new InvalidDataException(string.Join("\n", errors));
            var root = AddonExporter.Export(project, resolve, temporary);
            var identities = ClothingIdentityMap.Read(Path.Combine(root, ClothingIdentityMap.FileName));
            var metadataRoot = "data/pcs/";
            var declarations = "";
            foreach (Match declaration in Regex.Matches(File.ReadAllText(Path.Combine(root, "fxmanifest.lua")),
                @"(?m)^data_file\s+'([^']+)'\s+'([^']+)'\s*$"))
                declarations += "data_file '" + declaration.Groups[1].Value + "' '" + metadataRoot + MetadataName(root, declaration.Groups[2].Value) + "'\n";
            result.Add(new(name, root, identities, declarations));
        }
        return result;
    }

    private static readonly HashSet<string> IntegrationFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "fxmanifest.lua", "clothing-identities.json", "clothing-ids.client.lua", "CLOTHING-IDS.md", "collection-index.json", "clothes.json"
    };

    private static IEnumerable<(string Source, string Relative)> FilesToMerge(string root)
    {
        foreach (var source in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, source);
            if (Path.GetDirectoryName(relative) is "" && IntegrationFiles.Contains(relative))
                continue;
            yield return (source, relative.StartsWith("stream" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                ? Path.Combine("stream", "pcs", Path.GetFileName(source))
                : Path.Combine("data", "pcs", MetadataName(root, source)));
        }
    }

    private static void ValidateNames(string sourceRoot, List<AddedExport> exports)
    {
        var names = Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var export in exports)
        foreach (var file in FilesToMerge(export.Root))
            if (!names.Add(Path.GetFileName(file.Relative)))
                throw new InvalidDataException("An added collection conflicts with an existing resource filename: " + Path.GetFileName(file.Source));
    }

    private static void Merge(string root, string output)
    {
        foreach (var file in FilesToMerge(root))
        {
            var destination = Path.Combine(output, file.Relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file.Source, destination);
        }
    }

    private static string SourceCollection(ClothItem item)
    {
        var name = ModelName(item);
        var separator = name.IndexOf('^');
        return separator < 0 ? "" : name[..separator].ToLowerInvariant();
    }

    private static string ModelName(ClothItem item) => Path.GetFileName(item.OriginalModelFileName ?? item.ModelPath);

    private static string MetadataName(string root, string path)
    {
        var name = Path.GetFileName(path);
        var collection = new DirectoryInfo(root).Name;
        return name.Contains(collection, StringComparison.OrdinalIgnoreCase) ? name : collection + "_" + name;
    }
}
