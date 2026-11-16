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
        var positions = new[] { new Point3D(-0.3, -0.3, 0), new Point3D(0.3, -0.3, 0),
            new Point3D(0.3, 0.3, 0), new Point3D(-0.3, 0.3, 0) };
        var coordinates = new[] { new Point(0, 1), new Point(1, 1), new Point(1, 0), new Point(0, 0) };
        var part = new ModelPreviewPart(positions, [0, 1, 2, 0, 2, 3], coordinates, "__ped_head_test", false,
            DecorationCoordinates: coordinates.Select(point => new Point(point.X + 1, point.Y - 3)).ToArray());
        var white = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 255, 255, 255, 255 }, 4);
        var red = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 0, 255, 255 }, 4);
        var ped = new PhotoPed([part], new Dictionary<string, BitmapSource> { [part.TextureName!] = white }, []);
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
        Console.WriteLine("PASS: skin surface application, native head UV tile, negative placement, mirrored scale, hair tint and source preservation.");
    }
}
