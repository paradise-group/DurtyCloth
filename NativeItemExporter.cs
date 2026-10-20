using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace FooxClothStudio;

public static partial class NativeItemExporter
{
    public sealed record Asset(string Source, string Name);

    [GeneratedRegex(@"(?<prefix>_diff_\d{3}_)[a-z](?<tail>_[a-z]{3})?\.ytd$",
        RegexOptions.IgnoreCase)]
    private static partial Regex TextureVariantName();

    public static (string Folder, int FileCount) Export(ClothItem item,
        Func<string, string> resolvePath, string parentFolder)
    {
        var planned = Plan(item, resolvePath);
        var safeName = new string(item.Name.ToLowerInvariant().Select(ch =>
            char.IsAsciiLetterOrDigit(ch) ? ch : '_').ToArray()).Trim('_');
        if (safeName.Length == 0) safeName = "peca";
        var parent = Path.GetFullPath(parentFolder);
        var baseFolder = Path.Combine(parent, "foox_item_" + safeName + "_" + item.Id.ToString("N")[..8]);
        var output = baseFolder;
        for (var index = 2; Directory.Exists(output) || File.Exists(output); index++)
            output = baseFolder + "_" + index;
        Directory.CreateDirectory(output);
        try
        {
            foreach (var (source, name) in planned)
            {
                var destination = Path.Combine(output, name);
                File.Copy(source, destination);
                using var input = File.OpenRead(source);
                using var copied = File.OpenRead(destination);
                if (!SHA256.HashData(input).SequenceEqual(SHA256.HashData(copied)))
                    throw new IOException("Falha na verificação da cópia: " + name);
            }
            return (output, planned.Count);
        }
        catch
        {
            if (Directory.Exists(output)) Directory.Delete(output, true);
            throw;
        }
    }

    public static IReadOnlyList<Asset> Plan(ClothItem item, Func<string, string> resolvePath)
    {
        if (item.IsDummy) return [];
        if (item.TexturePaths.Count > 26)
            throw new InvalidDataException("A peça excede 26 variantes YTD.");
        var modelName = FileName(item.OriginalModelFileName ?? item.ModelPath, ".ydd");
        var modelStem = Path.GetFileNameWithoutExtension(modelName);
        var planned = new List<Asset> { new(resolvePath(item.ModelPath), modelName) };
        for (var index = 0; index < item.AlternateModelPaths.Count; index++)
            planned.Add(new(resolvePath(item.AlternateModelPaths[index]),
                modelStem + "_" + (index + 1) + ".ydd"));
        for (var index = 0; index < item.TexturePaths.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(item.TexturePaths[index])) continue;
            var original = FileName(item.OriginalTextureFileNames.ElementAtOrDefault(index) ??
                item.TexturePaths[index], ".ytd");
            var match = TextureVariantName().Match(original);
            var name = match.Success
                ? original[..match.Index] + match.Groups["prefix"].Value +
                  (char)('a' + index) + match.Groups["tail"].Value + ".ytd"
                : original;
            planned.Add(new(resolvePath(item.TexturePaths[index]), name));
        }
        if (item.PhysicsPath is not null)
        {
            var stem = Regex.Replace(modelStem, @"_(?:u|r)$", "", RegexOptions.IgnoreCase);
            planned.Add(new(resolvePath(item.PhysicsPath), stem + ".yld"));
        }
        if (planned.Select(pair => pair.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != planned.Count)
            throw new InvalidDataException("Nomes de saída duplicados; ajuste os nomes das texturas antes de exportar.");
        foreach (var (source, _) in planned)
            if (!File.Exists(source)) throw new FileNotFoundException("Arquivo da peça ausente", source);
        return planned;
    }

    private static string FileName(string path, string extension)
    {
        var name = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(name) || !name.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException("Nome de arquivo inválido: " + path);
        return name;
    }
}
