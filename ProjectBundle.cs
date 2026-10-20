using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace FooxClothStudio;

public static class ProjectBundle
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static void Export(ClothProject source, Func<string, string> resolvePath, string output)
    {
        if (File.Exists(output)) throw new IOException("O pacote já existe: " + output);
        var project = JsonSerializer.Deserialize<ClothProject>(JsonSerializer.Serialize(source))!;
        var assets = new List<(string Source, string Entry)>();
        string Add(string path, string entry)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            var absolute = resolvePath(path);
            if (!File.Exists(absolute)) throw new FileNotFoundException("Arquivo do projeto ausente", absolute);
            assets.Add((absolute, entry));
            return entry;
        }

        if (project.SourceAlternateVariationsPath is not null)
            project.SourceAlternateVariationsPath = Add(project.SourceAlternateVariationsPath,
                "assets/project/alternate" + Path.GetExtension(project.SourceAlternateVariationsPath));
        foreach (var item in project.Items)
        {
            var root = "assets/" + item.Id.ToString("N") + "/";
            item.ModelPath = item.IsDummy ? "" : Add(item.ModelPath, root + "model.ydd");
            item.AlternateModelPaths = item.AlternateModelPaths.Select((path, index) =>
                Add(path, root + $"model_{index + 1}.ydd")).ToList();
            item.TexturePaths = item.TexturePaths.Select((path, index) =>
                Add(path, root + $"texture_{index:D2}.ytd")).ToList();
            if (item.PhysicsPath is not null)
                item.PhysicsPath = Add(item.PhysicsPath, root + "physics.yld");
        }
        foreach (var tattoo in project.Tattoos)
            tattoo.ImagePath = Add(tattoo.ImagePath, "assets/" + tattoo.Id.ToString("N") + "/tattoo" + Path.GetExtension(tattoo.ImagePath));
        if (assets.Select(x => x.Entry).Distinct(StringComparer.OrdinalIgnoreCase).Count() != assets.Count)
            throw new InvalidDataException("Peças com identificadores duplicados no projeto.");

        var temporary = output + ".tmp";
        try
        {
            using (var stream = File.Create(temporary))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("project.fcsproj", CompressionLevel.Optimal);
                using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
                    writer.Write(JsonSerializer.Serialize(project, JsonOptions));
                foreach (var asset in assets)
                    archive.CreateEntryFromFile(asset.Source, asset.Entry, CompressionLevel.Optimal);
            }
            File.Move(temporary, output);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static string Import(string bundle, string parentFolder)
    {
        using var archive = ZipFile.OpenRead(bundle);
        var projectEntry = archive.GetEntry("project.fcsproj")
            ?? throw new InvalidDataException("Pacote sem project.fcsproj.");
        ClothProject project;
        using (var reader = new StreamReader(projectEntry.Open(), Encoding.UTF8))
            project = JsonSerializer.Deserialize<ClothProject>(reader.ReadToEnd())
                ?? throw new InvalidDataException("Projeto inválido no pacote.");
        if (project.FormatVersion != 1) throw new InvalidDataException("Versão de projeto não suportada.");
        var paths = project.Items.SelectMany(item => new[] { item.ModelPath, item.PhysicsPath }
                .Concat(item.AlternateModelPaths).Concat(item.TexturePaths))
            .Concat(project.Tattoos.Select(tattoo => tattoo.ImagePath))
            .Append(project.SourceAlternateVariationsPath).Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!.Replace('\\', '/')).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var path in paths)
        {
            if (!path.StartsWith("assets/", StringComparison.Ordinal) ||
                path.Split('/').Any(part => part is "" or "." or "..") ||
                archive.GetEntry(path) is null)
                throw new InvalidDataException("Arquivo inválido ou ausente no pacote: " + path);
        }
        var name = Path.GetFileNameWithoutExtension(bundle);
        var output = Path.Combine(Path.GetFullPath(parentFolder), name);
        if (Directory.Exists(output) || File.Exists(output))
            throw new IOException("A pasta de importação já existe: " + output);
        Directory.CreateDirectory(output);
        foreach (var path in paths)
        {
            var destination = Path.GetFullPath(Path.Combine(output, path.Replace('/', Path.DirectorySeparatorChar)));
            if (!destination.StartsWith(output + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Caminho fora do projeto: " + path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            archive.GetEntry(path)!.ExtractToFile(destination);
        }
        var projectPath = Path.Combine(output, "project.fcsproj");
        File.WriteAllText(projectPath, JsonSerializer.Serialize(project, JsonOptions), new UTF8Encoding(false));
        return projectPath;
    }
}
