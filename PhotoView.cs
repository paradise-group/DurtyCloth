using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace FooxClothStudio;

public static class PhotoView
{
    private readonly record struct SurfaceSample(double Depth, double Weight, byte Blue, byte Green, byte Red, byte Alpha);
    private static readonly (double First, double Second, double Third)[] SamplePoints =
    [
        (1d / 3, 1d / 3, 1d / 3), (0.6, 0.2, 0.2), (0.2, 0.6, 0.2), (0.2, 0.2, 0.6),
        (0.8, 0.1, 0.1), (0.1, 0.8, 0.1), (0.1, 0.1, 0.8),
        (0.5, 0.5, 0), (0.5, 0, 0.5), (0, 0.5, 0.5)
    ];
    private sealed class Pixels(BitmapSource source)
    {
        public int Width { get; } = source.PixelWidth;
        public int Height { get; } = source.PixelHeight;
        public byte[] Data { get; } = Read(source);
        private static byte[] Read(BitmapSource source)
        {
            var data = new byte[checked(source.PixelWidth * source.PixelHeight * 4)];
            source.CopyPixels(data, source.PixelWidth * 4, 0);
            return data;
        }
    }

    public static bool IsDecorationTemplate(BitmapSource image)
    {
        var pixels = new Pixels(image);
        var cornerWidth = Math.Max(1, pixels.Width / 16);
        var cornerHeight = Math.Max(1, pixels.Height / 16);
        if (cornerWidth == pixels.Width && cornerHeight == pixels.Height)
            return pixels.Data[0] == 0 && pixels.Data[1] == 0 && pixels.Data[2] == 0;
        for (var y = 0; y < pixels.Height; y++)
            for (var x = 0; x < pixels.Width; x++)
            {
                if (x < cornerWidth && y < cornerHeight) continue;
                var offset = (y * pixels.Width + x) * 4;
                if (pixels.Data[offset] != 0 || pixels.Data[offset + 1] != 0 || pixels.Data[offset + 2] != 0) return false;
            }
        return true;
    }

    public static bool ShowBack(ModelPreviewData model, IReadOnlyDictionary<string, BitmapSource> images, string slot)
    {
        if (slot == "hand") return true;
        if (slot != "p_head") return false;
        var positions = model.Parts.SelectMany(part => part.Positions).ToArray();
        if (positions.Length == 0) return false;
        var minimum = positions.Min(point => point.Z);
        var maximum = positions.Max(point => point.Z);
        var depth = maximum - minimum;
        if (depth <= 0) return false;
        var center = new Point3D((positions.Min(point => point.X) + positions.Max(point => point.X)) / 2,
            (positions.Min(point => point.Y) + positions.Max(point => point.Y)) / 2, (minimum + maximum) / 2);
        var orientation = 0d;
        foreach (var part in model.Parts)
            for (var index = 0; index + 2 < part.Indices.Length; index += 3)
            {
                var first = part.Positions[part.Indices[index]];
                var second = part.Positions[part.Indices[index + 1]];
                var third = part.Positions[part.Indices[index + 2]];
                var centroid = new Point3D((first.X + second.X + third.X) / 3, (first.Y + second.Y + third.Y) / 3, (first.Z + second.Z + third.Z) / 3);
                orientation += Vector3D.DotProduct(Vector3D.CrossProduct(second - first, third - first), centroid - center);
            }
        var outward = orientation < 0 ? -1 : 1;
        double weightedDepth = 0, total = 0;
        foreach (var part in model.Parts)
        {
            if (part.TextureCoordinates.Length != part.Positions.Length ||
                !images.TryGetValue(part.TextureName ?? "__selected_diffuse", out var image)) continue;
            var pixels = new Pixels(image);
            var samples = new List<SurfaceSample>();
            for (var index = 0; index + 2 < part.Indices.Length; index += 3)
            {
                var first = part.Indices[index]; var second = part.Indices[index + 1]; var third = part.Indices[index + 2];
                var normal = Vector3D.CrossProduct(part.Positions[second] - part.Positions[first], part.Positions[third] - part.Positions[first]);
                if (normal.Z == 0) continue;
                var centroid = new Point3D((part.Positions[first].X + part.Positions[second].X + part.Positions[third].X) / 3,
                    (part.Positions[first].Y + part.Positions[second].Y + part.Positions[third].Y) / 3,
                    (part.Positions[first].Z + part.Positions[second].Z + part.Positions[third].Z) / 3);
                if (outward * Vector3D.DotProduct(normal, centroid - center) < 0) continue;
                foreach (var location in SamplePoints)
                {
                    var u = part.TextureCoordinates[first].X * location.First + part.TextureCoordinates[second].X * location.Second + part.TextureCoordinates[third].X * location.Third;
                    var v = part.TextureCoordinates[first].Y * location.First + part.TextureCoordinates[second].Y * location.Second + part.TextureCoordinates[third].Y * location.Third;
                    var offset = ((int)(Wrap(v) * pixels.Height) * pixels.Width + (int)(Wrap(u) * pixels.Width)) * 4;
                    if (part.UsesTextureAlpha && pixels.Data[offset + 3] == 0) continue;
                    var position = part.Positions[first].Z * location.First + part.Positions[second].Z * location.Second + part.Positions[third].Z * location.Third;
                    samples.Add(new(position, Math.Abs(normal.Z), pixels.Data[offset], pixels.Data[offset + 1], pixels.Data[offset + 2], pixels.Data[offset + 3]));
                }
            }
            var area = samples.Sum(sample => sample.Weight);
            if (area <= 0) continue;
            static int Bucket(byte blue, byte green, byte red) => (blue >> 4) | ((green >> 4) << 4) | ((red >> 4) << 8);
            var atlas = new Dictionary<int, int>();
            for (var offset = 0; offset < pixels.Data.Length; offset += 4)
            {
                var bucket = Bucket(pixels.Data[offset], pixels.Data[offset + 1], pixels.Data[offset + 2]);
                atlas[bucket] = atlas.GetValueOrDefault(bucket) + 1;
            }
            var dominant = samples.GroupBy(sample => Bucket(sample.Blue, sample.Green, sample.Red))
                .OrderByDescending(group => group.Sum(sample => sample.Weight))
                .ThenByDescending(group => atlas.GetValueOrDefault(group.Key)).First().ToArray();
            var dominantArea = dominant.Sum(sample => sample.Weight);
            var blue = dominant.Sum(sample => sample.Blue * sample.Weight) / dominantArea;
            var green = dominant.Sum(sample => sample.Green * sample.Weight) / dominantArea;
            var red = dominant.Sum(sample => sample.Red * sample.Weight) / dominantArea;
            double Contrast(SurfaceSample sample) => Math.Pow(sample.Blue - blue, 2) + Math.Pow(sample.Green - green, 2) + Math.Pow(sample.Red - red, 2);
            var variance = samples.Sum(sample => Contrast(sample) * sample.Weight) / (area * 3);
            const double minimumDetailContrast = 20;
            if (!part.IsEmissive && variance < minimumDetailContrast * minimumDetailContrast) continue;
            foreach (var sample in samples)
            {
                var weight = sample.Weight * Contrast(sample) * (part.IsEmissive ? sample.Alpha / 255d : 1);
                weightedDepth += weight * sample.Depth;
                total += weight;
            }
        }
        const double decisionMargin = 0.1;
        return total > 0 && weightedDepth / total < (minimum + maximum) / 2 - depth * decisionMargin;
    }

    private static double Wrap(double coordinate) => coordinate - Math.Floor(coordinate);
}
