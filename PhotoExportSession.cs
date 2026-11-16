using System.IO;
using System.Text;
using System.Text.Json;

namespace FooxClothStudio;

public sealed record PhotoExportSettings(PhotoImageOptions image, string background, bool includePed, bool allTextures = false);

public sealed class PhotoExportSession
{
    private readonly string output;
    private readonly Dictionary<string, List<string>> _photos = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _indexed = new(StringComparer.OrdinalIgnoreCase);
    public bool OverwriteExisting { get; }

    public PhotoExportSession(string folder, bool overwriteExisting)
    {
        output = Path.GetFullPath(folder);
        OverwriteExisting = overwriteExisting;
        Directory.CreateDirectory(output);
        foreach (var path in Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories))
            if (Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp") Register(path);
    }

    private string Key(string path)
    {
        var stem = Path.GetFileNameWithoutExtension(path);
        var identifiers = stem.Split('.');
        if (identifiers.Length == 2 && Guid.TryParse(identifiers[0], out _) &&
            (Guid.TryParse(identifiers[1], out _) || identifiers[1] == "sem_textura")) return stem;
        var folders = Path.GetRelativePath(output, Path.GetDirectoryName(path)!).Split(Path.DirectorySeparatorChar);
        var gender = folders.FirstOrDefault(folder => folder is "Masculino" or "Feminino" or "Indefinido") ?? "";
        return gender + "/" + stem;
    }

    private void Register(string path)
    {
        var key = Key(path);
        if (!_photos.TryGetValue(key, out var paths)) _photos[key] = paths = [];
        if (!paths.Contains(path, StringComparer.OrdinalIgnoreCase)) paths.Add(path);
    }

    private string[] Existing(string path, string? legacyPath = null) =>
        (_photos.GetValueOrDefault(Key(path)) ?? []).Concat(legacyPath is null ? [] : _photos.GetValueOrDefault(Key(legacyPath)) ?? [])
            .Append(path).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public string? FindExisting(string path, string? legacyPath = null) => Existing(path, legacyPath).FirstOrDefault();
    public bool NeedsIndexEntry(string path) => _indexed.Add(Path.GetRelativePath(output, path).Replace('\\', '/'));

    public string SaveImage(string path, Action<string> render, string? legacyPath = null)
    {
        var existing = Existing(path, legacyPath);
        if (!OverwriteExisting && existing.Length > 0) return existing[0];
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".pending-" + Guid.NewGuid().ToString("N");
        try
        {
            render(temporary);
            File.Move(temporary, path, OverwriteExisting);
            foreach (var previous in existing.Where(previous => !string.Equals(previous, path, StringComparison.OrdinalIgnoreCase)))
                File.Delete(previous);
            Register(path);
            return path;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public StreamWriter OpenIndex()
    {
        var path = Path.Combine(output, "index.jsonl");
        if (File.Exists(path))
        {
            var valid = new List<string>();
            var damaged = false;
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    using var json = JsonDocument.Parse(line);
                    var entry = json.RootElement;
                    if (entry.ValueKind != JsonValueKind.Object) { damaged = true; continue; }
                    valid.Add(line);
                    if (entry.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String && status.GetString() == "ok" &&
                        entry.TryGetProperty("file", out var file) && file.ValueKind == JsonValueKind.String)
                        _indexed.Add(file.GetString()!.Replace('\\', '/'));
                }
                catch (JsonException) { damaged = true; }
            }
            if (damaged)
            {
                File.Copy(path, path + ".interrupted-" + Guid.NewGuid().ToString("N"));
                File.WriteAllLines(path, valid, new UTF8Encoding(false));
            }
            else if (new FileInfo(path).Length > 0)
                File.AppendAllText(path, "\n", new UTF8Encoding(false));
        }
        return new StreamWriter(path, true, new UTF8Encoding(false)) { AutoFlush = true };
    }
}
