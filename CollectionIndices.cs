using System.IO;
using System.Text.Json;

namespace FooxClothStudio;

public sealed record CollectionIndexEntry(string Gender, string Slot, string Collection, int LocalIndex,
    Guid ItemId, string Name, string Label, int TextureCount, bool IsDummy);

public static class CollectionIndices
{
    public const int DrawablesPerSlot = 128;
    private static readonly string[] Components = ["head", "berd", "hair", "uppr", "lowr", "hand", "feet", "teef", "accs", "task", "decl", "jbib"];
    private static readonly string[] Props = ["p_head", "p_eyes", "p_ears", "p_mouth", "p_lwrist", "p_rwrist"];
    private static readonly int[] Anchors = [0, 1, 2, 3, 6, 7];

    public static string DlcName(ClothProject project)
    {
        if (!string.IsNullOrWhiteSpace(project.DlcName)) return project.DlcName;
        var name = new string(project.Name.ToLowerInvariant().Where(char.IsAsciiLetterOrDigit).Take(14).ToArray());
        return name.Length == 0 ? "roupas" : name;
    }

    public static string CodeOf(string category)
    {
        var start = category.LastIndexOf('[');
        return start < 0 ? "" : category[(start + 1)..].TrimEnd(']');
    }

    public static IOrderedEnumerable<ClothItem> Ordered(IEnumerable<ClothItem> items) =>
        items.OrderBy(item => item.ExportPosition ?? item.SourceIndex).ThenBy(item => item.Name);

    public static List<CollectionIndexEntry> Current(ClothProject project)
    {
        var entries = new List<CollectionIndexEntry>();
        foreach (var group in project.Items.GroupBy(item => (item.Gender, Code: CodeOf(item.Category))))
        {
            var component = Array.IndexOf(Components, group.Key.Code);
            var prop = Array.IndexOf(Props, group.Key.Code);
            var slot = component >= 0 ? component.ToString() : prop >= 0 ? "p" + Anchors[prop] : group.Key.Code;
            var collection = project.ReplaceDlcCollection
                ? group.Key.Gender == "Feminino" ? project.ReplacementFemaleCollection : project.ReplacementMaleCollection
                : (project.RemoveDefaultCollectionPrefix ? "" : group.Key.Gender == "Feminino" ? "mp_f_" : "mp_m_") + DlcName(project);
            var index = 0;
            foreach (var item in Ordered(group))
                entries.Add(new(item.Gender, slot, collection, index++, item.Id, item.Name,
                    item.ShopTextLabel, item.IsDummy ? 0 : item.TexturePaths.Count, item.IsDummy));
        }
        return entries;
    }

    public static bool IsPublished(ClothProject project, ClothItem item) =>
        project.PublishedCollection?.Drawables.Any(entry => entry.ItemId == item.Id) == true;

    public static void Append(ClothProject project, ClothItem item)
    {
        var group = project.Items.Where(other => other.Id != item.Id && other.Gender == item.Gender && CodeOf(other.Category) == CodeOf(item.Category)).ToArray();
        item.ExportPosition = group.Length == 0 ? 0 : checked(group.Max(other => other.ExportPosition ?? other.SourceIndex ?? 0) + 1);
    }

    public static void Publish(ClothProject project)
    {
        var errors = Validate(project);
        if (errors.Count > 0) throw new InvalidDataException(string.Join("\n", errors));
        if (project.Items.Count == 0 || project.Items.Any(item => item.Gender is not ("Masculino" or "Feminino") ||
            !Components.Contains(CodeOf(item.Category)) && !Props.Contains(CodeOf(item.Category))))
            throw new InvalidDataException("Defina gênero e slot válidos e adicione peças antes de publicar.");
        project.DlcName = DlcName(project);
        if (!System.Text.RegularExpressions.Regex.IsMatch(project.DlcName, "^[a-z0-9_]{1,24}$"))
            throw new InvalidDataException("DLC name deve conter 1–24 letras minúsculas, números ou _.");
        var current = Current(project);
        foreach (var entry in current) project.Items.Single(item => item.Id == entry.ItemId).ExportPosition = entry.LocalIndex;
        project.PublishedCollection = new()
        {
            DlcName = project.DlcName, RemoveDefaultCollectionPrefix = project.RemoveDefaultCollectionPrefix,
            ReplaceDlcCollection = project.ReplaceDlcCollection, ReplacementResource = project.ReplacementResource,
            ReplacementMaleCollection = project.ReplacementMaleCollection, ReplacementFemaleCollection = project.ReplacementFemaleCollection,
            Drawables = current.Select(entry => new PublishedDrawable(entry.Gender, entry.Slot, entry.Collection, entry.LocalIndex, entry.ItemId)).ToList()
        };
    }

    public static List<string> Validate(ClothProject project)
    {
        var errors = new List<string>();
        if (project.Items.Select(item => item.Id).Distinct().Count() != project.Items.Count)
            errors.Add("Erro: identificadores de drawable duplicados.");
        if (project.PublishedCollection is not { } published) return errors;
        if (project.DlcName != published.DlcName || project.RemoveDefaultCollectionPrefix != published.RemoveDefaultCollectionPrefix ||
            project.ReplaceDlcCollection != published.ReplaceDlcCollection ||
            project.ReplaceDlcCollection && (project.ReplacementResource != published.ReplacementResource ||
                project.ReplacementMaleCollection != published.ReplacementMaleCollection || project.ReplacementFemaleCollection != published.ReplacementFemaleCollection))
            errors.Add("Erro: DLC name/prefixo da collection publicada foi alterado. Crie uma nova collection.");
        var current = Current(project).ToDictionary(entry => (entry.Gender, entry.Slot, entry.LocalIndex));
        foreach (var entry in published.Drawables)
            if (!current.TryGetValue((entry.Gender, entry.Slot, entry.LocalIndex), out var actual) ||
                actual.ItemId != entry.ItemId || actual.Collection != entry.Collection)
                errors.Add($"Erro: índice publicado alterado: {entry.Gender}/{entry.Slot}/{entry.Collection}/{entry.LocalIndex}. Preserve a peça ou use Replace with Dummy.");
        return errors;
    }

    public static List<string> Warnings(ClothProject project) => Current(project)
        .GroupBy(entry => (entry.Gender, entry.Slot, entry.Collection)).Where(group => group.Count() >= DrawablesPerSlot)
        .Select(group => $"Aviso: {group.Key.Gender}/{group.Key.Slot}/{group.Key.Collection} tem {group.Count()} drawables (limite recomendado: {DrawablesPerSlot}). Abra uma nova collection para as próximas peças.").ToList();

    public static void EnsureValid(ClothProject project)
    {
        var errors = Validate(project);
        if (errors.Count > 0) throw new InvalidDataException(string.Join("\n", errors));
    }

    public static void Write(ClothProject project, string output) => File.WriteAllText(Path.Combine(output, "collection-index.json"),
        JsonSerializer.Serialize(new { FormatVersion = 1, DlcName = DlcName(project), Entries = Current(project) },
            new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
}
