using System.Security.Cryptography;
using FooxClothStudio;

var savedRoot = Path.GetFullPath(args[0]);
var assets = Directory.EnumerateFiles(Path.Combine(savedRoot, "assets"), "*", SearchOption.AllDirectories).ToArray();
var model = assets.Single(path => path.EndsWith("teef_000_u.ydd", StringComparison.OrdinalIgnoreCase));
var textureA = assets.Single(path => path.EndsWith("teef_diff_000_a_uni.ytd", StringComparison.OrdinalIgnoreCase));
var textureB = assets.Single(path => path.EndsWith("teef_diff_000_b_uni.ytd", StringComparison.OrdinalIgnoreCase));
string Original(string path) => System.Text.RegularExpressions.Regex.Replace(
    Path.GetFileName(path), @"^texture_\d{2}_", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
var item = new ClothItem
{
    Name = "Native export smoke",
    ModelPath = model,
    OriginalModelFileName = Path.GetFileName(model),
    TexturePaths = [textureB, textureA],
    OriginalTextureFileNames = [Original(textureB), Original(textureA)]
};
var output = NativeItemExporter.Export(item, path => path, args[1]);
if (output.FileCount != 3) throw new Exception("Wrong exported file count.");
var resultA = Path.Combine(output.Folder, Original(textureA));
var resultB = Path.Combine(output.Folder, Original(textureB));
bool Same(string left, string right) => SHA256.HashData(File.ReadAllBytes(left))
    .SequenceEqual(SHA256.HashData(File.ReadAllBytes(right)));
if (!Same(resultA, textureB) || !Same(resultB, textureA) ||
    !Same(Path.Combine(output.Folder, Path.GetFileName(model)), model))
    throw new Exception("Native export lost the edited variant order or changed file bytes.");
Console.WriteLine($"OK: {output.FileCount} native files, YTD a/b names follow edited order, hashes match, folder={output.Folder}");
if (args.Length > 2)
{
    var alternateAssets = Directory.EnumerateFiles(Path.Combine(Path.GetFullPath(args[2]), "assets"),
        "*", SearchOption.AllDirectories).ToArray();
    var mainBerd = alternateAssets.First(path => path.EndsWith("berd_000_u.ydd", StringComparison.OrdinalIgnoreCase));
    var alternateBerd = alternateAssets.First(path => path.EndsWith("berd_000_u_1.ydd", StringComparison.OrdinalIgnoreCase));
    var berd = new ClothItem
    {
        Name = "Alternate export smoke",
        ModelPath = mainBerd,
        OriginalModelFileName = Path.GetFileName(mainBerd),
        AlternateModelPaths = [alternateBerd]
    };
    var alternateOutput = NativeItemExporter.Export(berd, path => path, args[1]);
    var exportedAlternate = Path.Combine(alternateOutput.Folder,
        Path.GetFileNameWithoutExtension(mainBerd) + "_1.ydd");
    if (alternateOutput.FileCount != 2 || !Same(exportedAlternate, alternateBerd))
        throw new Exception("Native export lost the alternate YDD.");
    Console.WriteLine($"OK alternate: original _u_1 name and hash match, folder={alternateOutput.Folder}");
}
