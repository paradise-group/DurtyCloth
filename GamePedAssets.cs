using System.IO;
using CodeWalker.GameFiles;

namespace FooxClothStudio;

public sealed record PedAsset(string Slot, string ModelPath, string? TexturePath);
public static class GamePedAssets
{
    private static readonly object Gate = new();
    public static string? FindGameFolder()
    {
        var saved = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ParadiseClothStudio", "gta-folder.txt");
        if (File.Exists(saved) && File.Exists(Path.Combine(File.ReadAllText(saved).Trim(), "x64v.rpf"))) return File.ReadAllText(saved).Trim();
        var ini = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FiveM", "FiveM.app", "CitizenFX.ini");
        return File.Exists(ini) ? File.ReadLines(ini).Where(l => l.StartsWith("IVPath=", StringComparison.OrdinalIgnoreCase)).Select(l => l[7..].Trim()).FirstOrDefault(p => File.Exists(Path.Combine(p, "x64v.rpf"))) : null;
    }
    public static IReadOnlyList<PedAsset> Load(string gameFolder, string gender)
    {
        lock (Gate)
        {
            var ped = gender == "Feminino" ? "mp_f_freemode_01" : "mp_m_freemode_01";
            var archivePath = Path.Combine(gameFolder, "x64v.rpf");
            var stamp = new FileInfo(archivePath);
            var identity = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(gameFolder).ToLowerInvariant() + stamp.Length + stamp.LastWriteTimeUtc.Ticks)))[..16];
            var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ParadiseClothStudio", "ped-cache-v3", identity, ped);
            Directory.CreateDirectory(cache);
            GTA5Keys.LoadFromPath(gameFolder);
            var archive = new RpfFile(Path.Combine(gameFolder, "x64v.rpf"), "x64v.rpf");
            archive.ScanStructure(null, null);
            IEnumerable<RpfFile> All(RpfFile f) => new[] { f }.Concat((f.Children ?? []).SelectMany(All));
            var directory = All(archive).SelectMany(f => f.AllEntries ?? []).OfType<RpfDirectoryEntry>().FirstOrDefault(e => e.Name == ped)
                ?? throw new InvalidDataException("Freemode ped assets not found in x64v.rpf.");
            var result = new List<PedAsset>();
            foreach (var (slot, preferred) in new[] { ("head", "000"), ("hair", "004"), ("uppr", "015"), ("lowr", "015"), ("feet", "001") })
            {
                var model = directory.Files.FirstOrDefault(f => f.Name.StartsWith(slot + "_" + preferred + "_") && f.Name.EndsWith(".ydd"))
                    ?? directory.Files.FirstOrDefault(f => f.Name.StartsWith(slot + "_000_") && f.Name.EndsWith(".ydd"));
                if (model is null) throw new InvalidDataException("Missing base ped component: " + slot);
                var index = model.Name.Split('_')[1];
                var texture = directory.Files.FirstOrDefault(f => f.Name.StartsWith(slot + "_diff_" + index + "_a_") && f.Name.EndsWith(".ytd"));
                string Extract(RpfFileEntry entry)
                {
                    var target = Path.Combine(cache, entry.Name);
                    if (!File.Exists(target))
                    {
                        var bytes = entry.File.ExtractFile(entry) ?? throw new InvalidDataException("Cannot read " + entry.Name);
                        if (entry.Name.EndsWith(".ydd")) { var model = new YddFile(); model.Load(bytes, entry); bytes = model.Save(); }
                        else { var texture = new YtdFile(); texture.Load(bytes, entry); bytes = texture.Save(); }
                        File.WriteAllBytes(target + ".tmp", bytes); File.Move(target + ".tmp", target, true);
                    }
                    return target;
                }
                result.Add(new PedAsset(slot, Extract(model), texture is null ? null : Extract(texture)));
            }
            return result;
        }
    }
}
