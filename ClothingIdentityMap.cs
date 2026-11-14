using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FooxClothStudio;

public sealed record ClothingTextureIdentity(Guid Id, int Index, string? File, bool Available,
    bool Inherited = false, Guid? AliasOf = null);
public sealed record ClothingIdentityEntry(Guid Id, string Name, string Gender, string Model, string Kind,
    int Slot, string Collection, int Index, string? ModelFile, bool Available, List<ClothingTextureIdentity> Textures,
    string? SourceModelFile = null, int? SourceIndex = null, Guid? AliasOf = null);
public sealed record ClothingIdentityDocument(int FormatVersion, List<ClothingIdentityEntry> Entries, bool PreserveSourceLayout = false);

public static class ClothingIdentityMap
{
    public const string FileName = "clothing-identities.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static int GenerateMissing(ClothProject project)
    {
        var existing = project.Items.Select(item => item.Id).Concat(project.Items.SelectMany(item => item.TextureIds))
            .Where(id => id != Guid.Empty).ToArray();
        if (existing.Distinct().Count() != existing.Length)
            throw new InvalidDataException("O projeto contém IDs duplicados. Resolva as peças duplicadas antes de gerar o catálogo.");
        var added = 0;
        foreach (var item in project.Items)
        {
            if (item.Id == Guid.Empty) { item.Id = Guid.NewGuid(); added++; }
            var count = item.TextureIds.Count;
            EnsureTextures(item);
            added += Math.Max(0, item.TextureIds.Count - count);
            for (var index = 0; index < item.TextureIds.Count; index++)
                if (item.TextureIds[index] == Guid.Empty) { item.TextureIds[index] = Guid.NewGuid(); added++; }
        }
        return added;
    }

    public static Guid SourceId(string model, string gender)
    {
        var key = "paradise-clothing/source/v1/" + gender + "/" + Path.GetFileName(model).ToLowerInvariant();
        return new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(key)).AsSpan(0, 16));
    }

    public static Guid InheritedTextureId(Guid itemId, int index = 0) => new(SHA256.HashData(
        Encoding.UTF8.GetBytes($"paradise-clothing/inherited-texture/v1/{itemId:D}/{index}")).AsSpan(0, 16));

    public static void EnsureTextures(ClothItem item)
    {
        while (item.TextureIds.Count < item.TexturePaths.Count)
        {
            var index = item.TextureIds.Count;
            var file = item.OriginalTextureFileNames.ElementAtOrDefault(index) ?? Path.GetFileName(item.TexturePaths[index]);
            var seed = $"paradise-clothing/texture/v1/{item.Id:D}/{file.ToLowerInvariant()}/{index}";
            item.TextureIds.Add(new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(seed)).AsSpan(0, 16)));
        }
        if (item.TextureIds.Count > item.TexturePaths.Count)
            item.TextureIds.RemoveRange(item.TexturePaths.Count, item.TextureIds.Count - item.TexturePaths.Count);
    }

    public static ClothingIdentityDocument Describe(ClothProject project)
    {
        var items = project.Items.ToDictionary(item => item.Id);
        var entries = new List<ClothingIdentityEntry>();
        foreach (var entry in CollectionIndices.Current(project))
        {
            var item = items[entry.ItemId];
            EnsureTextures(item);
            var prop = entry.Slot.StartsWith('p');
            var slot = int.Parse(prop ? entry.Slot[1..] : entry.Slot);
            var model = item.Gender == "Feminino" ? "mp_f_freemode_01" : "mp_m_freemode_01";
            var prefix = model + (prop ? "_p_" : "_") + entry.Collection;
            var code = CollectionIndices.CodeOf(item.Category);
            var number = entry.LocalIndex.ToString("D3");
            var suffix = Path.GetFileNameWithoutExtension(item.OriginalModelFileName ?? item.ModelPath)
                .EndsWith("_r", StringComparison.OrdinalIgnoreCase) ? "r" : "u";
            var modelFile = item.IsDummy ? null : prefix + "^" + code + "_" + number + (prop ? "" : "_" + suffix) + ".ydd";
            var textures = new List<ClothingTextureIdentity>();
            for (var index = 0; index < item.TexturePaths.Count; index++)
            {
                var available = !item.IsDummy && !string.IsNullOrWhiteSpace(item.TexturePaths[index]);
                var original = Path.GetFileNameWithoutExtension(item.OriginalTextureFileNames.ElementAtOrDefault(index) ?? item.TexturePaths[index]);
                var match = Regex.Match(original, "_([a-z]{3})$", RegexOptions.IgnoreCase);
                var tail = prop ? "" : "_" + (match.Success ? match.Groups[1].Value.ToLowerInvariant() : suffix == "r" ? "whi" : "uni");
                textures.Add(new(item.TextureIds[index], index,
                    available ? prefix + "^" + code + "_diff_" + number + "_" + (char)('a' + index) + tail + ".ytd" : null, available));
            }
            entries.Add(new(item.Id, item.Name, item.Gender, model, prop ? "prop" : "component",
                slot, entry.Collection, entry.LocalIndex, modelFile, !item.IsDummy, textures,
                Path.GetFileName(item.OriginalModelFileName ?? item.ModelPath), item.SourceIndex));
        }
        var document = new ClothingIdentityDocument(1, entries);
        Validate(document);
        return document;
    }

    public static void Validate(ClothingIdentityDocument document)
    {
        if (document.FormatVersion != 1 || document.Entries is null) throw new InvalidDataException("Versão do mapa de IDs não suportada.");
        var ids = new HashSet<Guid>();
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var addresses = new HashSet<(string, string, int, string, int)>();
        foreach (var entry in document.Entries)
        {
            if (entry.Id == Guid.Empty || !ids.Add(entry.Id) || entry.Textures is null ||
                entry.Kind is not ("component" or "prop") || entry.Index < 0 || entry.Slot < 0 ||
                !Regex.IsMatch(entry.Collection, "^[a-z0-9_]{0,64}$") ||
                entry.Model is not ("mp_m_freemode_01" or "mp_f_freemode_01"))
                throw new InvalidDataException("Identidade de roupa inválida ou duplicada.");
            var address = (entry.Model, entry.Kind, entry.Slot, entry.Collection, entry.Index);
            var alias = entry.AliasOf is Guid aliasId ? document.Entries.FirstOrDefault(candidate => candidate.Id == aliasId) : null;
            var validAlias = document.PreserveSourceLayout && alias is not null && alias.Id != entry.Id && alias.AliasOf is null &&
                (alias.Model, alias.Kind, alias.Slot, alias.Collection, alias.Index) == address &&
                Regex.Replace(alias.ModelFile ?? "", "_[ur](?=\\.ydd$)", "") == Regex.Replace(entry.ModelFile ?? "", "_[ur](?=\\.ydd$)", "");
            if ((entry.Kind == "component" ? entry.Slot > 11 : !new[] { 0, 1, 2, 3, 6, 7 }.Contains(entry.Slot)) ||
                ((!document.PreserveSourceLayout || entry.Available) && !addresses.Add(address) && !validAlias) || (entry.AliasOf is not null && !validAlias))
                throw new InvalidDataException("Slot/endereço duplicado ou inválido no mapa de IDs.");
            if (entry.ModelFile is not null && (Path.GetFileName(entry.ModelFile) != entry.ModelFile || !files.Add(entry.ModelFile)))
                throw new InvalidDataException("Ficheiro duplicado/inválido no mapa de IDs.");
            var textureIndexes = new HashSet<int>();
            foreach (var texture in entry.Textures)
            {
                var textureAlias = texture.AliasOf is Guid target ? entry.Textures.FirstOrDefault(candidate => candidate.Id == target) : null;
                var validTextureAlias = document.PreserveSourceLayout && textureAlias is not null && textureAlias.Id != texture.Id && textureAlias.AliasOf is null && textureAlias.Index == texture.Index;
                if (texture.Id == Guid.Empty || !ids.Add(texture.Id) || texture.Index < 0 ||
                    (!textureIndexes.Add(texture.Index) && !validTextureAlias) || (texture.AliasOf is not null && !validTextureAlias) ||
                    texture.File is not null && Path.GetFileName(texture.File) != texture.File)
                    throw new InvalidDataException("Identidade de textura inválida ou duplicada.");
            }
            if (!document.PreserveSourceLayout && !textureIndexes.Order().SequenceEqual(Enumerable.Range(0, entry.Textures.Count)))
                throw new InvalidDataException("O mapa deve preservar todas as posições de textura, incluindo reservas.");
        }
    }

    public static void Write(ClothProject project, string output)
    {
        Write(Describe(project), output);
    }

    public static void Write(ClothingIdentityDocument document, string output)
    {
        Validate(document);
        File.WriteAllText(Path.Combine(output, FileName), JsonSerializer.Serialize(document, JsonOptions));
        File.WriteAllText(Path.Combine(output, "clothing-ids.client.lua"), ClothingIdentityRuntime.Client);
        File.WriteAllText(Path.Combine(output, "CLOTHING-IDS.md"), ClothingIdentityRuntime.IntegrationGuide);
    }

    public static void Restore(string root, List<ClothItem> items)
    {
        var path = Path.Combine(root, FileName);
        if (!File.Exists(path)) return;
        var document = Read(path);
        var byFile = document.Entries.Where(entry => entry.ModelFile is not null)
            .ToDictionary(entry => entry.ModelFile!, StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (!byFile.TryGetValue(Path.GetFileName(item.OriginalModelFileName ?? item.ModelPath), out var entry)) continue;
            item.Id = entry.Id;
            item.SourceIndex = item.ExportPosition = entry.Index;
            var textures = item.TexturePaths.ToDictionary(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase);
            var paths = new List<string>(); var ids = new List<Guid>(); var names = new List<string>();
            foreach (var texture in entry.Textures.OrderBy(texture => texture.Index))
            {
                if (texture.Inherited) continue;
                if (texture.Available && (texture.File is null || !textures.ContainsKey(texture.File)))
                    throw new InvalidDataException("Textura declarada no mapa de IDs ausente: " + texture.File);
                paths.Add(texture.Available ? textures[texture.File!] : "");
                ids.Add(texture.Id); names.Add(texture.File ?? "(dummy)");
            }
            item.TexturePaths = paths; item.TextureIds = ids; item.OriginalTextureFileNames = names;
        }
        var components = new[] { "head", "berd", "hair", "uppr", "lowr", "hand", "feet", "teef", "accs", "task", "decl", "jbib" };
        var props = new Dictionary<int, string> { [0] = "p_head", [1] = "p_eyes", [2] = "p_ears", [3] = "p_mouth", [6] = "p_lwrist", [7] = "p_rwrist" };
        foreach (var entry in document.Entries.Where(entry => !entry.Available))
        {
            if (items.Any(item => item.Id == entry.Id)) continue;
            var code = entry.Kind == "prop" ? props.GetValueOrDefault(entry.Slot) : components.ElementAtOrDefault(entry.Slot);
            if (code is null) throw new InvalidDataException("Slot inválido no mapa de IDs.");
            items.Add(new ClothItem { Id = entry.Id, Name = entry.Name, Gender = entry.Gender, IsDummy = true,
                Category = ClothCatalog.ResolveType(code + "_000.ydd").Category, SourceIndex = entry.Index, ExportPosition = entry.Index });
        }
    }

    public static ClothingIdentityDocument Read(string path)
    {
        var document = JsonSerializer.Deserialize<ClothingIdentityDocument>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("The clothing identity map is empty.");
        Validate(document);
        return document;
    }
}
