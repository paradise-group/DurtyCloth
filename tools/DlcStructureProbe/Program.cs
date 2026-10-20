using System.IO;
using System.Xml.Linq;
using FooxClothStudio;

var source = Path.GetFullPath(args[0]);
var project = DctProjectImporter.Import(source);
if (project.DlcName != "empregos" || project.SourceAlternateVariationsPath is null) throw new Exception("Expected reference project and real alternate metadata.");
var output = args.Length > 2 && args[2] == "--verify-existing" ? Path.GetFullPath(args[1]) : AddonExporter.Export(project, Path.GetFullPath, Path.GetFullPath(args[1]));
if (Path.GetFileName(output) != "empregos") throw new Exception("Unexpected resource prefix.");
foreach (var gender in new[] { "m", "f" })
{
    var shopPath = Path.Combine(output, $"mp_{gender}_freemode_01_mp_{gender}_empregos_shop.meta");
    var shop = XDocument.Load(shopPath);
    if (shop.Root!.Element("dlcName")!.Value != $"mp_{gender}_empregos") throw new Exception("Wrong DLC name in shop.");
}
if (Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories).Any(path => Path.GetFileName(path).Contains("foox_"))) throw new Exception("Brand prefix leaked into output names.");
var alternate = Path.Combine(output, "pedalternatevariations.meta");
var doc = XDocument.Load(alternate); var reference = XDocument.Load(project.SourceAlternateVariationsPath);
var rules = doc.Descendants("switches").SelectMany(s => s.Elements("Item")).Count();
if (rules == 0 || rules != reference.Descendants("switches").SelectMany(s => s.Elements("Item")).Count()) throw new Exception("Alternate rules lost.");
var manifest = File.ReadAllText(Path.Combine(output, "fxmanifest.lua"));
if (!manifest.Contains("data_file 'ALTERNATE_VARIATIONS_FILE' 'pedalternatevariations.meta'")) throw new Exception("Alternate metadata not registered.");
if (doc.Descendants("dlcNameHash").Any(e => e.Value.Contains("foox_"))) throw new Exception("Wrong collection in alternate metadata.");
Console.WriteLine($"OK: {project.Items.Count} drawables; original DLC names, both shop files, {rules} alternate rules, manifest registration. {output}");
