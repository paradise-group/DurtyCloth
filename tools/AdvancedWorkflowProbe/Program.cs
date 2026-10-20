using System.IO;
using System.Text.Json;
using System.Xml.Linq;
using FooxClothStudio;
using CodeWalker.GameFiles;

var fixture = Path.GetFullPath(args[0]); var root = Path.GetDirectoryName(fixture)!;
var project = JsonSerializer.Deserialize<ClothProject>(File.ReadAllText(fixture))!;
foreach (var item in project.Items) { item.ModelPath = Path.Combine(root, item.ModelPath); item.TexturePaths = item.TexturePaths.Select(p => Path.Combine(root, p)).ToList(); }
project.Name = "advanced"; project.DlcName = "advanced"; project.CreateDrawableSubfolders = true;
var item0 = project.Items[0]; item0.ExportPosition = 1;
project.Items.Insert(0, new ClothItem { IsDummy = true, Name = "Reservation", Gender = item0.Gender, Category = item0.Category, ExportPosition = 0 });
item0.TexturePaths.Insert(0, ""); item0.OriginalTextureFileNames.Insert(0, "(dummy)");
var nativeRoot = Path.GetFullPath(args[2]);
var hatPath = Directory.EnumerateFiles(nativeRoot, "*p_head_000.ydd", SearchOption.AllDirectories).First(p => p.Contains("mp_m_"));
var hat = ClothCatalog.Import(ClothCatalog.WithSiblingCompanions([hatPath])).Single();
hat.HairOption = "Remove hairs"; project.Items.Add(hat);
hat.FirstPersonMaskModel = "prop_mask_motobike_b"; hat.MaskOffsetY = -0.023f; hat.MaskOffsetZ = -0.045f;
hat.TextureEffects = hat.TexturePaths.Select(_ => new TextureEffect { TimeCycleModifier = "glasses_black", Intensity = 0.5f }).ToList();
item0.HasFirstPersonModel = true; item0.AlternateModelPaths.Add(item0.ModelPath);
var wristFixture = JsonSerializer.Deserialize<ClothItem>(JsonSerializer.Serialize(hat))!;
wristFixture.Id = Guid.NewGuid(); wristFixture.Category = ClothCatalog.Categories[16]; wristFixture.HairOption = "Show hairs";
project.Items.Add(wristFixture);
Directory.CreateDirectory(args[1]);
File.WriteAllText(Path.Combine(args[1], "project.fcsproj"), JsonSerializer.Serialize(project, new JsonSerializerOptions { WriteIndented = true }));
var output = PlatformExporter.Export(project, Path.GetFullPath, args[1], "FiveM Resource");
var ymtPath = Directory.EnumerateFiles(Path.Combine(output, "stream"), "*.ymt").First(p => !Path.GetFileName(p).StartsWith("mp_creaturemetadata_"));
var ymt = new YmtFile(); ymt.Load(File.ReadAllBytes(ymtPath)); var xml = XDocument.Parse(MetaXml.GetXml(ymt.Meta));
var props = xml.Descendants("aPropMetaData").Elements("Item").ToArray();
if (!props.Single(p => p.Element("anchorId")!.Attribute("value")!.Value == "0").Element("expressionMods")!.Value.Contains("-1")) throw new Exception("Hair expression modifier was lost.");
if (!props.Any(p => p.Element("anchorId")!.Attribute("value")!.Value == "6")) throw new Exception("Left wrist used an incorrect anchor.");
if (Directory.EnumerateFiles(output, "*teef_000_u.ydd", SearchOption.AllDirectories).Any() ||
    !Directory.EnumerateFiles(output, "*teef_001_u.ydd", SearchOption.AllDirectories).Any() ||
    Directory.EnumerateFiles(output, "*teef_diff_001_a_uni.ytd", SearchOption.AllDirectories).Any() ||
    !Directory.EnumerateFiles(output, "*teef_diff_001_b_uni.ytd", SearchOption.AllDirectories).Any())
    throw new Exception("Reservation positions shifted or wrote fake payloads.");
if (!Directory.EnumerateFiles(Path.Combine(output, "stream"), "mp_creaturemetadata_*.ymt").Any())
    throw new Exception("Required hair creature metadata was not generated.");
var firstPerson = XDocument.Load(Directory.GetFiles(output, "*_first_person.meta").Single());
if (firstPerson.Descendants("fpsPropModel").Any(e => e.Value != "prop_mask_motobike_b") ||
    firstPerson.Descendants("timeCycleMod").Any(e => e.Value != "glasses_black")) throw new Exception("First person mask or tint effects were lost.");
if (!Directory.GetFiles(output, "*_first_person_alternates.meta").Any()) throw new Exception("First person model mapping was missing.");
var bundle = Path.Combine(args[1], "reservations.fcsbundle"); ProjectBundle.Export(project, Path.GetFullPath, bundle);
var importedPath = ProjectBundle.Import(bundle, Path.Combine(args[1], "unpacked"));
var imported = JsonSerializer.Deserialize<ClothProject>(File.ReadAllText(importedPath))!;
if (!imported.Items[0].IsDummy || imported.Items[1].TexturePaths[0] != "") throw new Exception("Reservations were lost in project bundle roundtrip.");
foreach (var target in new[] { "alt:V Resource", "Single player RPF", "RageMP Resource" })
{
    var exported = PlatformExporter.Export(project, Path.GetFullPath, args[1], target);
    Console.WriteLine("OK export: " + target + " -> " + exported);
}
var hairPath = Directory.EnumerateFiles(nativeRoot, "*hair_000_u.ydd", SearchOption.AllDirectories).First();
var hair = ClothCatalog.Import(ClothCatalog.WithSiblingCompanions([hairPath])).Single();
var alternateModel = hair.AlternateModelPaths.Single();
// Two references deliberately exercise numbered rule output, not visual correctness of a new hoodie mesh.
hair.AlternateModelPaths.Add(alternateModel);
var hoodieProject = new ClothProject { Name = "hoodie-rule", DlcName = "hoodierule", Items = [hair] };
var hoodie = JsonSerializer.Deserialize<ClothItem>(JsonSerializer.Serialize(item0))!;
hoodie.Id = Guid.NewGuid(); hoodie.Gender = hair.Gender; hoodie.Category = ClothCatalog.Categories[11]; hoodie.HoodieHairs = true;
hoodieProject.Items.Add(hoodie);
var hoodieOutput = AddonExporter.Export(hoodieProject, Path.GetFullPath, args[1]);
var hairRules = XDocument.Load(Path.Combine(hoodieOutput, "pedalternatevariations.meta"));
if (hairRules.Descendants("alt").Single().Attribute("value")!.Value != "2" ||
    !Directory.EnumerateFiles(hoodieOutput, "*hair_000_u_2.ydd", SearchOption.AllDirectories).Any())
    throw new Exception("Hoodie alternate rule or referenced model was lost.");
Console.WriteLine("OK: drawable/texture reservations, negative hair modifier and creature metadata reopened; all four build targets generated.");
var creatureFile = Directory.GetFiles(Path.Combine(output, "stream"), "mp_creaturemetadata_*.ymt").Single();
var creatureBytes = File.ReadAllBytes(creatureFile);
File.Delete(creatureFile);
if (!FiveMResourceValidator.Validate(output).Any(e => e.Contains("Creature metadata ausente")))
    throw new Exception("Missing creature metadata was accepted.");
File.WriteAllBytes(creatureFile, [1, 2, 3]);
if (!FiveMResourceValidator.Validate(output).Any(e => e.Contains("Creature metadata inválido")))
    throw new Exception("Corrupt creature metadata was accepted.");
File.WriteAllBytes(creatureFile, creatureBytes);
var manifestPath = Path.Combine(output, "fxmanifest.lua");
var manifestText = File.ReadAllText(manifestPath);
File.WriteAllText(manifestPath, manifestText.Replace("PED_FIRST_PERSON_ASSET_DATA", "INCORRECT_TYPE"));
if (!FiveMResourceValidator.Validate(output).Any(e => e.Contains("Registro PED_FIRST_PERSON_ASSET_DATA ausente")))
    throw new Exception("Incorrect first person registration was accepted.");
File.WriteAllText(manifestPath, manifestText);
var firstPersonFile = Directory.GetFiles(output, "*_first_person.meta").Single();
var firstPersonText = File.ReadAllText(firstPersonFile); File.Delete(firstPersonFile);
if (!FiveMResourceValidator.Validate(output).Any(e => e.Contains("Metadata declarado ausente")))
    throw new Exception("Missing declared first person metadata was accepted.");
File.WriteAllText(firstPersonFile, firstPersonText);
if (FiveMResourceValidator.Validate(output).Count != 0) throw new Exception("Restored resource failed validation.");
Console.WriteLine("OK: missing/corrupt creature metadata and missing/incorrect first person declarations rejected; restored resource accepted.");
var optionsProject = JsonSerializer.Deserialize<ClothProject>(JsonSerializer.Serialize(project))!;
optionsProject.DlcName = "optioncheck";
optionsProject.RemoveDefaultCollectionPrefix = true; optionsProject.DisableExtraYmtFiles = true;
optionsProject.BuildTarget = "alt:V Resource";
var femaleHat = JsonSerializer.Deserialize<ClothItem>(JsonSerializer.Serialize(hat))!;
femaleHat.Id = Guid.NewGuid(); femaleHat.Gender = "Feminino"; optionsProject.Items.Add(femaleHat);
var optionsOutput = AddonExporter.Export(optionsProject, Path.GetFullPath, args[1]);
foreach (var shopFile in Directory.GetFiles(optionsOutput, "*_shop.meta"))
{
    var shopXml = XDocument.Load(shopFile);
    if (shopXml.Root!.Element("dlcName")!.Value != "optioncheck" || !string.IsNullOrEmpty(shopXml.Root.Element("creatureMetaData")!.Value))
        throw new Exception("Prefix or extra metadata option did not affect shop metadata.");
}
if (Directory.GetFiles(Path.Combine(optionsOutput, "stream"), "mp_creaturemetadata_*.ymt").Length != 0)
    throw new Exception("Extra creature YMT was generated despite disabling it.");
foreach (var collectionFile in Directory.GetFiles(Path.Combine(optionsOutput, "stream"), "*.ymt"))
{
    var collectionYmt = new YmtFile(); collectionYmt.Load(File.ReadAllBytes(collectionFile));
    var collectionXml = XDocument.Parse(MetaXml.GetXml(collectionYmt.Meta));
    if (collectionXml.Descendants().Where(x => x.Name.LocalName is "expressionMods" or "pedXml_expressionMods")
        .Any(x => x.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Any(value => float.Parse(value, System.Globalization.CultureInfo.InvariantCulture) != 0)))
        throw new Exception("Disabled creature metadata retained dependent deformation modifiers.");
}
if (hat.HairOption != "Remove hairs" || optionsProject.Items.Single(x => x.Id == hat.Id).HairOption != "Remove hairs")
    throw new Exception("Export changed authored hair options.");
optionsProject.DisableExtraYmtFiles = false; optionsProject.DlcName = "prefixboth";
var prefixedOutput = AddonExporter.Export(optionsProject, Path.GetFullPath, args[1]);
if (Directory.GetFiles(Path.Combine(prefixedOutput, "stream"), "mp_creaturemetadata_*.ymt").Length != 2)
    throw new Exception("Male/female creature metadata collided without collection prefixes.");
var restoredOptions = JsonSerializer.Deserialize<ClothProject>(JsonSerializer.Serialize(optionsProject))!;
if (!restoredOptions.RemoveDefaultCollectionPrefix || restoredOptions.BuildTarget != "alt:V Resource")
    throw new Exception("Build options did not survive save roundtrip.");
Console.WriteLine("OK: prefix removal, extra YMT suppression, male/female metadata isolation, unchanged source options and saved target.");
