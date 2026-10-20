using CodeWalker.GameFiles;
using FooxClothStudio;
using System.IO;
using System.Windows.Media.Media3D;

var target = Path.GetFullPath(args[0]);
var paths = File.Exists(target) ? [target] :
    Directory.EnumerateFiles(target, "*.ydd", SearchOption.AllDirectories).ToArray();
var tested = 0;
var largestSource = 0;
var largestPreview = 0;
var largestPath = "";
foreach (var path in paths)
{
    var ydd = new YddFile();
    ydd.Load(File.ReadAllBytes(path));
    var geometryCount = ydd.Drawables?.SelectMany(drawable =>
            drawable?.DrawableModels?.High ?? []).SelectMany(model => model?.Geometries ?? [])
        .Count(geometry => geometry?.VertexData?.Info is not null &&
            geometry.IndexBuffer?.Indices is { Length: >= 3 } &&
            (geometry.VertexData.Info.Flags & (1u << (int)VertexSemantics.Position)) != 0) ?? 0;
    var preview = ModelPreview.Read(path);
    if (preview.PreviewTriangles > 25_000 || preview.PreviewTriangles == 0 ||
        preview.SourceTriangles < preview.PreviewTriangles || preview.Parts.Length != geometryCount)
        throw new Exception($"Preview coverage failed: {preview.SourceTriangles} source / " +
            $"{preview.PreviewTriangles} preview triangles, {preview.Parts.Length}/{geometryCount} geometries. {path}");
    if (preview.SourceTriangles > largestSource)
    {
        largestSource = preview.SourceTriangles;
        largestPreview = preview.PreviewTriangles;
        largestPath = path;
    }
    tested++;
}
if (tested == 0) throw new Exception("No YDD files found.");
Console.WriteLine($"OK: {tested} YDD preview(s); largest has {largestSource} source and " +
    $"{largestPreview} sampled triangles, with all geometries represented. {largestPath}");
