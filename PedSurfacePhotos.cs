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
        PhotoPed ped, Color background, PhotoImageOptions? options = null, bool isColourMask = false)
    {
        var bodySlot = category is "chestHair" or "bodyBlemishes" or "addBodyBlemishes" ? "uppr" : "head";
        (options ?? new()).Write(destination, Render(ped, bodySlot, AppearanceOverlay(category, image, ped, isColourMask), background,
            null, chooseView: false));
    }

    public static BitmapSource AppearanceOverlay(string category, BitmapSource image, PhotoPed ped, bool isColourMask = false)
    {
        var useMakeupPalette = category is "blush" or "lipstick" || category == "makeup" && isColourMask;
        var tint = category is "facialHair" or "eyebrows" or "chestHair" ? Color.FromRgb(38, 28, 23)
            : useMakeupPalette ? Color.FromRgb(140, 45, 55) : (Color?)null;
        var paletteName = category is "facialHair" or "eyebrows" or "chestHair" ? "mp_hair_tint" : "mp_makeup_tint";
        var palette = tint is not null ? ped.OverlayPalettes?.GetValueOrDefault(paletteName) : null;
        return Overlay(image, tint, palette);
    }

    public static void SaveTattoo(string destination, TattooItem tattoo, BitmapSource image,
        PhotoPed ped, Color background, PhotoImageOptions? options = null)
        => (options ?? new()).Write(destination, RenderTattoo(tattoo, image, ped, background));

    public static BitmapSource RenderTattoo(TattooItem tattoo, BitmapSource image, PhotoPed ped, Color background)
    {
        if (tattoo.IsHairOverlay)
            return Render(ped, "head", Overlay(image, Color.FromRgb(38, 28, 23)), background, null, chooseView: false);
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
            var halfWidth = Math.Abs(tattoo.ScaleX) * AtlasSize;
            var halfHeight = Math.Abs(tattoo.ScaleY) * AtlasSize;
            foreach (var repeat in new[] { -1, 0, 1 })
            {
                drawing.PushTransform(new TranslateTransform(repeat * AtlasSize, 0));
                drawing.PushTransform(new RotateTransform(-tattoo.Rotation, center.X, center.Y));
                drawing.PushTransform(new ScaleTransform(Math.Sign(tattoo.ScaleX), Math.Sign(tattoo.ScaleY), center.X, center.Y));
                drawing.DrawImage(image, new Rect(center.X - halfWidth, center.Y - halfHeight, halfWidth * 2, halfHeight * 2));
                drawing.Pop(); drawing.Pop(); drawing.Pop();
            }
        }
        var atlas = new RenderTargetBitmap(AtlasSize, AtlasSize, 96, 96, PixelFormats.Pbgra32);
        atlas.Render(visual);
        return Render(ped, bodySlot, atlas, background, zone, chooseView: true, modulateWithSkin: bodySlot != "head");
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

    private static BitmapSource Render(PhotoPed ped, string bodySlot, BitmapSource overlay, Color background, int? decorationZone, bool chooseView,
        bool modulateWithSkin = false)
    {
        var parts = ped.Parts.Where(part => part.TextureName?.StartsWith("__ped_" + bodySlot + "_", StringComparison.Ordinal) == true).ToArray();
        if (parts.Length == 0) throw new InvalidDataException("O ped não tem a superfície necessária: " + bodySlot);
        var textures = new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);
        var surfaces = new List<ModelPreviewPart>();
        var affected = new List<Point3D>();
        var affectedNormals = new List<Vector3D>();
        var alpha = new FormatConvertedBitmap(overlay, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[alpha.PixelWidth * alpha.PixelHeight * 4];
        alpha.CopyPixels(pixels, alpha.PixelWidth * 4, 0);
        bool Visible(Point uv)
        {
            if (uv.X < 0 || uv.X > 1 || uv.Y < 0 || uv.Y > 1) return false;
            var x = Math.Min((int)(uv.X * alpha.PixelWidth), alpha.PixelWidth - 1);
            var y = Math.Min((int)(uv.Y * alpha.PixelHeight), alpha.PixelHeight - 1);
            var offset = (y * alpha.PixelWidth + x) * 4;
            return pixels[offset + 3] > byte.MaxValue * AlphaThreshold && (!modulateWithSkin
                || pixels[offset] < 250 || pixels[offset + 1] < 250 || pixels[offset + 2] < 250);
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
            textures[name] = modulateWithSkin
                ? ModulateSkin(overlay, skin, part.TextureCoordinates, coordinates, surfaceIndices) : overlay;
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
                    affectedNormals.Add(normals[first] * remainder + normals[second] * sample.X + normals[third] * sample.Y);
                }
            }
        }
        var all = parts.SelectMany(part => part.Positions).ToArray();
        var center = new Point3D((all.Min(point => point.X) + all.Max(point => point.X)) / 2,
            (all.Min(point => point.Y) + all.Max(point => point.Y)) / 2, (all.Min(point => point.Z) + all.Max(point => point.Z)) / 2);
        var rotation = Matrix3D.Identity;
        if (chooseView && affected.Count > 0)
        {
            var direction = new Vector3D(affectedNormals.Average(normal => normal.X), 0, affectedNormals.Average(normal => normal.Z));
            var angle = Math.Round(-Math.Atan2(direction.X, direction.Z) * 2 / Math.PI) * 90;
            if (decorationZone == 0) angle = direction.Z >= 0 ? 0 : 180;
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

    public static BitmapSource ModulateSkin(BitmapSource overlay, BitmapSource skin, Point[] skinCoordinates,
        Point[] decorationCoordinates, int[] indices)
    {
        var source = new FormatConvertedBitmap(overlay, PixelFormats.Bgra32, null, 0);
        var baseImage = new FormatConvertedBitmap(skin, PixelFormats.Bgra32, null, 0);
        var ink = new byte[source.PixelWidth * source.PixelHeight * 4];
        var skinPixels = new byte[baseImage.PixelWidth * baseImage.PixelHeight * 4];
        source.CopyPixels(ink, source.PixelWidth * 4, 0);
        baseImage.CopyPixels(skinPixels, baseImage.PixelWidth * 4, 0);
        var result = new byte[ink.Length];
        for (var triangle = 0; triangle + 2 < indices.Length; triangle += 3)
        {
            var first = indices[triangle]; var second = indices[triangle + 1]; var third = indices[triangle + 2];
            var firstUv = decorationCoordinates[first]; var secondUv = decorationCoordinates[second]; var thirdUv = decorationCoordinates[third];
            var denominator = (secondUv.Y - thirdUv.Y) * (firstUv.X - thirdUv.X)
                + (thirdUv.X - secondUv.X) * (firstUv.Y - thirdUv.Y);
            if (Math.Abs(denominator) < 1e-12) continue;
            var left = Math.Clamp((int)Math.Floor(Math.Min(firstUv.X, Math.Min(secondUv.X, thirdUv.X)) * source.PixelWidth), 0, source.PixelWidth - 1);
            var right = Math.Clamp((int)Math.Ceiling(Math.Max(firstUv.X, Math.Max(secondUv.X, thirdUv.X)) * source.PixelWidth), 0, source.PixelWidth - 1);
            var top = Math.Clamp((int)Math.Floor(Math.Min(firstUv.Y, Math.Min(secondUv.Y, thirdUv.Y)) * source.PixelHeight), 0, source.PixelHeight - 1);
            var bottom = Math.Clamp((int)Math.Ceiling(Math.Max(firstUv.Y, Math.Max(secondUv.Y, thirdUv.Y)) * source.PixelHeight), 0, source.PixelHeight - 1);
            for (var y = top; y <= bottom; y++)
                for (var x = left; x <= right; x++)
                {
                    var offset = (y * source.PixelWidth + x) * 4;
                    if (ink[offset + 3] == 0 || ink[offset] == 255 && ink[offset + 1] == 255 && ink[offset + 2] == 255) continue;
                    var u = (x + 0.5) / source.PixelWidth; var v = (y + 0.5) / source.PixelHeight;
                    var firstWeight = ((secondUv.Y - thirdUv.Y) * (u - thirdUv.X) + (thirdUv.X - secondUv.X) * (v - thirdUv.Y)) / denominator;
                    var secondWeight = ((thirdUv.Y - firstUv.Y) * (u - thirdUv.X) + (firstUv.X - thirdUv.X) * (v - thirdUv.Y)) / denominator;
                    var thirdWeight = 1 - firstWeight - secondWeight;
                    if (firstWeight < -1e-7 || secondWeight < -1e-7 || thirdWeight < -1e-7) continue;
                    var skinU = skinCoordinates[first].X * firstWeight + skinCoordinates[second].X * secondWeight + skinCoordinates[third].X * thirdWeight;
                    var skinV = skinCoordinates[first].Y * firstWeight + skinCoordinates[second].Y * secondWeight + skinCoordinates[third].Y * thirdWeight;
                    var skinX = Math.Min((int)((skinU - Math.Floor(skinU)) * baseImage.PixelWidth), baseImage.PixelWidth - 1);
                    var skinY = Math.Min((int)((skinV - Math.Floor(skinV)) * baseImage.PixelHeight), baseImage.PixelHeight - 1);
                    var skinOffset = (skinY * baseImage.PixelWidth + skinX) * 4;
                    for (var channel = 0; channel < 3; channel++) result[offset + channel] = (byte)((skinPixels[skinOffset + channel] * ink[offset + channel] + 127) / 255);
                    result[offset + 3] = ink[offset + 3];
                }
        }
        var bitmap = BitmapSource.Create(source.PixelWidth, source.PixelHeight, 96, 96, PixelFormats.Bgra32, null, result, source.PixelWidth * 4);
        bitmap.Freeze();
        return bitmap;
    }
}
