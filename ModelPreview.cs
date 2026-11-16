using System.IO;
using System.Windows;
using System.Windows.Media.Media3D;
using CodeWalker.GameFiles;

namespace FooxClothStudio;

public sealed record ModelPreviewPart(Point3D[] Positions, int[] Indices,
    Point[] TextureCoordinates, string? TextureName);

public sealed record ModelPreviewData(ModelPreviewPart[] Parts,
    int SourceTriangles, int PreviewTriangles);

public static class ModelPreview
{
    private const int MaxTriangles = 25_000;

    public static ModelPreviewData Read(string path, bool normalize = true, bool preserveAllTriangles = false)
    {
        var ydd = new YddFile();
        ydd.Load(File.ReadAllBytes(path));
        var parts = new List<ModelPreviewPart>();
        var sourceTriangles = 0;
        var geometryEntries = new List<(DrawableGeometry Geometry, ShaderFX? Shader,
            ushort[] Indices, int VertexCount, int TriangleCount)>();
        foreach (var drawable in ydd.Drawables ?? [])
            foreach (var model in new[] { drawable?.DrawableModels?.High, drawable?.DrawableModels?.Med,
                         drawable?.DrawableModels?.Low, drawable?.DrawableModels?.VLow }
                         .FirstOrDefault(models => models is { Length: > 0 }) ?? [])
                foreach (var geometry in model?.Geometries ?? [])
                {
                    if (geometry?.VertexData?.Info is null || geometry.IndexBuffer?.Indices is null ||
                        (geometry.VertexData.Info.Flags & (1u << (int)VertexSemantics.Position)) == 0)
                        continue;
                    var sourceIndices = geometry.IndexBuffer.Indices;
                    var vertexCount = geometry.VertexData.VertexCount;
                    var validTriangles = 0;
                    for (var offset = 0; offset + 2 < sourceIndices.Length; offset += 3)
                        if (sourceIndices[offset] < vertexCount &&
                            sourceIndices[offset + 1] < vertexCount &&
                            sourceIndices[offset + 2] < vertexCount)
                            validTriangles++;
                    if (validTriangles == 0) continue;
                    sourceTriangles += validTriangles;
                    var shader = geometry.Shader ?? drawable?.ShaderGroup?.Shaders?.data_items?
                        .ElementAtOrDefault(geometry.ShaderID);
                    geometryEntries.Add((geometry, shader, sourceIndices, vertexCount, validTriangles));
                }

        if (sourceTriangles == 0)
            throw new InvalidDataException("O YDD não contém triângulos válidos para prévia.");
        var budget = preserveAllTriangles ? sourceTriangles : Math.Min(MaxTriangles, sourceTriangles);
        var allocations = geometryEntries.Select(entry =>
            Math.Max(1, (int)((long)budget * entry.TriangleCount / sourceTriangles))).ToArray();
        while (allocations.Sum() > budget)
        {
            var largest = Enumerable.Range(0, allocations.Length)
                .Where(index => allocations[index] > 1)
                .OrderByDescending(index => allocations[index]).FirstOrDefault(-1);
            if (largest < 0) break;
            allocations[largest]--;
        }
        while (allocations.Sum() < budget)
        {
            var largest = Enumerable.Range(0, geometryEntries.Count)
                .Where(index => allocations[index] < geometryEntries[index].TriangleCount)
                .OrderByDescending(index => geometryEntries[index].TriangleCount - allocations[index])
                .FirstOrDefault(-1);
            if (largest < 0) break;
            allocations[largest]++;
        }

        for (var geometryIndex = 0; geometryIndex < geometryEntries.Count; geometryIndex++)
        {
            var entry = geometryEntries[geometryIndex];
            var geometry = entry.Geometry;
            var hasUvs = (geometry.VertexData.Info.Flags &
                (1u << (int)VertexSemantics.TexCoord0)) != 0;
            var positions = new List<Point3D>();
            var uvs = new List<Point>();
            var indices = new List<int>();
            var vertexMap = new Dictionary<ushort, int>();
            var seen = 0;
            var targetCount = allocations[geometryIndex];
            for (var offset = 0; offset + 2 < entry.Indices.Length; offset += 3)
            {
                var a = entry.Indices[offset];
                var b = entry.Indices[offset + 1];
                var c = entry.Indices[offset + 2];
                if (a >= entry.VertexCount || b >= entry.VertexCount || c >= entry.VertexCount) continue;
                var select = (long)seen * targetCount / entry.TriangleCount !=
                    (long)(seen + 1) * targetCount / entry.TriangleCount;
                seen++;
                if (!select) continue;
                foreach (var sourceIndex in new[] { a, b, c })
                {
                    if (!vertexMap.TryGetValue(sourceIndex, out var mapped))
                    {
                        var vertex = geometry.VertexData.GetVector3(sourceIndex,
                            (int)VertexSemantics.Position);
                        mapped = positions.Count;
                        vertexMap[sourceIndex] = mapped;
                        positions.Add(new Point3D(vertex.X, vertex.Z, -vertex.Y));
                        if (hasUvs)
                        {
                            var uv = geometry.VertexData.GetVector2(sourceIndex,
                                (int)VertexSemantics.TexCoord0);
                            uvs.Add(new Point(uv.X, uv.Y));
                        }
                    }
                    indices.Add(mapped);
                }
            }
            if (indices.Count == 0) continue;
            parts.Add(new ModelPreviewPart(positions.ToArray(), indices.ToArray(),
                uvs.ToArray(), hasUvs ? DiffuseTextureName(entry.Shader) : null));
        }
        var previewTriangles = parts.Sum(part => part.Indices.Length / 3);
        if (previewTriangles == 0)
            throw new InvalidDataException("O YDD não contém triângulos de alta resolução para prévia.");

        if (!normalize) return new ModelPreviewData(parts.ToArray(), sourceTriangles, previewTriangles);
        var all = parts.SelectMany(part => part.Positions).ToArray();
        var minX = all.Min(p => p.X);
        var maxX = all.Max(p => p.X);
        var minY = all.Min(p => p.Y);
        var maxY = all.Max(p => p.Y);
        var minZ = all.Min(p => p.Z);
        var maxZ = all.Max(p => p.Z);
        var centerX = (minX + maxX) / 2;
        var centerY = (minY + maxY) / 2;
        var centerZ = (minZ + maxZ) / 2;
        var span = Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ));
        var scale = span > 0 ? 1.7 / span : 1;
        foreach (var part in parts)
            for (var i = 0; i < part.Positions.Length; i++)
            {
                var point = part.Positions[i];
                part.Positions[i] = new Point3D((point.X - centerX) * scale,
                    (point.Y - centerY) * scale, (point.Z - centerZ) * scale);
            }
        return new ModelPreviewData(parts.ToArray(), sourceTriangles, previewTriangles);
    }

    public static int? PropBoneTag(string slot) => slot switch
    {
        "p_head" or "p_eyes" or "p_ears" or "p_mouth" => 31086,
        "p_lwrist" => 61007,
        "p_rwrist" => 43810,
        _ => null
    };

    public static Matrix3D HeadAttachment(string headModelPath) => BoneAttachment(headModelPath, 31086);

    public static Matrix3D BoneAttachment(string headModelPath, int boneTag)
    {
        var ydd = new YddFile();
        ydd.Load(File.ReadAllBytes(headModelPath));
        var bone = ydd.Drawables?.SelectMany(d => d.Skeleton?.Bones?.Items ?? [])
            .FirstOrDefault(b => b.Tag == boneTag)
            ?? throw new InvalidDataException("Base ped has no attachment bone " + boneTag + ".");
        var matrix = SharpDX.Matrix.AffineTransformation(1, bone.Rotation, bone.Translation);
        for (var parent = bone.Parent; parent is not null; parent = parent.Parent)
            matrix *= SharpDX.Matrix.AffineTransformation(1, parent.Rotation, parent.Translation);
        // Convert GTA coordinates (X,Y,Z) to preview coordinates (X,Z,-Y).
        Point3D Convert(SharpDX.Vector3 v) => new(v.X, v.Z, -v.Y);
        var origin = Convert(matrix.TranslationVector);
        var x = Convert(SharpDX.Vector3.TransformNormal(SharpDX.Vector3.UnitX, matrix));
        var y = Convert(SharpDX.Vector3.TransformNormal(SharpDX.Vector3.UnitZ, matrix));
        var z = Convert(SharpDX.Vector3.TransformNormal(-SharpDX.Vector3.UnitY, matrix));
        return new Matrix3D(x.X,x.Y,x.Z,0, y.X,y.Y,y.Z,0, z.X,z.Y,z.Z,0,
            origin.X,origin.Y,origin.Z,1);
    }

    public static ModelPreviewData AttachToHead(ModelPreviewData data, Matrix3D attachment) =>
        AttachToBone(data, attachment);

    public static ModelPreviewData AttachToBone(ModelPreviewData data, Matrix3D attachment) =>
        data with { Parts = data.Parts.Select(p => p with {
            Positions = p.Positions.Select(attachment.Transform).ToArray()
        }).ToArray() };

    private static string? DiffuseTextureName(ShaderFX? shader)
    {
        var parameters = shader?.ParametersList?.Parameters ?? [];
        var hashes = shader?.ParametersList?.Hashes ?? [];
        var priority = new[]
        {
            ShaderParamNames.DiffuseSampler, ShaderParamNames.DiffuseTexSampler,
            ShaderParamNames.DiffuseNoBorderTexSampler, ShaderParamNames.DiffuseHfSampler,
            ShaderParamNames.PlateBgSampler
        };
        foreach (var requested in priority)
            for (var index = 0; index < Math.Min(parameters.Length, hashes.Length); index++)
                if ((uint)hashes[index] == (uint)requested &&
                    parameters[index]?.Data is TextureBase texture &&
                    !string.IsNullOrWhiteSpace(texture.Name))
                    return texture.Name;
        return null;
    }
}
