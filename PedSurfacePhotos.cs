using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace FooxClothStudio;

public static class PedSurfacePhotos
{
    private const int AtlasSize = 1024;
    private const double AlphaThreshold = 0.08;
    private const double SurfaceOffset = 0.0002;
    private const int DecorationColumn = 1;
    private const int DecorationZoneStride = 2;
    private const int DecorationFirstRow = 1;
    private static readonly Point[] Samples = [new(0.333, 0.333), new(0.1, 0.1), new(0.8, 0.1), new(0.1, 0.8)];

    public static void SaveAppearance(string destination, string category, BitmapSource image,
        PhotoPed ped, Color background, PhotoImageOptions? options = null)
    {
        var bodySlot = category is "chestHair" or "bodyBlemishes" or "addBodyBlemishes" ? "uppr" : "head";
        var tint = category is "facialHair" or "eyebrows" or "chestHair" ? Color.FromRgb(38, 28, 23)
            : category is "blush" or "lipstick" or "makeup" ? Color.FromRgb(140, 45, 55) : (Color?)null;
        var paletteName = category is "facialHair" or "eyebrows" or "chestHair" ? "mp_hair_tint" : "mp_makeup_tint";
        var palette = tint is not null ? ped.OverlayPalettes?.GetValueOrDefault(paletteName) : null;
        (options ?? new()).Write(destination, Render(ped, bodySlot, Overlay(image, tint, palette), background,
            null, chooseView: false));
    }

    public static void SaveTattoo(string destination, TattooItem tattoo, BitmapSource image,
        PhotoPed ped, Color background, PhotoImageOptions? options = null)
        => (options ?? new()).Write(destination, RenderTattoo(tattoo, image, ped, background));

    public static BitmapSource RenderTattoo(TattooItem tattoo, BitmapSource image, PhotoPed ped, Color background)
    {
        if (string.IsNullOrEmpty(tattoo.Zone))
            throw new InvalidDataException("A tatuagem não tem metadados de posição; não é possível fotografá-la aplicada ao corpo.");
        var bodySlot = tattoo.Zone == "ZONE_HEAD" ? "head"
            : tattoo.Zone is "ZONE_LEFT_LEG" or "ZONE_RIGHT_LEG" ? "lowr" : "uppr";
        var coordinates = ped.Parts.Where(part => part.TextureName?.StartsWith("__ped_" + bodySlot + "_", StringComparison.Ordinal) == true)
            .SelectMany(part => part.DecorationCoordinates ?? part.TextureCoordinates).ToArray();
        if (coordinates.Length == 0 || !float.IsFinite(tattoo.ScaleX) || !float.IsFinite(tattoo.ScaleY)
            || tattoo.ScaleX == 0 || tattoo.ScaleY == 0 || !float.IsFinite(tattoo.U) || !float.IsFinite(tattoo.V)
            || !float.IsFinite(tattoo.Rotation)) throw new InvalidDataException("Coordenadas de tatuagem inválidas.");
        var zone = Array.IndexOf(TattooExporter.Zones, tattoo.Zone);
        if (zone < 0) throw new InvalidDataException("Zona de tatuagem inválida.");
        if (tattoo.UseTintColour) image = Overlay(image, Color.FromRgb(38, 28, 23), ped.OverlayPalettes?.GetValueOrDefault("mp_hair_tint"));
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var center = new Point((tattoo.U - Math.Floor(tattoo.U)) * AtlasSize, (1 - tattoo.V) * AtlasSize);
            var width = Math.Abs(tattoo.ScaleX) * AtlasSize;
            var height = Math.Abs(tattoo.ScaleY) * AtlasSize;
            foreach (var repeat in new[] { -1, 0, 1 })
            {
                drawing.PushTransform(new TranslateTransform(repeat * AtlasSize, 0));
                drawing.PushTransform(new RotateTransform(-tattoo.Rotation, center.X, center.Y));
                drawing.PushTransform(new ScaleTransform(Math.Sign(tattoo.ScaleX), Math.Sign(tattoo.ScaleY), center.X, center.Y));
                drawing.DrawImage(image, new Rect(center.X - width / 2, center.Y - height / 2, width, height));
                drawing.Pop(); drawing.Pop(); drawing.Pop();
            }
        }
        var atlas = new RenderTargetBitmap(AtlasSize, AtlasSize, 96, 96, PixelFormats.Pbgra32);
        atlas.Render(visual);
        return Render(ped, bodySlot, atlas, background, zone, chooseView: true);
    }

    public static BitmapSource Overlay(BitmapSource source, Color? tint, BitmapSource? palette = null)
    {
        var image = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[image.PixelWidth * image.PixelHeight * 4];
        image.CopyPixels(pixels, image.PixelWidth * 4, 0);
        byte[]? ramp = null;
        if (palette is not null)
        {
            var converted = new FormatConvertedBitmap(palette, PixelFormats.Bgra32, null, 0);
            ramp = new byte[converted.PixelWidth * converted.PixelHeight * 4];
            converted.CopyPixels(ramp, converted.PixelWidth * 4, 0);
        }
        if (tint is Color color)
            for (var offset = 0; offset < pixels.Length; offset += 4)
            {
                if (ramp is not null)
                {
                    var coordinate = Math.Clamp(pixels[offset + 1] / 255.0 * palette!.PixelWidth - 0.5, 0, palette.PixelWidth - 1);
                    var first = (int)coordinate;
                    var second = Math.Min(first + 1, palette.PixelWidth - 1);
                    var mix = coordinate - first;
                    byte Channel(int channel) => (byte)Math.Round(ramp[first * 4 + channel] * (1 - mix) + ramp[second * 4 + channel] * mix);
                    color = Color.FromRgb(Channel(2), Channel(1), Channel(0));
                }
                pixels[offset] = color.B;
                pixels[offset + 1] = color.G;
                pixels[offset + 2] = color.R;
            }
        var result = BitmapSource.Create(image.PixelWidth, image.PixelHeight, 96, 96,
            PixelFormats.Bgra32, null, pixels, image.PixelWidth * 4);
        result.Freeze();
        return result;
    }

    private static BitmapSource Render(PhotoPed ped, string bodySlot, BitmapSource overlay, Color background, int? decorationZone, bool chooseView)
    {
        var parts = ped.Parts.Where(part => part.TextureName?.StartsWith("__ped_" + bodySlot + "_", StringComparison.Ordinal) == true).ToArray();
        if (parts.Length == 0) throw new InvalidDataException("O ped não tem a superfície necessária: " + bodySlot);
        var textures = new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);
        var surfaces = new List<ModelPreviewPart>();
        var affected = new List<Point3D>();
        var alpha = new FormatConvertedBitmap(overlay, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[alpha.PixelWidth * alpha.PixelHeight * 4];
        alpha.CopyPixels(pixels, alpha.PixelWidth * 4, 0);
        bool Visible(Point uv)
        {
            if (uv.X < 0 || uv.X > 1 || uv.Y < 0 || uv.Y > 1) return false;
            var x = Math.Min((int)(uv.X * alpha.PixelWidth), alpha.PixelWidth - 1);
            var y = Math.Min((int)(uv.Y * alpha.PixelHeight), alpha.PixelHeight - 1);
            return pixels[(y * alpha.PixelWidth + x) * 4 + 3] > byte.MaxValue * AlphaThreshold;
        }
        foreach (var part in parts)
        {
            if (!ped.Textures.TryGetValue(part.TextureName!, out var skin))
                throw new InvalidDataException("Textura de pele indisponível: " + part.TextureName);
            textures[part.TextureName!] = skin;
            var coordinates = decorationZone is int zone
                ? (part.DecorationCoordinates ?? throw new InvalidDataException("O ped não tem coordenadas de decoração."))
                    .Select(point => new Point(point.X - DecorationColumn,
                        point.Y + zone * DecorationZoneStride + DecorationFirstRow)).ToArray()
                : part.TextureCoordinates;
            if (coordinates.Length != part.Positions.Length) continue;
            const double seamTolerance = 0.005;
            var surfaceIndices = part.Indices.Chunk(3).Where(triangle => triangle.Length == 3 && triangle.All(index =>
                coordinates[index].X >= -seamTolerance && coordinates[index].X <= 1 + seamTolerance
                && coordinates[index].Y >= -seamTolerance && coordinates[index].Y <= 1 + seamTolerance)).SelectMany(triangle => triangle).ToArray();
            var normals = new Vector3D[part.Positions.Length];
            for (var triangle = 0; triangle + 2 < part.Indices.Length; triangle += 3)
            {
                var first = part.Indices[triangle];
                var second = part.Indices[triangle + 1];
                var third = part.Indices[triangle + 2];
                var normal = Vector3D.CrossProduct(part.Positions[second] - part.Positions[first], part.Positions[third] - part.Positions[first]);
                normals[first] += normal; normals[second] += normal; normals[third] += normal;
            }
            var positions = part.Positions.Select((point, index) =>
            {
                var normal = normals[index];
                if (normal.LengthSquared > 0) normal.Normalize();
                return point + normal * SurfaceOffset;
            }).ToArray();
            var name = "__surface_overlay_" + surfaces.Count;
            surfaces.Add(part with { Positions = positions, Indices = surfaceIndices, TextureName = name, TextureCoordinates = coordinates,
                UsesTextureAlpha = true, IsEmissive = false, IsDecoration = false });
            textures[name] = overlay;
            for (var triangle = 0; triangle + 2 < surfaceIndices.Length; triangle += 3)
            {
                var first = surfaceIndices[triangle];
                var second = surfaceIndices[triangle + 1];
                var third = surfaceIndices[triangle + 2];
                foreach (var sample in Samples)
                {
                    var remainder = 1 - sample.X - sample.Y;
                    var uv = new Point(coordinates[first].X * remainder + coordinates[second].X * sample.X + coordinates[third].X * sample.Y,
                        coordinates[first].Y * remainder + coordinates[second].Y * sample.X + coordinates[third].Y * sample.Y);
                    if (!Visible(uv)) continue;
                    affected.Add(new Point3D(part.Positions[first].X * remainder + part.Positions[second].X * sample.X + part.Positions[third].X * sample.Y,
                        part.Positions[first].Y * remainder + part.Positions[second].Y * sample.X + part.Positions[third].Y * sample.Y,
                        part.Positions[first].Z * remainder + part.Positions[second].Z * sample.X + part.Positions[third].Z * sample.Y));
                }
            }
        }
        var all = parts.SelectMany(part => part.Positions).ToArray();
        var center = new Point3D((all.Min(point => point.X) + all.Max(point => point.X)) / 2,
            (all.Min(point => point.Y) + all.Max(point => point.Y)) / 2, (all.Min(point => point.Z) + all.Max(point => point.Z)) / 2);
        var rotation = Matrix3D.Identity;
        if (chooseView && affected.Count > 0)
        {
            var direction = new Vector3D(affected.Average(point => point.X) - center.X, 0, affected.Average(point => point.Z) - center.Z);
            var angle = -Math.Atan2(direction.X, direction.Z) * 180 / Math.PI;
            rotation.RotateAt(new Quaternion(new Vector3D(0, 1, 0), angle), center);
        }
        var rotated = parts.Select(part => part with { UsesTextureAlpha = false }).Concat(surfaces)
            .Select(part => part with { Positions = part.Positions.Select(rotation.Transform).ToArray() }).ToArray();
        var triangles = rotated.Sum(part => part.Indices.Length / 3);
        ModelPreviewPart[]? framing = null;
        if (bodySlot != "head" && affected.Count > 0)
        {
            const double contextMargin = 0.12;
            var focus = affected.Select(rotation.Transform).ToArray();
            framing = [new ModelPreviewPart([
                new Point3D(focus.Min(point => point.X) - contextMargin, focus.Min(point => point.Y) - contextMargin, focus.Min(point => point.Z)),
                new Point3D(focus.Max(point => point.X) + contextMargin, focus.Max(point => point.Y) + contextMargin, focus.Max(point => point.Z))
            ], [], [], null)];
        }
        return PreviewPhotoRenderer.Render(new ModelPreviewData(rotated, triangles, triangles), textures, null,
            "Applied surface", background, framingOverride: framing);
    }
}
