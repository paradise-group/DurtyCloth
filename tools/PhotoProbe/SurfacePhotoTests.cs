using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using FooxClothStudio;
using CodeWalker.GameFiles;
using CodeWalker.Utils;

internal static class SurfacePhotoTests
{
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        var diffuse = new Texture { Name = "custom_colour", NameHash = 1, Usage = TextureUsage.DIFFUSE };
        var normal = new Texture { Name = "custom_detail", NameHash = 2, Usage = TextureUsage.NORMAL };
        var dictionary = new TextureDictionary(); dictionary.BuildFromTextureList([diffuse, normal]);
        var selected = AppearancePhotoTextures.Select(new YtdFile { TextureDict = dictionary }, "replacement_dictionary");
        if (selected.Length != 1 || selected[0] != diffuse) throw new Exception("A normal map was offered as an appearance photo.");
        Texture AliasedTexture(string name, byte red, byte green, byte blue)
        {
            var file = Path.Combine(output, name + ".png");
            var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { blue, green, red, 255 }, 4);
            using (var stream = File.Create(file))
            {
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(stream);
            }
            var texture = PngTextureReader.Read(file); texture.Name = name; texture.Usage = TextureUsage.DEFAULT;
            return texture;
        }
        var colourMask = AliasedTexture("q", 33, 97, 0);
        var normalMask = AliasedTexture("q2", 189, 65, 247);
        dictionary.BuildFromTextureList([normalMask, colourMask]);
        if (AppearancePhotoTextures.Select(new YtdFile { TextureDict = dictionary }, "lip_mask", paletteMask: true).Single() != colourMask)
            throw new Exception("An aliased lip normal map was selected instead of its green palette mask.");
        var alias = new Texture { Name = "q", NameHash = JenkHash.GenHash("declared_overlay"), Usage = TextureUsage.DEFAULT };
        var extra = new Texture { Name = "q2", NameHash = JenkHash.GenHash("other_image"), Usage = TextureUsage.DEFAULT };
        dictionary.BuildFromTextureList([alias, extra]);
        if (AppearancePhotoTextures.Select(new YtdFile { TextureDict = dictionary }, "declared_overlay").Single() != alias)
            throw new Exception("The declared dictionary hash was ignored in favor of an internal texture alias.");
        var pixels = new byte[] { 0, 128, 0, 170 };
        var greenMask = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, pixels, 4);
        var tinted = PedSurfacePhotos.Overlay(greenMask, Color.FromRgb(38, 28, 23));
        var actual = new byte[4]; tinted.CopyPixels(actual, 4, 0);
        if (!actual.SequenceEqual(new byte[] { 23, 28, 38, 170 })) throw new Exception("Hair tint failed to preserve the alpha mask.");
        greenMask.CopyPixels(actual, 4, 0);
        if (!actual.SequenceEqual(pixels)) throw new Exception("The appearance source was modified.");
        var ramp = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Bgra32, null,
            new byte[] { 0, 0, 0, 255, 255, 255, 255, 255 }, 8);
        var tones = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Bgra32, null,
            new byte[] { 200, 0, 100, 170, 0, 255, 100, 80 }, 8);
        var ramped = PedSurfacePhotos.Overlay(tones, Colors.Brown, ramp);
        var rampPixels = new byte[8]; ramped.CopyPixels(rampPixels, 8, 0);
        if (!rampPixels.SequenceEqual(new byte[] { 0, 0, 0, 170, 255, 255, 255, 80 }))
            throw new Exception("The overlay lost its green-channel palette tones or source opacity.");
        var positions = new[] { new Point3D(-0.3, -0.3, 0), new Point3D(0.3, -0.3, 0),
            new Point3D(0.3, 0.3, 0), new Point3D(-0.3, 0.3, 0) };
        var coordinates = new[] { new Point(0, 1), new Point(1, 1), new Point(1, 0), new Point(0, 0) };
        var part = new ModelPreviewPart(positions, [0, 1, 2, 0, 2, 3], coordinates, "__ped_head_test", false,
            DecorationCoordinates: coordinates.Select(point => new Point(point.X + 1, point.Y - 3)).ToArray());
        var white = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 255, 255, 255, 255 }, 4);
        var red = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 0, 255, 255 }, 4);
        var ped = new PhotoPed([part], new Dictionary<string, BitmapSource> { [part.TextureName!] = white }, []);
        var artworkPixels = new byte[] { 255, 255, 255, 200, 0, 0, 0, 180, 0, 0, 255, 255, 255, 0, 0, 90 };
        var artwork = BitmapSource.Create(4, 1, 96, 96, PixelFormats.Bgra32, null, artworkPixels, 16);
        var makeup = PedSurfacePhotos.AppearanceOverlay("makeup", artwork, ped);
        var makeupPixels = new byte[16]; makeup.CopyPixels(makeupPixels, 16, 0);
        if (!makeupPixels.SequenceEqual(artworkPixels))
            throw new Exception("Makeup artwork lost its white, black or flag colours to a palette tint.");
        var mask = PedSurfacePhotos.AppearanceOverlay("makeup", white, ped, isColourMask: true);
        var maskPixels = new byte[4]; mask.CopyPixels(maskPixels, 4, 0);
        if (!maskPixels.SequenceEqual(new byte[] { 55, 45, 140, 255 }))
            throw new Exception("The alpha-only makeup mask no longer receives its preview colour.");
        foreach (var (position, scale) in new[] { (0.5f, 0.3f), (-0.5f, -0.3f) })
        {
            var file = Path.Combine(output, position < 0 ? "signed-head-decal.png" : "head-decal.png");
            PedSurfacePhotos.SaveTattoo(file, new TattooItem { Zone = "ZONE_HEAD", U = position, V = 0.5f,
                ScaleX = scale, ScaleY = 0.3f }, red, ped, Colors.Gray, new PhotoImageOptions(128));
            var bitmap = new BitmapImage(new Uri(file));
            var image = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            var rendered = new byte[128 * 128 * 4]; image.CopyPixels(rendered, 128 * 4, 0);
            var redPixels = Enumerable.Range(0, 128 * 128).Count(index => rendered[index * 4 + 2] > 2 * rendered[index * 4 + 1]
                && rendered[index * 4 + 2] > 2 * rendered[index * 4]);
            if (redPixels < 30) throw new Exception("The head tattoo did not reach the skin using the native decoration UV tile.");
        }
        if (!part.Positions.SequenceEqual(positions) || !part.TextureCoordinates.SequenceEqual(coordinates))
            throw new Exception("Surface rendering modified the original mesh.");
        var lowerFile = Path.Combine(output, "lower-head-decal.png");
        PedSurfacePhotos.SaveTattoo(lowerFile, new TattooItem { Zone = "ZONE_HEAD", U = 0.5f, V = 0.25f,
            ScaleX = 0.23f, ScaleY = 0.23f }, red, ped, Colors.Gray, new PhotoImageOptions(128));
        var lowerImage = new FormatConvertedBitmap(new BitmapImage(new Uri(lowerFile)), PixelFormats.Bgra32, null, 0);
        var lowerPixels = new byte[128 * 128 * 4]; lowerImage.CopyPixels(lowerPixels, 128 * 4, 0);
        var lowerRows = Enumerable.Range(0, 128 * 128).Where(index => lowerPixels[index * 4 + 2] > 2 * lowerPixels[index * 4 + 1]
            && lowerPixels[index * 4 + 2] > 2 * lowerPixels[index * 4]).Select(index => index / 128).ToArray();
        if (lowerRows.Length < 30 || lowerRows.Average() <= 64)
            throw new Exception("The native lower-head position was inverted onto the forehead.");
        var extentFile = Path.Combine(output, "decal-half-extents.png");
        PedSurfacePhotos.SaveTattoo(extentFile, new TattooItem { Zone = "ZONE_HEAD", U = 0.5f, V = 0.5f,
            ScaleX = 0.1f, ScaleY = 0.1f }, red, ped, Colors.Gray, new PhotoImageOptions(128));
        var extentImage = new FormatConvertedBitmap(new BitmapImage(new Uri(extentFile)), PixelFormats.Bgra32, null, 0);
        var extentPixels = new byte[128 * 128 * 4]; extentImage.CopyPixels(extentPixels, 128 * 4, 0);
        var extentColumns = Enumerable.Range(0, 128 * 128).Where(index => extentPixels[index * 4 + 2] > 2 * extentPixels[index * 4 + 1]
            && extentPixels[index * 4 + 2] > 2 * extentPixels[index * 4]).Select(index => index % 128).ToArray();
        if (extentColumns.Length == 0 || extentColumns.Max() - extentColumns.Min() < 18)
            throw new Exception("The tattoo scale was halved by treating its extents as full dimensions.");
        var hairFile = Path.Combine(output, "scalp-overlay.png");
        PedSurfacePhotos.SaveTattoo(hairFile, new TattooItem { Zone = "ZONE_HEAD", Overlay = "Test_Hair_001",
            U = 0, V = 0, ScaleX = 0.01f, ScaleY = 0.01f }, white, ped, Colors.Gray, new PhotoImageOptions(128));
        var hairImage = new FormatConvertedBitmap(new BitmapImage(new Uri(hairFile)), PixelFormats.Bgra32, null, 0);
        var hairPixels = new byte[128 * 128 * 4]; hairImage.CopyPixels(hairPixels, 128 * 4, 0);
        if (Enumerable.Range(0, 128 * 128).Count(index => hairPixels[index * 4 + 2] > hairPixels[index * 4 + 1] * 1.1
            && hairPixels[index * 4 + 2] > hairPixels[index * 4] * 1.2) < 100)
            throw new Exception("The scalp overlay used the tattoo rectangle instead of the head skin UVs.");
        var body = part with { TextureName = "__ped_uppr_test", DecorationCoordinates = Enumerable.Repeat(new Point(100, 100), 4).ToArray() };
        var bodyPed = new PhotoPed([body], new Dictionary<string, BitmapSource> { [body.TextureName!] = white }, []);
        var bodyFile = Path.Combine(output, "primary-body-overlay.png");
        PedSurfacePhotos.SaveAppearance(bodyFile, "chestHair", red, bodyPed, Colors.Gray, new PhotoImageOptions(128));
        var bodyImage = new FormatConvertedBitmap(new BitmapImage(new Uri(bodyFile)), PixelFormats.Bgra32, null, 0);
        var bodyPixels = new byte[128 * 128 * 4]; bodyImage.CopyPixels(bodyPixels, 128 * 4, 0);
        if (Enumerable.Range(0, 128 * 128).Count(index => bodyPixels[index * 4 + 2] > bodyPixels[index * 4 + 1] * 1.1
            && bodyPixels[index * 4 + 2] > bodyPixels[index * 4] * 1.2) < 100)
            throw new Exception("Body appearance was projected through the tattoo UV atlas instead of the skin UVs.");
        Console.WriteLine("PASS: skin surface application, native head UV tile, negative placement, mirrored scale, hair tint and source preservation.");
    }
}
