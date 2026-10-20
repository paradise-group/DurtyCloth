using System.Globalization;
using System.Windows.Data;

namespace FooxClothStudio;

public sealed class ClothLabelConverter : IValueConverter
{
    public static string Label(string category)
    {
        var code = category.Split('[').Last().TrimEnd(']');
        var labels = new Dictionary<string, string>
        {
            ["teef"] = "Accessories", ["task"] = "Armor", ["hand"] = "Bags", ["uppr"] = "Torso",
            ["decl"] = "Decals", ["hair"] = "Hair", ["lowr"] = "Legs", ["berd"] = "Mask",
            ["p_eyes"] = "Glasses", ["p_head"] = "Hats", ["feet"] = "Shoes", ["jbib"] = "Top",
            ["accs"] = "Undershirt", ["head"] = "Head", ["p_ears"] = "Ears", ["p_mouth"] = "Mouth",
            ["p_lwrist"] = "Left wrist", ["p_rwrist"] = "Right wrist"
        };
        return labels.TryGetValue(code, out var label) ? $"{label} [{code}]" : "Other";
    }
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is CollectionViewGroup group ? GroupLabel(group.Name as string ?? "") + " (" + group.Items.OfType<ClothItem>().Count(item => item.Gender == "Masculino") + "M " + group.Items.OfType<ClothItem>().Count(item => item.Gender == "Feminino") + "F)" :
        parameter as string == "Detailed" ? DetailedLabel(value as string ?? "") : Label(value as string ?? "");
    private static string GroupLabel(string category)
    {
        var description = category.Split('[').Last().TrimEnd(']') switch { "task" => " (Vests)", "teef" => " (Parachute, Scuba tank etc.)", "hand" => " (Backpacks)", "uppr" => " (Torso & Gloves)", "decl" => " (Shirt printings, Emblems etc.)", "hair" => " (Ped head hairs)", "lowr" => " (Pants/Trousers..)", "berd" => " (Big masks)", "p_head" => " (Hats, Helmets, Masks, Ear phones etc.)", "feet" => " (Sneakers/High heels etc.)", "jbib" => " (Auxiliary parts for torso)", "accs" => " (Shirts to wear with top/jbib)", _ => "" };
        return Label(category) + description;
    }
    private static readonly string[] ComponentCodes = ["head", "berd", "hair", "uppr", "lowr", "hand", "feet", "teef", "accs", "task", "decl", "jbib"];
    private static readonly string[] PropCodes = ["p_head", "p_eyes", "p_ears", "p_mouth", "p_lwrist", "p_rwrist"];
    public static IEnumerable<string> TypeChoices(bool props) => (props ? PropCodes : ComponentCodes)
        .Select(code => ClothCatalog.Categories.Single(category => category.EndsWith("[" + code + "]", StringComparison.Ordinal)));
    public static string DetailedLabel(string category)
    {
        var code = category.Split('[').Last().TrimEnd(']');
        var index = Array.IndexOf(ComponentCodes, code);
        if (index < 0) { var prop = Array.IndexOf(PropCodes, code); index = prop < 0 ? -1 : new[] { 0, 1, 2, 3, 6, 7 }[prop]; }
        var description = code switch { "berd" => " (Big masks)", "jbib" => " (Auxiliary parts for torso)", "accs" => " (Shirts to wear with top/jbib)", _ => "" };
        return (index < 0 ? "" : index + "  ") + Label(category) + description;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
