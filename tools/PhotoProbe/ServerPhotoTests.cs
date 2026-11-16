using System.Text.Json;
using FooxClothStudio;

internal static class ServerPhotoTests
{
    public static void RunLive(string game, string existing, string output)
    {
        var server = FiveMPhotoCatalog.ReadAsync(System.IO.Path.Combine(output, "server-catalog.json")).GetAwaiter().GetResult();
        var gta = GtaPhotoCatalog.Scan(game, System.IO.Path.Combine(output, "cache"));
        var result = server.Filter(gta.Project);
        var saved = System.IO.Directory.EnumerateFiles(existing, "*", System.IO.SearchOption.AllDirectories)
            .Where(path => new[] { ".png", ".webp", ".jpg", ".jpeg" }.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant()))
            .Select(System.IO.Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var photos = gta.Project.Items.SelectMany(item => item.TextureIds.Select(texture => item.Id + "." + texture)).Distinct().ToArray();
        var defaults = OriginalResourceExporter.Describe(gta.Project).Entries.Select(entry =>
            new { entry.Id, Texture = entry.Textures.FirstOrDefault(texture => texture.Index == 0 && texture.Available && !texture.Inherited) })
            .Where(entry => entry.Texture is not null).Select(entry => entry.Id + "." + entry.Texture!.Id).Distinct().ToArray();
        System.IO.Directory.CreateDirectory(output);
        var report = new { result.ServerItems, result.MatchedItems, result.Textures, result.UnmatchedItems,
            existingPhotos = photos.Count(saved.Contains), newPhotos = photos.Count(photo => !saved.Contains(photo)),
            thumbnailCount = defaults.Length, existingThumbnails = defaults.Count(saved.Contains),
            missingThumbnails = defaults.Count(photo => !saved.Contains(photo)) };
        System.IO.File.WriteAllText(System.IO.Path.Combine(output, "server-filter-report.json"), JsonSerializer.Serialize(report));
        Console.WriteLine(JsonSerializer.Serialize(report));
    }

    public static void Run()
    {
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
        using var incomplete = JsonDocument.Parse(document.RootElement.GetRawText().Replace("\"complete\":true", "\"complete\":false"));
        try { ServerPhotoCatalog.Parse(incomplete.RootElement); throw new Exception("Partial server catalog was accepted."); }
        catch (System.IO.InvalidDataException) { }
        using var dynamicCount = JsonDocument.Parse(document.RootElement.GetRawText()
            .Replace("\"textures\":[1]", "\"textures\":[299]").Replace("\"textureCount\":2", "\"textureCount\":300"));
        if (ServerPhotoCatalog.Parse(dynamicCount.RootElement).Count != 1)
            throw new Exception("Dynamic texture count above 256 was rejected.");
        Console.WriteLine("PASS: server catalog excludes unavailable GTA models and textures, preserves UUID/native texture indices and source items, and rejects partial snapshots.");
    }
}
