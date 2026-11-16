using System.Globalization;
using System.IO;
using CodeWalker.GameFiles;

namespace FooxClothStudio;

public sealed record AssetBudgetIssue(string Severity, string File, string Asset, int Width, int Height,
    string Format, int Levels, long MemoryBytes, List<string> Reasons, string Message);

public static class AssetBudget
{
    public const string Critical = "critical";
    public const string Warning = "warning";
    public const string Notice = "notice";
    public const int StandardTextureSize = 1024;
    public const int MaximumTextureSize = 2048;
    public const long HeavyMemoryBytes = 16L * 1024 * 1024;
    public const long PageCapacityBytes = 64L * 1024 * 1024;
    private const int MipmapRelevantSize = 512;
    private const long UncompressedRelevantBytes = 1024 * 1024;
    private const double BytesPerMegabyte = 1024 * 1024;
    private static readonly string[] UncompressedFormats = ["A8R8G8B8", "X8R8G8B8", "A8B8G8R8"];

    public static List<AssetBudgetIssue> Inspect(string path, string relative) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".ytd" => InspectTextures(path, relative),
        ".ydd" or ".yld" => InspectFile(path, relative),
        _ => []
    };

    private static List<AssetBudgetIssue> InspectTextures(string path, string relative)
    {
        Texture[]? textures;
        try
        {
            var ytd = new YtdFile();
            ytd.Load(File.ReadAllBytes(path));
            textures = ytd.TextureDict?.Textures?.data_items;
        }
        catch (Exception error)
        {
            return [new(Warning, relative, "", 0, 0, "", 0, 0, ["unreadable"], $"{relative}: texture could not be read for budget checks ({error.Message})")];
        }
        var issues = new List<AssetBudgetIssue>();
        foreach (var texture in textures ?? [])
        {
            var format = texture.Format.ToString().Replace("D3DFMT_", "");
            var memory = texture.Data?.FullData?.LongLength ?? 0;
            var largestSide = Math.Max(texture.Width, texture.Height);
            var reasons = new List<string>();
            var critical = memory > PageCapacityBytes;
            var oversized = largestSide > MaximumTextureSize;
            var heavy = !critical && memory >= HeavyMemoryBytes;
            if (critical)
                reasons.Add("does not fit a 64 MB RAGE resource page, so the client crashes with ERR_STR_FAILURE_3");
            if (oversized)
                reasons.Add($"larger than the {MaximumTextureSize} maximum (standard is {StandardTextureSize})");
            if (UncompressedFormats.Contains(format) && memory >= UncompressedRelevantBytes)
                reasons.Add("uncompressed, use DXT1 (opaque) or DXT5 (alpha)");
            if (texture.Levels <= 1 && largestSide >= MipmapRelevantSize)
                reasons.Add("no mipmaps");
            if (heavy)
                reasons.Add("over 16 MB of memory, which FiveM treats as an oversized asset");
            if (reasons.Count == 0) continue;
            var severity = critical ? Critical : oversized || heavy ? Warning : Notice;
            var description = string.Create(CultureInfo.InvariantCulture,
                $"{relative}: {texture.Name} {texture.Width}x{texture.Height} {format}, {texture.Levels} level(s), {memory / BytesPerMegabyte:F1} MB in memory");
            issues.Add(new(severity, relative, texture.Name, texture.Width, texture.Height, format, texture.Levels,
                memory, reasons, description + ": " + string.Join("; ", reasons)));
        }
        return issues;
    }

    private static List<AssetBudgetIssue> InspectFile(string path, string relative)
    {
        var length = new FileInfo(path).Length;
        if (length < HeavyMemoryBytes) return [];
        var message = string.Create(CultureInfo.InvariantCulture,
            $"{relative}: {length / BytesPerMegabyte:F1} MB on disk: over 16 MB, which FiveM treats as an oversized asset");
        return [new(Warning, relative, "", 0, 0, "", 0, length, ["over 16 MB on disk"], message)];
    }
}
