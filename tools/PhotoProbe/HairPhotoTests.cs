using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using FooxClothStudio;

internal static class HairPhotoTests
{
    private readonly record struct Triangle(string? Texture, Point3D A, Point3D B, Point3D C, Point UvA, Point UvB, Point UvC);

    public static void Run(string modelPath, string texturePath, string output)
    {
        Directory.CreateDirectory(output);
        var model = ModelPreview.Read(modelPath, false, preserveAllTriangles: true);
        if (model.SourceTriangles != model.PreviewTriangles) throw new Exception("Photo reader dropped source triangles.");
        var sort = typeof(PreviewPhotoRenderer).GetMethod("OrderHairLayers", BindingFlags.NonPublic | BindingFlags.Static)!;
        var ordered = (ModelPreviewPart[])sort.Invoke(null, [model.Parts])!;
        var before = Triangles(model.Parts).GroupBy(triangle => triangle).ToDictionary(group => group.Key, group => group.Count());
        var after = Triangles(ordered).GroupBy(triangle => triangle).ToDictionary(group => group.Key, group => group.Count());
        if (before.Count != after.Count || before.Any(pair => !after.TryGetValue(pair.Key, out var count) || count != pair.Value))
            throw new Exception("Hair sorting changed a triangle, winding, UV or material.");
        foreach (var part in ordered)
        {
            var previous = double.NegativeInfinity;
            foreach (var triangle in part.Indices.Chunk(3))
            {
                var depth = triangle.Average(index => part.Positions[index].Z);
                if (depth < previous) throw new Exception("Hair cards are not ordered back to front.");
                previous = depth;
            }
        }
        var read = typeof(MainWindow).GetMethod("ReadMatchingTextures", BindingFlags.NonPublic | BindingFlags.Static)!;
        var textures = (Dictionary<string, BitmapSource>)read.Invoke(null, [texturePath, model])!;
        var ped = PreviewPhotoRenderer.LoadPed("D:/Jogos/GTAV", "Masculino");
        var background = Color.FromRgb(0x62, 0x5F, 0x68);
        foreach (var format in new[] { "PNG", "WebP" })
        {
            var options = new PhotoImageOptions(512, format, 85);
            PreviewPhotoRenderer.Save(Path.Combine(output, "corrected" + options.Extension), model, textures, ped,
                "Cabelo [hair]", background, options);
        }
        Console.WriteLine($"PASS: {model.SourceTriangles} triangles preserved with exact positions, winding, UVs and materials; " +
            $"{model.Parts.Length} geometries merged into {ordered.Length} ordered material groups. Corrected PNG and WebP: {Path.GetFullPath(output)}");
    }

    private static IEnumerable<Triangle> Triangles(IEnumerable<ModelPreviewPart> parts)
    {
        foreach (var part in parts)
            foreach (var indices in part.Indices.Chunk(3))
            {
                var hasUvs = part.TextureCoordinates.Length == part.Positions.Length;
                yield return new Triangle(part.TextureName, part.Positions[indices[0]], part.Positions[indices[1]], part.Positions[indices[2]],
                    hasUvs ? part.TextureCoordinates[indices[0]] : default,
                    hasUvs ? part.TextureCoordinates[indices[1]] : default,
                    hasUvs ? part.TextureCoordinates[indices[2]] : default);
            }
    }
}
