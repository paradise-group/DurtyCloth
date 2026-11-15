using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace FooxClothStudio;

public sealed record PhotoPed(ModelPreviewPart[] Parts,
    Dictionary<string, BitmapSource> Textures, Dictionary<int, Matrix3D> Attachments);

public static class PreviewPhotoRenderer
{
    public const int Size = 768;

    public static PhotoPed LoadPed(string gameFolder, string gender)
    {
        var parts = new List<ModelPreviewPart>();
        var textures = new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);
        var assets = GamePedAssets.Load(gameFolder, gender);
        var head = assets.First(asset => asset.Slot == "head");
        var attachments = new[] { 31086, 61007, 43810 }
            .ToDictionary(tag => tag, tag => ModelPreview.BoneAttachment(head.ModelPath, tag));
        foreach (var asset in assets)
        {
            var model = ModelPreview.Read(asset.ModelPath, false, preserveAllTriangles: true);
            var images = asset.TexturePath is null ? [] : MainWindow.ReadMatchingTextures(asset.TexturePath, model);
            foreach (var part in model.Parts)
            {
                var name = "__ped_" + asset.Slot + "_" + parts.Count;
                if (images.TryGetValue(part.TextureName ?? "__selected_diffuse", out var image)) textures[name] = image;
                parts.Add(part with { TextureName = name });
            }
        }
        return new PhotoPed(parts.ToArray(), textures, attachments);
    }

    public static void Save(string destination, ModelPreviewData garment,
        Dictionary<string, BitmapSource> garmentTextures, PhotoPed? ped,
        string category, Color background, PhotoImageOptions? options = null)
        => (options ?? new()).Write(destination, Render(garment, garmentTextures, ped, category, background));

    private static RenderTargetBitmap Render(ModelPreviewData garment,
        Dictionary<string, BitmapSource> garmentTextures, PhotoPed? ped,
        string category, Color background)
    {
        var slot = category.Contains('[') ? category.Split('[').Last().TrimEnd(']') : "";
        if (ped is not null && ModelPreview.PropBoneTag(slot) is int boneTag)
            garment = ModelPreview.AttachToBone(garment, ped.Attachments[boneTag]);
        var focus = Bounds(garment.Parts);
        var framingParts = garment.Parts;
        if (ped is not null)
        {
            var headParts = ped.Parts.Where(part => part.TextureName?.StartsWith("__ped_head_", StringComparison.Ordinal) == true).ToArray();
            if (headParts.Length > 0)
            {
                var headBounds = Bounds(headParts);
                if (focus.CenterY >= headBounds.CenterY - headBounds.SizeY / 2)
                {
                    framingParts = garment.Parts.Concat(headParts).ToArray();
                    focus = Bounds(framingParts);
                }
            }
        }
        var itemReachesFeet = focus.CenterY - focus.SizeY / 2 < -0.85;
        var garmentParts = slot == "hair" ? OrderHairLayers(garment.Parts) : garment.Parts;
        var bodyParts = ped is null ? [] : ped.Parts.Where(p => !p.TextureName!.StartsWith("__ped_" + slot + "_", StringComparison.Ordinal) &&
                !p.TextureName.StartsWith("__ped_hair_", StringComparison.Ordinal) &&
                !(slot == "teef" && itemReachesFeet && p.TextureName.StartsWith("__ped_feet_", StringComparison.Ordinal))).ToArray();
        var parts = slot == "hair" ? bodyParts.Concat(garmentParts).ToArray() : garmentParts.Concat(bodyParts).ToArray();
        var images = new Dictionary<string, BitmapSource>(garmentTextures, StringComparer.OrdinalIgnoreCase);
        if (ped is not null)
            foreach (var entry in ped.Textures) images[entry.Key] = entry.Value;

        var models = new Model3DGroup();
        foreach (var part in parts)
        {
            if (part.Indices.Length == 0) continue;
            var normals = new Vector3D[part.Positions.Length];
            for (var i = 0; i + 2 < part.Indices.Length; i += 3)
            {
                var a = part.Indices[i]; var b = part.Indices[i + 1]; var c = part.Indices[i + 2];
                var normal = Vector3D.CrossProduct(part.Positions[b] - part.Positions[a],
                    part.Positions[c] - part.Positions[a]);
                normals[a] += normal; normals[b] += normal; normals[c] += normal;
            }
            for (var i = 0; i < normals.Length; i++) if (normals[i].LengthSquared > 0) normals[i].Normalize();
            var mesh = new MeshGeometry3D
            {
                Positions = new Point3DCollection(part.Positions),
                TriangleIndices = new Int32Collection(part.Indices),
                Normals = new Vector3DCollection(normals)
            };
            Material material;
            if (images.TryGetValue(part.TextureName ?? "__selected_diffuse", out var image) &&
                part.TextureCoordinates.Length == part.Positions.Length)
            {
                mesh.TextureCoordinates = new PointCollection(part.TextureCoordinates);
                material = new DiffuseMaterial(new ImageBrush(image)
                {
                    TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute,
                    Viewport = new Rect(0, 0, 1, 1), Stretch = Stretch.Fill
                });
            }
            else material = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(194, 202, 215)));
            mesh.Freeze();
            models.Children.Add(new GeometryModel3D(mesh, material) { BackMaterial = material });
        }
        models.Transform = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0),
            new Point3D(focus.CenterX, focus.CenterY, focus.CenterZ));
        var scene = new Model3DGroup();
        scene.Children.Add(new AmbientLight(Color.FromRgb(95, 95, 95)));
        scene.Children.Add(new DirectionalLight(Colors.White, new Vector3D(-0.4, -0.7, -1)));
        scene.Children.Add(models);

        // Body context scales with the actual ped, independently of the item category.
        var contextHeight = ped is null ? 0 : Bounds(ped.Parts).SizeY * 0.30;
        const double fieldOfView = 40;
        const double frameUsage = 0.86; // Keep seven percent clear on each edge.
        var halfAngleTangent = Math.Tan(fieldOfView * Math.PI / 360);
        var distance = contextHeight / (2 * halfAngleTangent * frameUsage);
        // Fit each vertex in perspective, including its depth towards the camera.
        // A width/height multiplier alone can crop long or deep accessories.
        foreach (var point in framingParts.SelectMany(part => part.Positions))
        {
            var depth = point.Z - focus.CenterZ;
            var radius = Math.Max(Math.Abs(point.X - focus.CenterX), Math.Abs(point.Y - focus.CenterY));
            distance = Math.Max(distance, depth + Math.Max(0.15, radius / (halfAngleTangent * frameUsage)));
        }
        var viewport = new Viewport3D
        {
            Camera = new PerspectiveCamera(new Point3D(focus.CenterX, focus.CenterY, focus.CenterZ + distance),
                new Vector3D(0, 0, -1), new Vector3D(0, 1, 0), fieldOfView)
        };
        viewport.Children.Add(new ModelVisual3D { Content = scene });
        var surface = new Border { Width = Size, Height = Size, Background = new SolidColorBrush(background), Child = viewport };
        surface.Measure(new Size(Size, Size)); surface.Arrange(new Rect(0, 0, Size, Size)); surface.UpdateLayout();
        var bitmap = new RenderTargetBitmap(Size, Size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(surface);
        return bitmap;
    }

    private static ModelPreviewPart[] OrderHairLayers(ModelPreviewPart[] parts)
    {
        // WPF does not sort transparent hair cards. Merge matching materials so
        // cards from different geometries can be drawn together, back to front.
        // The photo camera looks down -Z; keep every vertex, UV and triangle.
        return parts.GroupBy(part => (part.TextureName, HasUvs: part.TextureCoordinates.Length == part.Positions.Length))
            .Select(group =>
            {
                var positions = new List<Point3D>();
                var coordinates = new List<Point>();
                var triangles = new List<(int A, int B, int C, double Depth)>();
                foreach (var part in group)
                {
                    var offset = positions.Count;
                    positions.AddRange(part.Positions);
                    if (group.Key.HasUvs) coordinates.AddRange(part.TextureCoordinates);
                    for (var index = 0; index + 2 < part.Indices.Length; index += 3)
                    {
                        var a = part.Indices[index]; var b = part.Indices[index + 1]; var c = part.Indices[index + 2];
                        triangles.Add((offset + a, offset + b, offset + c,
                            (part.Positions[a].Z + part.Positions[b].Z + part.Positions[c].Z) / 3));
                    }
                }
                return new ModelPreviewPart(positions.ToArray(), triangles.OrderBy(triangle => triangle.Depth)
                    .SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C }).ToArray(),
                    coordinates.ToArray(), group.Key.TextureName);
            })
            .OrderBy(part => part.Positions.Length == 0 ? 0 : part.Positions.Average(point => point.Z)).ToArray();
    }

    public static void SaveFlatTexture(string destination, BitmapSource image, Color background)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(new SolidColorBrush(background), null, new Rect(0, 0, Size, Size));
            var scale = Math.Min((double)(Size - 80) / image.PixelWidth, (double)(Size - 80) / image.PixelHeight);
            var width = image.PixelWidth * scale; var height = image.PixelHeight * scale;
            drawing.DrawImage(image, new Rect((Size - width) / 2, (Size - height) / 2, width, height));
        }
        var bitmap = new RenderTargetBitmap(Size, Size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        WritePng(destination, bitmap);
    }

    public static void SaveTattoo(string destination, TattooItem tattoo, BitmapSource image,
        PhotoPed ped, Color background, PhotoImageOptions? options = null)
        => (options ?? new()).Write(destination, RenderTattoo(tattoo, image, ped, background));

    public static BitmapSource RenderTattoo(TattooItem tattoo, BitmapSource image,
        PhotoPed ped, Color background)
    {
        var slot = tattoo.Zone switch
        {
            "ZONE_HEAD" => "head",
            "ZONE_LEFT_LEG" or "ZONE_RIGHT_LEG" => "lowr",
            _ => "uppr"
        };
        var points = ped.Parts.Where(p => p.TextureName?.StartsWith("__ped_" + slot + "_", StringComparison.Ordinal) == true)
            .SelectMany(p => p.Positions).ToArray();
        if (points.Length == 0) throw new InvalidDataException("Ped base sem geometria para " + tattoo.Zone);
        var minX = points.Min(p => p.X); var maxX = points.Max(p => p.X);
        var minY = points.Min(p => p.Y); var maxY = points.Max(p => p.Y);
        var width = maxX - minX; var height = maxY - minY;
        var x = tattoo.Zone switch
        {
            "ZONE_LEFT_ARM" => minX + width * 0.16,
            "ZONE_RIGHT_ARM" => maxX - width * 0.16,
            "ZONE_LEFT_LEG" => minX + width * 0.25,
            "ZONE_RIGHT_LEG" => maxX - width * 0.25,
            _ => (minX + maxX) / 2
        };
        x += (tattoo.U - 0.5) * width * 0.3;
        var y = maxY - height * (tattoo.Zone == "ZONE_HEAD" ? 0.45 : 0.55)
            - (tattoo.V - 0.5) * height * 0.3;
        var z = points.Max(p => p.Z) + 0.07;
        var halfWidth = Math.Clamp(width * tattoo.ScaleX * 0.24, 0.05, 0.40);
        var halfHeight = Math.Clamp(height * tattoo.ScaleY * 0.24, 0.05, 0.40);
        var radians = tattoo.Rotation * Math.PI / 180;
        Point3D Corner(double dx, double dy) => new(x + dx * Math.Cos(radians) - dy * Math.Sin(radians),
            y + dx * Math.Sin(radians) + dy * Math.Cos(radians), z);
        var part = new ModelPreviewPart(
            [Corner(-halfWidth, halfHeight), Corner(halfWidth, halfHeight),
             Corner(halfWidth, -halfHeight), Corner(-halfWidth, -halfHeight)],
            [], [], "__tattoo_focus");
        var data = new ModelPreviewData([part], 0, 0);
        var baseImage = Render(data, new Dictionary<string, BitmapSource>(), ped, "Tattoo", background);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawImage(baseImage, new Rect(0, 0, Size, Size));
            var photoWidth = Math.Clamp(halfWidth * 500, 70, 280);
            var photoHeight = photoWidth * image.PixelHeight / image.PixelWidth;
            drawing.DrawImage(image, new Rect((Size - photoWidth) / 2, (Size - photoHeight) / 2,
                photoWidth, photoHeight));
        }
        var composite = new RenderTargetBitmap(Size, Size, 96, 96, PixelFormats.Pbgra32);
        composite.Render(visual);
        return composite;
    }

    public static void SaveUnplacedTattoo(string destination, BitmapSource image, PhotoPed ped, Color background, PhotoImageOptions? options = null)
        => (options ?? new()).Write(destination, RenderUnplacedTattoo(image, ped, background));

    public static BitmapSource RenderUnplacedTattoo(BitmapSource image, PhotoPed ped, Color background)
    {
        // A YTD without overlay metadata contains no body zone. Show the source next
        // to a full ped instead of inventing a tattoo placement.
        var pedPoints = ped.Parts.SelectMany(part => part.Positions).ToArray();
        var centerX = (pedPoints.Min(point => point.X) + pedPoints.Max(point => point.X)) / 2 + 0.28;
        var centerZ = (pedPoints.Min(point => point.Z) + pedPoints.Max(point => point.Z)) / 2;
        var focus = new ModelPreviewPart([new Point3D(centerX, pedPoints.Min(point => point.Y), centerZ),
            new Point3D(centerX, pedPoints.Max(point => point.Y), centerZ)],
            [], [], "__focus");
        var baseImage = Render(new ModelPreviewData([focus], 0, 0),
            new Dictionary<string, BitmapSource>(), ped, "Tattoo", background);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawImage(baseImage, new Rect(0, 0, Size, Size));
            var scale = Math.Min(220d / image.PixelWidth, 220d / image.PixelHeight);
            var width = image.PixelWidth * scale; var height = image.PixelHeight * scale;
            drawing.DrawImage(image, new Rect(510 + (220 - width) / 2, 270 + (220 - height) / 2, width, height));
        }
        var composite = new RenderTargetBitmap(Size, Size, 96, 96, PixelFormats.Pbgra32);
        composite.Render(visual);
        return composite;
    }

    private static void WritePng(string destination, BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
        encoder.Save(output);
    }

    private static (double CenterX, double CenterY, double CenterZ, double SizeX, double SizeY) Bounds(ModelPreviewPart[] parts)
    {
        var points = parts.SelectMany(p => p.Positions).ToArray();
        if (points.Length == 0) throw new InvalidDataException("Modelo sem geometria para fotografar.");
        return ((points.Min(p => p.X) + points.Max(p => p.X)) / 2,
            (points.Min(p => p.Y) + points.Max(p => p.Y)) / 2,
            (points.Min(p => p.Z) + points.Max(p => p.Z)) / 2,
            points.Max(p => p.X) - points.Min(p => p.X),
            points.Max(p => p.Y) - points.Min(p => p.Y));
    }
}
