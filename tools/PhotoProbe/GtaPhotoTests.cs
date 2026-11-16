using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FooxClothStudio;

internal static class GtaPhotoTests
{
    private sealed class Progress : IProgress<(int Done, int Total, string Stage)>
    {
        public void Report((int Done, int Total, string Stage) value)
        {
            if (value.Done % 25 == 0 || value.Done == value.Total)
                Console.WriteLine($"{value.Done}/{value.Total}: {value.Stage}");
        }
    }

    public static void Run(string game, string output)
    {
        Directory.CreateDirectory(output);
        var catalog = GtaPhotoCatalog.Scan(game, Path.Combine(output, "cache"), new Progress());
        if (catalog.Project.Items.Select(item => item.Id).Distinct().Count() != catalog.Project.Items.Count)
            throw new Exception("Duplicate GTA model UUIDs.");
        var pants = catalog.Project.Items.Single(item => item.OriginalModelFileName == "mp_m_freemode_01^lowr_000_r.ydd");
        if (pants.Id != Guid.Parse("483c4feb-d7c2-2cd4-ddbe-78dbeeec388e"))
            throw new Exception("GTA source UUID differs from the existing exported photo UUID.");
        if (pants.TextureIds[0] != Guid.Parse("643c6e47-b34a-13e8-9a1c-a59b4cdd1182"))
            throw new Exception("GTA texture UUID differs from the existing exported photo UUID.");
        catalog.WriteIdentityMap(output);
        var sourceAppearance = catalog.Project.AppearanceAssets.First(asset => asset.Kind == "Sobrancelhas");
        var customAppearance = new AppearanceAsset { Id = Guid.NewGuid(), Name = sourceAppearance.Name,
            Kind = sourceAppearance.Kind, Gender = "Female", TexturePath = "appearance.ytd" };
        var prepared = PhotoDecorationSources.Prepare(new ClothProject { AppearanceAssets = [customAppearance] }, catalog, false);
        var sharedAppearance = prepared.AppearanceAssets.Single();
        if (sharedAppearance.Id != customAppearance.Id || sharedAppearance.Gender != "Both" || sharedAppearance.OverlayIndex is null
            || sharedAppearance.DiffuseTextureHash is null || customAppearance.Gender != "Female"
            || customAppearance.OverlayIndex is not null || customAppearance.DiffuseTextureHash is not null)
            throw new Exception("Native shared overlay definitions lost a gender, changed a project UUID or modified the source project.");
        foreach (var kind in new[] { "Barba", "Sobrancelhas", "Maquilhagem", "Pelos corporais" })
        {
            var appearance = catalog.Project.AppearanceAssets.First(asset => asset.Kind == kind);
            var file = new CodeWalker.GameFiles.YtdFile();
            file.Load(File.ReadAllBytes(catalog.Resolve(appearance.TexturePath)));
            var texture = AppearancePhotoTextures.Select(file, appearance.Name).Single();
            var decode = typeof(MainWindow).GetMethod("DecodeTextureBitmap", BindingFlags.NonPublic | BindingFlags.Static)!;
            var image = (BitmapSource)decode.Invoke(null, [texture, 1024])!;
            using (var raw = File.Create(Path.Combine(output, appearance.OverlayCategory + "-texture.png")))
            {
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); encoder.Save(raw);
            }
            PedSurfacePhotos.SaveAppearance(Path.Combine(output, appearance.OverlayCategory + "-applied.png"),
                appearance.OverlayCategory!, image, PreviewPhotoRenderer.LoadPed(game, "Masculino"), Color.FromRgb(98, 95, 104));
        }
        var basePed = PreviewPhotoRenderer.LoadPed(game, "Masculino");
        var tiles = basePed.Parts.Where(part => part.DecorationCoordinates is not null).SelectMany(part =>
            part.Positions.Select((point, index) => new { part.TextureName, point,
                tile = Math.Floor(part.DecorationCoordinates![index].Y) })).GroupBy(vertex => new { vertex.TextureName, vertex.tile })
            .Select(group => new { group.Key, x = group.Average(vertex => vertex.point.X), y = group.Average(vertex => vertex.point.Y),
                z = group.Average(vertex => vertex.point.Z), count = group.Count() });
        File.WriteAllText(Path.Combine(output, "decoration-tiles.json"), JsonSerializer.Serialize(tiles, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(output, "surface-coordinates.json"), JsonSerializer.Serialize(basePed.Parts.Select(part => new
        {
            part.TextureName,
            first = part.TextureCoordinates.Length == 0 ? null : new[] { part.TextureCoordinates.Min(uv => uv.X), part.TextureCoordinates.Max(uv => uv.X), part.TextureCoordinates.Min(uv => uv.Y), part.TextureCoordinates.Max(uv => uv.Y) },
            second = part.DecorationCoordinates is not { Length: > 0 } ? null : new[] { part.DecorationCoordinates.Min(uv => uv.X), part.DecorationCoordinates.Max(uv => uv.X), part.DecorationCoordinates.Min(uv => uv.Y), part.DecorationCoordinates.Max(uv => uv.Y) }
        }), new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(output, "native-tattoos.json"), JsonSerializer.Serialize(catalog.Project.Tattoos));
        var headTattoo = catalog.Project.Tattoos.First(tattoo => tattoo.Zone == "ZONE_HEAD" && tattoo.Gender == "Male"
            && tattoo.U >= 0 && !tattoo.Name.Contains("Hair", StringComparison.OrdinalIgnoreCase));
        var tattooTexture = TattooExporter.ReadImageForPreview(headTattoo, catalog.Resolve(headTattoo.ImagePath));
        var decodeTattoo = typeof(MainWindow).GetMethod("DecodeTextureBitmap", BindingFlags.NonPublic | BindingFlags.Static)!;
        var tattooImage = (BitmapSource)decodeTattoo.Invoke(null, [tattooTexture, 1024])!;
        File.WriteAllText(Path.Combine(output, "head-tattoo-definition.json"), JsonSerializer.Serialize(headTattoo, new JsonSerializerOptions { WriteIndented = true }));
        using (var raw = File.Create(Path.Combine(output, "head-tattoo-texture.png")))
        {
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(tattooImage)); encoder.Save(raw);
        }
        PedSurfacePhotos.SaveTattoo(Path.Combine(output, "head-tattoo-applied.png"), headTattoo, tattooImage,
            PreviewPhotoRenderer.LoadPed(game, "Masculino"), Color.FromRgb(98, 95, 104));
        var identities = ClothingIdentityMap.Read(Path.Combine(output, "gta-clothing-identities.json"));
        if (identities.Entries.Count != catalog.Project.Items.Count) throw new Exception("Incomplete GTA identity map.");
        var optionsType = typeof(MainWindow).Assembly.GetType("FooxClothStudio.PhotoExportOptionsWindow")!;
        var options = (Window)Activator.CreateInstance(optionsType, catalog.Project,
            "Origem: roupas e acessórios do GTA V instalado, incluindo os DLCs encontrados. Pode usar a mesma pasta das fotos do pack para juntar os dois lotes.")!;
        var content = (UIElement)options.Content;
        content.Measure(new Size(options.Width - 20, double.PositiveInfinity));
        if (content.DesiredSize.Height > options.Height - SystemParameters.CaptionHeight)
            throw new Exception("GTA photo options overflow the export window.");
        options.Close();
        var samples = new List<object>();
        var read = typeof(MainWindow).GetMethod("ReadMatchingTextures", BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (var gender in new[] { "Masculino", "Feminino" })
        {
            foreach (var code in new[] { "jbib", "p_head", "hair" })
            {
                var item = catalog.Project.Items.Where(item => item.Gender == gender
                    && item.Category.EndsWith("[" + code + "]") && item.TexturePaths.Count > 0
                    && (code != "hair" || item.SourceIndex == 4))
                    .OrderByDescending(item => code == "jbib" && item.Name.Contains("_2024_01^", StringComparison.OrdinalIgnoreCase)).First();
                var modelPath = catalog.Resolve(item.ModelPath);
                var model = ModelPreview.Read(modelPath, false, preserveAllTriangles: true);
                if (model.SourceTriangles != model.PreviewTriangles) throw new Exception("GTA photo reader discarded geometry.");
                var texturePath = catalog.Resolve(item.TexturePaths[0]);
                var images = (Dictionary<string, BitmapSource>)read.Invoke(null, [texturePath, model])!;
                if (images.Count == 0) throw new Exception("GTA model has no matching diffuse textures: " + item.Name);
                var isEmpty = PreviewPhotoRenderer.IsEmptySlot(model, item.Category);
                var ped = code is "hair" or "p_head" || isEmpty ? PreviewPhotoRenderer.LoadPed(game, gender) : null;
                var target = Path.Combine(output, item.Id + "." + item.TextureIds[0] + ".webp");
                var session = new PhotoExportSession(output, false);
                var saved = session.SaveImage(target, temporary => PreviewPhotoRenderer.Save(temporary, model, images,
                    code == "p_head" && !isEmpty ? null : ped, item.Category, Color.FromRgb(0x62, 0x5F, 0x68),
                    new PhotoImageOptions(512, "WebP", 85), code == "p_head" ? ped?.Attachments[31086] : null));
                if (session.FindExisting(target) != saved) throw new Exception("Native GTA UUID photo cannot be reused.");
                samples.Add(new { item.Name, item.Gender, item.Category, item.Id, target, model.SourceTriangles });
                if (code == "hair") HairPhotoTests.Run(modelPath, texturePath, Path.Combine(output, "hair", gender), gender);
            }
        }
        var report = new
        {
            items = catalog.Project.Items.Count, textures = catalog.Project.Items.Sum(item => item.TexturePaths.Count),
            missingOwnTextures = catalog.Project.Items.Count(item => item.TexturePaths.Count == 0),
            warnings = catalog.Warnings, samples,
            categories = catalog.Project.Items.GroupBy(item => item.Gender + "/" + item.Category)
                .ToDictionary(group => group.Key, group => group.Count())
        };
        foreach (var (filename, rear) in new[]
        {
            ("mp_m_freemode_01_p_mp_m_sum2^p_head_005.ydd", false),
            ("mp_m_freemode_01_p_mp_m_sum2^p_head_007.ydd", true)
        })
        {
            var hat = catalog.Project.Items.SingleOrDefault(item => item.OriginalModelFileName == filename);
            if (hat is null) continue;
            var ped = PreviewPhotoRenderer.LoadPed(game, hat.Gender);
            var model = ModelPreview.Read(catalog.Resolve(hat.ModelPath), false, true);
            var textures = (Dictionary<string, BitmapSource>)read.Invoke(null, [catalog.Resolve(hat.TexturePaths[0]), model])!;
            var aligned = ModelPreview.AttachToBone(model, ped.Attachments[31086]);
            if (PhotoView.ShowBack(aligned, textures, "p_head") != rear)
                throw new Exception("Incorrect hat view for " + filename + "; plain caps must face front and the rear luminous logo must stay visible.");
        }
        File.WriteAllText(Path.Combine(output, "gta-photo-report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS: scanned {catalog.Project.Items.Count} GTA models and {report.textures} textures; stable UUIDs match previous exports; validated identity map; six real male/female clothing, hat and hair photos rendered with complete geometry and reusable UUID names.");
    }
}
