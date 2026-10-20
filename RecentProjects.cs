using System.IO;
using System.Text.Json;

namespace FooxClothStudio;

public sealed class RecentProjects
{
    private readonly string _file;
    public RecentProjects(string? file = null) => _file = file ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ParadiseClothStudio", "recent-projects.json");
    public string[] Read()
    {
        try
        {
            return (JsonSerializer.Deserialize<string[]>(File.ReadAllText(_file)) ?? [])
                .Where(p => !string.IsNullOrWhiteSpace(p)).Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToArray();
        }
        catch (Exception) { return []; }
    }
    public void Remember(string path)
    {
        path = Path.GetFullPath(path);
        var paths = new[] { path }.Concat(Read()).Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_file))!);
        var temporary = _file + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(paths));
        File.Move(temporary, _file, true);
    }
}
