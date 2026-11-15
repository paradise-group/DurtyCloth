using CodeWalker.GameFiles;
using System.Text.Json;
using System.Xml.Linq;

if (args.Length == 3 && args[0] == "--facial-info")
{
    var folder = args[1];
    GTA5Keys.LoadFromPath(folder);
    Directory.CreateDirectory(args[2]);
    foreach (var archivePath in new[] { Path.Combine(folder, "common.rpf"), Path.Combine(folder, "update/update.rpf") })
    {
        var rpf = new RpfFile(archivePath, Path.GetFileName(archivePath));
        rpf.ScanStructure(null, null);
        foreach (var entry in (rpf.AllEntries ?? []).OfType<RpfFileEntry>().Where(entry => entry.Name == "facial_overlays.meta"))
            File.WriteAllBytes(Path.Combine(args[2], Path.GetFileName(archivePath) + ".facial_overlays.xml"), entry.File.ExtractFile(entry));
    }
    return;
}

if (args.Length == 3 && args[0] == "--server-binary")
{
    using var input = JsonDocument.Parse(File.ReadAllText(args[1]));
    var binaryResults = new List<object>();
    foreach (var file in input.RootElement.GetProperty("BinaryFiles").EnumerateArray())
    {
        var path = file.GetString()!;
        try
        {
            var bytes = File.ReadAllBytes(path);
            string? xml;
            string format;
            if (bytes.AsSpan(0, Math.Min(4, bytes.Length)).SequenceEqual("RBF0"u8))
            {
                var rbf = new RbfFile();
                rbf.Load(bytes);
                xml = RbfXml.GetXml(rbf);
                format = "RBF";
            }
            else
            {
                var ymt = new YmtFile();
                ymt.Load(bytes);
                xml = ymt.Meta is not null ? MetaXml.GetXml(ymt.Meta) : ymt.Pso is not null ? PsoXml.GetXml(ymt.Pso) : null;
                format = ymt.FileFormat.ToString();
            }
            var document = xml is null ? null : XDocument.Parse(xml);
            binaryResults.Add(new { File = path, Format = format, Root = document?.Root?.Name.LocalName,
                DecorationEntries = document?.Descendants().Count(node => node.Elements().Any(child => child.Name.LocalName == "txdHash")) ?? 0,
                Error = xml is null ? "Unsupported metadata format" : null });
        }
        catch (Exception error) { binaryResults.Add(new { File = path, Error = error.Message }); }
    }
    Directory.CreateDirectory(args[2]);
    File.WriteAllText(Path.Combine(args[2], "server-binary-audit.json"), JsonSerializer.Serialize(binaryResults, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"Inspected {binaryResults.Count} server binary metadata files.");
    return;
}

if (args.Length == 1 && args[0] == "--inspect")
{
    foreach (var type in typeof(RpfFile).Assembly.GetTypes().Where(t => t.Name is "RbfFile" or "RbfXml"))
    {
        Console.WriteLine(type.FullName);
        foreach (var method in type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
            .Where(m => m.Name.Contains("Xml") || m.Name.Contains("Load") || m.Name.Contains("File"))) Console.WriteLine(method);
    }
    return;
}

if (args.Length is not (1 or 3))
    throw new ArgumentException("Usage: TattooMetadataProbe <GTA folder> [<project.fcsproj> <report folder>]");
var game = args[0];
GTA5Keys.LoadFromPath(game);
IEnumerable<RpfFile> All(RpfFile archive) => new[] { archive }.Concat((archive.Children ?? []).SelectMany(All));
var archives = Directory.EnumerateFiles(game, "*.rpf", SearchOption.AllDirectories)
    .OrderBy(path => Path.GetFileName(path).Equals("update.rpf", StringComparison.OrdinalIgnoreCase) ? 1 :
        Path.GetFileName(path).Equals("update2.rpf", StringComparison.OrdinalIgnoreCase) ? 2 : 0)
    .ThenBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
var collections = new Dictionary<string, (string Source, XDocument Document)>(StringComparer.OrdinalIgnoreCase);
var errors = new List<string>();
var count = 0;
foreach (var path in archives)
{
    var archive = new RpfFile(path, Path.GetFileName(path));
    archive.ScanStructure(null, null);
    foreach (var file in All(archive).SelectMany(a => a.AllEntries ?? []).OfType<RpfFileEntry>()
        .Where(file => file.Name.EndsWith("_overlays.xml", StringComparison.OrdinalIgnoreCase)))
    {
        try
        {
            var bytes = file.File.ExtractFile(file);
            using var input = new MemoryStream(bytes);
            var document = XDocument.Load(input);
            if (document.Root?.Name.LocalName != "PedDecorationCollection") continue;
            collections[file.Name] = (path + " | " + file.Path, document);
            count++;
        }
        catch (Exception ex) { errors.Add(path + " | " + file.Path + " | " + ex.Message); }
    }
}
Console.WriteLine($"Read {count} overlay files; {collections.Count} effective collections; {errors.Count} errors.");
var entries = collections.SelectMany(pair => pair.Value.Document.Descendants("presets").Elements("Item")
    .Where(entry => entry.Element("type")?.Value == "TYPE_TATTOO")
    .Select(entry => new { Collection = Path.GetFileNameWithoutExtension(pair.Key), pair.Value.Source, Entry = entry })).ToArray();
if (args.Length < 3)
{
    foreach (var entry in entries.Take(3)) Console.WriteLine(entry.Entry);
    foreach (var error in errors) Console.WriteLine(error);
    return;
}
using var project = JsonDocument.Parse(File.ReadAllText(args[1]));
var results = project.RootElement.GetProperty("Tattoos").EnumerateArray().Select(tattoo =>
{
    var imagePath = tattoo.GetProperty("ImagePath").GetString()!;
    var dictionary = Path.GetFileNameWithoutExtension(imagePath);
    var texture = tattoo.GetProperty("SourceTextureName").GetString()!;
    var matches = entries.Where(entry => string.Equals(entry.Entry.Element("txdHash")?.Value, dictionary, StringComparison.OrdinalIgnoreCase)
        && string.Equals(entry.Entry.Element("txtHash")?.Value, texture, StringComparison.OrdinalIgnoreCase))
        .Select(entry => new {
            entry.Collection, entry.Source,
            Overlay = entry.Entry.Element("nameHash")?.Value,
            Zone = entry.Entry.Element("zone")?.Value,
            Gender = entry.Entry.Element("gender")?.Value,
            U = entry.Entry.Element("uvPos")?.Attribute("x")?.Value,
            V = entry.Entry.Element("uvPos")?.Attribute("y")?.Value,
            ScaleX = entry.Entry.Element("scale")?.Attribute("x")?.Value,
            ScaleY = entry.Entry.Element("scale")?.Attribute("y")?.Value,
            Rotation = entry.Entry.Element("rotation")?.Attribute("value")?.Value
        }).ToArray();
    var candidates = matches.Length > 0 ? [] : entries.Where(entry =>
        string.Equals(entry.Entry.Element("txdHash")?.Value, dictionary, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(entry.Entry.Element("txtHash")?.Value, texture, StringComparison.OrdinalIgnoreCase))
        .Select(entry => new { entry.Collection, Dictionary = entry.Entry.Element("txdHash")?.Value,
            Texture = entry.Entry.Element("txtHash")?.Value, Zone = entry.Entry.Element("zone")?.Value }).ToArray();
    return new { Id = tattoo.GetProperty("Id").GetString(), Name = tattoo.GetProperty("Name").GetString(), Dictionary = dictionary,
        Texture = texture, SavedZone = tattoo.GetProperty("Zone").GetString(), Matches = matches, Candidates = candidates };
}).ToArray();
Directory.CreateDirectory(args[2]);
File.WriteAllText(Path.Combine(args[2], "tattoo-zone-audit.json"), JsonSerializer.Serialize(new {
    Project = args[1], Game = game, PhysicalArchivesScanned = archives.Length, OverlayFiles = count, EffectiveCollections = collections.Count,
    Total = results.Length, Matched = results.Count(result => result.Matches.Length > 0),
    Unmatched = results.Count(result => result.Matches.Length == 0),
    ConflictingZones = results.Count(result => result.Matches.Select(match => match.Zone).Distinct().Count() > 1),
    Errors = errors, Results = results
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Tattoos: {results.Length}; matched: {results.Count(result => result.Matches.Length > 0)}; unmatched: {results.Count(result => result.Matches.Length == 0)}.");
foreach (var group in results.SelectMany(result => result.Matches).GroupBy(match => match.Zone)) Console.WriteLine(group.Key + ": " + group.Count() + " presets");
foreach (var result in results.Where(result => result.Matches.Length == 0).Take(12)) Console.WriteLine("Unmatched: " + result.Texture);
