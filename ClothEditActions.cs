using System.IO;
using System.Text.Json;

namespace FooxClothStudio;

public static class ClothEditActions
{
    public static ClothItem CopyForOtherGender(ClothProject project, ClothItem source)
    {
        if (source.Gender is not ("Masculino" or "Feminino")) throw new InvalidDataException("Select Male or Female before creating a copy.");
        var copy = JsonSerializer.Deserialize<ClothItem>(JsonSerializer.Serialize(source))!;
        copy.Id = Guid.NewGuid(); copy.Gender = source.Gender == "Masculino" ? "Feminino" : "Masculino";
        copy.SourceIndex = null;
        var group = project.Items.Where(item => item.Gender == copy.Gender && item.Category == copy.Category).ToArray();
        copy.ExportPosition = group.Length == 0 ? 0 : group.Max(item => item.ExportPosition ?? item.SourceIndex ?? 0) + 1;
        project.Items.Add(copy); return copy;
    }
    public static void ReplaceWithDummy(ClothItem item)
    {
        item.IsDummy = true; item.ModelPath = ""; item.AlternateModelPaths.Clear(); item.TexturePaths.Clear();
        item.OriginalTextureFileNames.Clear(); item.TextureLabels.Clear(); item.TextureEffects.Clear();
        item.PhysicsPath = null; item.HasFirstPersonModel = false;
    }
}
