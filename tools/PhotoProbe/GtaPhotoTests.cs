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
                var ped = code == "hair" ? PreviewPhotoRenderer.LoadPed(game, gender) : null;
                var target = Path.Combine(output, item.Id + "." + item.TextureIds[0] + ".webp");
                var session = new PhotoExportSession(output, false);
                var saved = session.SaveImage(target, temporary => PreviewPhotoRenderer.Save(temporary, model, images, ped,
                    item.Category, Color.FromRgb(0x62, 0x5F, 0x68), new PhotoImageOptions(512, "WebP", 85)));
                if (session.FindExisting(target) != saved) throw new Exception("Native GTA UUID photo cannot be reused.");
                samples.Add(new { item.Name, item.Gender, item.Category, item.Id, target, model.SourceTriangles });
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
        File.WriteAllText(Path.Combine(output, "gta-photo-report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS: scanned {catalog.Project.Items.Count} GTA models and {report.textures} textures; stable UUIDs match previous exports; validated identity map; six real male/female clothing, hat and hair photos rendered with complete geometry and reusable UUID names.");
    }
}
