using System.IO;
using System.Text;
using System.Xml.Linq;
using System.Xml;
using System.Security.Cryptography;
using CodeWalker.GameFiles;

namespace FooxClothStudio;

public static class PlatformExporter
{
    public static string Export(ClothProject project, Func<string, string> resolve, string parent, string target)
    {
        if (target == "FiveM Resource") return AddonExporter.Export(project, resolve, parent);
        parent = Path.GetFullPath(parent);
        Directory.CreateDirectory(parent);
        var stage = Path.Combine(parent, ".foox-build-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        string? output = null;
        try
        {
            var source = AddonExporter.Export(project, resolve, stage);
            var name = Path.GetFileName(source);
            var suffix = target switch { "alt:V Resource" => "altv", "RageMP Resource" => "ragemp", "Single player RPF" => "singleplayer", _ => throw new InvalidDataException("Unknown build target.") };
            output = Path.Combine(parent, name + "_" + suffix);
            if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Build output already exists: " + output);
            // Finish all payloads in a private staging directory before moving the completed result.
            var completed = Path.Combine(stage, "completed"); Directory.CreateDirectory(completed);
            if (target == "alt:V Resource")
            {
                CopyTree(Path.Combine(source, "stream"), Path.Combine(completed, "stream"));
                var stream = new StringBuilder("files = [\n");
                foreach (var file in Directory.EnumerateFiles(Path.Combine(completed, "stream"), "*", SearchOption.AllDirectories))
                    stream.AppendLine("  \"" + Path.GetRelativePath(completed, file).Replace('\\', '/') + "\",");
                foreach (var file in MetadataFiles(source))
                    stream.AppendLine("  \"" + Path.GetFileName(file) + "\",");
                stream.AppendLine("]\n[meta]");
                foreach (var file in MetadataFiles(source))
                {
                    var filename = Path.GetFileName(file); File.Copy(file, Path.Combine(completed, filename));
                    stream.AppendLine("\"" + filename + "\" = \"" + FirstPersonMetadata.TypeOf(filename) + "\"");
                }
                File.WriteAllText(Path.Combine(completed, "stream.toml"), stream.ToString(), new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(completed, "resource.toml"), "type = 'dlc'\nmain = 'stream.toml'\n", new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(completed, "INSTALL.txt"), "Copy this folder to alt:V resources and add its folder name to server.toml resources.\n");
            }
            else
            {
                var pack = target == "RageMP Resource"
                    ? Path.Combine(completed, "client_packages", "game_resources", "dlcpacks", name) : Path.Combine(completed, name);
                Directory.CreateDirectory(pack);
                BuildRpf(source, Path.Combine(pack, "dlc.rpf"), name, stage, project.SingleplayerSetupOrder);
                File.WriteAllText(Path.Combine(completed, "INSTALL.txt"), target == "RageMP Resource"
                    ? "Merge client_packages/game_resources/dlcpacks into your server's matching directory.\n"
                    : "Copy the DLC folder into mods/update/x64/dlcpacks. Add <Item>dlcpacks:/" + name + "/</Item> to mods/update/update.rpf/common/data/dlclist.xml.\n");
            }
            foreach (var file in Directory.EnumerateFiles(source, "*.json")) File.Copy(file, Path.Combine(completed, Path.GetFileName(file)));
            if (Directory.Exists(Path.Combine(source, "configs"))) CopyTree(Path.Combine(source, "configs"), Path.Combine(completed, "configs"));
            Directory.Move(completed, output);
            return output;
        }
        finally
        {
            var prefix = parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(stage).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid staging path.");
            if (Directory.Exists(stage)) Directory.Delete(stage, true);
        }
    }
    private static void CopyTree(string source, string target)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var output = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(output)!); File.Copy(file, output);
            if (!SHA256.HashData(File.ReadAllBytes(file)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(output))))
                throw new IOException("Payload copy verification failed.");
        }
    }
    private static IEnumerable<string> MetadataFiles(string source) => Directory.EnumerateFiles(source, "*.meta").Concat(Directory.EnumerateFiles(source, "*_overlays.xml"));
    private static void BuildRpf(string source, string output, string name, string stage, int setupOrder)
    {
        var innerPath = Path.Combine(stage, "clothes.rpf");
        var inner = RpfFile.CreateNew(stage, "clothes.rpf");
        var files = Directory.EnumerateFiles(Path.Combine(source, "stream"), "*", SearchOption.AllDirectories).ToArray();
        var duplicate = files.GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null) throw new InvalidDataException("Duplicate RPF entry: " + duplicate.Key);
        foreach (var file in files) RpfFile.CreateFile(inner.Root, Path.GetFileName(file), File.ReadAllBytes(file), false);
        var check = new RpfFile(innerPath, "clothes.rpf");
        var errors = new List<string>(); check.ScanStructure(_ => { }, errors.Add);
        if (errors.Count > 0 || check.Root.Files.Count != files.Length) throw new InvalidDataException("RPF payload did not reopen: " + string.Join("; ", errors));
        foreach (var entry in check.Root.Files)
        {
            var sourceFile = files.Single(file => Path.GetFileName(file).Equals(entry.Name, StringComparison.OrdinalIgnoreCase));
            var expected = File.ReadAllBytes(sourceFile);
            if (entry is RpfResourceFileEntry) expected = ResourceBuilder.Decompress(expected[16..]);
            if (!SHA256.HashData(expected).SequenceEqual(SHA256.HashData(check.ExtractFile(entry))))
                throw new InvalidDataException("RPF payload changed: " + entry.Name);
        }
        var archive = RpfFile.CreateNew(Path.GetDirectoryName(output)!, Path.GetFileName(output));
        var x64 = RpfFile.CreateDirectory(archive.Root, "x64");
        var models = RpfFile.CreateDirectory(x64, "models");
        var cdimages = RpfFile.CreateDirectory(models, "cdimages");
        RpfFile.CreateFile(cdimages, name + ".rpf", File.ReadAllBytes(innerPath), false);
        var device = "dlc_" + name;
        XElement Entry(string path, string type) => new("Item", new XElement("filename", device + ":/" + path),
            new XElement("fileType", type), new XElement("overlay", new XAttribute("value", "true")),
            new XElement("disabled", new XAttribute("value", "true")), new XElement("persistent", new XAttribute("value", "true")));
        var metadata = MetadataFiles(source).ToArray();
        var data = new List<XElement> { Entry("x64/models/cdimages/" + name + ".rpf", "RPF_FILE") };
        foreach (var meta in metadata)
        {
            RpfFile.CreateFile(archive.Root, Path.GetFileName(meta), File.ReadAllBytes(meta), false);
            data.Add(Entry(Path.GetFileName(meta), FirstPersonMetadata.TypeOf(Path.GetFileName(meta))));
        }
        var content = new XElement("CDataFileMgr__ContentsOfDataFileXml", new XElement("disabledFiles"), new XElement("includedXmlFiles"),
            new XElement("includedDataFiles"), new XElement("dataFiles", data), new XElement("contentChangeSets", new XElement("Item",
                new XElement("changeSetName", name + "_AUTOGEN"), new XElement("filesToDisable"), new XElement("filesToEnable",
                    data.Select(item => new XElement("Item", item.Element("filename")!.Value))), new XElement("txdToLoad"), new XElement("txdToUnload"),
                new XElement("residentResources"), new XElement("unregisterResources"), new XElement("requiresLoadingScreen", new XAttribute("value", "false")))));
        var setup = new XElement("SSetupData", new XElement("deviceName", device), new XElement("datFile", "content.xml"),
            new XElement("timeStamp", "01/01/2026 00:00:00"), new XElement("nameHash", name),
            new XElement("contentChangeSetGroups", new XElement("Item", new XElement("NameHash", name + "_GROUP"),
                new XElement("ContentChangeSets", new XElement("Item", name + "_AUTOGEN")))), new XElement("type", "EXTRACONTENT_COMPAT_PACK"),
            new XElement("order", new XAttribute("value", setupOrder)), new XElement("minorOrder", new XAttribute("value", "0")), new XElement("isLevelPack", new XAttribute("value", "false")),
            new XElement("subPackCount", new XAttribute("value", "0")), new XElement("dependencyPackHash"), new XElement("requiredVersion"));
        var setupDocument = new XmlDocument(); setupDocument.LoadXml(setup.ToString());
        var setupCheck = new DlcSetupFile(); setupCheck.Load(setupDocument);
        var contentDocument = new XmlDocument(); contentDocument.LoadXml(content.ToString());
        var contentCheck = new DlcContentFile(); contentCheck.Load(contentDocument);
        if (setupCheck.type != "EXTRACONTENT_COMPAT_PACK" || setupCheck.contentChangeSetGroups.Count != 1 ||
            contentCheck.dataFiles.Count != data.Count || contentCheck.contentChangeSets.Count != 1)
            throw new InvalidDataException("DLC setup/content metadata did not pass format parsing.");
        RpfFile.CreateFile(archive.Root, "content.xml", Encoding.UTF8.GetBytes(content.ToString()), false);
        RpfFile.CreateFile(archive.Root, "setup2.xml", Encoding.UTF8.GetBytes(setup.ToString()), false);
        var final = new RpfFile(output, "dlc.rpf"); errors.Clear(); final.ScanStructure(_ => { }, errors.Add);
        if (errors.Count > 0 || final.Children is null || final.Children.Count != 1)
            throw new InvalidDataException("DLC RPF did not reopen: " + string.Join("; ", errors));
    }
}
