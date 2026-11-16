using System.Text.Json;
using System.IO;
using CodeWalker.GameFiles;

namespace FooxClothStudio;

public static class PhotoDecorationSources
{
    public static ClothProject Prepare(ClothProject source, GtaPhotoCatalog catalog, bool includeNative, Func<string, string>? resolveAsset = null)
    {
        var result = new ClothProject { Name = source.Name, SourceResourceRoot = source.SourceResourceRoot,
            Items = source.Items.ToList(), AppearanceAssets = [], Tattoos = [] };
        var definitions = catalog.Project.AppearanceAssets.ToDictionary(asset => asset.Name, StringComparer.OrdinalIgnoreCase);
        var bindings = new Dictionary<string, uint?>();
        uint? Binding(AppearanceAsset? definition)
        {
            if (definition is null) return null;
            if (bindings.TryGetValue(definition.Name, out var existing)) return existing;
            try
            {
                var dictionary = new YtdFile(); dictionary.Load(File.ReadAllBytes(catalog.Resolve(definition.TexturePath)));
                var texture = AppearancePhotoTextures.Select(dictionary, definition.Name).Single();
                var index = Array.IndexOf(dictionary.TextureDict.Textures.data_items, texture);
                return bindings[definition.Name] = dictionary.TextureDict.TextureNameHashes.data_items[index];
            }
            catch (Exception error)
            {
                catalog.Warnings.Add(definition.Name + ": " + error.Message);
                return bindings[definition.Name] = null;
            }
        }
        foreach (var asset in source.AppearanceAssets)
        {
            var definition = definitions.GetValueOrDefault(asset.Name);
            result.AppearanceAssets.Add(new AppearanceAsset
            {
                Id = asset.Id, Name = asset.Name, Kind = definition?.Kind ?? asset.Kind, Gender = definition?.Gender ?? asset.Gender,
                TexturePath = asset.TexturePath, OverlayCategory = definition?.OverlayCategory ?? asset.OverlayCategory,
                OverlayIndex = definition?.OverlayIndex ?? asset.OverlayIndex, DiffuseTextureHash = Binding(definition) ?? asset.DiffuseTextureHash
            });
        }
        foreach (var tattoo in source.Tattoos)
            foreach (var gender in tattoo.Gender == "Both" ? new[] { "Masculino", "Feminino" } :
                new[] { tattoo.Gender is "Male" or "Masculino" ? "Masculino" : "Feminino" })
            {
                var resolved = catalog.ResolveTattoo(tattoo, gender, resolveAsset) ?? JsonSerializer.Deserialize<TattooItem>(JsonSerializer.Serialize(tattoo))!;
                resolved.Gender = gender == "Masculino" ? "Male" : "Female";
                result.Tattoos.Add(resolved);
            }
        if (!includeNative) return result;
        var names = result.AppearanceAssets.Select(asset => asset.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in catalog.Project.AppearanceAssets.Where(asset => !names.Contains(asset.Name)))
            result.AppearanceAssets.Add(new AppearanceAsset
            {
                Id = asset.Id, Name = asset.Name, Kind = asset.Kind, Gender = asset.Gender,
                TexturePath = "gta://" + asset.TexturePath, OverlayCategory = asset.OverlayCategory, OverlayIndex = asset.OverlayIndex,
                DiffuseTextureHash = Binding(asset)
            });
        var addresses = result.Tattoos.Select(Address).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var tattoo in catalog.Project.Tattoos)
        {
            foreach (var gender in tattoo.Gender == "Both" ? new[] { "Male", "Female" } : new[] { tattoo.Gender })
            {
                var copy = JsonSerializer.Deserialize<TattooItem>(JsonSerializer.Serialize(tattoo))!;
                copy.Gender = gender;
                if (!addresses.Add(Address(copy))) continue;
                copy.ImagePath = "gta://" + copy.ImagePath;
                result.Tattoos.Add(copy);
            }
        }
        return result;
    }

    private static string Address(TattooItem tattoo) => string.Join('|', tattoo.Gender, tattoo.Collection, tattoo.Overlay);
}
