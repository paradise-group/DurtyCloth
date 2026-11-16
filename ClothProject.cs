using System.Text.Json.Serialization;

namespace FooxClothStudio;

public sealed class ClothProject
{
    public int FormatVersion { get; set; } = 1;
    public string Name { get; set; } = "New created project";
    public bool AlwaysImportDuplicates { get; set; }
    public string Author { get; set; } = "";
    public string Version { get; set; } = "";
    public string Description { get; set; } = "";
    public string DlcName { get; set; } = "";
    public string? SourceResourceRoot { get; set; }
    public PublishedCollectionSnapshot? PublishedCollection { get; set; }
    public bool UsePermanentClothingIds { get; set; }
    public double ModelWarningMiB { get; set; } = 4;
    public double TextureWarningMiB { get; set; } = 4;
    public string OutputFolder { get; set; } = "";
    public int TargetGameBuild { get; set; } = 3095;
    public bool UseTextureNameAsUniqueHash { get; set; }
    public bool CreateDrawableSubfolders { get; set; }
    public bool CreateClothesInfoDump { get; set; }
    public bool DisableExtraAlternateVariationFeatures { get; set; }
    public bool RemoveDefaultCollectionPrefix { get; set; }
    public bool DisableExtraYmtFiles { get; set; }
    public string BuildTarget { get; set; } = "FiveM Resource";
    public bool ReplaceDlcCollection { get; set; }
    public string ReplacementResource { get; set; } = "mplowrider";
    public string ReplacementMaleCollection { get; set; } = "mp_m_lowrider_01";
    public string ReplacementFemaleCollection { get; set; } = "mp_f_lowrider_01";
    public int SingleplayerSetupOrder { get; set; } = 99;
    public bool CreateTattoosInfoDump { get; set; }
    public bool CreateIlleniumConfig { get; set; }
    public bool CreateEsxTattooConfig { get; set; }
    public bool CreateMenyooTattooConfig { get; set; }
    public List<TattooItem> Tattoos { get; set; } = [];
    public List<AppearanceAsset> AppearanceAssets { get; set; } = [];
    public bool ReplaceWithDummyOnRemove { get; set; }
    public bool ReplaceDummiesOnAdding { get; set; }
    public string? SourceDlcName { get; set; }
    public string? SourceAlternateVariationsPath { get; set; }
    public List<ClothItem> Items { get; set; } = [];
}

public sealed class PublishedCollectionSnapshot
{
    public string DlcName { get; set; } = "";
    public bool RemoveDefaultCollectionPrefix { get; set; }
    public bool ReplaceDlcCollection { get; set; }
    public string ReplacementResource { get; set; } = "";
    public string ReplacementMaleCollection { get; set; } = "";
    public string ReplacementFemaleCollection { get; set; } = "";
    public List<PublishedDrawable> Drawables { get; set; } = [];
}

public sealed record PublishedDrawable(string Gender, string Slot, string Collection, int LocalIndex, Guid ItemId);

public sealed class TattooItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "tattoo";
    public string Label { get; set; } = "Tattoo";
    public string ImagePath { get; set; } = "";
    public string SourceTextureName { get; set; } = "";
    public string? Collection { get; set; }
    public string? Overlay { get; set; }
    public bool UseTintColour { get; set; }
    [JsonIgnore] public bool IsHairOverlay => Zone == "ZONE_HEAD" && Overlay?.Contains("_Hair_", StringComparison.OrdinalIgnoreCase) == true;
    public string Gender { get; set; } = "Both";
    public string Zone { get; set; } = "ZONE_TORSO";
    public float U { get; set; } = 0.5f;
    public float V { get; set; } = 0.5f;
    public float ScaleX { get; set; } = 0.5f;
    public float ScaleY { get; set; } = 0.5f;
    public float Rotation { get; set; }
    public int Cost { get; set; } = 100;
    public float CameraX { get; set; } = 0.7f;
    public float CameraY { get; set; } = 0.4f;
    public float CameraZ { get; set; } = 0.3f;
    public float CameraHeading { get; set; } = 117.3f;
    public override string ToString() => Name + " • " + Gender + " • " + (string.IsNullOrEmpty(Zone) ? "Zona por definir" : Zone);
}

public sealed class AppearanceAsset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "Outros";
    public string Gender { get; set; } = "Both";
    public string TexturePath { get; set; } = "";
    public string? OverlayCategory { get; set; }
    public int? OverlayIndex { get; set; }
    public uint? DiffuseTextureHash { get; set; }
}

public sealed class ClothItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public List<Guid> TextureIds { get; set; } = [];
    public long? ModelSizeBytes { get; set; }
    public long? MaxTextureSizeBytes { get; set; }
    [JsonIgnore] public string? MetricsKey { get; set; }
    [JsonIgnore] public double ModelWarningMiB { get; set; } = 4;
    [JsonIgnore] public double TextureWarningMiB { get; set; } = 4;
    [JsonIgnore] public bool HeavyAsset => !IsDummy && (ModelSizeBytes > ModelWarningMiB * 1048576 || MaxTextureSizeBytes > TextureWarningMiB * 1048576);
    [JsonIgnore] public string WeightHint => $"Model: {(ModelSizeBytes ?? 0) / 1048576.0:F2} MiB (warning > {ModelWarningMiB} MiB)\nLargest YTD: {(MaxTextureSizeBytes ?? 0) / 1048576.0:F2} MiB (warning > {TextureWarningMiB} MiB)";
    [JsonIgnore] public string GenderIcon => Gender == "Masculino" ? "♂\uFE0E" : Gender == "Feminino" ? "♀\uFE0E" : "?";
    [JsonIgnore] public string RowLabel => DisplayName + (ModelSizeBytes is long bytes ? bytes >= 1048576 ? $"  {bytes / 1048576.0:F2} MB" : $"  {bytes / 1024.0:F2} KB" : "");
    public bool IsDummy { get; set; }
    public string HairOption { get; set; } = "Show hairs";
    public bool HoodieHairs { get; set; }
    public float HairCutValue { get; set; } = -0.5f;
    public float HeelHeight { get; set; }
    public string FirstPersonMaskModel { get; set; } = "";
    public bool HasFirstPersonModel { get; set; }
    public float MaskOffsetX { get; set; }
    public float MaskOffsetY { get; set; }
    public float MaskOffsetZ { get; set; }
    public List<TextureEffect> TextureEffects { get; set; } = [];
    public string Name { get; set; } = "";
    public string Gender { get; set; } = "Indefinido";
    public string Category { get; set; } = "Outros";
    public int? SourceIndex { get; set; }
    public int? ExportPosition { get; set; }
    public Guid? InsertBeforeItemId { get; set; }
    public string ModelPath { get; set; } = "";
    public string? OriginalModelFileName { get; set; }
    public List<string> AlternateModelPaths { get; set; } = [];
    public List<string> TexturePaths { get; set; } = [];
    public List<string> OriginalTextureFileNames { get; set; } = [];
    public List<string> TextureLabels { get; set; } = [];
    public string? PhysicsPath { get; set; }
    public string AudioPreset { get; set; } = "none";
    public uint? BehaviourFlags { get; set; }
    public int ShopCost { get; set; }
    public string ShopTextLabel { get; set; } = "";
    public string ShopLockHash { get; set; } = "";
    public string Creator { get; set; } = "";
    public string CreatorWebsite { get; set; } = "";
    public string CreatorDescription { get; set; } = "";

    [JsonIgnore]
    public string Summary => Gender + "  •  " + Category + "  •  " + TexturePaths.Count + " textura(s)";

    [JsonIgnore]
    public bool MissingTexture => !IsDummy && !TexturePaths.Any(path => !string.IsNullOrWhiteSpace(path));

    [JsonIgnore]
    public string DisplayName => ((ExportPosition ?? SourceIndex) is int index ? $"{index} {Name}" : Name) + (IsDummy ? " (dummy)" : "");
}

public sealed class TextureEffect
{
    public string TimeCycleModifier { get; set; } = "";
    public float Intensity { get; set; } = 1;
}
