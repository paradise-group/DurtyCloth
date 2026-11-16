using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FooxClothStudio;

internal static class ServerPhotoBatch
{
    public static void Run(string game, string output)
    {
        var server = FiveMPhotoCatalog.ReadAsync(Path.Combine(output, "server-photo-catalog.json")).GetAwaiter().GetResult();
        var gta = GtaPhotoCatalog.Scan(game);
        var filtered = server.Filter(gta.Project);
        Directory.CreateDirectory(output);
        gta.WriteIdentityMap(output);
        var options = new PhotoImageOptions(512, "WebP", 85);
        var session = new PhotoExportSession(output, false);
        var peds = new Dictionary<string, PhotoPed>();
        var readTextures = typeof(MainWindow).GetMethod("ReadMatchingTextures", BindingFlags.NonPublic | BindingFlags.Static)!;
        using var index = session.OpenIndex();
        var written = 0;
        var reused = 0;
        var failed = 0;
        var completed = 0;
        foreach (var item in gta.Project.Items)
        {
            var category = item.Category.Replace('[', '_').Replace(']', '_');
            var target = Path.Combine(output, item.Gender, category, item.Id + "." + item.TextureIds[0] + options.Extension);
            try
            {
                var existing = session.FindExisting(target);
                var mode = "isolated-model";
                if (existing is null)
                {
                    var model = ModelPreview.Read(gta.Resolve(item.ModelPath), false, preserveAllTriangles: true);
                    var textures = (Dictionary<string, BitmapSource>)readTextures.Invoke(null, [gta.Resolve(item.TexturePaths[0]), model])!;
                    if (textures.Count == 0) throw new InvalidDataException("No matching diffuse textures.");
                    var code = item.Category.Split('[').Last().TrimEnd(']');
                    PhotoPed? ped = null;
                    if (code is "hair" or "berd" or "p_eyes" or "p_ears" or "p_mouth")
                    {
                        if (!peds.TryGetValue(item.Gender, out ped)) peds[item.Gender] = ped = PreviewPhotoRenderer.LoadPed(game, item.Gender);
                        mode = "base-ped";
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    session.SaveImage(target, temporary => PreviewPhotoRenderer.Save(temporary, model, textures, ped,
                        item.Category, Color.FromRgb(0x62, 0x5F, 0x68), options));
                    written++;
                }
                else
                {
                    target = existing;
                    reused++;
                }
                if (existing is null || session.NeedsIndexEntry(target))
                    index.WriteLine(JsonSerializer.Serialize(new
                    {
                        kind = "drawable", status = "ok", item = item.Name, itemId = item.Id, textureId = item.TextureIds[0],
                        item.Gender, item.Category, item.SourceIndex, variant = 0, file = Path.GetRelativePath(output, target),
                        model = item.ModelPath, texture = item.TexturePaths[0], renderMode = mode
                    }));
            }
            catch (Exception error)
            {
                failed++;
                index.WriteLine(JsonSerializer.Serialize(new
                {
                    kind = "drawable", status = "error", item = item.Name, itemId = item.Id, textureId = item.TextureIds[0],
                    item.Gender, item.Category, variant = 0, message = error.GetBaseException().Message
                }));
            }
            if (++completed % 100 == 0)
            {
                index.Flush();
                Console.WriteLine($"{completed}/{filtered.MatchedItems}: {written} new, {reused} existing, {failed} failed.");
            }
        }
        index.Flush();
        var report = new { filtered.ServerItems, filtered.MatchedItems, written, reused, failed };
        File.WriteAllText(Path.Combine(output, "server-photo-export-report.json"), JsonSerializer.Serialize(report));
        Console.WriteLine(JsonSerializer.Serialize(report));
        if (failed > 0) Environment.ExitCode = 1;
    }
}
