using System.IO;
using System.Xml;
using System.Xml.Linq;
using CodeWalker.GameFiles;

namespace FooxClothStudio;

public static class CreatureMetadata
{
    private static XElement Value(string name, object value) => new(name, new XAttribute("value", value));
    public static bool Write(string path, IEnumerable<(int Index, ClothItem Item)> hats,
        IEnumerable<(int Index, ClothItem Item)> shoes)
    {
        var hatItems = hats.Where(x => !x.Item.IsDummy && x.Item.HairOption != "Show hairs").ToArray();
        var shoeItems = shoes.Where(x => !x.Item.IsDummy && x.Item.HeelHeight != 0).ToArray();
        if (hatItems.Length == 0 && shoeItems.Length == 0) return false;
        XElement Expression(string prefix, int slot, int index, int expression, int bone) =>
            new("Item", Value(prefix + "ID", slot), Value(prefix + "VarIndex", index),
                Value(prefix + "ExpressionIndex", expression < 0 ? uint.MaxValue : (uint)expression), new XElement("tracks", "33"),
                new XElement("ids", bone), new XElement("types", "2"), new XElement("components", "1"));
        var propEntries = new List<XElement>();
        if (hatItems.Length > 0)
        {
            propEntries.Add(Expression("pedProp", 0, -1, -1, 13201));
            propEntries.AddRange(hatItems.Select(x => Expression("pedProp", 0, x.Index, 0, 13201)));
        }
        var root = new XElement("CCreatureMetaData",
            new XElement("shaderVariableComponents", new XAttribute("itemType", "CShaderVariableComponent")),
            new XElement("pedPropExpressions", new XAttribute("itemType", "CPedPropExpressionData"), propEntries),
            new XElement("pedCompExpressions", new XAttribute("itemType", "CPedCompExpressionData"),
                shoeItems.Select(x => Expression("pedComp", 6, x.Index, 4, 28462))));
        var document = new XmlDocument(); document.LoadXml(root.ToString());
        var bytes = ResourceBuilder.Build(XmlMeta.GetMeta(document), 2);
        var check = new YmtFile(); check.Load(bytes);
        if (check.Meta is null || !MetaXml.GetXml(check.Meta).Contains("CCreatureMetaData"))
            throw new InvalidDataException("Creature metadata did not reopen.");
        File.WriteAllBytes(path, bytes); return true;
    }
    public static float HairValue(ClothItem item) => item.HairOption switch
    {
        "Show hairs" => 0, "Cut hairs" => -0.5f, "Remove hairs" => -1,
        "Custom cut hairs" when float.IsFinite(item.HairCutValue) && item.HairCutValue is >= -1 and <= 0 => item.HairCutValue,
        _ => throw new InvalidDataException("Invalid hair option or cut amount.")
    };
}
