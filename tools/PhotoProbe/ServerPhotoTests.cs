using System.Text.Json;
using FooxClothStudio;

internal static class ServerPhotoTests
{
    public static void Run()
    {
        CheckInheritedFirstTexture();
        var itemId = Guid.NewGuid();
        var firstTexture = Guid.NewGuid();
        var secondTexture = Guid.NewGuid();
        var selected = new ClothItem
        {
            Id = itemId, Name = "mp_m_freemode_01^feet_000_u", Gender = "Masculino", Category = "Sapato [feet]",
            ModelPath = "mp_m_freemode_01^feet_000_u.ydd", OriginalModelFileName = "mp_m_freemode_01^feet_000_u.ydd",
            TexturePaths = ["mp_m_freemode_01^feet_diff_000_a_uni.ytd", "mp_m_freemode_01^feet_diff_000_b_uni.ytd"],
            OriginalTextureFileNames = ["mp_m_freemode_01^feet_diff_000_a_uni.ytd", "mp_m_freemode_01^feet_diff_000_b_uni.ytd"],
            TextureIds = [firstTexture, secondTexture]
        };
        var excluded = new ClothItem
        {
            Id = Guid.NewGuid(), Name = "mp_m_freemode_01^feet_001_u", Gender = "Masculino", Category = selected.Category,
            ModelPath = "mp_m_freemode_01^feet_001_u.ydd", OriginalModelFileName = "mp_m_freemode_01^feet_001_u.ydd"
        };
        using var document = JsonDocument.Parse("""
            {"formatVersion":1,"source":"touz-server-clothing","complete":true,
             "models":["mp_m_freemode_01","mp_f_freemode_01"],"entries":[
             {"model":"mp_m_freemode_01","kind":"component","slot":6,"collection":"","index":0,"textures":[1],"textureCount":2}]}
            """);
        var project = new ClothProject { Items = [selected, excluded] };
        var catalog = ServerPhotoCatalog.Parse(document.RootElement);
        var result = catalog.Filter(project);
        if (project.Items.Count != 1 || result.Textures != 1 || project.Items[0].TextureIds.Single() != secondTexture)
            throw new Exception("Server filtering changed the selected texture UUID or included an unavailable model.");
        if (selected.TexturePaths.Count != 2) throw new Exception("Server filtering mutated the original project item.");
        ClothingIdentityMap.EnsureTextures(project.Items[0]);
        var map = OriginalResourceExporter.Describe(project);
        if (map.Entries[0].Textures[0].Index != 1 || map.Entries[0].Textures[0].Id != secondTexture)
            throw new Exception("Filtered texture was incorrectly reindexed to native texture zero.");
        using var allTextures = JsonDocument.Parse(document.RootElement.GetRawText().Replace("\"textures\":[1]", "\"textures\":[0,1]"));
        var firstOnly = new ClothProject { Items = [selected] };
        ServerPhotoCatalog.Parse(allTextures.RootElement).Filter(firstOnly);
        if (firstOnly.Items[0].TextureIds.Count != 1 || firstOnly.Items[0].TextureIds[0] != firstTexture)
            throw new Exception("Photo export did not select only the first available texture.");
        var everyTexture = new ClothProject { Items = [selected] };
        ServerPhotoCatalog.Parse(allTextures.RootElement).Filter(everyTexture, firstTextureOnly: false);
        if (!everyTexture.Items[0].TextureIds.SequenceEqual(new[] { firstTexture, secondTexture }))
            throw new Exception("All-texture mode lost available texture UUIDs before the export options.");
        using var incomplete = JsonDocument.Parse(document.RootElement.GetRawText().Replace("\"complete\":true", "\"complete\":false"));
        try { ServerPhotoCatalog.Parse(incomplete.RootElement); throw new Exception("Partial server catalog was accepted."); }
        catch (System.IO.InvalidDataException) { }
        using var dynamicCount = JsonDocument.Parse(document.RootElement.GetRawText()
            .Replace("\"textures\":[1]", "\"textures\":[299]").Replace("\"textureCount\":2", "\"textureCount\":300"));
        if (ServerPhotoCatalog.Parse(dynamicCount.RootElement).Count != 1)
            throw new Exception("Dynamic texture count above 256 was rejected.");
        Console.WriteLine("PASS: server catalog excludes unavailable GTA models and textures, preserves UUID/native texture indices and source items, and rejects partial snapshots.");
    }

    private static void CheckInheritedFirstTexture()
    {
        var itemId = Guid.NewGuid();
        var ownId = Guid.NewGuid();
        var firstId = Guid.NewGuid();
        var native = new ClothItem
        {
            Id = itemId, Name = "mp_m_freemode_01^jbib_000_u", Gender = "Masculino", Category = "Blusa [jbib]",
            ModelPath = "mp_m_freemode_01^jbib_000_u.ydd", OriginalModelFileName = "mp_m_freemode_01^jbib_000_u.ydd",
            TexturePaths = ["mp_m_freemode_01^jbib_diff_000_a_uni.ytd", "mp_m_freemode_01^jbib_diff_000_b_uni.ytd"],
            OriginalTextureFileNames = ["mp_m_freemode_01^jbib_diff_000_a_uni.ytd", "mp_m_freemode_01^jbib_diff_000_b_uni.ytd"],
            TextureIds = [firstId, Guid.NewGuid()]
        };
        var own = new ClothItem
        {
            Id = itemId, Name = native.Name, Gender = native.Gender, Category = native.Category,
            ModelPath = "custom-model.ydd", OriginalModelFileName = native.OriginalModelFileName,
            TexturePaths = ["custom-texture.ytd"], OriginalTextureFileNames = [native.OriginalTextureFileNames[1]], TextureIds = [ownId]
        };
        var merged = PhotoTextureSources.Merge(own, native);
        if (merged.ModelPath != own.ModelPath || merged.TextureIds[0] != firstId || merged.TextureIds[1] != ownId ||
            !merged.TexturePaths[0].StartsWith("gta://") || merged.TexturePaths[1] != own.TexturePaths[0] || own.TexturePaths.Count != 1)
            throw new Exception("Photo sources did not preserve custom models/textures while restoring the first inherited variant.");
    }
}
