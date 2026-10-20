using FooxClothStudio;
using CodeWalker.GameFiles;
using System.Text.Json;
using System.IO;
using System.Xml.Linq;

var fixture = Path.GetFullPath(args[0]);
var project = JsonSerializer.Deserialize<ClothProject>(File.ReadAllText(fixture))!;
var root = Path.GetDirectoryName(fixture)!;
project.DlcName = "workflowcheck"; project.Author = "Foox 'Author'"; project.Version = "1.2";
project.Description = "Recorded workflow\ncheck"; project.TargetGameBuild = 3095;
project.CreateDrawableSubfolders = true; project.CreateClothesInfoDump = true;
project.UseTextureNameAsUniqueHash = true;
var item = project.Items.Single(); item.AudioPreset = "none"; item.BehaviourFlags = 128;
item.ShopCost = 123; item.ShopTextLabel = "FOOX_TEST_LABEL"; item.ShopLockHash = "FOOX_TEST_LOCK";
item.Creator = "Test creator"; item.CreatorWebsite = "local-reference";
var saved = JsonSerializer.Deserialize<ClothProject>(JsonSerializer.Serialize(project))!;
if (saved.DlcName != project.DlcName || saved.Items[0].ShopCost != 123 || saved.TargetGameBuild != 3095)
    throw new Exception("Workflow settings were lost in project roundtrip.");
var output = AddonExporter.Export(saved, path => Path.GetFullPath(Path.Combine(root, path)), args[1]);
var shop = XDocument.Load(Directory.GetFiles(output, "*_shop.meta").Single());
var entry = shop.Root!.Element("pedComponents")!.Element("Item")!;
if (entry.Element("cost")!.Attribute("value")!.Value != "123" ||
    entry.Element("textLabel")!.Value != "FOOX_TEST_LABEL" ||
    entry.Element("lockHash")!.Value != "FOOX_TEST_LOCK" ||
    entry.Element("uniqueNameHash")!.Value != Path.GetFileNameWithoutExtension(item.OriginalTextureFileNames.Single()))
    throw new Exception("Shop Meta options were not exported.");
var ymt = new YmtFile(); ymt.Load(File.ReadAllBytes(Directory.GetFiles(Path.Combine(output, "stream"), "*.ymt").Single()));
var xml = XDocument.Parse(MetaXml.GetXml(ymt.Meta));
if (xml.Root!.Element("compInfos")!.Element("Item")!.Element("flags")!.Attribute("value")!.Value != "128")
    throw new Exception("Behaviour Flags were lost in binary YMT.");
if (!Directory.Exists(Path.Combine(output, "stream", "mp_m_freemode_01_mp_m_workflowcheck", "teef")))
    throw new Exception("Drawable subfolder option did not change output layout.");
var manifest = File.ReadAllText(Path.Combine(output, "fxmanifest.lua"));
if (!manifest.Contains("author 'Foox \\'Author\\''") || !manifest.Contains("dependency '/gameBuild:3095'"))
    throw new Exception("Author/build settings were not safely written to manifest.");
if (!File.ReadAllText(Path.Combine(output, "clothes-info.json")).Contains("Test creator"))
    throw new Exception("Creator Info was missing from clothes dump.");
var texture = Path.GetFullPath(Path.Combine(root, item.TexturePaths.Single()));
var copy = Path.Combine(output, "duplicate-test.ytd"); File.Copy(texture, copy);
if (TextureContent.Fingerprint(texture) != TextureContent.Fingerprint(copy))
    throw new Exception("Duplicate texture content was not recognized.");
Console.WriteLine("OK: settings roundtrip, Shop Meta, binary YMT flags, author/build manifest, type subfolders, Creator Info dump and texture fingerprints. " + output);
