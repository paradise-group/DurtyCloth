using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodeWalker.GameFiles;
using CodeWalker.Utils;
using FooxClothStudio;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var window = new MainWindow(); // Intentionally never shown: user is recording another app.
        var binding = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(MainWindow).GetMethod("LoadProject", binding)!.Invoke(window, [Path.GetFullPath(args[0])]);
        var project = (ClothProject)typeof(MainWindow).GetField("_project", binding)!.GetValue(window)!;
        var list = (ListBox)window.FindName("ItemList");
        var item = project.Items.First(x => !x.IsDummy);
        list.SelectedItem = item;
        var model = Path.IsPathRooted(item.ModelPath) ? item.ModelPath : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0]))!, item.ModelPath);
        var hash = SHA256.HashData(File.ReadAllBytes(model)); var count = project.Items.Count;
        void ItemMenu() => typeof(MainWindow).GetMethod("ItemContextMenu_Opening", binding)!.Invoke(window, [list, null]);
        void Click(ContextMenu menu, string title) => menu.Items.OfType<MenuItem>().Single(x => (string)x.Header == title).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        ItemMenu();
        if (!list.ContextMenu.Items.OfType<MenuItem>().Any(x => (string)x.Header == "Export with Textures")) throw new Exception("Item context menu missing export action.");
        Click(list.ContextMenu, "Create Copy for Other Gender");
        var copy = (ClothItem)list.SelectedItem;
        if (project.Items.Count != count + 1 || copy.Id == item.Id || copy.Gender == item.Gender || copy.SourceIndex is not null || ReferenceEquals(copy.TexturePaths, item.TexturePaths))
            throw new Exception("Other-gender copy was not independent or reused source indices.");
        var position = copy.ExportPosition;
        ItemMenu(); Click(list.ContextMenu, "Replace with Dummy");
        if (!copy.IsDummy || copy.ExportPosition != position || copy.TexturePaths.Count != 0 || copy.ModelPath != "" || item.IsDummy)
            throw new Exception("Dummy replacement lost its slot or modified the source item.");
        ItemMenu(); Click(list.ContextMenu, "Remove");
        if (project.Items.Count != count) throw new Exception("Removing the temporary reservation failed.");
        list.SelectedItem = item;
        var textures = (ListBox)window.FindName("TextureList"); textures.SelectedIndex = 0;
        typeof(MainWindow).GetMethod("TextureContextMenu_Opening", binding)!.Invoke(window, [textures, null]);
        var textureCount = item.TexturePaths.Count;
        var originalTexture = item.TexturePaths[0];
        var originalTextureName = item.OriginalTextureFileNames[0];
        var originalPlan = NativeItemExporter.Plan(item, path => Path.IsPathRooted(path) ? path : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0]))!, path)).Select(asset => asset.Name).ToArray();
        Click(textures.ContextMenu, "Replace with Dummy");
        if (item.TexturePaths.Count != textureCount || item.TexturePaths[0] != "" || item.TextureEffects[0].TimeCycleModifier != "") throw new Exception("Texture dummy shifted variants or retained effects.");
        item.TexturePaths[0] = originalTexture;
        item.OriginalTextureFileNames[0] = originalTextureName;
        item.TextureLabels[0] = "Edited display name";
        var roundtrip = JsonSerializer.Deserialize<ClothProject>(JsonSerializer.Serialize(project))!;
        if (roundtrip.Items.Single(x => x.Id == item.Id).TextureLabels[0] != "Edited display name") throw new Exception("Texture label did not survive project roundtrip.");
        var renamedPlan = NativeItemExporter.Plan(item, path => Path.IsPathRooted(path) ? path : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0]))!, path)).Select(asset => asset.Name).ToArray();
        if (!originalPlan.SequenceEqual(renamedPlan)) throw new Exception("Renaming a display label changed native asset names.");
        item.TexturePaths.Add(originalTexture); item.OriginalTextureFileNames.Add(originalTextureName);
        item.TextureLabels[0] = "First"; item.TextureLabels.Add("Second");
        item.TextureEffects.Add(new TextureEffect { TimeCycleModifier = "glasses_black", Intensity = 0.5f });
        typeof(MainWindow).GetMethod("RefreshTextureList", binding)!.Invoke(window, [item, 0]);
        typeof(MainWindow).GetMethod("MoveTexture", binding)!.Invoke(window, [1]);
        if (item.TextureLabels[0] != "Second" || item.TextureLabels[1] != "First" || item.TextureEffects[0].TimeCycleModifier != "glasses_black")
            throw new Exception("Reordering textures separated display labels from their effects.");
        if (textures.SelectedIndex != 1) throw new Exception("Reordering did not keep the moved texture selected.");
        if (!hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(model)))) throw new Exception("Context edit modified the original YDD.");
        var sourceTexture = Path.IsPathRooted(originalTexture) ? originalTexture : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0]))!, originalTexture);
        if (!File.Exists(sourceTexture)) throw new Exception("Texture dummy deleted the original YTD.");
        var output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
        var exported = Path.Combine(output, "exported.ydd"); AssetFileExporter.Copy(model, exported);
        if (!hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(exported)))) throw new Exception("Single-file export changed the native model.");
        try { AssetFileExporter.Copy(Path.Combine(output, "missing.ydd"), exported, true); throw new Exception("Missing source was accepted."); }
        catch (FileNotFoundException) { if (!hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(exported)))) throw new Exception("Failed export overwrote the existing destination."); }
        if (Directory.EnumerateFiles(output, ".foox-export-*.tmp").Any()) throw new Exception("Export left temporary files.");
        var nativeTexture = new YtdFile(); nativeTexture.Load(File.ReadAllBytes(sourceTexture));
        var textureEntries = nativeTexture.TextureDict.Textures.data_items;
        var changedTexture = DDSIO.GetTexture(DDSIO.GetDDSFile(textureEntries[0]));
        changedTexture.Name = textureEntries[0].Name; changedTexture.NameHash = textureEntries[0].NameHash;
        changedTexture.Data.FullData[0] ^= 1;
        try
        {
            AssetAnalysisExporter.ValidateTextureRoundtrip([textureEntries[0]], [changedTexture]);
            throw new Exception("Changed image data with the same texture count was accepted.");
        }
        catch (InvalidDataException) { }
        foreach (var format in new[] { "XML", "DDS", "PNG" })
        {
            var directory = AssetAnalysisExporter.Export(sourceTexture, output, format);
            if (format == "XML" && Directory.GetFiles(directory, "*.xml").Length != 1) throw new Exception("YTD XML missing.");
            if (format == "DDS" && Directory.GetFiles(directory, "*.dds").Length != textureEntries.Length) throw new Exception("DDS export lost entries.");
            if (format == "PNG")
                foreach (var texture in textureEntries)
                {
                    var png = new PngBitmapDecoder(new Uri(Path.Combine(directory, texture.Name + ".png")), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                    var converted = new FormatConvertedBitmap(png, PixelFormats.Bgra32, null, 0);
                    var pixels = new byte[texture.Width * texture.Height * 4]; converted.CopyPixels(pixels, texture.Width * 4, 0);
                    if (!SHA256.HashData(pixels).SequenceEqual(SHA256.HashData(DDSIO.GetPixels(texture, 0)))) throw new Exception("PNG changed decoded texture pixels.");
                }
        }
        AssetAnalysisExporter.Export(model, output, "XML");
        if (!hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(model)))) throw new Exception("XML export changed source YDD.");
        window.Close(); application.Shutdown();
        Console.WriteLine("OK: context actions, copy/dummies/labels, native copy, YDD/YTD XML reimport, DDS mipmaps, PNG pixel comparison and original files preserved; no UI windows shown. " + output);
    }
}
