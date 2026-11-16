using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using FooxClothStudio;

internal static class HeadPhotoTests
{
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        var positions = new[] { new Point3D(-1, -1, 0), new Point3D(1, -1, 0), new Point3D(1, 1, 0), new Point3D(-1, 1, 0) };
        var coordinates = new[] { new Point(0, 1), new Point(1, 1), new Point(1, 0), new Point(0, 0) };
        var part = new ModelPreviewPart(positions, [0, 1, 2, 0, 2, 3], coordinates, "sample");
        var placeholder = new ModelPreviewData([part], 2, 2);
        var options = new PhotoImageOptions(128, "PNG");
        var background = Color.FromRgb(0x62, 0x5f, 0x68);
        static BitmapSource Texture(byte alpha)
        {
            var image = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 0, 255, alpha }, 4);
            image.Freeze(); return image;
        }
        var image = Texture(0);
        var materialTextures = new Dictionary<string, BitmapSource> { ["sample"] = image };
        var opaque = placeholder with { Parts = [part with { UsesTextureAlpha = false }] };
        var transparentFile = Path.Combine(output, "transparent.png");
        var opaqueFile = Path.Combine(output, "opaque.png");
        PreviewPhotoRenderer.Save(transparentFile, placeholder, materialTextures, null, "Fixture", background, options);
        PreviewPhotoRenderer.Save(opaqueFile, opaque, materialTextures, null, "Fixture", background, options);
        if (File.ReadAllBytes(transparentFile).SequenceEqual(File.ReadAllBytes(opaqueFile)))
            throw new Exception("Opaque material incorrectly treated its mask as transparency.");
        var pixel = new byte[4]; image.CopyPixels(pixel, 4, 0);
        if (pixel[3] != 0) throw new Exception("Photo rendering modified the source texture.");
        var pedPart = part with { TextureName = "__ped_head_0" };
        var torsoPart = part with { TextureName = "__ped_uppr_0" };
        var torsoImage = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 255, 0, 0, 255 }, 4);
        torsoImage.Freeze();
        var ped = new PhotoPed([pedPart, torsoPart], new Dictionary<string, BitmapSource>
        {
            [pedPart.TextureName!] = Texture(255), [torsoPart.TextureName!] = torsoImage
        }, []);
        var expected = Path.Combine(output, "empty-head.png");
        PreviewPhotoRenderer.SaveEmptySlot(expected, ped, "Chapéu [p_head]", background, options);
        foreach (var slot in new[] { "p_head", "p_eyes", "p_ears", "p_mouth", "berd", "hair" })
        {
            var category = "Fixture [" + slot + "]";
            if (!PreviewPhotoRenderer.IsEmptySlot(placeholder, category)) throw new Exception("Placeholder not detected for " + slot);
            var path = Path.Combine(output, "empty-" + slot + ".png");
            PreviewPhotoRenderer.Save(path, placeholder, materialTextures, ped, category, background, options);
            if (!File.ReadAllBytes(expected).SequenceEqual(File.ReadAllBytes(path)))
                throw new Exception("Empty accessory did not show the clean head for " + slot);
        }
        if (PreviewPhotoRenderer.IsEmptySlot(placeholder, "Decal [decl]")) throw new Exception("A flat logo was classified as an empty clothing slot.");
        if (!PreviewPhotoRenderer.IsEmptySlot(placeholder, "Mochila [hand]")) throw new Exception("Empty bag placeholder was not detected.");
        var torsoFile = Path.Combine(output, "empty-torso.png");
        PreviewPhotoRenderer.SaveEmptySlot(torsoFile, ped, "Colete [task]", background, options);
        if (File.ReadAllBytes(torsoFile).SequenceEqual(File.ReadAllBytes(expected)))
            throw new Exception("An empty torso slot showed the head instead of the body.");
        var original = positions.ToArray();
        var actualHat = opaque with { SourceTriangles = 4, PreviewTriangles = 4 };
        PreviewPhotoRenderer.Save(Path.Combine(output, "hat.png"), actualHat, materialTextures, null,
            "Chapéu [p_head]", background, options, Matrix3D.Identity);
        if (!positions.SequenceEqual(original)) throw new Exception("Hat alignment modified the source geometry.");
        var backPositions = new[] { new Point3D(-1, -1, -1), new Point3D(1, -1, -1), new Point3D(0, 1, -1),
            new Point3D(-1, -1, 1), new Point3D(1, -1, 1), new Point3D(0, 1, 1) };
        var details = new byte[8 * 4];
        for (var index = 3; index < details.Length; index += 4) details[index] = 255;
        details[^2] = 255;
        var detailImage = BitmapSource.Create(8, 1, 96, 96, PixelFormats.Bgra32, null, details, 32); detailImage.Freeze();
        var backCoordinates = Enumerable.Repeat(new Point(0.95, 0.5), 3).Concat(Enumerable.Repeat(new Point(0.1, 0.5), 3)).ToArray();
        var detailPart = new ModelPreviewPart(backPositions, [0, 2, 1, 3, 4, 5], backCoordinates, "detail", false);
        var detailedHat = new ModelPreviewData([detailPart], 2, 2);
        var detailTextures = new Dictionary<string, BitmapSource> { ["detail"] = detailImage };
        if (!PhotoView.ShowBack(detailedHat, detailTextures, "p_head")) throw new Exception("Hat back detail was not selected.");
        var frontCoordinates = backCoordinates.Reverse().ToArray();
        if (PhotoView.ShowBack(detailedHat with { Parts = [detailPart with { TextureCoordinates = frontCoordinates }] }, detailTextures, "p_head"))
            throw new Exception("Hat front detail incorrectly selected the back view.");
        if (!PhotoView.ShowBack(detailedHat, detailTextures, "hand")) throw new Exception("Backpack did not select the rear view.");
        if (PhotoView.ShowBack(detailedHat, new Dictionary<string, BitmapSource> { ["detail"] = Texture(255) }, "p_head"))
            throw new Exception("A plain hat was turned around without a distinctive rear detail.");
        var template = BitmapSource.Create(64, 64, 96, 96, PixelFormats.Bgra32, null, new byte[64 * 64 * 4], 256);
        if (!PhotoView.IsDecorationTemplate(template) || PhotoView.IsDecorationTemplate(Texture(255)))
            throw new Exception("Dynamic decoration template was confused with real artwork.");
        Console.WriteLine("PASS: opaque masks, source alpha preservation, clean head for empty accessories and non-mutating hat alignment.");
    }
}
