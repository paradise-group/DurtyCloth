using System.IO;
using System.Text.Json;

namespace FooxClothStudio;

public sealed record ServerPhotoFilterResult(int ServerItems, int MatchedItems, int Textures, int UnmatchedItems);

public sealed class ServerPhotoCatalog
{
    private readonly Dictionary<string, HashSet<int>> _entries = new(StringComparer.Ordinal);
    public int Count => _entries.Count;

    private static string Address(string model, string kind, int slot, string collection, int index) =>
        $"{model.ToLowerInvariant()}|{kind}|{slot}|{collection.ToLowerInvariant()}|{index}";

    public static ServerPhotoCatalog Parse(JsonElement root)
    {
        if (root.GetProperty("formatVersion").GetInt32() != 1 || !root.GetProperty("complete").GetBoolean()
            || root.GetProperty("source").GetString() != "touz-server-clothing")
            throw new InvalidDataException("O FiveM devolveu um catálogo incompleto ou incompatível. Atualize o skinshop e tente novamente.");
        var models = root.GetProperty("models").EnumerateArray().Select(value => value.GetString()).ToHashSet();
        if (!models.SetEquals(new[] { "mp_m_freemode_01", "mp_f_freemode_01" }))
            throw new InvalidDataException("O catálogo precisa de incluir os modelos masculino e feminino.");
        var entries = root.GetProperty("entries");
        if (entries.GetArrayLength() > 32768) throw new InvalidDataException("Catálogo de roupas demasiado grande.");
        var catalog = new ServerPhotoCatalog();
        foreach (var entry in entries.EnumerateArray())
        {
            var model = entry.GetProperty("model").GetString()!;
            var kind = entry.GetProperty("kind").GetString()!;
            var slot = entry.GetProperty("slot").GetInt32();
            var collection = entry.GetProperty("collection").GetString()!;
            var index = entry.GetProperty("index").GetInt32();
            var textureCount = entry.GetProperty("textureCount").GetInt32();
            var textures = entry.GetProperty("textures").EnumerateArray().Select(value => value.GetInt32()).ToHashSet();
            var validSlot = kind == "component" && slot is >= 1 and <= 11 || kind == "prop" && slot is 0 or 1 or 2 or 6 or 7;
            if (!models.Contains(model) || !validSlot || collection is null || collection.Length > 128 || index is < 0 or > 65535
                || textureCount <= 0 || textures.Count == 0 || textures.Any(texture => texture < 0 || texture >= textureCount))
                throw new InvalidDataException("O catálogo contém uma referência de roupa inválida.");
            var address = Address(model, kind, slot, collection, index);
            if (catalog._entries.TryGetValue(address, out var previous)) previous.UnionWith(textures);
            else catalog._entries.Add(address, textures);
        }
        if (catalog.Count == 0) throw new InvalidDataException("O catálogo do servidor não contém peças disponíveis.");
        return catalog;
    }

    public ServerPhotoFilterResult Filter(ClothProject project)
    {
        var map = OriginalResourceExporter.Describe(project).Entries.ToDictionary(entry => entry.Id);
        var selected = new List<ClothItem>();
        var matched = new HashSet<string>();
        foreach (var item in project.Items)
        {
            var entry = map[item.Id];
            var address = Address(entry.Model, entry.Kind, entry.Slot, entry.Collection, entry.Index);
            if (!_entries.TryGetValue(address, out var allowed)) continue;
            var indices = entry.Textures.Where(texture => !texture.Inherited && allowed.Contains(texture.Index))
                .Select(texture => texture.Id).ToHashSet();
            var positions = Enumerable.Range(0, item.TextureIds.Count).Where(index => indices.Contains(item.TextureIds[index])).ToArray();
            if (positions.Length == 0) continue;
            selected.Add(new ClothItem
            {
                Id = item.Id, Name = item.Name, Gender = item.Gender, Category = item.Category,
                ModelPath = item.ModelPath, OriginalModelFileName = item.OriginalModelFileName, SourceIndex = item.SourceIndex,
                TexturePaths = positions.Select(index => item.TexturePaths[index]).ToList(),
                TextureIds = positions.Select(index => item.TextureIds[index]).ToList(),
                OriginalTextureFileNames = positions.Select(index => item.OriginalTextureFileNames[index]).ToList()
            });
            matched.Add(address);
        }
        if (selected.Count == 0)
            throw new InvalidDataException("Nenhuma peça do catálogo foi encontrada no GTA instalado. Verifique a instalação e o catálogo selecionados.");
        project.Items = selected;
        return new ServerPhotoFilterResult(Count, selected.Count, selected.Sum(item => item.TexturePaths.Count), Count - matched.Count);
    }
}
