using System.IO;
using CodeWalker.GameFiles;

namespace FooxClothStudio;

// Restrict lookup to the model's own DLC collection, the base ped and exact
// shared texture names. A matching component number in another DLC is unsafe.
public sealed class GameTextureDependencies(string gameFolder, string cacheFolder)
{
    private readonly Dictionary<string, RpfFile> _archives = new(StringComparer.OrdinalIgnoreCase);

    public string? Resolve(ClothItem item, string modelPath, string[] diffuse)
    {
        var source = Path.GetFileNameWithoutExtension(item.OriginalModelFileName ?? item.ModelPath).Split('^')[0];
        var ped = item.Gender == "Feminino" ? "mp_f_freemode_01" : "mp_m_freemode_01";
        var archiveNames = new List<string> { Path.Combine(gameFolder, "x64v.rpf"), Path.Combine(gameFolder, "x64w.rpf") };
        var dlcs = Path.Combine(gameFolder, "update", "x64", "dlcpacks");
        if (Directory.Exists(dlcs))
            archiveNames.AddRange(Directory.EnumerateDirectories(dlcs).Where(path => MatchesDlc(Path.GetFileName(path), source))
                .Select(path => Path.Combine(path, "dlc.rpf")));
        GTA5Keys.LoadFromPath(gameFolder);
        foreach (var ownCollection in new[] { true, false })
        foreach (var archivePath in archiveNames.Where(File.Exists))
        {
            if (!_archives.TryGetValue(archivePath, out var archive))
            {
                archive = new RpfFile(archivePath, Path.GetFileName(archivePath));
                archive.ScanStructure(null, null); _archives[archivePath] = archive;
            }
            IEnumerable<RpfFile> All(RpfFile file) => new[] { file }.Concat((file.Children ?? []).SelectMany(All));
            var directories = All(archive).SelectMany(file => file.AllEntries ?? []).OfType<RpfDirectoryEntry>()
                .Where(directory => ownCollection ? directory.Name.Equals(source, StringComparison.OrdinalIgnoreCase) :
                    directory.Name.Equals(ped, StringComparison.OrdinalIgnoreCase) || directory.Name.Equals(ped + "_p", StringComparison.OrdinalIgnoreCase) ||
                    diffuse.Any(name => name.StartsWith("mp_fm_skin_", StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(directory => directory.Name.Equals(source, StringComparison.OrdinalIgnoreCase));
            foreach (var directory in directories)
            {
                var code = CollectionIndices.CodeOf(item.Category);
                var candidates = directory.Files.Where(file => file.Name.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase) &&
                    (diffuse.Any(name => file.Name.Equals(name + ".ytd", StringComparison.OrdinalIgnoreCase)) ||
                     file.Name.StartsWith(code + "_diff_" + (item.SourceIndex ?? 0).ToString("D3") + "_", StringComparison.OrdinalIgnoreCase) ||
                     diffuse.Any(name => name.StartsWith("mp_fm_skin_", StringComparison.OrdinalIgnoreCase)) && file.Name.Contains("skin", StringComparison.OrdinalIgnoreCase)));
                foreach (var candidate in candidates.OrderBy(file => file.Name))
                {
                    var bytes = candidate.File.ExtractFile(candidate);
                    if (bytes is null) continue;
                    var ytd = new YtdFile(); ytd.Load(bytes, candidate);
                    var names = (ytd.TextureDict?.Textures?.data_items ?? []).Select(texture => texture.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    if (!diffuse.All(names.Contains)) continue;
                    return Store(item, ytd.Save(), candidate.Name);
                }
            }
        }
        return null;
    }

    public string? Embedded(ClothItem item, string modelPath, string[] requiredDiffuse)
    {
        var model = new YddFile(); model.Load(File.ReadAllBytes(modelPath));
        var textures = (model.Drawables ?? []).SelectMany(drawable => drawable.ShaderGroup?.TextureDictionary?.Textures?.data_items ?? [])
            .Where(texture => texture.Data?.FullData is { Length: > 0 }).GroupBy(texture => texture.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First()).ToArray();
        if (textures.Length == 0) return null;
        var names = textures.Select(texture => texture.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (requiredDiffuse.Length > 0 && !requiredDiffuse.All(names.Contains)) return null;
        if (requiredDiffuse.Length == 0)
        {
            var hasVolume = false;
            foreach (var shader in (model.Drawables ?? []).SelectMany(drawable => drawable.ShaderGroup?.Shaders?.data_items ?? []))
            {
                var hashes = shader.ParametersList?.Hashes ?? []; var parameters = shader.ParametersList?.Parameters ?? [];
                for (var index = 0; index < Math.Min(hashes.Length, parameters.Length); index++)
                {
                    var sampler = hashes[index].ToString();
                    if (parameters[index]?.Data is not TextureBase texture) continue;
                    if (sampler.Contains("diffuse", StringComparison.OrdinalIgnoreCase) && !names.Contains(texture.Name)) return null;
                    hasVolume |= sampler.Contains("volume", StringComparison.OrdinalIgnoreCase);
                }
            }
            if (!hasVolume) return null;
        }
        var ytd = new YtdFile { TextureDict = new TextureDictionary() }; ytd.TextureDict.BuildFromTextureList(textures.ToList());
        var skinTone = Path.GetFileNameWithoutExtension(item.OriginalModelFileName ?? item.ModelPath).EndsWith("_r", StringComparison.OrdinalIgnoreCase);
        return Store(item, ytd.Save(), skinTone ? "embedded_a_whi.ytd" : "embedded_a_uni.ytd");
    }

    private string Store(ClothItem item, byte[] bytes, string name)
    {
        var directory = Path.Combine(cacheFolder, item.Id.ToString("N")); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name); File.WriteAllBytes(path, bytes);
        var check = new YtdFile(); check.Load(File.ReadAllBytes(path));
        if (check.TextureDict?.Textures?.data_items is not { Length: > 0 }) throw new InvalidDataException("Resolved YTD is empty.");
        return path;
    }

    private static bool MatchesDlc(string directory, string source)
    {
        var token = directory.ToLowerInvariant(); var name = source.ToLowerInvariant();
        if (token.Contains("patch") || token.Contains("g9ec")) return false;
        var aliases = new Dictionary<string, string> { ["mpbiker"] = "bikerdlc", ["mpvalentines2"] = "valentines_02" };
        var match = aliases.GetValueOrDefault(token, token.StartsWith("mp") ? token[2..] : token);
        return match.Length > 0 && name.Contains(match, StringComparison.OrdinalIgnoreCase);
    }
}
