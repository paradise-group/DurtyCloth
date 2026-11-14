using System.IO;
using System.Security.Cryptography;

namespace FooxClothStudio;

public static class AssetContent
{
    public static bool Equivalent(string first, string second)
    {
        if (string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase)) return true;
        if (new FileInfo(first).Length != new FileInfo(second).Length) return false;
        using var a = File.OpenRead(first); using var b = File.OpenRead(second);
        return SHA256.HashData(a).SequenceEqual(SHA256.HashData(b));
    }
}
