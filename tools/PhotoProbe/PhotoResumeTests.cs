using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FooxClothStudio;

internal static class PhotoResumeTests
{
    public static void Run()
    {
        CheckSelection();
        var output = Path.Combine(Path.GetTempPath(), "photo-export-" + Guid.NewGuid().ToString("N"));
        var stem = Guid.NewGuid().ToString("D") + "." + Guid.NewGuid().ToString("D");
        Directory.CreateDirectory(output);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(Brushes.Red, null, new System.Windows.Rect(0, 0, 64, 64));
        var source = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32); source.Render(visual);
        var original = Path.Combine(output, stem + ".png");
        new PhotoImageOptions(64, "PNG").Write(original, source);
        var originalBytes = File.ReadAllBytes(original);
        var requested = Path.Combine(output, "Masculino", "Cabelo", stem + ".webp");
        var skip = new PhotoExportSession(output, false);
        Check(skip.FindExisting(requested) == original, "UUID match ignores format and folder");
        Check(skip.SaveImage(requested, _ => throw new Exception("Skipped image was rendered")) == original, "Skip avoids rendering");
        Check(File.ReadAllBytes(original).SequenceEqual(originalBytes) && !File.Exists(requested), "Skipped image unchanged, no duplicate");
        Check(skip.FindExisting(Path.Combine(output, Guid.NewGuid().ToString("D") + "." + Guid.NewGuid().ToString("D") + ".png")) is null, "Different UUID not skipped");
        var replace = new PhotoExportSession(output, true);
        try
        {
            replace.SaveImage(requested, temporary => { File.WriteAllText(temporary, "partial"); throw new InvalidOperationException("Simulated rendering failure"); });
            throw new Exception("Failed render accepted");
        }
        catch (InvalidOperationException) { }
        Check(File.ReadAllBytes(original).SequenceEqual(originalBytes) && !File.Exists(requested), "Failed replacement preserves original");
        Check(!Directory.EnumerateFiles(output, "*.pending-*", SearchOption.AllDirectories).Any(), "Temporary render cleaned after failure");
        replace.SaveImage(requested, temporary => new PhotoImageOptions(64, "WebP").Write(temporary, source));
        Check(File.Exists(requested) && !File.Exists(original), "Replace migrates UUID to chosen folder and format");
        Check(!Directory.EnumerateFiles(output, "*.previous-*", SearchOption.AllDirectories).Any(), "Replacement does not create copies");
        var next = new PhotoExportSession(output, false);
        Check(next.FindExisting(requested) == requested, "Next export finds replacement");
        var tattoo = Guid.NewGuid().ToString("D");
        var male = Path.Combine(output, "tattoos", "Masculino", tattoo + ".png");
        var female = Path.Combine(output, "tattoos", "Feminino", tattoo + ".png");
        next.SaveImage(male, temporary => new PhotoImageOptions(64, "PNG").Write(temporary, source));
        Check(next.FindExisting(female) is null, "Both-gender tattoos have independent photographs");
        next.SaveImage(female, temporary => new PhotoImageOptions(64, "PNG").Write(temporary, source));
        Check(File.Exists(male) && File.Exists(female), "Both tattoo photographs written");
        var legacy = Path.Combine(output, "tattoos", "Masculino", "old_name_12345678.jpg");
        new PhotoImageOptions(64, "JPEG").Write(legacy, source);
        var legacySession = new PhotoExportSession(output, false);
        var renamed = Path.Combine(output, "tattoos", "Masculino", Guid.NewGuid().ToString("D") + ".webp");
        Check(legacySession.FindExisting(renamed, Path.ChangeExtension(legacy, ".webp")) == legacy, "Older tattoo naming remains reusable across formats");
        var indexPath = Path.Combine(output, "index.jsonl");
        var success = JsonSerializer.Serialize(new { kind = "drawable", status = "ok", file = Path.GetRelativePath(output, requested) });
        File.WriteAllText(indexPath, success + "\n{\"incomplete\":");
        var indexed = new PhotoExportSession(output, false);
        using (var index = indexed.OpenIndex())
        {
            Check(!indexed.NeedsIndexEntry(requested), "Skipped success not duplicated in index");
            Check(indexed.NeedsIndexEntry(male), "New image gets an index entry");
            index.WriteLine(JsonSerializer.Serialize(new { kind = "tattoo", status = "ok", file = Path.GetRelativePath(output, male) }));
        }
        foreach (var line in File.ReadLines(indexPath)) { using var json = JsonDocument.Parse(line); }
        Check(File.ReadLines(indexPath).Count() == 2 && Directory.GetFiles(output, "index.jsonl.interrupted-*").Length == 1, "Interrupted index repaired with backup");
        Console.WriteLine("PASS: category/gender selection; skip and replace by UUID across folders/formats; separate gender images; legacy names; atomic replacement without copies; index recovery. Fixtures: " + output);
    }

    private static void CheckSelection()
    {
        var hair = new ClothItem { Category = "Cabelo [hair]", Gender = "Masculino" };
        var shirt = new ClothItem { Category = "Blusa [jbib]", Gender = "Feminino" };
        var dummy = new ClothItem { Category = "Cabelo [hair]", Gender = "Masculino", IsDummy = true };
        var tattoo = new TattooItem { Gender = "Both" };
        var eyes = new AppearanceAsset { Kind = "Olhos", Gender = "Both" };
        var project = new ClothProject { Items = [hair, shirt, dummy], Tattoos = [tattoo], AppearanceAssets = [eyes] };
        var type = typeof(MainWindow).Assembly.GetType("FooxClothStudio.PhotoExportOptionsWindow")!;
        var dialog = (System.Windows.Window)Activator.CreateInstance(type, [project])!;
        object? Invoke(string method, object value) => type.GetMethod(method)!.Invoke(dialog, [value]);
        Dictionary<string, CheckBox> Options(string field) => (Dictionary<string, CheckBox>)type.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dialog)!;
        Check((bool)Invoke("IncludesItem", hair)! && (bool)Invoke("IncludesItem", shirt)! && !(bool)Invoke("IncludesItem", dummy)!, "Defaults select real pieces only");
        Options("_categories")[hair.Category].IsChecked = false;
        Check(!(bool)Invoke("IncludesItem", hair)! && (bool)Invoke("IncludesItem", shirt)!, "Categories filtered independently");
        Options("_genders")["Feminino"].IsChecked = false;
        Check(!(bool)Invoke("IncludesItem", shirt)! && ((string[])Invoke("ExportGenders", "Both")!).SequenceEqual(["Masculino"]), "Gender filtering applies to both-gender assets");
        Check((bool)Invoke("IncludesTattoo", tattoo)! && (bool)Invoke("IncludesAppearance", eyes)!, "Tattoo and appearance selections enabled");
        ((CheckBox)type.GetField("_tattoos", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dialog)!).IsChecked = false;
        Options("_appearanceKinds")["Olhos"].IsChecked = false;
        Check(!(bool)Invoke("IncludesTattoo", tattoo)! && !(bool)Invoke("IncludesAppearance", eyes)!, "Tattoo and appearance can be excluded");
        Check(!(bool)type.GetProperty("OverwriteExisting")!.GetValue(dialog)!, "Default is skip");
        var multiTexture = new ClothItem { TexturePaths = ["a.ytd", "b.ytd", "c.ytd"] };
        Check(!(bool)type.GetProperty("ExportAllTextures")!.GetValue(dialog)!, "Default mode is one photo per piece");
        Check(((int[])Invoke("ExportVariants", multiTexture)!).SequenceEqual([0]), "First mode exports only the first texture");
        ((ComboBox)type.GetField("_variants", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dialog)!).SelectedIndex = 1;
        Check((bool)type.GetProperty("ExportAllTextures")!.GetValue(dialog)!, "All-texture mode is explicit");
        Check(((int[])Invoke("ExportVariants", multiTexture)!).SequenceEqual([0, 1, 2]), "All mode retains every texture without a fixed cap");
        Check(((int[])Invoke("ExportVariants", new ClothItem())!).SequenceEqual([-1]), "Untextured pieces still export once");
        Check(!(bool)type.GetProperty("OverwriteExisting")!.GetValue(dialog)!, "Texture mode does not change UUID skip policy");
        ((ComboBox)type.GetField("_existing", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dialog)!).SelectedIndex = 1;
        Check((bool)type.GetProperty("OverwriteExisting")!.GetValue(dialog)!, "Replacement is explicit");
        Check(type.GetProperty("RebuildHair") is null && type.GetProperty("ResumeExisting") is null, "Special hair/restore modes removed");
        dialog.Close();
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
