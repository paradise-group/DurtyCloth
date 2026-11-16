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
        string category, Color background, PhotoImageOptions? options = null, Matrix3D? standaloneAttachment = null)
        => (options ?? new()).Write(destination, Render(garment, garmentTextures, ped, category, background, standaloneAttachment));

    public static bool IsEmptySlot(ModelPreviewData model, string category)
    {
        var slot = category.Contains('[') ? category.Split('[').Last().TrimEnd(']') : "";
        return (slot is "p_head" or "p_eyes" or "p_ears" or "p_mouth" or "berd" or "hair" or
            "uppr" or "lowr" or "hand" or "feet" or "teef" or "accs" or "task" or "jbib") && model.SourceTriangles <= 2;
    }

    public static void SaveEmptySlot(string destination, PhotoPed ped, string category, Color background, PhotoImageOptions? options = null)
        => (options ?? new()).Write(destination, RenderEmptySlot(ped, category, background));

    private static RenderTargetBitmap RenderEmptySlot(PhotoPed ped, string category, Color background)
    {
        var slot = category.Contains('[') ? category.Split('[').Last().TrimEnd(']') : category;
        var bodySlot = slot switch
        {
            "p_head" or "p_eyes" or "p_ears" or "p_mouth" or "berd" or "hair" or "head" => "head",
            "lowr" => "lowr",
            "feet" => "feet",
            _ => "uppr"
        };
        var parts = ped.Parts.Where(part => part.TextureName?.StartsWith("__ped_" + bodySlot + "_", StringComparison.Ordinal) == true).ToArray();
        if (parts.Length == 0) throw new InvalidDataException("Ped base sem geometria para a zona " + bodySlot);
        var triangles = parts.Sum(part => part.Indices.Length / 3);
        return Render(new ModelPreviewData(parts, triangles, triangles), ped.Textures, null, "Body context", background);
    }

    internal static RenderTargetBitmap Render(ModelPreviewData garment,
        Dictionary<string, BitmapSource> garmentTextures, PhotoPed? ped,
        string category, Color background, Matrix3D? standaloneAttachment = null, ModelPreviewPart[]? framingOverride = null)
    {
        var slot = category.Contains('[') ? category.Split('[').Last().TrimEnd(']') : "";
        if (IsEmptySlot(garment, category))
            return RenderEmptySlot(ped ?? throw new InvalidDataException("A fotografia sem acessório requer o ped base."), category, background);
        if (slot == "decl" && garment.Parts.All(part => part.IsDecoration &&
            garmentTextures.TryGetValue(part.TextureName ?? "__selected_diffuse", out var texture) && PhotoView.IsDecorationTemplate(texture)))
            return RenderEmptySlot(ped ?? throw new InvalidDataException("A fotografia de decal requer o ped base."), category, background);
        if (slot == "p_head" && ped is null)
            garment = standaloneAttachment is Matrix3D attachment ? ModelPreview.AttachToBone(garment, attachment)
                : throw new InvalidDataException("A fotografia de chapéus requer o alinhamento do ped base.");
        if (ped is not null && ModelPreview.PropBoneTag(slot) is int boneTag)
            garment = ModelPreview.AttachToBone(garment, ped.Attachments[boneTag]);
        var framingParts = framingOverride ?? garment.Parts;
        var focus = Bounds(framingParts);
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
        var headContext = ped is not null && (slot is "hair" or "berd" or "p_eyes" or "p_ears" or "p_mouth");
        var garmentParts = slot == "hair" ? OrderHairLayers(garment.Parts) : garment.Parts;
        var bodyParts = ped is null ? [] : ped.Parts.Where(p => !p.TextureName!.StartsWith("__ped_" + slot + "_", StringComparison.Ordinal) &&
                !p.TextureName.StartsWith("__ped_hair_", StringComparison.Ordinal) &&
                (!headContext || p.TextureName.StartsWith("__ped_head_", StringComparison.Ordinal)) &&
                !(slot == "teef" && itemReachesFeet && p.TextureName.StartsWith("__ped_feet_", StringComparison.Ordinal))).ToArray();
        var parts = slot == "hair" ? bodyParts.Concat(garmentParts).ToArray() : garmentParts.Concat(bodyParts).ToArray();
        if (slot == "p_ears" && ped is not null)
        {
            (parts, framingParts) = FrameEar(parts, garment.Parts, bodyParts);
            focus = Bounds(framingParts);
        }
        if (PhotoView.ShowBack(garment, garmentTextures, slot))
        {
            var rotation = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), 180),
                new Point3D(focus.CenterX, focus.CenterY, focus.CenterZ));
            ModelPreviewPart Rotate(ModelPreviewPart part) => part with { Positions = part.Positions.Select(rotation.Transform).ToArray() };
            parts = parts.Select(Rotate).ToArray();
            framingParts = framingParts.Select(Rotate).ToArray();
            focus = Bounds(framingParts);
        }
        var images = new Dictionary<string, BitmapSource>(garmentTextures, StringComparer.OrdinalIgnoreCase);
        if (ped is not null)
            foreach (var entry in ped.Textures) images[entry.Key] = entry.Value;

        var models = new Model3DGroup();
        var opaqueImages = new Dictionary<BitmapSource, BitmapSource>();
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
                var sourceImage = image;
                if (!part.UsesTextureAlpha)
                {
                    if (!opaqueImages.TryGetValue(image, out var opaque))
                    {
                        opaque = OpaqueTexture(image);
                        opaqueImages[image] = opaque;
                    }
                    image = opaque;
                }
                var brush = new ImageBrush(image)
                {
                    TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute,
                    Viewport = new Rect(0, 0, 1, 1), Stretch = Stretch.Fill
                };
                material = new DiffuseMaterial(brush);
                if (part.IsEmissive)
                {
                    var emission = brush.Clone(); emission.ImageSource = sourceImage;
                    material = new MaterialGroup { Children = { material, new EmissiveMaterial(emission) } };
                }
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
        var contextHeight = ped is null || headContext ? 0 : Bounds(ped.Parts).SizeY * 0.30;
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

    private static BitmapSource OpaqueTexture(BitmapSource image)
    {
        var pixels = new byte[checked(image.PixelWidth * image.PixelHeight * 4)];
        image.CopyPixels(pixels, image.PixelWidth * 4, 0);
        for (var offset = 3; offset < pixels.Length; offset += 4) pixels[offset] = 255;
        var result = BitmapSource.Create(image.PixelWidth, image.PixelHeight, 96, 96, PixelFormats.Bgra32,
            null, pixels, image.PixelWidth * 4);
        result.Freeze();
        return result;
    }

    private static (ModelPreviewPart[] Parts, ModelPreviewPart[] Framing) FrameEar(
        ModelPreviewPart[] parts, ModelPreviewPart[] earrings, ModelPreviewPart[] head)
    {
        const double sideViewDegrees = 55;
        const double headWidthRatio = 0.5;
        const double headHeightRatio = 0.7;
        var headBounds = Bounds(head);
        var points = earrings.SelectMany(part => part.Positions).ToArray();
        var side = points.Any(point => point.X > headBounds.CenterX) ? 1 : -1;
        var visible = points.Where(point => side * (point.X - headBounds.CenterX) >= 0).ToArray();
        if (visible.Length == 0) visible = points;
        var rotation = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), -side * sideViewDegrees),
            new Point3D(headBounds.CenterX, headBounds.CenterY, headBounds.CenterZ));
        var aligned = parts.Select(part => part with { Positions = part.Positions.Select(rotation.Transform).ToArray() }).ToArray();
        var selected = visible.Select(rotation.Transform).ToArray();
        var bounds = Bounds([new ModelPreviewPart(selected, [], [], null)]);
        var halfWidth = Math.Max(bounds.SizeX, headBounds.SizeX * headWidthRatio) / 2;
        var halfHeight = Math.Max(bounds.SizeY, headBounds.SizeY * headHeightRatio) / 2;
        var frame = new ModelPreviewPart([
            new Point3D(bounds.CenterX - halfWidth, bounds.CenterY - halfHeight, bounds.CenterZ),
            new Point3D(bounds.CenterX + halfWidth, bounds.CenterY + halfHeight, bounds.CenterZ)
        ], [], [], null);
        return (aligned, [frame]);
    }

    private static ModelPreviewPart[] OrderHairLayers(ModelPreviewPart[] parts)
    {
        // WPF does not sort transparent hair cards. Merge matching materials so
        // cards from different geometries can be drawn together, back to front.
        // The photo camera looks down -Z; keep every vertex, UV and triangle.
        return parts.GroupBy(part => (part.TextureName, part.UsesTextureAlpha, part.IsEmissive, part.IsDecoration, HasUvs: part.TextureCoordinates.Length == part.Positions.Length))
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
                    coordinates.ToArray(), group.Key.TextureName, group.Key.UsesTextureAlpha, group.Key.IsEmissive, group.Key.IsDecoration);
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
        => PedSurfacePhotos.RenderTattoo(tattoo, image, ped, background);

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
