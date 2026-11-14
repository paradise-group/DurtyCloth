using System.IO;
using System.Text.RegularExpressions;

namespace FooxClothStudio;

public static class OriginalResourceExporter
{
    public static string? FindSource(ClothProject project, string? source)
    {
        static bool Valid(string? path) => !string.IsNullOrWhiteSpace(path) && File.Exists(Path.Combine(path, "fxmanifest.lua")) && Directory.Exists(Path.Combine(path, "stream"));
        if (Valid(source)) return source;
        if (Valid(project.SourceResourceRoot)) return project.SourceResourceRoot;
        if (project.Name != Path.GetFileName(project.Name) || project.Name is "." or "..") return null;
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, project.Name);
            if (Valid(candidate)) return candidate;
        }
        return null;
    }

    public static ClothingIdentityDocument Describe(ClothProject project)
    {
        ClothingIdentityMap.GenerateMissing(project);
        var entries = new List<ClothingIdentityEntry>();
        var addressIds = new Dictionary<(string, string, int, string, int), Guid>();
        foreach (var item in project.Items)
        {
            if (item.IsDummy) throw new InvalidDataException("The original-resource build cannot apply dummy replacements. Use the rebuilt addon mode for asset changes.");
            var model = item.Gender == "Masculino" ? "mp_m_freemode_01" : item.Gender == "Feminino" ? "mp_f_freemode_01" : throw new InvalidDataException("Undefined ped model: " + item.Name);
            var file = Path.GetFileName(item.OriginalModelFileName ?? item.ModelPath);
            var code = CollectionIndices.CodeOf(item.Category);
            var prop = code.StartsWith("p_");
            var components = new[] { "head", "berd", "hair", "uppr", "lowr", "hand", "feet", "teef", "accs", "task", "decl", "jbib" };
            var props = new Dictionary<string, int> { ["p_head"] = 0, ["p_eyes"] = 1, ["p_ears"] = 2, ["p_mouth"] = 3, ["p_lwrist"] = 6, ["p_rwrist"] = 7 };
            var slot = prop ? props.GetValueOrDefault(code, -1) : Array.IndexOf(components, code);
            var split = file.Split('^');
            var index = ClothCatalog.ResolveType(file).Index;
            if (split.Length != 2 || index is null || slot < 0 || !split[0].StartsWith(model, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Cannot identify the original collection/index: " + file);
            if (CollectionIndices.CodeOf(ClothCatalog.ResolveType(file).Category) != code)
                throw new InvalidDataException("The project changed the original model's slot: " + file);
            var prefix = model + (prop ? "_p_" : "_");
            var collection = split[0].Equals(model, StringComparison.OrdinalIgnoreCase) || split[0].Equals(model + "_p", StringComparison.OrdinalIgnoreCase) ? "" :
                split[0].StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? split[0][prefix.Length..] : throw new InvalidDataException("Unexpected source collection: " + file);
            var kind = prop ? "prop" : "component";
            var address = (model, kind, slot, collection, index.Value);
            var standardModelName = !Regex.IsMatch(file, @"\^(?:p_[a-z]+|[a-z]{4})_diff_", RegexOptions.IgnoreCase);
            Guid? alias = standardModelName && addressIds.TryGetValue(address, out var canonical) ? canonical : null;
            if (standardModelName) addressIds.TryAdd(address, item.Id);
            var textures = new List<ClothingTextureIdentity>();
            var textureIndexes = new Dictionary<int, Guid>();
            for (var position = 0; position < item.TexturePaths.Count; position++)
            {
                var name = Path.GetFileName(item.OriginalTextureFileNames.ElementAtOrDefault(position) ?? item.TexturePaths[position]);
                var match = Regex.Match(name, @"\^(?:p_[a-z]+|[a-z]{4})_(?:diff_)?\d+(?:_([a-z]))?(?:_|\.|$)", RegexOptions.IgnoreCase);
                if (!match.Success || string.IsNullOrWhiteSpace(item.TexturePaths[position]))
                    throw new InvalidDataException("Cannot preserve this texture position: " + name);
                var textureIndex = match.Groups[1].Success ? char.ToLowerInvariant(match.Groups[1].Value[0]) - 'a' : 0;
                if (!name.Contains("_diff_", StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(name, @"_[ur]\.ytd$", RegexOptions.IgnoreCase) && item.TexturePaths.Count == 1) textureIndex = 0;
                Guid? textureAlias = textureIndexes.TryGetValue(textureIndex, out var first) ? first : null;
                textureIndexes.TryAdd(textureIndex, item.TextureIds[position]);
                textures.Add(new(item.TextureIds[position], textureIndex, name, true, AliasOf: textureAlias));
            }
            // Source metadata remains authoritative for inherited game textures.
            // IDs cover the byte-sized native slots; the adapter checks the actual
            // native texture count before resolving/applying any of them.
            if (textures.Count == 0)
                for (var texture = 0; texture < 256; texture++)
                    textures.Add(new(ClothingIdentityMap.InheritedTextureId(item.Id, texture), texture, null, true, Inherited: true));
            entries.Add(new(item.Id, item.Name, item.Gender, model, kind, slot, collection, index.Value, file, standardModelName,
                textures, file, index, alias));
        }
        var document = new ClothingIdentityDocument(1, entries, PreserveSourceLayout: true);
        ClothingIdentityMap.Validate(document);
        return document;
    }

    public static ClothingIdentityDocument Validate(ClothProject project, Func<string, string> resolve, string sourceRoot,
        IProgress<string>? progress = null)
    {
        var source = FiveMResource.Open(sourceRoot);
        var document = Describe(project);
        var existingMap = Path.Combine(source.Root, ClothingIdentityMap.FileName);
        if (!File.Exists(existingMap) && new[] { "clothing-ids.client.lua", "CLOTHING-IDS.md", "ORIGINAL-LAYOUT.txt" }
            .Any(name => File.Exists(Path.Combine(source.Root, name))))
            throw new InvalidDataException("The source contains reserved ID-integration filenames without a valid identity map. They will not be overwritten.");
        if (File.Exists(existingMap))
        {
            var existing = ClothingIdentityMap.Read(existingMap).Entries.Where(entry => entry.ModelFile is not null)
                .ToDictionary(entry => entry.ModelFile!, StringComparer.OrdinalIgnoreCase);
            foreach (var entry in document.Entries)
                if (entry.ModelFile is not null && existing.TryGetValue(entry.ModelFile, out var old))
                {
                    if (old.Id != entry.Id)
                        throw new InvalidDataException("The original resource already has a different permanent ID for " + entry.ModelFile + ". Reimport its identity map before exporting.");
                    var knownTextures = old.Textures.ToDictionary(texture => (texture.Index, texture.File));
                    foreach (var texture in entry.Textures)
                        if (knownTextures.TryGetValue((texture.Index, texture.File), out var previous) && previous.Id != texture.Id)
                            throw new InvalidDataException("The original resource already has a different permanent texture ID: " + texture.File);
                }
        }
        var sourceFiles = Directory.EnumerateFiles(Path.Combine(source.Root, "stream"), "*", SearchOption.AllDirectories)
            .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key!, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var sourceModels = sourceFiles.Keys.Where(name => name.EndsWith(".ydd", StringComparison.OrdinalIgnoreCase) &&
            !Regex.IsMatch(Path.GetFileNameWithoutExtension(name), @"_(?:u|r)_[1-3]$", RegexOptions.IgnoreCase)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!sourceModels.SetEquals(document.Entries.Select(entry => entry.ModelFile!)))
            throw new InvalidDataException("The project's model list differs from the original resource. This mode preserves the complete original pack; use rebuilt addon mode for added/removed models.");
        var checkedAssets = new HashSet<(string, string)>();
        foreach (var item in project.Items)
        {
            progress?.Report("Verifying original assets: " + item.Name);
            var modelName = Path.GetFileName(item.OriginalModelFileName ?? item.ModelPath);
            var assets = new List<(string Path, string Name)> { (item.ModelPath, modelName) };
            assets.AddRange(item.TexturePaths.Select((path, index) => (path, Path.GetFileName(item.OriginalTextureFileNames.ElementAtOrDefault(index) ?? path))));
            assets.AddRange(item.AlternateModelPaths.Select((path, index) => (path, Path.GetFileNameWithoutExtension(modelName) + "_" + (index + 1) + ".ydd")));
            if (item.PhysicsPath is not null)
            {
                var savedName = Path.GetFileName(item.PhysicsPath);
                var candidates = new[] { savedName, savedName.StartsWith("physics_", StringComparison.OrdinalIgnoreCase) ? savedName[8..] : savedName,
                    Path.GetFileNameWithoutExtension(modelName) + ".yld", Regex.Replace(Path.GetFileNameWithoutExtension(modelName), @"_(?:u|r)$", "") + ".yld" };
                assets.Add((item.PhysicsPath, candidates.FirstOrDefault(sourceFiles.ContainsKey) ?? candidates[^1]));
            }
            foreach (var asset in assets)
            {
                if (!checkedAssets.Add((asset.Path, asset.Name))) continue;
                var projectAsset = resolve(asset.Path);
                if (!File.Exists(projectAsset))
                    throw new FileNotFoundException("Project asset is missing: " + projectAsset + ". Restore the project's assets folder or reopen the original FiveM resource. No output was generated.", projectAsset);
                if (!sourceFiles.TryGetValue(asset.Name, out var candidates) || !candidates.Any(candidate => AssetContent.Equivalent(projectAsset, candidate)))
                    throw new InvalidDataException("Project asset differs from the original resource: " + asset.Name + ". Use rebuilt addon mode to apply asset edits.");
            }
        }
        return document;
    }

    public static string Export(ClothProject project, Func<string, string> resolve, string sourceRoot, string parent,
        IProgress<string>? progress = null)
    {
        var source = FiveMResource.Open(sourceRoot);
        var document = Validate(project, resolve, source.Root, progress);
        var parentPath = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var output = Path.GetFullPath(Path.Combine(parentPath, new DirectoryInfo(source.Root).Name));
        if (!output.StartsWith(parentPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The output must stay inside the selected build folder.");
        progress?.Report("Copying and verifying the complete original resource...");
        source.Export(output);
        try
        {
            ClothingIdentityMap.Write(document, output);
            var manifest = Path.Combine(output, "fxmanifest.lua");
            var text = File.ReadAllText(manifest);
            if (!text.Contains("clothing-ids.client.lua", StringComparison.Ordinal))
                File.AppendAllText(manifest, "\n-- Permanent IDs added by Paradise Cloth Studio; original clothing layout retained.\nfiles { 'clothing-identities.json' }\nclient_script 'clothing-ids.client.lua'\n");
            File.WriteAllText(Path.Combine(output, "ORIGINAL-LAYOUT.txt"),
                "Original collections, local indices, YMT, models, textures and tattoo assets are retained.\n" +
                "Only permanent-ID integration files and a manifest declaration have been added.\n" +
                "Source _u/_r models or _uni/_whi textures sharing one native address are explicit aliases in the map.\n" +
                "Saved character/wardrobe data still needs adapter integration before it can be stored by UUID.\n" +
                $"{document.Entries.Count(entry => !entry.Available)} nonstandard source model names were preserved as files but are not exposed as equippable IDs; verify their original metadata/naming before assigning a native address.\n");
            project.SourceResourceRoot = source.Root;
            return output;
        }
        catch
        {
            // output was newly created by FiveMResource.Export and is outside source.Root.
            if (Directory.Exists(output)) Directory.Delete(output, true);
            throw;
        }
    }
}
