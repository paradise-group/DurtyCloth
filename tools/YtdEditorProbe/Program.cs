using System.IO;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodeWalker.GameFiles;
using CodeWalker.Utils;
using FooxClothStudio;

var source = Path.GetFullPath(args[0]);
var outputFolder = Path.GetFullPath(args[1]);
Directory.CreateDirectory(outputFolder);
if (args.Length > 2 && args[2] == "multi")
{
    var fixture = new YtdFile();
    fixture.Load(File.ReadAllBytes(source));
    var baseTexture = fixture.TextureDict?.Textures?.data_items?.FirstOrDefault()
        ?? throw new Exception("Fixture has no textures.");
    var originalLevels = baseTexture.Levels;
    baseTexture.Levels = 1;
    byte[] copyDds;
    try { copyDds = DDSIO.GetDDSFile(baseTexture); }
    finally { baseTexture.Levels = originalLevels; }
    var extra = DDSIO.GetTexture(copyDds);
    extra.Name = "foox_probe_extra";
    extra.NameHash = JenkHash.GenHash(extra.Name);
    fixture.TextureDict!.BuildFromTextureList([baseTexture, extra]);
    source = Path.Combine(outputFolder, "synthetic-multi.ytd");
    File.WriteAllBytes(source, fixture.Save());
}
var originalHash = SHA256.HashData(File.ReadAllBytes(source));
var ytd = new YtdFile();
ytd.Load(File.ReadAllBytes(source));
var textures = ytd.TextureDict?.Textures?.data_items ?? [];
if (textures.Length == 0) throw new Exception("Fixture has no textures.");
var first = textures[0];
var replacementSource = args.Length > 2 && args[2] != "multi"
    ? Path.GetFullPath(args[2]) : source;
var ddsPath = Path.Combine(outputFolder, "replacement.dds");
YtdTextureEditor.ExportDds(replacementSource, 0, ddsPath);
var roundTripDds = DDSIO.GetTexture(File.ReadAllBytes(ddsPath));
var replacementYtd = new YtdFile();
replacementYtd.Load(File.ReadAllBytes(replacementSource));
var reference = replacementYtd.TextureDict!.Textures.data_items[0];
if (roundTripDds is null || roundTripDds.Levels != reference.Levels ||
    roundTripDds.Format != reference.Format ||
    !SHA256.HashData(roundTripDds.Data?.FullData ?? [])
        .SequenceEqual(SHA256.HashData(reference.Data?.FullData ?? [])))
    throw new Exception("DDS export lost mipmaps or texture data.");
var output = Path.Combine(outputFolder, "edited.ytd");
var name = YtdTextureEditor.ReplaceFromDds(source, 0, ddsPath, output);
if (!string.Equals(name, first.Name, StringComparison.Ordinal) ||
    !originalHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(source))))
    throw new Exception("Texture name changed or source YTD was modified.");
var reopened = new YtdFile();
reopened.Load(File.ReadAllBytes(output));
if (reopened.TextureDict?.Textures?.data_items?.Length != textures.Length)
    throw new Exception("Edited YTD lost texture entries.");
var edited = reopened.TextureDict.Textures.data_items.Single(texture => texture.Name == name);
if (replacementSource != source &&
    SHA256.HashData(edited.Data?.FullData ?? []).SequenceEqual(
        SHA256.HashData(first.Data?.FullData ?? [])))
    throw new Exception("Replacement did not change texture bytes.");
try
{
    YtdTextureEditor.ReplaceFromDds(source, 0, ddsPath, output);
    throw new Exception("Expected existing output to be rejected.");
}
catch (IOException) { }
var addedPath = Path.Combine(outputFolder, "added.ytd");
YtdTextureEditor.AddFromDds(source, ddsPath, "foox_probe_new", addedPath);
var addedYtd = new YtdFile();
addedYtd.Load(File.ReadAllBytes(addedPath));
var addedTextures = addedYtd.TextureDict?.Textures?.data_items ?? [];
if (addedTextures.Length != textures.Length + 1 ||
    !addedTextures.Any(texture => texture.Name == "foox_probe_new"))
    throw new Exception("DDS addition did not add one named entry.");
try
{
    YtdTextureEditor.AddFromDds(source, ddsPath, first.Name, Path.Combine(outputFolder, "duplicate.ytd"));
    throw new Exception("Expected duplicate texture name to be rejected.");
}
catch (InvalidDataException) { }
var newIndex = Array.FindIndex(addedTextures, texture => texture.Name == "foox_probe_new");
var removedPath = Path.Combine(outputFolder, "removed.ytd");
YtdTextureEditor.Remove(addedPath, newIndex, removedPath);
var removedYtd = new YtdFile();
removedYtd.Load(File.ReadAllBytes(removedPath));
if (removedYtd.TextureDict?.Textures?.data_items?.Length != textures.Length ||
    removedYtd.TextureDict.Textures.data_items.Any(texture => texture.Name == "foox_probe_new"))
    throw new Exception("Removal did not restore the original entry count.");
if (textures.Length == 1)
{
    try
    {
        YtdTextureEditor.Remove(source, 0, Path.Combine(outputFolder, "empty.ytd"));
        throw new Exception("Expected last-texture removal to be rejected.");
    }
    catch (InvalidDataException) { }
}
var renamedPath = Path.Combine(outputFolder, "renamed.ytd");
YtdTextureEditor.Rename(source, 0, "foox_probe_renamed", renamedPath);
var renamedYtd = new YtdFile();
renamedYtd.Load(File.ReadAllBytes(renamedPath));
var renamedTextures = renamedYtd.TextureDict?.Textures?.data_items ?? [];
var renamed = renamedTextures.Single(texture => texture.Name == "foox_probe_renamed");
if (renamedTextures.Length != textures.Length ||
    renamed.NameHash != JenkHash.GenHash("foox_probe_renamed") ||
    !SHA256.HashData(renamed.Data?.FullData ?? [])
        .SequenceEqual(SHA256.HashData(first.Data?.FullData ?? [])))
    throw new Exception("Rename changed pixels, count or hash.");
if (textures.Length > 1)
{
    try
    {
        YtdTextureEditor.Rename(source, 0, textures[1].Name,
            Path.Combine(outputFolder, "duplicate-rename.ytd"));
        throw new Exception("Expected duplicate rename to be rejected.");
    }
    catch (InvalidDataException) { }
}
if (!originalHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(source))))
    throw new Exception("Source YTD changed after editing.");
var pngPath = Path.Combine(outputFolder, "probe.png");
var pngPixels = new byte[]
{
    0, 0, 255, 255, 0, 255, 0, 255, 255, 0, 0, 255, 255, 255, 255, 0,
    255, 255, 255, 255, 0, 0, 0, 0, 50, 100, 150, 128, 25, 50, 75, 255,
    0, 0, 255, 255, 0, 255, 0, 255, 255, 0, 0, 255, 255, 255, 255, 0,
    255, 255, 255, 255, 0, 0, 0, 0, 50, 100, 150, 128, 25, 50, 75, 255
};
var bitmap = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32, null, pngPixels, 16);
var encoder = new PngBitmapEncoder();
encoder.Frames.Add(BitmapFrame.Create(bitmap));
using (var pngStream = File.Create(pngPath)) encoder.Save(pngStream);
var pngTexture = PngTextureReader.Read(pngPath);
if (pngTexture.Width != 4 || pngTexture.Height != 4 || pngTexture.Levels != 3 ||
    pngTexture.Data?.FullData is not { Length: 84 } ||
    !pngTexture.Data.FullData.Take(64).SequenceEqual(pngPixels))
    throw new Exception("PNG pixels or mip chain changed during conversion.");
var pngReplaced = Path.Combine(outputFolder, "png-replaced.ytd");
YtdTextureEditor.ReplaceFromPng(source, 0, pngPath, pngReplaced);
var pngAdded = Path.Combine(outputFolder, "png-added.ytd");
YtdTextureEditor.AddFromPng(source, pngPath, "foox_png_probe", pngAdded);
var pngYtd = new YtdFile();
pngYtd.Load(File.ReadAllBytes(pngAdded));
var pngEntry = pngYtd.TextureDict?.Textures?.data_items?.Single(t => t.Name == "foox_png_probe");
if (pngEntry?.Levels != 3 || pngEntry.Data?.FullData.Length != 84 ||
    !originalHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(source))))
    throw new Exception("PNG addition was not preserved in YTD or source changed.");
var pngDdsPath = Path.Combine(outputFolder, "png-with-mips.dds");
YtdTextureEditor.ExportDds(pngAdded,
    Array.FindIndex(pngYtd.TextureDict!.Textures.data_items, t => t.Name == "foox_png_probe"),
    pngDdsPath);
var pngDds = DDSIO.GetTexture(File.ReadAllBytes(pngDdsPath));
if (pngDds?.Levels != 3 ||
    !SHA256.HashData(pngDds.Data?.FullData ?? [])
        .SequenceEqual(SHA256.HashData(pngEntry.Data.FullData)))
    throw new Exception("Three-mip PNG texture was not preserved in DDS export.");
Console.WriteLine($"OK: replace/add/remove/rename on {textures.Length}-texture YTD; pixels and source hash preserved, duplicate and empty YTD rejected. {outputFolder}");
