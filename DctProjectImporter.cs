using System.IO;
using System.Text.Json;
using MessagePack;

namespace FooxClothStudio;

public static class DctProjectImporter
{
    private static readonly IReadOnlyDictionary<int, string> CategoriesByType = new Dictionary<int, string>
    {
        [0] = ClothCatalog.Categories[13],
        [1] = ClothCatalog.Categories[7],  [2] = ClothCatalog.Categories[5],
        [3] = ClothCatalog.Categories[3],  [4] = ClothCatalog.Categories[6],
        [5] = ClothCatalog.Categories[2],  [6] = ClothCatalog.Categories[10],
        [7] = ClothCatalog.Categories[0],  [8] = ClothCatalog.Categories[12],
        [9] = ClothCatalog.Categories[1],  [10] = ClothCatalog.Categories[4],
        [11] = ClothCatalog.Categories[11], [12] = ClothCatalog.Categories[9],
        [13] = ClothCatalog.Categories[8]
    };

    public static ClothProject Import(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length > 64 * 1024 * 1024)
            throw new InvalidDataException("Projeto ausente ou maior que 64 MiB.");
        var options = MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray);
        var json = MessagePackSerializer.ConvertToJson(File.ReadAllBytes(file.FullName), options);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.TryGetProperty("DecorationData", out var decorations) && decorations.ValueKind == JsonValueKind.Array && decorations.GetArrayLength() > 0)
            throw new InvalidDataException("Tattoo entries in .dctproj are not supported yet. Import the built tattoo resource through Options > Add tattoo / Edit tattoos > Import tattoo resource.");
        if (GetInt(root, "SaveVersion") != 5)
            throw new InvalidDataException("Versão de projeto DCT ainda não suportada; esperado SaveVersion 5.");
        if (!root.TryGetProperty("ClothData", out var data) || data.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Este arquivo não contém a estrutura de projeto de roupas esperada.");
        var name = GetString(root, "ProjectName") ?? Path.GetFileNameWithoutExtension(path);
        var parent = file.DirectoryName!;
        var adjacent = Path.Combine(parent, "clothproject_" + name, "data");
        var declared = GetString(root, "TargetBuildOutputPath");
        var dataFolders = new[]
            {
                adjacent,
                Path.Combine(parent, "data"),
                declared is not null ? Path.Combine(declared, "data") : null
            }
            .Where(folder => folder is not null)
            .Select(folder => Path.GetFullPath(folder!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var existingDataFolders = dataFolders.Where(Directory.Exists).ToArray();
        if (existingDataFolders.Length == 0)
            throw new DirectoryNotFoundException("Pasta de dados do projeto não encontrada: " +
                string.Join(" ou ", dataFolders));

        var alternateMeta = new[]
        {
            Path.Combine(parent, "clothproject_" + name, name, "pedalternatevariations.meta"),
            Path.Combine(parent, name, "pedalternatevariations.meta"),
            declared is null ? "" : Path.Combine(declared, name, "pedalternatevariations.meta")
        }.FirstOrDefault(File.Exists);
        var project = new ClothProject
        {
            Name = name,
            Author = GetString(root, "ProjectAuthor") ?? "",
            Version = GetString(root, "ProjectVersion") ?? "",
            Description = GetString(root, "ProjectDescription") ?? "",
            DlcName = GetString(root, "DlcName") ?? "",
            OutputFolder = declared is null ? parent : Path.GetDirectoryName(declared) ?? parent,
            UseTextureNameAsUniqueHash = GetBool(root, "UseTextureNameForUniqueNameHash"),
            CreateDrawableSubfolders = GetBool(root, "CreateSubfolderForClothDrawableTypes"),
            CreateClothesInfoDump = GetBool(root, "CreateClothesInfoFile"),
            CreateTattoosInfoDump = GetBool(root, "CreateDecorationsInfoFile"),
            CreateIlleniumConfig = GetBool(root, "CreateIlleniumAppearanceConfigFile"),
            CreateEsxTattooConfig = GetBool(root, "CreateEsxTattooShopConfigFile"),
            CreateMenyooTattooConfig = GetBool(root, "CreateMenyooPedDecalOverlaysFile"),
            SingleplayerSetupOrder = GetInt(root, "SingleplayerSetupOrder") >= 0 ? GetInt(root, "SingleplayerSetupOrder") : 99,
            AlwaysImportDuplicates = GetBool(root, "AlwaysImportDuplicatesWithoutConfirmation"),
            DisableExtraAlternateVariationFeatures = GetBool(root, "DisableExtraAlternateVariationFeatures"),
            DisableExtraYmtFiles = GetBool(root, "DisableExtraYmtCreation"),
            RemoveDefaultCollectionPrefix = GetBool(root, "RemoveDefaultClothResourcePrefix"),
            ReplaceWithDummyOnRemove = GetBool(root, "ReplaceWithDummyOnRemove"),
            ReplaceDummiesOnAdding = GetBool(root, "ReplaceDummysOnAdding"),
            SourceDlcName = GetString(root, "DlcName"),
            SourceAlternateVariationsPath = File.Exists(alternateMeta) ? alternateMeta : null
        };
        var missing = new List<string>();
        foreach (var entry in data.EnumerateArray())
        {
            if (GetBool(entry, "IsDummy"))
            {
                project.Items.Add(new ClothItem { IsDummy = true, Name = GetString(entry, "Name") ?? "Reservation", SourceIndex = GetInt(entry, "Position"),
                    Gender = GetInt(entry, "TargetGender") == 1 ? "Feminino" : "Masculino",
                    Category = CategoriesByType.GetValueOrDefault(GetInt(entry, "DrawableType"), "Outros") });
                continue;
            }
            var model = ResolveDataFile(existingDataFolders, GetString(entry, "MainPath"));
            if (model is null) continue;
            if (!File.Exists(model)) missing.Add(model);
            var type = GetInt(entry, "DrawableType");
            var original = GetString(entry, "OriginalFileName") ?? Path.GetFileName(model);
            var category = CategoriesByType.GetValueOrDefault(type, ClothCatalog.ResolveType(original).Category);
            var gender = GetInt(entry, "TargetGender") switch
            {
                0 => "Masculino", 1 => "Feminino", _ => "Indefinido"
            };
            var item = new ClothItem
            {
                Name = GetString(entry, "Name") ?? Path.GetFileNameWithoutExtension(original),
                Gender = gender,
                Category = category,
                SourceIndex = GetInt(entry, "Position"),
                ModelPath = model,
                OriginalModelFileName = original,
                PhysicsPath = ResolveDataFile(existingDataFolders, GetString(entry, "MeshPhysicsFilePath"))
            };
            foreach (var key in new[] { "FirstPersonModelFilePath", "AlternationModelFilePathTwo",
                "AlternationModelFilePathThree" })
            {
                var alternate = ResolveDataFile(existingDataFolders, GetString(entry, key));
                if (alternate is null) continue;
                if (key == "FirstPersonModelFilePath") item.HasFirstPersonModel = true;
                if (!File.Exists(alternate)) missing.Add(alternate);
                item.AlternateModelPaths.Add(alternate);
            }
            if (item.PhysicsPath is not null && !File.Exists(item.PhysicsPath)) missing.Add(item.PhysicsPath);
            if (entry.TryGetProperty("Textures", out var textures) && textures.ValueKind == JsonValueKind.Array)
            {
                foreach (var texture in textures.EnumerateArray().OrderBy(x => GetInt(x, "Position")))
                {
                    if (GetBool(texture, "IsDummy")) { item.TexturePaths.Add(""); item.OriginalTextureFileNames.Add("(dummy)"); item.TextureEffects.Add(new TextureEffect()); continue; }
                    var texturePath = ResolveDataFile(existingDataFolders, GetString(texture, "FilePath"));
                    if (texturePath is null) continue;
                    if (!File.Exists(texturePath)) missing.Add(texturePath);
                    item.TexturePaths.Add(texturePath);
                    item.TextureEffects.Add(new TextureEffect { TimeCycleModifier = GetString(texture, "FirstPersonTimeCycleModifier") ?? "",
                        Intensity = GetFloat(texture, "FirstPersonTimeCycleModifierIntensity", 1) });
                    var textureName = GetString(texture, "OriginalFileName") ?? Path.GetFileName(texturePath);
                    item.OriginalTextureFileNames.Add(textureName.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase)
                        ? textureName : textureName + ".ytd");
                }
            }
            if (entry.TryGetProperty("CreatorInfoData", out var creator))
            {
                item.Creator = GetString(creator, "Name") ?? "";
                item.CreatorWebsite = GetString(creator, "WebsiteUrl") ?? "";
                item.CreatorDescription = GetString(creator, "Description") ?? "";
            }
            var optionsKey = item.Category.Contains("[p_", StringComparison.Ordinal) ? "PedPropOptions" : "PedComponentOptions";
            if (entry.TryGetProperty(optionsKey, out var clothOptions))
            {
                item.HairOption = GetBool(clothOptions, "HideHairs") ? "Remove hairs" : GetBool(clothOptions, "CutHairs") ? "Cut hairs" : "Show hairs";
                item.HoodieHairs = GetBool(clothOptions, "HoodieHars");
                if (GetBool(clothOptions, "CustomCutHairs")) { item.HairOption = "Custom cut hairs"; item.HairCutValue = GetFloat(clothOptions, "HairCutValue", -0.5f); }
                if (clothOptions.TryGetProperty("FirstPersonData", out var firstPerson))
                    item.FirstPersonMaskModel = GetString(firstPerson, "FpsPropModel") ?? "";
            }
            project.Items.Add(item);
        }
        if (missing.Count > 0)
            throw new FileNotFoundException($"{missing.Count} arquivos do projeto estão ausentes. Primeiro: {missing[0]}");
        if (project.Items.Count == 0) throw new InvalidDataException("O projeto não contém peças importáveis.");
        return project;
    }

    private static string? ResolveDataFile(IReadOnlyList<string> roots, string? file)
    {
        if (string.IsNullOrWhiteSpace(file)) return null;
        if (!string.Equals(Path.GetFileName(file), file, StringComparison.Ordinal))
            throw new InvalidDataException("Caminho inesperado no projeto: " + file);
        foreach (var root in roots)
        {
            var candidate = Path.Combine(root, file);
            if (File.Exists(candidate)) return candidate;
        }
        return Path.Combine(roots[0], file);
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int GetInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : -1;
    private static float GetFloat(JsonElement element, string name, float fallback) =>
        element.TryGetProperty(name, out var value) && value.TryGetSingle(out var number) ? number : fallback;

    private static bool GetBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
