using System.IO;
using CodeWalker.GameFiles;
using CodeWalker.Utils;
using FooxClothStudio;

internal static class BudgetTests
{
    private const int DdsHeaderSize = 128;
    private const int BytesPerPixel = 4;
    private const int Dxt1BlockBytes = 8;
    private const int Dxt5BlockBytes = 16;

    public static void Run(string root)
    {
        Directory.CreateDirectory(root);
        string Write(string name, int size, int levels, string? fourCc)
        {
            var texture = DDSIO.GetTexture(Dds(size, levels, fourCc));
            texture.Name = name;
            var ytd = new YtdFile { TextureDict = new TextureDictionary() };
            ytd.TextureDict.BuildFromTextureList([texture]);
            var path = Path.Combine(root, name + ".ytd");
            File.WriteAllBytes(path, ytd.Save());
            return path;
        }
        List<AssetBudgetIssue> Issues(string path) => AssetBudget.Inspect(path, Path.GetFileName(path));

        var crashing = Issues(Write("crashing", 4096, 13, null)).Single();
        Check(crashing.Severity == AssetBudget.Critical && crashing.Message.Contains("ERR_STR_FAILURE_3") && crashing.Message.Contains("4096x4096"),
            "A texture over one resource page must be critical: " + crashing.Message);

        var heavy = Issues(Write("heavy", 4096, 1, null)).Single();
        Check(heavy.Severity == AssetBudget.Warning && heavy.Reasons.Count == 4 && heavy.Message.Contains("64.0 MB"),
            "A 64 MB texture must warn about size, compression, mipmaps and memory: " + heavy.Message);

        var oversizedCompressed = Issues(Write("oversized", 4096, 13, "DXT5")).Single();
        Check(oversizedCompressed.Severity == AssetBudget.Warning && oversizedCompressed.Reasons.Count == 2,
            "A compressed 4096 texture must warn about resolution and memory: " + oversizedCompressed.Message);

        var uncompressed = Issues(Write("uncompressed", 1024, 11, null)).Single();
        Check(uncompressed.Severity == AssetBudget.Notice && uncompressed.Reasons.Single().Contains("uncompressed"),
            "An uncompressed 1024 texture must be a notice about compression: " + uncompressed.Message);

        var flat = Issues(Write("flat", 1024, 1, "DXT5")).Single();
        Check(flat.Severity == AssetBudget.Notice && flat.Reasons.Single() == "no mipmaps", "A texture without mipmaps must be a notice: " + flat.Message);

        Check(Issues(Write("standard", 1024, 9, "DXT1")).Count == 0, "A standard 1024 DXT1 texture must not warn");
        Check(Issues(Write("detailed", 2048, 10, "DXT5")).Count == 0, "A 2048 DXT5 texture is allowed and must not warn");
        Check(Issues(Write("icon", 256, 1, null)).Count == 0, "A small texture must not warn");

        var broken = Path.Combine(root, "broken.ytd");
        File.WriteAllBytes(broken, [1, 2, 3]);
        Check(Issues(broken).Single() is { Severity: AssetBudget.Warning } unreadable && unreadable.Reasons.Single() == "unreadable",
            "An unreadable texture must be reported as a warning, not thrown");
        Console.WriteLine("PASS: asset budget flags crashing textures as critical, heavy or oversized ones as warnings and uncompressed or mipmap-less ones as notices, with MB; standard 1024 and 2048 compressed textures stay clean.");
    }

    private static byte[] Dds(int size, int levels, string? fourCc)
    {
        long length = 0;
        for (var level = 0; level < levels; level++)
        {
            var side = Math.Max(1, size >> level);
            var blocks = Math.Max(1, (side + 3) / 4);
            length += fourCc switch { "DXT1" => (long)blocks * blocks * Dxt1BlockBytes, "DXT5" => (long)blocks * blocks * Dxt5BlockBytes, _ => (long)side * side * BytesPerPixel };
        }
        var data = new byte[DdsHeaderSize + length];
        using var writer = new BinaryWriter(new MemoryStream(data));
        writer.Write(0x20534444);
        writer.Write(124);
        writer.Write(0x1 | 0x2 | 0x4 | 0x1000 | 0x20000 | (fourCc is null ? 0x8 : 0x80000));
        writer.Write(size);
        writer.Write(size);
        writer.Write(fourCc is null ? size * BytesPerPixel : Math.Max(1, size / 4) * Math.Max(1, size / 4) * (fourCc == "DXT1" ? Dxt1BlockBytes : Dxt5BlockBytes));
        writer.Write(0);
        writer.Write(levels);
        writer.BaseStream.Position = 76;
        writer.Write(32);
        if (fourCc is null)
        {
            writer.Write(0x41);
            writer.Write(0);
            writer.Write(32);
            writer.Write(0x00FF0000);
            writer.Write(0x0000FF00);
            writer.Write(0x000000FF);
            writer.Write(0xFF000000);
        }
        else
        {
            writer.Write(0x4);
            writer.Write(System.Text.Encoding.ASCII.GetBytes(fourCc));
        }
        writer.BaseStream.Position = 108;
        writer.Write(0x1000 | 0x8 | 0x400000);
        return data;
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
