using System.Text.Json;
using MessagePack;
using FooxClothStudio;

var root = Path.Combine(Path.GetTempPath(), "foox-dct-import-" + Guid.NewGuid().ToString("N"));
var projectRoot = Path.Combine(root, "clothproject_fixture");
var adjacentData = Path.Combine(projectRoot, "data");
var declaredData = Path.Combine(root, "build-output", "data");
Directory.CreateDirectory(adjacentData);
Directory.CreateDirectory(declaredData);
var modelName = "mp_m_freemode_01_fixture^teef_000_u.ydd";
var textureName = "mp_m_freemode_01_fixture^teef_diff_000_a_uni.ytd";
var adjacentModel = Path.Combine(adjacentData, modelName);
var declaredTexture = Path.Combine(declaredData, textureName);
File.WriteAllBytes(adjacentModel, [1]);
File.WriteAllBytes(Path.Combine(adjacentData, "unrelated.ytd"), [2]);
File.WriteAllBytes(declaredTexture, [3]);
var dctPath = Path.Combine(root, "fixture.dctproj");
var json = JsonSerializer.Serialize(new
{
    SaveVersion = 5,
    ProjectName = "fixture",
    DlcName = "fixture",
    TargetBuildOutputPath = Path.Combine(root, "build-output"),
    ClothData = new[]
    {
        new
        {
            IsDummy = false,
            MainPath = modelName,
            OriginalFileName = modelName,
            DrawableType = 7,
            TargetGender = 0,
            Position = 0,
            Name = "fixture",
            MeshPhysicsFilePath = (string?)null,
            Textures = new[]
            {
                new { IsDummy = false, FilePath = textureName, OriginalFileName = textureName, Position = 0 }
            }
        }
    }
});
var options = MessagePackSerializerOptions.Standard
    .WithCompression(MessagePackCompression.Lz4BlockArray);
File.WriteAllBytes(dctPath, MessagePackSerializer.ConvertFromJson(json, options));
try
{
    var project = DctProjectImporter.Import(dctPath);
    var item = project.Items.Single();
    if (item.ModelPath != adjacentModel || item.TexturePaths.Single() != declaredTexture)
        throw new Exception("Importer did not locate files independently across both data folders.");
    Console.WriteLine("OK: incomplete adjacent data folder fell back per file to TargetBuildOutputPath/data.");
}
finally { Directory.Delete(root, true); }
if (args.Length > 0)
{
    var real = DctProjectImporter.Import(Path.GetFullPath(args[0]));
    if (real.Items.Count == 0 || real.Items.SelectMany(item => item.OriginalTextureFileNames)
        .Any(name => !name.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase)))
        throw new Exception("Real DCT project import lost native texture filename extensions.");
    if (Path.GetFileNameWithoutExtension(args[0]) == "empregos" && real.SourceAlternateVariationsPath is null)
        throw new Exception("Real DCT project lost its alternate variations metadata.");
    Console.WriteLine($"OK real project: {real.Items.Count} drawables, {real.Items.Sum(item => item.TexturePaths.Count)} textures, DLC={real.DlcName}.");
}
