using System.IO;
using System.Text.Json;
using System.Security.Cryptography;
using System.Xml.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodeWalker.GameFiles;
using FooxClothStudio;

var output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
var image = Path.Combine(output, "tattoo-source.png");
var pixels = new byte[64 * 64 * 4];
for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = 40; pixels[i + 1] = 70; pixels[i + 2] = 180; pixels[i + 3] = (byte)((i / 4) % 256); }
var bitmap = BitmapSource.Create(64, 64, 96, 96, PixelFormats.Bgra32, null, pixels, 64 * 4);
var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
using (var file = File.Create(image)) encoder.Save(file);
var originalHash = SHA256.HashData(File.ReadAllBytes(image));
var project = new ClothProject { Name = "tattoo-probe", DlcName = "tattooprobe", CreateTattoosInfoDump = true, CreateIlleniumConfig = true,
    CreateEsxTattooConfig = true, CreateMenyooTattooConfig = true, SingleplayerSetupOrder = 17,
    Tattoos = [new TattooItem { Name = "sample", Label = "Foox 'tattoo'", ImagePath = image, Gender = "Both", U = 0.37f, V = 0.61f, ScaleX = 0.25f, ScaleY = 0.42f, Rotation = 33, Cost = 250 }] };
File.WriteAllText(Path.Combine(output, "project.fcsproj"), JsonSerializer.Serialize(project, new JsonSerializerOptions { WriteIndented = true }));
var fivem = PlatformExporter.Export(project, Path.GetFullPath, output, "FiveM Resource");
var importedTattoos = TattooImporter.Import(fivem);
if (importedTattoos.Count != 2 || importedTattoos.Any(x => x.Cost != 250 || x.U != 0.37f)) throw new Exception("Tattoo resource import lost genders, positioning or cost.");
var overlays = XDocument.Load(Directory.GetFiles(fivem, "*_overlays.xml").Single());
var entries = overlays.Descendants("presets").Elements("Item").ToArray();
if (entries.Length != 2 || entries[0].Element("uvPos")!.Attribute("x")!.Value != "0.37" || entries[0].Element("rotation")!.Attribute("value")!.Value != "33") throw new Exception("Tattoo positioning/genders were lost.");
var ytd = new YtdFile(); ytd.Load(File.ReadAllBytes(Directory.GetFiles(Path.Combine(fivem, "stream", "tattoos"), "*.ytd").Single()));
if (ytd.TextureDict.Textures.data_items.Single().Levels != 7) throw new Exception("Tattoo mipmaps were lost.");
if (!File.ReadAllText(Path.Combine(fivem, "configs", "illenium-appearance.lua")).Contains("hashFemale = 'foox_tattooprobe_sample_F'")) throw new Exception("Illenium female hash is missing.");
if (!File.ReadAllText(Path.Combine(fivem, "configs", "esx-tattooshop.lua")).Contains("addedX = 0.7")) throw new Exception("ESX camera configuration is missing.");
if (XDocument.Load(Path.Combine(fivem, "configs", "PedDecalOverlays.xml")).Descendants("OVERLAY").Count() != 2) throw new Exception("Menyoo gender records are missing.");
foreach (var target in new[] { "alt:V Resource", "Single player RPF", "RageMP Resource" })
{
    var destination = PlatformExporter.Export(project, Path.GetFullPath, output, target);
    if (target == "alt:V Resource")
    {
        if (!File.ReadAllText(Path.Combine(destination, "stream.toml")).Contains("PED_OVERLAY_FILE")) throw new Exception("altV overlay registration missing.");
    }
    else
    {
        var rpfPath = Directory.GetFiles(destination, "dlc.rpf", SearchOption.AllDirectories).Single();
        var rpf = new RpfFile(rpfPath, "dlc.rpf"); var errors = new List<string>(); rpf.ScanStructure(_ => { }, errors.Add);
        var setup = XDocument.Parse(System.Text.Encoding.UTF8.GetString(rpf.ExtractFile(rpf.Root.Files.Single(x => x.Name == "setup2.xml"))));
        if (setup.Root!.Element("order")!.Attribute("value")!.Value != "17" || !rpf.Root.Files.Any(x => x.Name.EndsWith("_overlays.xml"))) throw new Exception("RPF tattoo metadata or DLC load order missing.");
    }
    Console.WriteLine("OK tattoo target: " + target);
}
var bundle = Path.Combine(output, "tattoos.fcsbundle"); ProjectBundle.Export(project, Path.GetFullPath, bundle);
var unpacked = ProjectBundle.Import(bundle, Path.Combine(output, "unpacked"));
var restored = JsonSerializer.Deserialize<ClothProject>(File.ReadAllText(unpacked))!;
if (restored.Tattoos.Count != 1 || !File.Exists(Path.Combine(Path.GetDirectoryName(unpacked)!, restored.Tattoos[0].ImagePath))) throw new Exception("Tattoo package lost its image.");
var fixture = Path.GetFullPath(args[0]); var clothes = JsonSerializer.Deserialize<ClothProject>(File.ReadAllText(fixture))!;
var root = Path.GetDirectoryName(fixture)!;
foreach (var item in clothes.Items) { item.ModelPath = Path.GetFullPath(Path.Combine(root, item.ModelPath)); item.TexturePaths = item.TexturePaths.Select(p => Path.GetFullPath(Path.Combine(root, p))).ToList(); }
clothes.ReplaceDlcCollection = true; clothes.ReplacementResource = "replacementprobe";
clothes.ReplacementMaleCollection = "mp_m_lowrider_01"; clothes.ReplacementFemaleCollection = "mp_f_lowrider_01";
var female = JsonSerializer.Deserialize<ClothItem>(JsonSerializer.Serialize(clothes.Items.Single()))!; female.Id = Guid.NewGuid(); female.Gender = "Feminino"; clothes.Items.Add(female);
var replaced = AddonExporter.Export(clothes, Path.GetFullPath, output);
if (!File.Exists(Path.Combine(replaced, "stream", "mp_m_freemode_01_mp_m_lowrider_01.ymt")) || !File.Exists(Path.Combine(replaced, "stream", "mp_f_freemode_01_mp_f_lowrider_01.ymt"))) throw new Exception("DLC replacement collection identities missing.");
if (Directory.GetFiles(replaced, "*foox*", SearchOption.AllDirectories).Length != 0) throw new Exception("Replacement retained addon collection names.");
foreach (var target in new[] { "alt:V Resource", "Single player RPF", "RageMP Resource" }) PlatformExporter.Export(clothes, Path.GetFullPath, output, target);
var invalid = JsonSerializer.Deserialize<ClothProject>(JsonSerializer.Serialize(project))!; invalid.DlcName = "invalidtattoo"; invalid.Tattoos[0].U = float.NaN;
try { AddonExporter.Export(invalid, Path.GetFullPath, output); throw new Exception("Invalid UV coordinates accepted."); }
catch (InvalidDataException) { if (Directory.Exists(Path.Combine(output, "foox_invalidtattoo"))) throw new Exception("Failed export left output behind."); }
var tattooYtd = Directory.GetFiles(Path.Combine(fivem, "stream", "tattoos"), "*.ytd").Single(); var ytdBytes = File.ReadAllBytes(tattooYtd); File.Delete(tattooYtd);
if (!FiveMResourceValidator.Validate(fivem).Any(x => x.Contains("YTD de tatuagem ausente"))) throw new Exception("Missing tattoo image was accepted.");
File.WriteAllBytes(tattooYtd, ytdBytes);
var overlayPath = Directory.GetFiles(fivem, "*_overlays.xml").Single(); var overlayText = File.ReadAllText(overlayPath); File.Delete(overlayPath);
if (!FiveMResourceValidator.Validate(fivem).Any(x => x.Contains("Metadata de tatuagem declarado ausente"))) throw new Exception("Missing declared tattoo overlays were accepted.");
File.WriteAllText(overlayPath, overlayText);
if (FiveMResourceValidator.Validate(fivem).Count != 0) throw new Exception("Restored tattoo resource failed validation.");
if (!originalHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(image)))) throw new Exception("Original tattoo image was modified.");
Console.WriteLine("OK: tattoo-only project, PNG transparency/mipmaps, XML positioning, genders, 3 configs, four targets, bundle, DLC replacement identities and negative validation. " + output);
