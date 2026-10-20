using FooxClothStudio;
using System.IO;
using System.Text.Json;

var resource = Path.GetFullPath(args[0]);
var errors = FiveMResourceValidator.Validate(resource);
if (errors.Count > 0)
    throw new Exception(errors.Count + " validation error(s): " + string.Join(" | ", errors.Take(10)));
Console.WriteLine("OK: resource metadata, YDD/YTD files and variant counts validated: " + resource);
if (args.Length > 2)
{
    var fixture = Path.GetFullPath(args[1]);
    var parent = Path.GetFullPath(args[2]);
    var project = JsonSerializer.Deserialize<ClothProject>(File.ReadAllText(fixture))!;
    var root = Path.GetDirectoryName(fixture)!;
    var output = AddonExporter.Export(project, path => Path.GetFullPath(Path.Combine(root, path)), parent);
    var ytd = Directory.EnumerateFiles(Path.Combine(output, "stream"), "*.ytd",
        SearchOption.AllDirectories).Single();
    File.Delete(ytd);
    var missing = FiveMResourceValidator.Validate(output);
    if (!missing.Any(error => error.StartsWith("YTD ausente:")))
        throw new Exception("Validator did not detect the removed YTD: " + string.Join(" | ", missing));
    Console.WriteLine("OK: removed YTD was detected in isolated generated fixture.");
    if (args.Length > 3)
    {
        var physicsParent = Path.Combine(parent, "physics-check");
        Directory.CreateDirectory(physicsParent);
        var physicsProject = JsonSerializer.Deserialize<ClothProject>(File.ReadAllText(fixture))!;
        physicsProject.Items[0].PhysicsPath = Path.GetFullPath(args[3]);
        var physicsOutput = AddonExporter.Export(physicsProject,
            path => Path.GetFullPath(Path.Combine(root, path)), physicsParent);
        var yld = Directory.EnumerateFiles(Path.Combine(physicsOutput, "stream"), "*.yld",
            SearchOption.AllDirectories).Single();
        File.Delete(yld);
        var physicsErrors = FiveMResourceValidator.Validate(physicsOutput);
        if (!physicsErrors.Any(error => error.StartsWith("YLD ausente para drawable com ownsCloth=true:")))
            throw new Exception("Validator did not detect missing cloth YLD: " + string.Join(" | ", physicsErrors));
        Console.WriteLine("OK: missing YLD for ownsCloth=true was detected.");
    }
    if (args.Length > 4)
    {
        var invalidParent = Path.Combine(parent, "invalid-physics-check");
        Directory.CreateDirectory(invalidParent);
        var physicsProject = JsonSerializer.Deserialize<ClothProject>(File.ReadAllText(fixture))!;
        physicsProject.Items[0].PhysicsPath = Path.GetFullPath(args[4]);
        var physicsOutput = AddonExporter.Export(physicsProject,
            path => Path.GetFullPath(Path.Combine(root, path)), invalidParent);
        var yldPath = Directory.EnumerateFiles(Path.Combine(physicsOutput, "stream"), "*.yld",
            SearchOption.AllDirectories).Single();
        File.WriteAllBytes(yldPath, [1, 2, 3, 4]);
        var physicsErrors = FiveMResourceValidator.Validate(physicsOutput);
        if (!physicsErrors.Any(error => error.StartsWith("YLD inválido para drawable com ownsCloth=true:")))
            throw new Exception("Validator did not detect corrupt cloth YLD: " + string.Join(" | ", physicsErrors));
        Console.WriteLine("OK: corrupt YLD for ownsCloth=true was detected.");
    }
}
