using System.IO;
using System.Text.Json;
using CodeWalker.GameFiles;
using CodeWalker.Utils;
using FooxClothStudio;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class PhotoFailureProbe
{
    public static void Verify(string projectPath, string root, string indexPath, string output, string game)
    {
        // The fixture project supplies metadata only. Render into a separate test folder;
        // never invent replacement UUIDs for an unsaved project or modify its export.
        if (Path.GetFullPath(output).Equals(Path.GetDirectoryName(Path.GetFullPath(indexPath)), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Verification output must be separate from the real export.");
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new InvalidOperationException("Use a new, empty verification output folder.");
        Directory.CreateDirectory(output);
        var project = JsonSerializer.Deserialize<ClothProject>(File.ReadAllText(projectPath))!;
        var sources = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToLookup(Path.GetFileName, StringComparer.OrdinalIgnoreCase);
        string Resolve(string path)
        {
            var name = Path.GetFileName(path);
            var exact = sources[name].ToArray();
            if (exact.Length > 0) return exact.Single();
            // Portable projects prefix texture filenames with their variant index.
            name = System.Text.RegularExpressions.Regex.Replace(name, @"^texture_\d+_", "");
            return sources[name].Single();
        }
        var decode = typeof(MainWindow).GetMethod("DecodeTextureBitmap", BindingFlags.Static | BindingFlags.NonPublic)!;
        BitmapSource Decode(Texture texture, int size) => (BitmapSource)decode.Invoke(null, [texture, size])!;
        var readTextures = typeof(MainWindow).GetMethod("ReadMatchingTextures", BindingFlags.Static | BindingFlags.NonPublic)!;
        var peds = new Dictionary<string, PhotoPed>();
        PhotoPed Ped(string gender)
        {
            if (!peds.TryGetValue(gender, out var ped)) peds[gender] = ped = PreviewPhotoRenderer.LoadPed(game, gender);
            return ped;
        }
        var rows = File.ReadLines(indexPath).Select(line => JsonDocument.Parse(line).RootElement.Clone())
            .Where(row => row.GetProperty("status").GetString() == "error").ToArray();
        var results = new List<object>();
        var recovered = 0;
        var invalidSources = 0;
        var options = new PhotoImageOptions(512, "WebP", 85);
        var background = Color.FromRgb(0x62, 0x5f, 0x68);
        foreach (var row in rows)
        {
            var kind = row.GetProperty("kind").GetString();
            var name = row.GetProperty(kind == "drawable" ? "item" : "Name").GetString()!;
            var gender = row.GetProperty(kind == "drawable" ? "Gender" : "gender").GetString()!;
            var target = Path.Combine(output, recovered.ToString("D3") + options.Extension);
            try
            {
                if (kind == "tattoo")
                {
                    var tattoo = project.Tattoos.Single(item => item.Name == name);
                    var path = Resolve(tattoo.ImagePath);
                    // Build validation must remain strict even though photo loading accepts rectangles.
                    var strictRejected = false;
                    try { TattooExporter.ReadImage(tattoo, path); }
                    catch (InvalidDataException) { strictRejected = true; }
                    if (!strictRejected) throw new Exception("Expected the addon build validation to reject this non-square fixture.");
                    var texture = TattooExporter.ReadImageForPreview(tattoo, path);
                    var image = Decode(texture, 1024);
                    if (string.IsNullOrEmpty(tattoo.Zone)) PreviewPhotoRenderer.SaveUnplacedTattoo(target, image, Ped(gender), background, options);
                    else PreviewPhotoRenderer.SaveTattoo(target, tattoo, image, Ped(gender), background, options);
                }
                else if (kind == "appearance")
                {
                    var appearance = project.AppearanceAssets.Single(item => item.Name == name);
                    var ytd = new YtdFile(); ytd.Load(File.ReadAllBytes(Resolve(appearance.TexturePath)));
                    var texture = ytd.TextureDict.Textures.data_items.Single(item => item.Name == row.GetProperty("texture").GetString());
                    // Exercise both the full mip and the smaller mip used in previews.
                    var small = Decode(texture, 512);
                    if (small.PixelWidth != 512 || small.PixelHeight != 512) throw new Exception("Wrong BC7 mip dimensions.");
                    var image = Decode(texture, 1024);
                    var overlay = new TattooItem { Zone = appearance.Kind == "Pelos corporais" ? "ZONE_TORSO" : "ZONE_HEAD", ScaleX = 0.45f, ScaleY = 0.35f };
                    PreviewPhotoRenderer.SaveTattoo(target, overlay, image, Ped(gender), background, options);
                }
                else
                {
                    var item = project.Items.Single(item => item.Name == name);
                    var model = ModelPreview.Read(Resolve(item.ModelPath), false, preserveAllTriangles: true);
                    if (model.SourceTriangles != model.PreviewTriangles || model.SourceTriangles == 0) throw new Exception("Photo lost geometry.");
                    var images = (Dictionary<string, BitmapSource>)readTextures.Invoke(null, [Resolve(item.TexturePaths[0]), model])!;
                    PreviewPhotoRenderer.Save(target, model, images, null, item.Category, background, options);
                }
                if (new FileInfo(target).Length == 0) throw new Exception("Empty photo.");
                recovered++;
                results.Add(new { kind, name, gender, status = "recovered", file = Path.GetFileName(target) });
            }
            catch (Exception error)
            {
                invalidSources++;
                results.Add(new { kind, name, gender, status = "failed", message = error.GetBaseException().Message });
                Console.WriteLine(name + ": " + error.GetBaseException().Message);
            }
        }
        var report = new { total = rows.Length, recovered, invalidSources, results };
        File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Verified {rows.Length}: {recovered} rendered successfully; {invalidSources} invalid sources.");
        if (recovered != 85 || invalidSources != 4) throw new Exception("Unexpected recovery count; inspect report.json.");
    }

    public static void Run(string root)
    {
        foreach (var name in new[] { "hwskull_007.ytd", "mp_fm_body_hair_002.ytd", "mp_airraces_tattoo_007.ytd" })
        {
            var path = Directory.EnumerateFiles(root, name, SearchOption.AllDirectories).Single();
            var file = new YtdFile(); file.Load(File.ReadAllBytes(path));
            foreach (var texture in file.TextureDict?.Textures?.data_items ?? [])
            {
                int? pixels = null;
                try { pixels = DDSIO.GetPixels(texture, 0)?.Length; } catch { }
                Console.WriteLine(JsonSerializer.Serialize(new { file = name, textureName = texture.Name, texture.Width, texture.Height,
                    texture.Levels, format = texture.Format.ToString(), data = texture.Data?.FullData?.Length, pixels }));
            }
        }
        foreach (var name in new[] { "mp_f_freemode_01_mp_f_meta_01^decl_039_u.ydd", "mp_f_freemode_01_mp_f_meta_01^decl_047_u.ydd",
            "mp_f_freemode_01_mp_f_meta_01^decl_051_u.ydd", "mp_f_freemode_01_mp_f_meta_01^uppr_054_r.ydd", "mp_f_freemode_01_female_freemode_hipster^jbib_diff_006_a_uni.ydd" })
        {
            var paths = Directory.EnumerateFiles(root, name, SearchOption.AllDirectories).ToArray();
            foreach (var path in paths)
            {
                var bytes = File.ReadAllBytes(path);
                try
                {
                    var file = new YddFile(); file.Load(bytes);
                    Console.WriteLine(JsonSerializer.Serialize(new { name, length = bytes.Length, drawables = file.Drawables?.Length,
                        models = file.Drawables?.Select(drawable => new { high = drawable.DrawableModels?.High?.Length,
                            medium = drawable.DrawableModels?.Med?.Length, low = drawable.DrawableModels?.Low?.Length }) }));
                    var texturePath = Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.ytd")
                        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
                    if (texturePath is not null)
                    {
                        var textureBytes = File.ReadAllBytes(texturePath);
                        try { var textureFile = new YtdFile(); textureFile.Load(textureBytes); Console.WriteLine("YTD OK " + Path.GetFileName(texturePath)); }
                        catch (Exception error) { Console.WriteLine(JsonSerializer.Serialize(new { textureFile = texturePath, length = textureBytes.Length,
                            header = Convert.ToHexString(textureBytes.AsSpan(0, Math.Min(24, textureBytes.Length))), error = error.Message })); }
                    }
                }
                catch (Exception error)
                {
                    Console.WriteLine(JsonSerializer.Serialize(new { name, length = bytes.Length, header = Convert.ToHexString(bytes.AsSpan(0, Math.Min(16, bytes.Length))), error = error.Message }));
                    try
                    {
                        var textureFile = new YtdFile(); textureFile.Load(bytes);
                        Console.WriteLine(JsonSerializer.Serialize(new { name, mislabeledYtd = textureFile.TextureDict?.Textures?.data_items?.Select(texture => texture.Name) }));
                    }
                    catch { /* The resource is not a readable texture dictionary either. */ }
                }
            }
        }
    }
}
