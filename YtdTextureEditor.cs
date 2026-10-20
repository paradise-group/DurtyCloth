using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using CodeWalker.GameFiles;
using CodeWalker.Utils;

namespace FooxClothStudio;

public static class YtdTextureEditor
{
    public static void ExportDds(string sourceYtd, int textureIndex, string outputDds)
    {
        var output = Path.GetFullPath(outputDds);
        if (File.Exists(output)) throw new IOException("O arquivo DDS de saída já existe: " + output);
        var ytd = new YtdFile();
        ytd.Load(File.ReadAllBytes(sourceYtd));
        var texture = ytd.TextureDict?.Textures?.data_items?.ElementAtOrDefault(textureIndex)
            ?? throw new InvalidDataException("Textura não encontrada no YTD.");
        if (texture.Width == 0 || texture.Height == 0 ||
            texture.Data?.FullData is not { Length: > 0 })
            throw new InvalidDataException("Textura sem dados de imagem.");
        var dds = DDSIO.GetDDSFile(texture);
        var reopened = DDSIO.GetTexture(dds) ??
            throw new InvalidDataException("O DDS exportado não pôde ser relido.");
        if (reopened.Width != texture.Width || reopened.Height != texture.Height ||
            reopened.Format != texture.Format || reopened.Levels != texture.Levels ||
            !SHA256.HashData(reopened.Data?.FullData ?? [])
                .SequenceEqual(SHA256.HashData(texture.Data.FullData)))
            throw new InvalidDataException("O DDS exportado não preservou todos os mipmaps da textura.");
        File.WriteAllBytes(output, dds);
    }

    public static string ReplaceFromDds(string sourceYtd, int textureIndex, string sourceDds,
        string outputYtd)
        => Replace(sourceYtd, textureIndex, () => ReadDds(sourceDds), outputYtd);

    public static string ReplaceFromPng(string sourceYtd, int textureIndex, string sourcePng,
        string outputYtd)
        => Replace(sourceYtd, textureIndex, () => PngTextureReader.Read(sourcePng), outputYtd);

    private static string Replace(string sourceYtd, int textureIndex, Func<Texture> readTexture,
        string outputYtd)
    {
        var (ytd, textures) = Load(sourceYtd);
        if (textureIndex < 0 || textureIndex >= textures.Length)
            throw new ArgumentOutOfRangeException(nameof(textureIndex));
        var original = textures[textureIndex];
        var replacement = readTexture();
        replacement.Name = original.Name;
        replacement.NameHash = original.NameHash;
        var updated = textures.ToArray();
        updated[textureIndex] = replacement;
        WriteVerified(ytd, updated, sourceYtd, outputYtd);
        return original.Name;
    }

    public static string AddFromDds(string sourceYtd, string sourceDds, string textureName,
        string outputYtd)
        => Add(sourceYtd, () => ReadDds(sourceDds), textureName, outputYtd);

    public static string AddFromPng(string sourceYtd, string sourcePng, string textureName,
        string outputYtd)
        => Add(sourceYtd, () => PngTextureReader.Read(sourcePng), textureName, outputYtd);

    private static string Add(string sourceYtd, Func<Texture> readTexture,
        string textureName, string outputYtd)
    {
        var name = ValidateName(textureName);
        var (ytd, textures) = Load(sourceYtd);
        var hash = JenkHash.GenHash(name.ToLowerInvariant());
        if (textures.Any(texture => texture.NameHash == hash ||
            string.Equals(texture.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Já existe uma textura com esse nome ou hash no YTD.");
        var added = readTexture();
        added.Name = name;
        added.NameHash = hash;
        WriteVerified(ytd, [.. textures, added], sourceYtd, outputYtd);
        return name;
    }

    public static string Remove(string sourceYtd, int textureIndex, string outputYtd)
    {
        var (ytd, textures) = Load(sourceYtd);
        if (textureIndex < 0 || textureIndex >= textures.Length)
            throw new ArgumentOutOfRangeException(nameof(textureIndex));
        if (textures.Length == 1)
            throw new InvalidDataException("O YTD precisa manter ao menos uma textura.");
        var removed = textures[textureIndex].Name;
        WriteVerified(ytd, textures.Where((_, index) => index != textureIndex).ToArray(),
            sourceYtd, outputYtd);
        return removed;
    }

    public static string Rename(string sourceYtd, int textureIndex, string newName,
        string outputYtd)
    {
        var name = ValidateName(newName);
        var (ytd, textures) = Load(sourceYtd);
        if (textureIndex < 0 || textureIndex >= textures.Length)
            throw new ArgumentOutOfRangeException(nameof(textureIndex));
        var hash = JenkHash.GenHash(name.ToLowerInvariant());
        if (textures.Where((_, index) => index != textureIndex).Any(texture =>
            texture.NameHash == hash ||
            string.Equals(texture.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Já existe uma textura com esse nome ou hash no YTD.");
        var renamed = textures[textureIndex];
        if (string.Equals(renamed.Name, name, StringComparison.Ordinal) && renamed.NameHash == hash)
            throw new InvalidDataException("A textura já tem esse nome.");
        renamed.Name = name;
        renamed.NameHash = hash;
        WriteVerified(ytd, textures, sourceYtd, outputYtd);
        return name;
    }

    private static string ValidateName(string textureName)
    {
        var name = textureName.Trim();
        if (!Regex.IsMatch(name, @"^[A-Za-z0-9_]{1,64}$"))
            throw new InvalidDataException("Use de 1 a 64 letras, números ou _ no nome da textura.");
        return name;
    }

    private static (YtdFile Ytd, Texture[] Textures) Load(string path)
    {
        var ytd = new YtdFile();
        ytd.Load(File.ReadAllBytes(path));
        var textures = ytd.TextureDict?.Textures?.data_items;
        if (textures is null || textures.Length == 0)
            throw new InvalidDataException("YTD sem texturas.");
        return (ytd, textures);
    }

    private static Texture ReadDds(string path)
    {
        var texture = DDSIO.GetTexture(File.ReadAllBytes(path)) ??
            throw new InvalidDataException("DDS inválido.");
        if (texture.Width == 0 || texture.Height == 0 ||
            texture.Data?.FullData is not { Length: > 0 })
            throw new InvalidDataException("DDS sem dados de imagem.");
        return texture;
    }

    private static void WriteVerified(YtdFile ytd, Texture[] updated, string sourceYtd,
        string outputYtd)
    {
        var input = Path.GetFullPath(sourceYtd);
        var output = Path.GetFullPath(outputYtd);
        if (string.Equals(input, output, StringComparison.OrdinalIgnoreCase) || File.Exists(output))
            throw new IOException("Escolha um nome novo para o YTD editado; o arquivo de origem será preservado.");
        var snapshots = updated.Select(texture => new
        {
            texture.Name, texture.NameHash, texture.Width, texture.Height,
            texture.Format, texture.Levels,
            Hash = SHA256.HashData(texture.Data?.FullData ?? [])
        }).ToArray();
        ytd.TextureDict!.BuildFromTextureList(updated.ToList());
        var encoded = ytd.Save();
        var check = new YtdFile();
        check.Load(encoded);
        var actual = check.TextureDict?.Textures?.data_items ?? [];
        if (actual.Length != snapshots.Length)
            throw new InvalidDataException("O YTD regravado perdeu texturas.");
        foreach (var expected in snapshots)
        {
            var found = actual.SingleOrDefault(texture =>
                texture.NameHash == expected.NameHash &&
                string.Equals(texture.Name, expected.Name, StringComparison.OrdinalIgnoreCase));
            if (found is null || found.Width != expected.Width || found.Height != expected.Height ||
                found.Format != expected.Format || found.Levels != expected.Levels ||
                !SHA256.HashData(found.Data?.FullData ?? []).SequenceEqual(expected.Hash))
                throw new InvalidDataException("O YTD regravado alterou uma textura inesperadamente: " +
                    expected.Name);
        }
        File.WriteAllBytes(output, encoded);
    }
}
