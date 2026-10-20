namespace FooxClothStudio;

public static class ProjectOperationSummary
{
    public static string Describe(ClothProject project)
    {
        var pieces = project.Items.Count(i => !i.IsDummy);
        var reserves = project.Items.Count(i => i.IsDummy);
        var textures = project.Items.Where(i => !i.IsDummy).Sum(i => i.TexturePaths.Count(p => !string.IsNullOrWhiteSpace(p)));
        return $"{pieces} peça(s), {textures} textura(s)" +
            (reserves > 0 ? $", {reserves} slot(s) reservado(s)" : "") +
            (project.Tattoos.Count > 0 ? $", {project.Tattoos.Count} tatuagem(ns)" : "");
    }
}
