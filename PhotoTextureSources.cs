using System.Text.Json;

namespace FooxClothStudio;

public static class PhotoTextureSources
{
    public static ClothItem Merge(ClothItem original, ClothItem native)
    {
        if (original.Id != native.Id) throw new ArgumentException("Texture sources must refer to the same item.");
        var item = JsonSerializer.Deserialize<ClothItem>(JsonSerializer.Serialize(original))!;
        var own = OriginalResourceExporter.Describe(new ClothProject { Items = [item] }).Entries.Single();
        var game = OriginalResourceExporter.Describe(new ClothProject { Items = [native] }).Entries.Single();
        var variants = new List<(int Index, string Path, Guid Id, string Name)>();
        foreach (var texture in own.Textures.Where(texture => !texture.Inherited))
        {
            var position = item.TextureIds.IndexOf(texture.Id);
            variants.Add((texture.Index, item.TexturePaths[position], texture.Id, texture.File!));
        }
        var ownIndices = variants.Select(variant => variant.Index).ToHashSet();
        foreach (var texture in game.Textures.Where(texture => !texture.Inherited && !ownIndices.Contains(texture.Index)))
        {
            var position = native.TextureIds.IndexOf(texture.Id);
            variants.Add((texture.Index, "gta://" + native.TexturePaths[position], texture.Id, texture.File!));
        }
        var selected = variants.OrderBy(variant => variant.Index).ToArray();
        item.TexturePaths = selected.Select(variant => variant.Path).ToList();
        item.TextureIds = selected.Select(variant => variant.Id).ToList();
        item.OriginalTextureFileNames = selected.Select(variant => variant.Name).ToList();
        return item;
    }
}
