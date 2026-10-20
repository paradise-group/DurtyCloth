using System.IO;
using System.Xml.Linq;

namespace FooxClothStudio;

public static class HairVariations
{
    public static bool Write(string path, ClothProject project, IReadOnlyDictionary<string, string> collections)
    {
        var document = File.Exists(path) ? XDocument.Load(path) : new XDocument(new XElement("CAlternateVariations", new XElement("peds")));
        var peds = document.Root!.Element("peds")!;
        var wrote = false;
        foreach (var (gender, collection) in collections)
        {
            var groups = project.Items.Where(x => x.Gender == gender)
                .GroupBy(x => x.Category).ToDictionary(g => g.Key, g => g.OrderBy(x => x.ExportPosition ?? x.SourceIndex).ThenBy(x => x.Name).ToArray());
            var hair = groups.FirstOrDefault(g => g.Key.EndsWith("[hair]", StringComparison.Ordinal)).Value ?? [];
            var requested = groups.Where(g => !g.Key.Contains("[p_", StringComparison.Ordinal) && !g.Key.EndsWith("[hair]", StringComparison.Ordinal))
                .SelectMany(g => g.Value.Select((item, index) => (Item: item, Index: index)))
                .Where(x => !x.Item.IsDummy && (x.Item.HairOption != "Show hairs" || x.Item.HoodieHairs)).ToArray();
            if (requested.Length == 0) continue;
            if (hair.Length == 0) throw new InvalidDataException("Hair options on components require hair drawables with matching alternate models in this project.");
            var pedName = gender == "Masculino" ? "mp_m_freemode_01" : "mp_f_freemode_01";
            var ped = peds.Elements("Item").FirstOrDefault(x => x.Element("name")?.Value == pedName);
            if (ped is null) { ped = new XElement("Item", new XElement("name", pedName), new XElement("switches")); peds.Add(ped); }
            var switches = ped.Element("switches")!;
            for (var index = 0; index < hair.Length; index++)
            {
                if (hair[index].IsDummy) continue;
                foreach (var group in requested.GroupBy(x => x.Item.HoodieHairs ? 2 : x.Item.HairOption == "Remove hairs" ? 3 : 1))
                {
                    if (hair[index].AlternateModelPaths.Count < group.Key)
                        throw new InvalidDataException($"Hair '{hair[index].Name}' needs alternate _{group.Key} for the selected hair/hoodie option.");
                    switches.Add(new XElement("Item", new XElement("dlcNameHash", collection),
                        Value("component", 2), Value("index", index), Value("alt", group.Key),
                        new XElement("sourceAssets", group.Select(x => new XElement("Item", new XElement("dlcNameHash", collection),
                            Value("component", Slot(x.Item.Category)), Value("index", x.Index))))));
                    wrote = true;
                }
            }
        }
        if (wrote) document.Save(path);
        return wrote;
    }
    private static XElement Value(string name, int value) => new(name, new XAttribute("value", value));
    private static int Slot(string category) => Array.IndexOf(new[] { "head", "berd", "hair", "uppr", "lowr", "hand", "feet", "teef", "accs", "task", "decl", "jbib" }, category.Split('[').Last().TrimEnd(']'));
}
