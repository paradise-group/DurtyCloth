using System.IO;
using System.Xml.Linq;
using System.Globalization;

namespace FooxClothStudio;

public static class FirstPersonMetadata
{
    public static string TypeOf(string filename) => filename.EndsWith("_first_person.meta", StringComparison.OrdinalIgnoreCase)
        ? "PED_FIRST_PERSON_ASSET_DATA" : filename.EndsWith("_first_person_alternates.meta", StringComparison.OrdinalIgnoreCase)
        ? "PED_FIRST_PERSON_ALTERNATE_DATA" : filename.EndsWith("_overlays.xml", StringComparison.OrdinalIgnoreCase) ? "PED_OVERLAY_FILE"
        : filename.EndsWith("_tattoo.meta", StringComparison.OrdinalIgnoreCase) ? "TATTOO_SHOP_DLC_FILE"
        : filename == "pedalternatevariations.meta" ? "ALTERNATE_VARIATIONS_FILE" : "SHOP_PED_APPAREL_META_FILE";
    private static readonly string[] Codes = ["head", "berd", "hair", "uppr", "lowr", "hand", "feet", "teef", "accs", "task", "decl", "jbib"];
    private static readonly string[] PropCodes = ["p_head", "p_eyes", "p_ears", "p_mouth", "p_lwrist", "p_rwrist"];
    private static readonly string[] Anchors = ["ANCHOR_HEAD", "ANCHOR_EYES", "ANCHOR_EARS", "ANCHOR_MOUTH", "ANCHOR_LEFT_WRIST", "ANCHOR_RIGHT_WRIST"];
    private static XElement Value(string name, object value) => new(name, new XAttribute("value", value));
    public static string[] Write(string output, string ped, string collection, string fullCollection,
        Dictionary<int, List<(ClothItem Item, int TextureCount)>> components,
        Dictionary<int, List<(ClothItem Item, int TextureCount)>> props)
    {
        var objects = new List<XElement>(); var alternates = new List<XElement>();
        void Add(int slot, int index, ClothItem item, bool prop)
        {
            if (item.IsDummy) return;
            var stem = (prop ? ped + "_p_" + collection : fullCollection) + "^" + (prop ? PropCodes[slot] : Codes[slot]) + "_" + index.ToString("D3");
            if (item.HasFirstPersonModel && item.AlternateModelPaths.Count > 0 && !prop)
            {
                var suffix = (item.OriginalModelFileName ?? item.ModelPath).EndsWith("_r.ydd", StringComparison.OrdinalIgnoreCase) ? "_r" : "_u";
                alternates.Add(new XElement("Item", new XElement("assetName", stem + suffix), Value("alternate", 1)));
            }
            var effects = item.TextureEffects.Select((effect, texture) => (effect, texture))
                .Where(x => x.texture < item.TexturePaths.Count && !string.IsNullOrWhiteSpace(item.TexturePaths[x.texture]) && !string.IsNullOrWhiteSpace(x.effect.TimeCycleModifier)).ToArray();
            if (effects.Length == 0 && string.IsNullOrWhiteSpace(item.FirstPersonMaskModel)) return;
            foreach (var (effect, _) in effects)
                if (!float.IsFinite(effect.Intensity) || effect.Intensity is < 0.1f or > 1) throw new InvalidDataException("TimeCycle intensity must be between 0.1 and 1.");
            if (!float.IsFinite(item.MaskOffsetX) || !float.IsFinite(item.MaskOffsetY) || !float.IsFinite(item.MaskOffsetZ))
                throw new InvalidDataException("Mask model offsets must be finite.");
            objects.Add(new XElement("Item", Value("localIndex", index),
                new XElement("eCompType", prop ? "PV_COMP_MAX" : "PV_COMP_" + Codes[slot].ToUpperInvariant()),
                new XElement("eAnchorPoint", prop ? Anchors[slot] : "ANCHOR_MAX"),
                new XElement("dlcHash", collection), new XElement("timeCycleMods", effects.Select(x => new XElement("Item",
                    Value("textureId", x.texture), new XElement("timeCycleMod", x.effect.TimeCycleModifier), Value("intensity", x.effect.Intensity.ToString(CultureInfo.InvariantCulture))))),
                new XElement("audioId", item.AudioPreset), Value("flags", 0), new XElement("fpsPropModel", item.FirstPersonMaskModel),
                new XElement("fpsPropModelOffset", new XAttribute("x", item.MaskOffsetX.ToString(CultureInfo.InvariantCulture)), new XAttribute("y", item.MaskOffsetY.ToString(CultureInfo.InvariantCulture)), new XAttribute("z", item.MaskOffsetZ.ToString(CultureInfo.InvariantCulture))),
                Value("seWeather", 1), Value("seEnvironment", 1), Value("seDamage", 1)));
        }
        foreach (var (slot, entries) in components) for (var index = 0; index < entries.Count; index++) Add(slot, index, entries[index].Item, false);
        foreach (var (slot, entries) in props) for (var index = 0; index < entries.Count; index++) Add(slot, index, entries[index].Item, true);
        var files = new List<string>();
        if (objects.Count > 0)
        {
            var name = fullCollection + "_first_person.meta";
            new XDocument(new XElement("FirstPersonAssetDataManager", new XElement("peds", new XElement("Item", new XElement("name", ped), new XElement("objects", objects))))).Save(Path.Combine(output, name)); files.Add(name);
        }
        if (alternates.Count > 0)
        {
            var name = fullCollection + "_first_person_alternates.meta";
            new XDocument(new XElement("FirstPersonAlternateData", new XElement("alternates", alternates))).Save(Path.Combine(output, name)); files.Add(name);
        }
        return files.ToArray();
    }
}
