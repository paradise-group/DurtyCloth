using System.IO;
using CodeWalker.GameFiles;

namespace FooxClothStudio;

public static class BuildDependencies
{
    // Work on the build snapshot: resolving a shared skin dictionary must not
    // change the user's source files or regenerate permanent IDs.
    public static List<string> Prepare(ClothProject project, Func<string, string> resolve, string? gameFolder, string? cacheFolder = null)
    {
        var errors = new List<string>();
        cacheFolder ??= Path.Combine(Path.GetTempPath(), "ParadiseClothStudio", "build-textures");
        var library = gameFolder is null ? null : new GameTextureDependencies(gameFolder, cacheFolder);
        foreach (var item in project.Items.Where(item => !item.IsDummy))
        {
            if (item.TexturePaths.Count > 26)
            {
                errors.Add(item.Name + ": more than 26 YTD variants.");
                continue;
            }
            if (item.TexturePaths.Count != 0) continue;
            try
            {
                var diffuse = ModelPreview.Read(resolve(item.ModelPath), false).Parts
                    .Select(part => part.TextureName).Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                string? texturePath = null;
                if (texturePath is null && library is not null && diffuse.Length > 0)
                    texturePath = library.Resolve(item, resolve(item.ModelPath), diffuse.Select(name => name!).ToArray());
                if (texturePath is null)
                {
                    // Skin-only shaders can use volume/skin samplers without any
                    // external diffuse. Preserve their authored embedded data.
                    var embedded = new GameTextureDependencies(gameFolder ?? "", cacheFolder);
                    texturePath = embedded.Embedded(item, resolve(item.ModelPath), diffuse.Select(name => name!).ToArray());
                }
                if (texturePath is null)
                {
                    errors.Add(item.Name + ": no YTD variants. Required diffuse textures: " +
                        (diffuse.Length == 0 ? "unknown" : string.Join(", ", diffuse)) +
                        ". Add the matching YTD" + (gameFolder is null ? " or configure the GTA V folder." : "; this model may depend on a base-game/DLC texture absent from the pack."));
                    continue;
                }
                item.TexturePaths.Add(texturePath);
                item.OriginalTextureFileNames.Add(Path.GetFileName(texturePath));
                if (item.TextureIds.Count == 0) item.TextureIds.Add(ClothingIdentityMap.InheritedTextureId(item.Id));
                ClothingIdentityMap.EnsureTextures(item);
            }
            catch (Exception ex) { errors.Add(item.Name + ": could not resolve texture dependencies: " + ex.Message); }
        }
        foreach (var tattoo in project.Tattoos)
            try { TattooExporter.Validate(tattoo); }
            catch (Exception ex) { errors.Add("Tattoo " + tattoo.Name + ": " + ex.Message); }
        return errors;
    }
}
