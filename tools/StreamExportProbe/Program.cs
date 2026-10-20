using System.Security.Cryptography;
using System.Text.Json;
using FooxClothStudio;

var fixture = Path.GetFullPath(args[0]);
var parent = Path.GetFullPath(args[1]);
Directory.CreateDirectory(parent);
var project = JsonSerializer.Deserialize<ClothProject>(File.ReadAllText(fixture))!;
var root = Path.GetDirectoryName(fixture)!;
string Resolve(string path) => Path.GetFullPath(Path.Combine(root, path));
var item = project.Items.Single();
var result = StreamExporter.Export(project, Resolve, parent);
if (result.FileCount != 2) throw new Exception("Expected one YDD and one YTD.");
var stream = Path.Combine(result.Folder, "stream");
var model = Path.Combine(stream, item.OriginalModelFileName!);
var texture = Path.Combine(stream, item.OriginalTextureFileNames.Single());
bool Same(string a, string b) => SHA256.HashData(File.ReadAllBytes(a))
    .SequenceEqual(SHA256.HashData(File.ReadAllBytes(b)));
if (!Same(model, Resolve(item.ModelPath)) || !Same(texture, Resolve(item.TexturePaths.Single())))
    throw new Exception("Exported bytes differ from saved project assets.");
if (Directory.GetFiles(stream).Any(path => Path.GetFileName(path).StartsWith("texture_")))
    throw new Exception("Internal asset prefix leaked into stream.");
if (!File.Exists(Path.Combine(result.Folder, "fxmanifest.lua")))
    throw new Exception("Missing manifest.");

var duplicate = new ClothItem
{
    ModelPath = item.TexturePaths.Single(),
    OriginalModelFileName = item.OriginalModelFileName,
    Name = "collision"
};
project.Items.Add(duplicate);
project.Name = "collision";
try
{
    StreamExporter.Export(project, Resolve, parent);
    throw new Exception("Expected output-name collision to fail.");
}
catch (InvalidDataException ex) when (ex.Message.Contains("mesmo nome")) { }
if (Directory.Exists(Path.Combine(parent, "foox_cloth_collision")))
    throw new Exception("Collision created a partial output folder.");
Console.WriteLine($"OK: {result.FileCount} native names and matching hashes; collision rejected before output. {result.Folder}");
