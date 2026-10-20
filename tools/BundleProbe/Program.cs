using System.Security.Cryptography;
using System.Text.Json;
using FooxClothStudio;

var sourcePath = Path.GetFullPath(args[0]);
var parent = Path.GetFullPath(args[1]);
var source = JsonSerializer.Deserialize<ClothProject>(File.ReadAllText(sourcePath))!;
var root = Path.GetDirectoryName(sourcePath)!;
string Resolve(string path) => Path.IsPathRooted(path) ? path : Path.Combine(root, path);
var bundle = Path.Combine(parent, "smoke-" + Guid.NewGuid().ToString("N") + ".fcsbundle");
ProjectBundle.Export(source, Resolve, bundle);
var imported = ProjectBundle.Import(bundle, parent);
var target = JsonSerializer.Deserialize<ClothProject>(File.ReadAllText(imported))!;
if (target.Items.Count != source.Items.Count) throw new Exception("Item count differs.");
for (var index = 0; index < source.Items.Count; index++)
{
    var before = source.Items[index];
    var after = target.Items[index];
    if (!before.OriginalTextureFileNames.SequenceEqual(after.OriginalTextureFileNames))
        throw new Exception("Texture order differs.");
    if (!SHA256.HashData(File.ReadAllBytes(Resolve(before.ModelPath))).SequenceEqual(
        SHA256.HashData(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(imported)!, after.ModelPath)))))
        throw new Exception("Model bytes differ.");
    for (var texture = 0; texture < before.TexturePaths.Count; texture++)
        if (!SHA256.HashData(File.ReadAllBytes(Resolve(before.TexturePaths[texture]))).SequenceEqual(
            SHA256.HashData(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(imported)!, after.TexturePaths[texture])))))
            throw new Exception("Texture bytes differ.");
}
Console.WriteLine($"OK: {target.Items.Count} item(s), {target.Items.Sum(x => x.TexturePaths.Count)} texture(s), bundle={bundle}, imported={imported}");
