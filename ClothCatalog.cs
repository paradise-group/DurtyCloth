using System.IO;
using System.Text.RegularExpressions;

namespace FooxClothStudio;

public static partial class ClothCatalog
{
    public static readonly string[] Categories =
    [
        "Acessórios [teef]", "Colete [task]", "Mochila [hand]",
        "Corpo [uppr]", "Decal [decl]", "Cabelo [hair]",
        "Calça [lowr]", "Máscara [berd]", "Óculos [p_eyes]",
        "Chapéu [p_head]", "Sapato [feet]", "Blusa [jbib]",
        "Camisa [accs]", "Cabeça [head]", "Brinco [p_ears]",
        "Boca [p_mouth]", "Pulso esquerdo [p_lwrist]", "Pulso direito [p_rwrist]", "Outros"
    ];

    private static readonly Dictionary<string, string> CategoryByCode = new(StringComparer.OrdinalIgnoreCase)
    {
        ["teef"] = Categories[0], ["task"] = Categories[1], ["hand"] = Categories[2],
        ["uppr"] = Categories[3], ["decl"] = Categories[4], ["hair"] = Categories[5],
        ["lowr"] = Categories[6], ["berd"] = Categories[7], ["p_eyes"] = Categories[8],
        ["p_head"] = Categories[9], ["feet"] = Categories[10], ["jbib"] = Categories[11],
        ["accs"] = Categories[12], ["head"] = Categories[13],
        ["p_ears"] = Categories[14], ["p_mouth"] = Categories[15],
        ["p_lwrist"] = Categories[16], ["p_rwrist"] = Categories[17]
    };

    [GeneratedRegex(@"^(?<type>p_[a-z]+|[a-z]{4})_(?:diff_)?(?<index>\d{3,})(?=_|$)", RegexOptions.IgnoreCase)]
    private static partial Regex ClothingName();

    public static string ResolveGender(string path)
    {
        var parts = path.Replace('/', '\\').ToLowerInvariant().Split('\\', StringSplitOptions.RemoveEmptyEntries);
        // The explicit filename wins over enclosing folders; otherwise use the closest known folder.
        foreach (var part in parts.Reverse())
        {
            if (part.Contains("mp_m_freemode_01") || part is "male" or "masculino") return "Masculino";
            if (part.Contains("mp_f_freemode_01") || part is "female" or "feminino") return "Feminino";
        }
        return "Indefinido";
    }

    public static (string Category, int? Index) ResolveType(string path)
    {
        var stem = Path.GetFileNameWithoutExtension(path).Split('^').Last();
        var match = ClothingName().Match(stem);
        if (!match.Success) return ("Outros", null);
        var code = match.Groups["type"].Value;
        var category = CategoryByCode.GetValueOrDefault(code, "Outros");
        return int.TryParse(match.Groups["index"].Value, out var index) ? (category, index) : ("Outros", null);
    }

    public static List<ClothItem> Import(IEnumerable<string> paths)
    {
        var files = paths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var models = files.Where(path => Path.GetExtension(path).Equals(".ydd", StringComparison.OrdinalIgnoreCase)).ToArray();
        var textures = files.Where(path => Path.GetExtension(path).Equals(".ytd", StringComparison.OrdinalIgnoreCase)).ToArray();
        var physics = files.Where(path => Path.GetExtension(path).Equals(".yld", StringComparison.OrdinalIgnoreCase)).ToArray();
        var result = new List<ClothItem>();
        var mainModels = models.Where(path => !Regex.IsMatch(Path.GetFileNameWithoutExtension(path), @"_(?:u|r)_[1-3]$", RegexOptions.IgnoreCase)).ToArray();
        bool MatchesTexture(string model, string texture)
        {
            var type = ResolveType(model);
            return SameCollection(model, texture) && SameGender(model, texture) &&
                (type.Index is not null ? ResolveType(texture) == type : Path.GetFileNameWithoutExtension(texture).StartsWith(Path.GetFileNameWithoutExtension(model), StringComparison.OrdinalIgnoreCase));
        }
        bool MatchesPhysics(string model, string path)
        {
            var stem = Path.GetFileNameWithoutExtension(model);
            var name = Path.GetFileNameWithoutExtension(path);
            return SameCollection(model, path) && SameGender(model, path) &&
                (string.Equals(name, stem, StringComparison.OrdinalIgnoreCase) || string.Equals(name, Regex.Replace(stem, @"_(?:u|r)$", "", RegexOptions.IgnoreCase), StringComparison.OrdinalIgnoreCase));
        }
        bool MatchesAlternate(string model, string path) => SameGender(model, path) &&
            Regex.IsMatch(Path.GetFileNameWithoutExtension(path), "^" + Regex.Escape(Path.GetFileNameWithoutExtension(model)) + @"_[1-3]$", RegexOptions.IgnoreCase);
        foreach (var model in mainModels)
        {
            var (category, index) = ResolveType(model);
            var folder = Path.GetDirectoryName(model);
            var modelStem = Path.GetFileNameWithoutExtension(model);
            var matchingTextures = textures.Where(path =>
                MatchesTexture(model, path) &&
                (string.Equals(Path.GetDirectoryName(path), folder, StringComparison.OrdinalIgnoreCase) ||
                    !mainModels.Any(other => !string.Equals(other, model, StringComparison.OrdinalIgnoreCase) && MatchesTexture(other, path))))
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToList();
            var physicsStem = Regex.Replace(modelStem, @"_(?:u|r)$", "", RegexOptions.IgnoreCase);
            var matchingPhysics = physics.FirstOrDefault(path =>
                string.Equals(Path.GetDirectoryName(path), folder, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Path.GetFileNameWithoutExtension(path), modelStem, StringComparison.OrdinalIgnoreCase)) ??
                physics.FirstOrDefault(path =>
                    string.Equals(Path.GetDirectoryName(path), folder, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(Path.GetFileNameWithoutExtension(path), physicsStem, StringComparison.OrdinalIgnoreCase));
            if (matchingPhysics is null)
            {
                var outside = physics.Where(path => MatchesPhysics(model, path) &&
                    !mainModels.Any(other => !string.Equals(other, model, StringComparison.OrdinalIgnoreCase) && MatchesPhysics(other, path))).ToArray();
                if (outside.Length == 1) matchingPhysics = outside[0];
            }
            var alternates = models.Where(path => MatchesAlternate(model, path) &&
                    (string.Equals(Path.GetDirectoryName(path), folder, StringComparison.OrdinalIgnoreCase) ||
                        !mainModels.Any(other => !string.Equals(other, model, StringComparison.OrdinalIgnoreCase) && MatchesAlternate(other, path))))
                .GroupBy(path => Path.GetFileNameWithoutExtension(path)[^1])
                .Select(group => group.FirstOrDefault(path => string.Equals(Path.GetDirectoryName(path), folder, StringComparison.OrdinalIgnoreCase)) ?? (group.Count() == 1 ? group.Single() : null))
                .Where(path => path is not null).Select(path => path!).OrderBy(Path.GetFileName).ToList();
            for (var alternate = 0; alternate < alternates.Count; alternate++)
                if (Path.GetFileNameWithoutExtension(alternates[alternate])[^1] != (char)('1' + alternate))
                    throw new InvalidDataException($"Missing alternate _{alternate + 1} for {modelStem}. Import consecutive alternate models together to preserve their GTA indices.");
            result.Add(new ClothItem
            {
                Name = modelStem,
                Gender = ResolveGender(model),
                Category = category,
                SourceIndex = index,
                ModelPath = model,
                OriginalModelFileName = Path.GetFileName(model),
                AlternateModelPaths = alternates,
                TexturePaths = matchingTextures,
                OriginalTextureFileNames = matchingTextures.Select(Path.GetFileName).ToList()!,
                PhysicsPath = matchingPhysics
            });
        }
        return result;
    }

    public static string[] WithSiblingCompanions(IEnumerable<string> selectedPaths)
    {
        var selected = selectedPaths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var result = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);
        foreach (var group in selected.Where(path => Path.GetExtension(path).Equals(".ydd",
                     StringComparison.OrdinalIgnoreCase)).GroupBy(Path.GetDirectoryName,
                     StringComparer.OrdinalIgnoreCase))
        {
            if (group.Key is null) continue;
            var mainStems = group.Select(path => Path.GetFileNameWithoutExtension(path) ?? "")
                .Where(stem => stem.Length > 0 &&
                    !Regex.IsMatch(stem, @"_(?:u|r)_[1-3]$", RegexOptions.IgnoreCase))
                .ToArray();
            foreach (var path in Directory.EnumerateFiles(group.Key))
            {
                var extension = Path.GetExtension(path);
                if (extension.Equals(".ytd", StringComparison.OrdinalIgnoreCase) ||
                    extension.Equals(".yld", StringComparison.OrdinalIgnoreCase))
                    result.Add(path);
                else if (extension.Equals(".ydd", StringComparison.OrdinalIgnoreCase) &&
                         mainStems.Any(stem => Regex.IsMatch(Path.GetFileNameWithoutExtension(path) ?? "",
                             "^" + Regex.Escape(stem) + @"_[1-3]$", RegexOptions.IgnoreCase)))
                    result.Add(path);
            }
        }
        return result.ToArray();
    }

    public static bool SameCollection(string model, string texture)
    {
        var modelName = Path.GetFileNameWithoutExtension(model);
        var textureName = Path.GetFileNameWithoutExtension(texture);
        var modelCaret = modelName.IndexOf('^');
        var textureCaret = textureName.IndexOf('^');
        return modelCaret < 0 || textureCaret < 0 ||
            string.Equals(modelName[..modelCaret], textureName[..textureCaret],
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool SameGender(string model, string texture)
    {
        var modelGender = ResolveGender(model);
        var textureGender = ResolveGender(texture);
        return modelGender == "Indefinido" || textureGender == "Indefinido" ||
            modelGender == textureGender;
    }
}
