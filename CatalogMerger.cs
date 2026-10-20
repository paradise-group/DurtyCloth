using System.IO;

namespace FooxClothStudio;

public static class CatalogMerger
{
    public static (int AddedPieces, int AddedTextures, int AddedModels, int AddedPhysics) Merge(
        ClothProject project, IEnumerable<ClothItem> imported,
        Func<string, string> resolvePath, Action<string> validateYtd,
        Func<ClothItem, IEnumerable<string>, string[]>? reviewTextures = null)
    {
        var incoming = imported.ToArray();
        static string Identity(ClothItem item, string path) => Path.GetFullPath(path) + "|" + item.Gender + "|" + item.Category + "|" + item.SourceIndex;
        var existing = project.Items.Where(item => !item.IsDummy).GroupBy(item => Identity(item, resolvePath(item.ModelPath)), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var pending = new List<(ClothItem Target, string[] Textures, string[] Models, string? Physics)>();
        var newPieces = new List<ClothItem>();
        var replaced = new List<ClothItem>();
        foreach (var candidate in incoming)
        {
            var model = Path.GetFullPath(candidate.ModelPath);
            var identity = Identity(candidate, model);
            if (!existing.ContainsKey(identity))
            {
                var possible = existing.Values.Distinct().Where(item => item.Category == candidate.Category && item.Gender == candidate.Gender && item.SourceIndex == candidate.SourceIndex &&
                    ClothCatalog.SameCollection(item.OriginalModelFileName ?? item.ModelPath, candidate.OriginalModelFileName ?? candidate.ModelPath) && File.Exists(resolvePath(item.ModelPath))).ToArray();
                if (possible.Length > 0)
                {
                    static string HashModel(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(file)); }
                    var fingerprint = HashModel(model);
                    var identical = possible.FirstOrDefault(item => HashModel(resolvePath(item.ModelPath)) == fingerprint);
                    if (identical is not null) existing[identity] = identical;
                }
            }
            if (!existing.TryGetValue(identity, out var target))
            {
                if (reviewTextures is not null) candidate.TexturePaths = reviewTextures(new ClothItem { Name = candidate.Name }, candidate.TexturePaths).ToList();
                candidate.OriginalTextureFileNames = candidate.TexturePaths.Select(path => Path.GetFileName(path)).ToList();
                if (candidate.TexturePaths.Count > 26)
                    throw new InvalidDataException("O addon aceita no máximo 26 variantes por peça: " + candidate.Name);
                foreach (var texture in candidate.TexturePaths) validateYtd(texture);
                var group = project.Items.Concat(newPieces).Where(item => item.Gender == candidate.Gender && item.Category == candidate.Category).ToArray();
                candidate.ExportPosition = group.Length == 0 ? 0 : checked(group.Max(item => item.ExportPosition ?? item.SourceIndex ?? 0) + 1);
                if (project.ReplaceDummiesOnAdding)
                {
                    var dummy = project.Items.Where(item => item.IsDummy && item.Gender == candidate.Gender && item.Category == candidate.Category && !replaced.Contains(item))
                        .OrderBy(item => item.ExportPosition ?? item.SourceIndex).FirstOrDefault();
                    if (dummy is not null) { candidate.ExportPosition = dummy.ExportPosition ?? dummy.SourceIndex; replaced.Add(dummy); }
                }
                newPieces.Add(candidate);
                existing.Add(identity, candidate);
                continue;
            }
            var effective = target;
            var previous = pending.Where(entry => ReferenceEquals(entry.Target, target)).ToArray();
            if (previous.Length > 0)
            {
                effective = System.Text.Json.JsonSerializer.Deserialize<ClothItem>(System.Text.Json.JsonSerializer.Serialize(target))!;
                foreach (var earlier in previous)
                {
                    ApplyTextures(effective, earlier.Textures, project.ReplaceDummiesOnAdding);
                    effective.AlternateModelPaths.AddRange(earlier.Models);
                    effective.PhysicsPath ??= earlier.Physics;
                }
            }
            var knownTextures = effective.TexturePaths.Where(path => !string.IsNullOrWhiteSpace(path)).Select(path => Path.GetFullPath(resolvePath(path)))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var textures = reviewTextures is null ? candidate.TexturePaths.Where(path => knownTextures.Add(Path.GetFullPath(path))).ToArray()
                : reviewTextures(effective, candidate.TexturePaths);
            var reserved = project.ReplaceDummiesOnAdding ? effective.TexturePaths.Count(string.IsNullOrWhiteSpace) : 0;
            if (effective.TexturePaths.Count + Math.Max(0, textures.Length - reserved) > 26)
                throw new InvalidDataException("O addon aceita no máximo 26 variantes por peça: " + target.Name);
            foreach (var texture in textures) validateYtd(texture);
            var knownModels = effective.AlternateModelPaths.Select(path => Path.GetFullPath(resolvePath(path)))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var models = candidate.AlternateModelPaths.Where(path => knownModels.Add(Path.GetFullPath(path)))
                .ToArray();
            var physics = effective.PhysicsPath is null ? candidate.PhysicsPath : null;
            pending.Add((target, textures, models, physics));
        }

        foreach (var dummy in replaced) project.Items.Remove(dummy);
        project.Items.AddRange(newPieces);
        var addedTextures = 0;
        var addedModels = 0;
        var addedPhysics = 0;
        foreach (var (target, textures, models, physics) in pending)
        {
            ApplyTextures(target, textures, project.ReplaceDummiesOnAdding);
            target.AlternateModelPaths.AddRange(models);
            if (physics is not null) target.PhysicsPath = physics;
            addedTextures += textures.Length;
            addedModels += models.Length;
            if (physics is not null) addedPhysics++;
        }
        return (newPieces.Count, addedTextures, addedModels, addedPhysics);
    }
    private static void ApplyTextures(ClothItem target, IEnumerable<string> textures, bool replaceDummies)
    {
        while (target.OriginalTextureFileNames.Count < target.TexturePaths.Count)
                target.OriginalTextureFileNames.Add(Path.GetFileName(
                    target.TexturePaths[target.OriginalTextureFileNames.Count]));
            foreach (var texture in textures)
            {
                var dummyIndex = replaceDummies ? target.TexturePaths.FindIndex(string.IsNullOrWhiteSpace) : -1;
                if (dummyIndex >= 0)
                {
                    target.TexturePaths[dummyIndex] = texture;
                    target.OriginalTextureFileNames[dummyIndex] = Path.GetFileName(texture);
                    continue;
                }
                target.TexturePaths.Add(texture);
                target.OriginalTextureFileNames.Add(Path.GetFileName(texture));
            }
    }
}
