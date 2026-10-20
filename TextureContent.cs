using System.IO;
using System.Security.Cryptography;
using System.Text;
using CodeWalker.GameFiles;

namespace FooxClothStudio;

public static class TextureContent
{
    private static readonly object CacheGate = new();
    private static readonly Dictionary<string, (long Length, long Modified, string Hash)> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static long _decodeCount;
    public static string Fingerprint(string path)
    {
        path = Path.GetFullPath(path);
        var info = new FileInfo(path);
        lock (CacheGate)
        {
            if (Cache.TryGetValue(path, out var cached) && cached.Length == info.Length && cached.Modified == info.LastWriteTimeUtc.Ticks) return cached.Hash;
            var hash = ReadFingerprint(path);
            if (Cache.Count >= 4096) Cache.Clear();
            Cache[path] = (info.Length, info.LastWriteTimeUtc.Ticks, hash);
            return hash;
        }
    }
    private static string ReadFingerprint(string path)
    {
        System.Threading.Interlocked.Increment(ref _decodeCount);
        var ytd = new YtdFile(); ytd.Load(File.ReadAllBytes(path));
        var textures = ytd.TextureDict?.Textures?.data_items;
        if (textures is not { Length: > 0 }) throw new InvalidDataException("YTD without textures: " + path);
        var entries = textures.Select(texture =>
            $"{texture.Width}:{texture.Height}:{texture.Format}:{texture.Levels}:" +
            Convert.ToHexString(SHA256.HashData(texture.Data?.FullData ?? [])))
            .OrderBy(value => value, StringComparer.Ordinal);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", entries))));
    }
}
