using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FooxClothStudio;

public static class ProjectCreator
{
    public static string Create(ClothProject project)
    {
        if (string.IsNullOrWhiteSpace(project.Name) || !Regex.IsMatch(project.DlcName, "^[a-z0-9_]{1,24}$"))
            throw new InvalidDataException("Enter a project name and a valid lowercase DLC name.");
        if (string.IsNullOrWhiteSpace(project.OutputFolder)) throw new InvalidDataException("Select an Output Folder.");
        var parent = Path.GetFullPath(project.OutputFolder);
        var folder = Path.GetFullPath(Path.Combine(parent, "clothproject_" + project.DlcName));
        if (!folder.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid project folder.");
        if (Directory.Exists(folder) || File.Exists(folder)) throw new IOException("Project folder already exists: " + folder);
        Directory.CreateDirectory(folder);
        try
        {
            var copy = JsonSerializer.Deserialize<ClothProject>(JsonSerializer.Serialize(project))!;
            copy.OutputFolder = folder;
            var path = Path.Combine(folder, project.DlcName + ".fcsproj");
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(copy, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(path + ".tmp", path);
            return path;
        }
        catch { Directory.Delete(folder, true); throw; }
    }
}
