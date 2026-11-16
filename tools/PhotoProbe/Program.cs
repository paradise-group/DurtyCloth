using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FooxClothStudio;
using CodeWalker.GameFiles;

internal static class Program
{
    private sealed class InlineProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length == 6 && args[0] == "--verify-photo-failures")
        {
            try { PhotoFailureProbe.Verify(args[1], args[2], args[3], args[4], args[5]); }
            catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
            return;
        }
        if (args.Length == 2 && args[0] == "--inspect-photo-failures")
        {
            try { PhotoFailureProbe.Run(args[1]); }
            catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
            return;
        }
        if (args.Length == 3 && args[0] == "--export-server-photos")
        {
            try { ServerPhotoBatch.Run(args[1], args[2]); }
            catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
            return;
        }
        if (args.Length == 4 && args[0] == "--live-server-photo-check")
        {
            try { ServerPhotoTests.RunLive(args[1], args[2], args[3]); }
            catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
            return;
        }
        if (args.Length == 1 && args[0] == "--server-photo-test")
        {
            try { ServerPhotoTests.Run(); }
            catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
            return;
        }
        if (args.Length == 3 && args[0] == "--gta-photo-check")
        {
            try { GtaPhotoTests.Run(args[1], args[2]); }
            catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
            return;
        }
        if (args.Length == 4 && args[0] == "--hair-photo-check")
        {
            try { HairPhotoTests.Run(args[1], args[2], args[3]); }
            catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
            return;
        }
        if (args.Length == 1 && args[0] == "--photo-resume-test")
        {
            try { PhotoResumeTests.Run(); }
            catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
            return;
        }
        if (args.Length == 1 && args[0] == "--photo-index-test")
        {
            try { PhotoIndexTests.Run(); }
            catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
            return;
        }
        if (args.Length == 2 && args[0] == "--webp-test")
        {
            try { WebPExportTests.Run(args[1]); }
            catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
            return;
        }
        if (args.Length == 4 && args[0] == "--original-dry-run")
        {
            var project = System.Text.Json.JsonSerializer.Deserialize<ClothProject>(File.ReadAllText(args[1]))!;
            var root = Path.GetDirectoryName(Path.GetFullPath(args[1]))!;
            var stages = 0; var timer = System.Diagnostics.Stopwatch.StartNew();
            var map = OriginalResourceExporter.Validate(project, path => Path.IsPathRooted(path) ? path : Path.Combine(root, path), args[2],
                new InlineProgress(stage => { if (++stages % 500 == 0) Console.WriteLine($"{stages} items verified; {timer.Elapsed.TotalSeconds:F1}s"); }));
            var report = $"PASS: verified {map.Entries.Count} original model identities and all referenced asset bytes against the saved project in {timer.Elapsed.TotalSeconds:F1}s. No resource copied or modified.\n";
            File.WriteAllText(args[3], report); Console.WriteLine(report);
            return;
        }
        if (args.Length == 2 && args[0] == "--build-ui")
        {
            var project = System.Text.Json.JsonSerializer.Deserialize<ClothProject>(File.ReadAllText(args[1]))!;
            var root = Path.GetDirectoryName(Path.GetFullPath(args[1]))!;
            var window = new BuildProjectWindow(project, path => Path.IsPathRooted(path) ? path : Path.Combine(root, path));
            var panel = (System.Windows.Controls.StackPanel)((System.Windows.Controls.ScrollViewer)window.Content).Content;
            var preserve = panel.Children.OfType<System.Windows.Controls.CheckBox>().Single(box => box.Content.ToString()!.StartsWith("Keep original collections"));
            var tattoos = panel.Children.OfType<System.Windows.Controls.CheckBox>().Single(box => box.Content.ToString()!.StartsWith("Include tattoos"));
            if (preserve.IsChecked != true || tattoos.IsEnabled || tattoos.IsChecked == true) throw new Exception("Wrong preservation/tattoo defaults.");
            preserve.IsChecked = false;
            if (!tattoos.IsEnabled || tattoos.IsChecked == true) throw new Exception("Rebuilt mode included unplaced tattoos.");
            preserve.IsChecked = true;
            if (tattoos.IsEnabled || project.Tattoos.Count != 391) throw new Exception("Mode switch changed the project tattoo data.");
            window.Close(); Console.WriteLine("PASS: original build mode detected and selected, tattoo-generation controls disabled without dropping source tattoos, mode switches leave the project unchanged.");
            return;
        }
        if (args.Length == 3 && args[0] == "--source-map")
        {
            var project = System.Text.Json.JsonSerializer.Deserialize<ClothProject>(File.ReadAllText(args[1]))!;
            var map = OriginalResourceExporter.Describe(project);
            Directory.CreateDirectory(args[2]); ClothingIdentityMap.Write(map, args[2]);
            var groups = map.Entries.Where(entry => entry.AliasOf is null).GroupBy(entry => (entry.Model, entry.Kind, entry.Slot, entry.Collection)).ToArray();
            Console.WriteLine($"{map.Entries.Count} model identities; {map.Entries.Count(entry => entry.AliasOf is not null)} explicit model aliases; {groups.Length} original collection/slot groups.");
            foreach (var group in groups.OrderByDescending(group => group.Count()).Take(8)) Console.WriteLine(group.Key + ": " + group.Count());
            return;
        }
        if (args.Length == 3 && args[0] == "--validation-report")
        {
            var project = System.Text.Json.JsonSerializer.Deserialize<ClothProject>(File.ReadAllText(args[1]))!;
            var window = new MainWindow();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(MainWindow).GetField("_project", flags)!.SetValue(window, project);
            typeof(MainWindow).GetField("_projectPath", flags)!.SetValue(window, Path.GetFullPath(args[1]));
            var report = (List<string>)typeof(MainWindow).GetMethod("ValidateProject", flags)!.Invoke(window, null)!;
            File.WriteAllLines(args[2], report);
            var pending = report.Count(message => message.StartsWith("Pending:"));
            var warnings = report.Count(message => message.StartsWith("Aviso:") || message.StartsWith("Warning:"));
            Console.WriteLine($"Errors: {report.Count - pending - warnings}; warnings: {warnings}; pending: {pending}");
            var dialog = new ErrorListWindow(report);
            var panel = (System.Windows.Controls.StackPanel)dialog.Content;
            Console.WriteLine(((System.Windows.Controls.TextBlock)panel.Children[0]).Text);
            dialog.Close(); window.Close();
            return;
        }
        if (args.Length is 3 or 4 && args[0] == "--project-dependencies")
        {
            var project = System.Text.Json.JsonSerializer.Deserialize<ClothProject>(File.ReadAllText(args[1]))!;
            if (args.Length == 4 && args[3] == "clothes-only") project.Tattoos.Clear();
            var root = Path.GetDirectoryName(Path.GetFullPath(args[1]))!;
            var errors = BuildDependencies.Prepare(project, path => Path.IsPathRooted(path) ? path : Path.Combine(root, path), "D:\\Jogos\\GTAV", Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[2]))!, "texture-cache"));
            File.WriteAllLines(args[2], errors);
            Console.WriteLine($"{errors.Count} errors; {errors.Count(error => error.StartsWith("Tattoo "))} tattoo entries");
            foreach (var error in errors.Where(error => !error.StartsWith("Tattoo "))) Console.WriteLine(error);
            return;
        }
        if (args.Length == 3 && args[0] == "--dependency")
        {
            var type = ClothCatalog.ResolveType(args[1]);
            var item = new ClothItem { ModelPath = args[1], OriginalModelFileName = Path.GetFileName(args[1]), Name = Path.GetFileNameWithoutExtension(args[1]),
                Category = type.Category, Gender = ClothCatalog.ResolveGender(args[1]), SourceIndex = type.Index };
            var id = item.Id;
            var project = new ClothProject { Name = "dependencyprobe", DlcName = "dependencyprobe", Items = [item] };
            var errors = BuildDependencies.Prepare(project, Path.GetFullPath, "D:\\Jogos\\GTAV");
            if (errors.Count > 0) throw new Exception(string.Join("\n", errors));
            if (item.Id != id || item.TexturePaths.Count != 1) throw new Exception("Shared texture resolution lost item identity.");
            var output = AddonExporter.Export(project, Path.GetFullPath, args[2]);
            if (FiveMResourceValidator.Validate(output).Count > 0) throw new Exception("Dependency build failed validation.");
            Console.WriteLine("PASS: model dependencies resolve and build valid addon metadata without changing the permanent item ID. " + output);
            return;
        }
        if (args.Length == 2 && args[0] == "--materials")
        {
            var ydd = new YddFile(); ydd.Load(File.ReadAllBytes(args[1]));
            foreach (var drawable in ydd.Drawables ?? [])
            {
                Console.WriteLine("Embedded: " + string.Join(", ", drawable.ShaderGroup?.TextureDictionary?.Textures?.data_items?.Select(t => t.Name) ?? []));
                foreach (var shader in drawable.ShaderGroup?.Shaders?.data_items ?? [])
                {
                    Console.WriteLine("Shader: " + shader.FileName);
                    var parameters = shader.ParametersList?.Parameters ?? [];
                    var hashes = shader.ParametersList?.Hashes ?? [];
                    for (var i = 0; i < Math.Min(parameters.Length, hashes.Length); i++)
                        if (parameters[i]?.Data is TextureBase texture) Console.WriteLine("Sampler: " + hashes[i] + " = " + texture.Name);
                }
                foreach (var part in ModelPreview.Read(args[1], false).Parts)
                    Console.WriteLine("Diffuse: " + part.TextureName);
            }
            return;
        }
        if (args.Length == 2 && args[0] == "--missing")
        {
            var items = ClothCatalog.Import(Directory.EnumerateFiles(args[1], "*", SearchOption.AllDirectories));
            foreach (var group in items.Where(i => i.TexturePaths.Count == 0).GroupBy(i => i.Category))
                Console.WriteLine(group.Key + ": " + group.Count() + " / " + group.First().Name);
            return;
        }
        if (args.Length == 3 && args[0] == "--encode")
        {
            Directory.CreateDirectory(args[2]);
            var source = new PngBitmapDecoder(new Uri(Path.GetFullPath(args[1])), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            foreach (var options in new[] { new PhotoImageOptions(), new PhotoImageOptions(384),
                new PhotoImageOptions(512, "JPEG", 85), new PhotoImageOptions(512, "JPEG", 50) })
            {
                var path = Path.Combine(args[2], $"sample_{options.PixelSize}_{options.Quality}{options.Extension}");
                options.Write(path, source);
                var decoded = BitmapDecoder.Create(new Uri(Path.GetFullPath(path)), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
                if (decoded.PixelWidth != options.PixelSize || decoded.PixelHeight != options.PixelSize)
                    throw new InvalidDataException("Incorrect output dimensions.");
                var bgra = new FormatConvertedBitmap(decoded, PixelFormats.Bgra32, null, 0);
                var corner = new byte[4];
                bgra.CopyPixels(new System.Windows.Int32Rect(0, 0, 1, 1), corner, 4, 0);
                if (corner[3] != (options.Format == "JPEG" ? 255 : 0))
                    throw new InvalidDataException("Incorrect output alpha.");
                Console.WriteLine($"{Path.GetFileName(path)}: {new FileInfo(path).Length} bytes; {decoded.PixelWidth}x{decoded.PixelHeight}; corner alpha={corner[3]}");
            }
            return;
        }
        if (args.Length == 3 && args[0] == "--prop")
        {
            var gender = args[1].Contains("mp_f_freemode_01", StringComparison.OrdinalIgnoreCase) ? "Feminino" : "Masculino";
            var ped = PreviewPhotoRenderer.LoadPed("D:\\Jogos\\GTAV", gender);
            var model = ModelPreview.Read(args[1], false);
            PreviewPhotoRenderer.Save(args[2], model,
                new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase), ped,
                ClothCatalog.ResolveType(args[1]).Category, Colors.Lime);
            Console.WriteLine($"Prop photo: {new FileInfo(args[2]).Length} bytes");
            return;
        }
        if (args.Length == 2 && args[0] == "--bounds")
        {
            var model = ModelPreview.Read(args[1], false);
            var points = model.Parts.SelectMany(part => part.Positions).ToArray();
            Console.WriteLine($"X {points.Min(point => point.X):F2}..{points.Max(point => point.X):F2}; " +
                $"Y {points.Min(point => point.Y):F2}..{points.Max(point => point.Y):F2}; " +
                $"Z {points.Min(point => point.Z):F2}..{points.Max(point => point.Z):F2}");
            return;
        }
        if (args.Length == 3 && args[0] == "--transparent")
        {
            var model = ModelPreview.Read(args[1], false);
            PreviewPhotoRenderer.Save(args[2], model,
                new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase), null,
                ClothCatalog.ResolveType(args[1]).Category, Colors.Transparent);
            var transparentImage = new PngBitmapDecoder(new Uri(args[2]), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            var transparentPixels = new byte[transparentImage.PixelWidth * transparentImage.PixelHeight * 4];
            transparentImage.CopyPixels(transparentPixels, transparentImage.PixelWidth * 4, 0);
            var transparent = 0; var visible = 0;
            for (var i = 0; i < transparentPixels.Length; i += 4)
            {
                if (transparentPixels[i + 3] == 0) transparent++;
                if (transparentPixels[i + 3] > 0) visible++;
            }
            Console.WriteLine($"Transparent pixels: {transparent}; visible pixels: {visible}");
            if (transparent == 0 || visible == 0) throw new InvalidDataException("PNG transparency did not render correctly.");
            return;
        }
        if (args.Length != 2) throw new ArgumentException("Model path and output PNG are required.");
        if (args[0] == "--inventory")
        {
            var tattoos = TattooImporter.Import(args[1]);
            var appearance = AppearanceImporter.Import(args[1]);
            Console.WriteLine($"Tattoos: {tattoos.Count}; unplaced: {tattoos.Count(t => string.IsNullOrEmpty(t.Zone))}; appearance: {appearance.Count}");
            foreach (var group in appearance.GroupBy(a => a.Kind).OrderBy(g => g.Key))
                Console.WriteLine($"{group.Key}: {group.Count()}");
            return;
        }
        if (args[0] == "--bundle")
        {
            var project = new ClothProject
            {
                Name = "photo bundle probe",
                Tattoos = [TattooImporter.Import(args[1]).First()],
                AppearanceAssets = [AppearanceImporter.Import(args[1]).First()]
            };
            var bundle = Path.Combine(Path.GetTempPath(), "photo_probe_" + Guid.NewGuid().ToString("N") + ".fcsbundle");
            ProjectBundle.Export(project, Path.GetFullPath, bundle);
            var restored = ProjectBundle.Import(bundle, Path.GetTempPath());
            var loaded = System.Text.Json.JsonSerializer.Deserialize<ClothProject>(File.ReadAllText(restored))!;
            Console.WriteLine($"Bundle: {loaded.Tattoos.Count} tattoo; {loaded.AppearanceAssets.Count} appearance; " +
                $"files present: {File.Exists(Path.Combine(Path.GetDirectoryName(restored)!, loaded.Tattoos[0].ImagePath)) && File.Exists(Path.Combine(Path.GetDirectoryName(restored)!, loaded.AppearanceAssets[0].TexturePath))}");
            return;
        }
        if (args[0] == "--bones")
        {
            foreach (var asset in GamePedAssets.Load(args[1], "Masculino"))
            {
                var ydd = new YddFile(); ydd.Load(File.ReadAllBytes(asset.ModelPath));
                var bones = ydd.Drawables?.SelectMany(d => d.Skeleton?.Bones?.Items ?? []).ToArray() ?? [];
                Console.WriteLine($"{asset.Slot}: {bones.Length} bones");
                foreach (var bone in bones.Where(b => b.Tag is 31086 or 18905 or 57005 or 28422 or 6286 or 52301))
                    Console.WriteLine($"  {bone.Tag} {bone.Translation} parent={bone.Parent?.Tag}");
            }
            return;
        }
        if (args[0] is "--tattoo" or "--unplaced")
        {
            var ped = PreviewPhotoRenderer.LoadPed("D:\\Jogos\\GTAV", "Masculino");
            var tattooPixels = new byte[128 * 128 * 4];
            for (var y = 0; y < 128; y++)
                for (var x = 0; x < 128; x++)
                {
                    var offset = (y * 128 + x) * 4;
                    tattooPixels[offset + 2] = 255;
                    tattooPixels[offset + 3] = x > 24 && x < 104 && y > 24 && y < 104 ? (byte)220 : (byte)0;
                }
            var tattooImage = BitmapSource.Create(128, 128, 96, 96, PixelFormats.Bgra32, null, tattooPixels, 128 * 4);
            tattooImage.Freeze();
            if (args[0] == "--tattoo")
                PreviewPhotoRenderer.SaveTattoo(args[1], new TattooItem { Zone = "ZONE_TORSO" }, tattooImage, ped, Colors.Lime);
            else PreviewPhotoRenderer.SaveUnplacedTattoo(args[1], tattooImage, ped, Colors.Lime);
        }
        else
        {
            var model = ModelPreview.Read(args[0], false);
            PreviewPhotoRenderer.Save(args[1], model,
                new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase), null,
                ClothCatalog.ResolveType(args[0]).Category, Colors.Lime);
        }
        var image = new PngBitmapDecoder(new Uri(args[1]), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        var pixels = new byte[image.PixelWidth * image.PixelHeight * 4];
        image.CopyPixels(pixels, image.PixelWidth * 4, 0);
        var green = 0;
        for (var i = 0; i < pixels.Length; i += 4)
            if (pixels[i] == 0 && pixels[i + 1] == 255 && pixels[i + 2] == 0) green++;
        Console.WriteLine($"{image.PixelWidth}x{image.PixelHeight}; {new FileInfo(args[1]).Length} bytes; background pixels {green}/{pixels.Length / 4}");
    }
}
