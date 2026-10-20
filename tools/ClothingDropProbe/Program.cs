using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FooxClothStudio;

internal static class Program
{
    [STAThread] static void Main(string[] args)
    {
        if (ClothCatalog.ResolveType("mp_f_freemode_01_demo^jbib_1000_u.ydd").Index != 1000 || ClothCatalog.ResolveType("jbib_diff_1000_a_uni.ytd").Index != 1000) throw new Exception("Four digit drawable/texture index was truncated.");
        if (ClothCatalog.ResolveType("jbib_123abc.ydd").Index is not null || ClothCatalog.ResolveType("jbib_999999999999999999999_u.ydd").Index is not null) throw new Exception("Malformed or overflowing index was accepted.");
        if (ClothCatalog.ResolveGender(@"C:\male\mp_f_freemode_01_demo^jbib_000_u.ydd") != "Feminino" || ClothCatalog.ResolveGender(@"C:\female\mp_m_freemode_01_demo^jbib_000_u.ydd") != "Masculino" || ClothCatalog.ResolveGender(@"C:\male\female\jbib_000_u.ydd") != "Feminino") throw new Exception("Folder name overrode explicit or nearer gender information.");
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var owner = new MainWindow { Left = -10000, Top = -10000, WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false }; owner.Show();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var fixturePath = Path.GetFullPath(args[0]);
        var fixture = System.Text.Json.JsonSerializer.Deserialize<ClothProject>(File.ReadAllText(fixturePath))!;
        var fixtureItem = fixture.Items.Single();
        var heavy = new ClothItem { ModelSizeBytes = 5 * 1048576, TexturePaths = ["texture.ytd"] };
        if (!heavy.HeavyAsset || heavy.MissingTexture) throw new Exception("Heavy asset warning mixed with missing texture.");
        heavy.ModelSizeBytes = 100; heavy.MaxTextureSizeBytes = 5 * 1048576;
        if (!heavy.HeavyAsset) throw new Exception("Heavy texture warning absent.");
        heavy.MaxTextureSizeBytes = 100; if (heavy.HeavyAsset) throw new Exception("Small asset incorrectly warned.");
        var marker = new ClothItem();
        if (!marker.MissingTexture) throw new Exception("Empty drawable missing its warning.");
        marker.TexturePaths.Add(""); if (!marker.MissingTexture) throw new Exception("Dummy texture removed warning.");
        marker.TexturePaths.Add("texture.ytd"); if (marker.MissingTexture) throw new Exception("Texture did not clear warning.");
        marker.IsDummy = true; marker.TexturePaths.Clear(); if (marker.MissingTexture) throw new Exception("Reserved drawable shows missing texture warning.");
        var sampleRow = (FrameworkElement)((ListBox)owner.FindName("ItemList")).ItemTemplate.LoadContent();
        marker.IsDummy = false; sampleRow.DataContext = marker;
        sampleRow.Measure(new Size(400,50)); sampleRow.Arrange(new Rect(0,0,400,50)); sampleRow.UpdateLayout();
        IEnumerable<DependencyObject> Descendants(DependencyObject node) { for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node); i++) { var child = System.Windows.Media.VisualTreeHelper.GetChild(node,i); yield return child; foreach (var descendant in Descendants(child)) yield return descendant; } }
        var warning = Descendants(sampleRow).OfType<TextBlock>().Single(t => t.Text == "❗");
        if (warning.Visibility != Visibility.Visible || warning.Foreground is not System.Windows.Media.SolidColorBrush red || red.Color.R != 255 || red.Color.G != 69) throw new Exception("Missing-texture marker is not visibly red.");

        var fixtureFolder = Path.GetDirectoryName(fixturePath)!;
        var dropFolder = Path.Combine(Path.GetTempPath(), "paradise-drop-probe-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dropFolder);
        var model = Path.Combine(dropFolder, fixtureItem.OriginalModelFileName!); File.Copy(Path.Combine(fixtureFolder, fixtureItem.ModelPath), model);
        File.Copy(Path.Combine(fixtureFolder, fixtureItem.TexturePaths[0]), Path.Combine(dropFolder, fixtureItem.OriginalTextureFileNames[0]));
        var metricsProject = new ClothProject { Items = [new ClothItem { Name = "Saved metrics", ModelPath = model, Gender = "Masculino", Category = "Acessórios [teef]", ModelSizeBytes = 123, MaxTextureSizeBytes = 456 }] };
        var metricsPath = Path.Combine(dropFolder, "saved-metrics.fcsproj"); File.WriteAllText(metricsPath, System.Text.Json.JsonSerializer.Serialize(metricsProject));
        typeof(MainWindow).GetMethod("LoadProject", flags)!.Invoke(owner, [metricsPath]);
        var reopenedMetrics = (ClothProject)typeof(MainWindow).GetField("_project", flags)!.GetValue(owner)!;
        if (reopenedMetrics.Items.Single().ModelSizeBytes != 123 || reopenedMetrics.Items.Single().MaxTextureSizeBytes != 456) throw new Exception("Opening saved metrics rescanned asset sizes.");
        var large = new ClothProject { Items = Enumerable.Range(0, 805).Select(i => new ClothItem { Name = "Metadata " + i, ModelPath = "missing_" + i + ".ydd", Gender = "Feminino", Category = "Blusa [jbib]" }).ToList() };
        var largePath = Path.Combine(dropFolder, "metadata-only.fcsproj"); File.WriteAllText(largePath, System.Text.Json.JsonSerializer.Serialize(large));
        var decodeCounter = typeof(TextureContent).GetField("_decodeCount", BindingFlags.Static | BindingFlags.NonPublic)!;
        var beforeOpen = (long)decodeCounter.GetValue(null)!;
        typeof(MainWindow).GetMethod("LoadProject", flags)!.Invoke(owner, [largePath]);
        if ((long)decodeCounter.GetValue(null)! != beforeOpen || typeof(MainWindow).GetField("_currentModelData", flags)!.GetValue(owner) is not null) throw new Exception("Opening metadata preloaded assets or checked duplicates.");
        typeof(MainWindow).GetField("_project", flags)!.SetValue(owner, new ClothProject());
        var textureSource = Path.Combine(dropFolder, fixtureItem.OriginalTextureFileNames[0]);
        TextureContent.Fingerprint(textureSource);
        var countField = typeof(TextureContent).GetField("_decodeCount", BindingFlags.Static | BindingFlags.NonPublic)!;
        var decodes = (long)countField.GetValue(null)!;
        TextureContent.Fingerprint(textureSource);
        if ((long)countField.GetValue(null)! != decodes) throw new Exception("Repeated duplicate check decoded the same texture again.");
        File.SetLastWriteTimeUtc(textureSource, File.GetLastWriteTimeUtc(textureSource).AddSeconds(2)); TextureContent.Fingerprint(textureSource);
        if ((long)countField.GetValue(null)! != decodes + 1) throw new Exception("Modified texture did not invalidate duplicate cache.");
        using (var lockedModel = new FileStream(model, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var lazyProject = new ClothProject();
            var lazyItem = new ClothItem { ModelPath = model, Gender = "Masculino", Category = "Acessórios [teef]", SourceIndex = 0 };
            if (CatalogMerger.Merge(lazyProject, [lazyItem], Path.GetFullPath, _ => { }).AddedPieces != 1) throw new Exception("New model did not import without preloading.");
        }
        var modelOnlyFiles = ClothingDrop.Materialize(new DataObject(DataFormats.FileDrop, new[] { model }));
        if (modelOnlyFiles.Length != 1 || !ClothCatalog.Import(modelOnlyFiles).Single().MissingTexture) throw new Exception("Model-only drag picked up adjacent texture automatically.");
        var data = new DataObject(DataFormats.FileDrop, new[] { model, textureSource });
        var files = ClothingDrop.Materialize(data);
        var separateTextureFolder = Path.Combine(dropFolder, "texture-only"); Directory.CreateDirectory(separateTextureFolder);
        var separateTexture = Path.Combine(separateTextureFolder, fixtureItem.OriginalTextureFileNames[0]); File.Copy(Path.Combine(dropFolder, fixtureItem.OriginalTextureFileNames[0]), separateTexture);
        if (ClothCatalog.Import([model, separateTexture]).Single().TexturePaths.Single() != separateTexture) throw new Exception("Unambiguous texture from another temporary folder was not associated.");
        var anotherModelFolder = Path.Combine(dropFolder, "another-model"); Directory.CreateDirectory(anotherModelFolder);
        var anotherModel = Path.Combine(anotherModelFolder, Path.GetFileName(model)); File.Copy(model, anotherModel);
        if (ClothCatalog.Import([model, anotherModel, separateTexture]).Any(i => i.TexturePaths.Count != 0)) throw new Exception("Ambiguous cross-folder texture was attached to multiple drawables.");
        var separatePhysics = Path.Combine(separateTextureFolder, System.Text.RegularExpressions.Regex.Replace(Path.GetFileNameWithoutExtension(model), @"_(?:u|r)$", "") + ".yld"); File.WriteAllBytes(separatePhysics, [1]);
        if (ClothCatalog.Import([model, separatePhysics]).Single().PhysicsPath != separatePhysics) throw new Exception("Unambiguous YLD from another folder was not associated.");
        if (ClothCatalog.Import([model, anotherModel, separatePhysics]).Any(i => i.PhysicsPath is not null)) throw new Exception("Ambiguous YLD was attached to several models.");
        var separateAlternate = Path.Combine(separateTextureFolder, Path.GetFileNameWithoutExtension(model) + "_1.ydd"); File.Copy(model, separateAlternate);
        if (ClothCatalog.Import([model, separateAlternate]).Single().AlternateModelPaths.Single() != separateAlternate) throw new Exception("Unambiguous alternate from another folder was not associated.");
        if (ClothCatalog.Import([model, anotherModel, separateAlternate]).Any(i => i.AlternateModelPaths.Count != 0)) throw new Exception("Ambiguous alternate was attached to several models.");
        var secondAlternate = Path.Combine(separateTextureFolder, Path.GetFileNameWithoutExtension(model) + "_2.ydd"); File.Copy(model, secondAlternate);
        try { ClothCatalog.Import([model, secondAlternate]); throw new Exception("Alternate _2 was silently renumbered to _1."); } catch (InvalidDataException) { }
        if (!ClothCatalog.Import([model, secondAlternate, separateAlternate]).Single().AlternateModelPaths.SequenceEqual(new[] { separateAlternate, secondAlternate })) throw new Exception("Consecutive alternates did not preserve GTA order.");
        if (!owner.AllowDrop || !files.Any(f => f.EndsWith(".ytd"))) throw new Exception("Drop disabled or missing companion texture.");
        typeof(MainWindow).GetMethod("ImportFiles", flags)!.Invoke(owner, [files]);
        var project = (ClothProject)typeof(MainWindow).GetField("_project", flags)!.GetValue(owner)!;
        var item = project.Items.Single();
        if (!item.Category.EndsWith("[teef]") || item.Gender != "Masculino" || item.TexturePaths.Count != 1) throw new Exception("Automatic type/gender/texture assignment failed.");
        ((ListBox)owner.FindName("ItemList")).SelectedItem = item;
        var otherGender = new ClothItem { ModelPath = item.ModelPath, Category = item.Category, Gender = "Feminino", SourceIndex = item.SourceIndex };
        project.Items.Add(otherGender);
        var checkedSlot = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) =>
        {
            var modal = app.Windows.OfType<DuplicateTextureWindow>().FirstOrDefault(); if (modal is null) return; timer.Stop();
            var panel = (StackPanel)modal.Content;
            if (!panel.Children.OfType<TextBlock>().Single().Text.Contains("slot A (posição 0)")) throw new Exception("Duplicate warning omitted the existing slot.");
            checkedSlot = true;
            var button = panel.Children.OfType<WrapPanel>().Single().Children.OfType<Button>().Single(b => (string)b.Content == "Don't import");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
        timer.Start();
        // A second drop is copied to another folder; model identity and texture contents must still match.
        typeof(MainWindow).GetMethod("ImportFiles", flags)!.Invoke(owner, [ClothingDrop.Materialize(data)]);
        if (!checkedSlot || project.Items.Count != 2 || item.TexturePaths.Count != 1 || otherGender.TexturePaths.Count != 0) throw new Exception("Duplicate drop changed the opposite gender or created extra items.");
        checkedSlot = false; timer.Start(); ((ListBox)owner.FindName("ItemList")).SelectedItem = null;
        var textureDrop = new DataObject(DataFormats.FileDrop, new[] { Path.Combine(dropFolder, fixtureItem.OriginalTextureFileNames[0]) });
        typeof(MainWindow).GetMethod("ImportFiles", flags)!.Invoke(owner, [ClothingDrop.Materialize(textureDrop)]);
        if (!checkedSlot || item.TexturePaths.Count != 1) throw new Exception("Texture-only drop did not find its matching drawable/slot.");
        var descriptor = new byte[4 + 592]; BitConverter.GetBytes(1).CopyTo(descriptor, 0);
        System.Text.Encoding.Unicode.GetBytes(Path.GetFileName(model)).CopyTo(descriptor, 76);
        var virtualData = new DataObject(); virtualData.SetData("FileGroupDescriptorW", new MemoryStream(descriptor)); virtualData.SetData("FileContents", new MemoryStream(File.ReadAllBytes(model)));
        var virtualFiles = ClothingDrop.Materialize(virtualData);
        if (!File.ReadAllBytes(model).SequenceEqual(File.ReadAllBytes(virtualFiles.Single()))) throw new Exception("Virtual drop content changed.");
        var repeatedVirtualFiles = ClothingDrop.Materialize(virtualData);
        if (!File.ReadAllBytes(model).SequenceEqual(File.ReadAllBytes(repeatedVirtualFiles.Single()))) throw new Exception("Reading a virtual drop consumed or disposed its source stream.");
        var virtualBytes = File.ReadAllBytes(model);
        BitConverter.GetBytes(0x40u).CopyTo(descriptor, 4); BitConverter.GetBytes((uint)virtualBytes.Length).CopyTo(descriptor, 72);
        virtualData.SetData("FileGroupDescriptorW", new MemoryStream(descriptor));
        virtualData.SetData("FileContents", new MemoryStream(virtualBytes.Concat(new byte[16]).ToArray()));
        var paddedFiles = ClothingDrop.Materialize(virtualData);
        if (!File.ReadAllBytes(paddedFiles.Single()).SequenceEqual(virtualBytes)) throw new Exception("Virtual file padding was kept past its declared size.");
        virtualData.SetData("FileContents", new MemoryStream(virtualBytes[..^1]));
        try { ClothingDrop.Materialize(virtualData); throw new Exception("Truncated virtual file was accepted."); } catch (InvalidDataException) { }
        var nativeBytes = Enumerable.Range(0, 140003).Select(i => (byte)(i % 251)).ToArray();
        var memory = GlobalAlloc(0x42, (UIntPtr)nativeBytes.Length);
        if (memory == IntPtr.Zero) throw new Exception("Native test allocation failed.");
        try
        {
            var pointer = GlobalLock(memory); Marshal.Copy(nativeBytes, 0, pointer, nativeBytes.Length); GlobalUnlock(memory);
            using var nativeOutput = new MemoryStream();
            typeof(ClothingDrop).GetMethod("CopyGlobalMemory", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [memory, nativeOutput]);
            if (!nativeOutput.ToArray().Take(nativeBytes.Length).SequenceEqual(nativeBytes)) throw new Exception("Native drop chunk copy corrupted file data.");
        }
        finally { GlobalFree(memory); }
        var reservedItem = new ClothItem { ModelPath = model, Gender = "Masculino", Category = item.Category, TexturePaths = new[] { item.TexturePaths[0] }.Concat(Enumerable.Repeat("", 25)).ToList() };
        var reservedProject = new ClothProject { ReplaceDummiesOnAdding = true, Items = [reservedItem] };
        var incomingItem = new ClothItem { ModelPath = model, Gender = "Masculino", Category = item.Category, TexturePaths = [item.TexturePaths[0]] };
        var reservedResult = CatalogMerger.Merge(reservedProject, [incomingItem], Path.GetFullPath, _ => { }, (_, paths) => paths.ToArray());
        if (reservedResult.AddedTextures != 1 || reservedItem.TexturePaths.Count != 26 || reservedItem.TexturePaths[1] != item.TexturePaths[0] || reservedItem.TexturePaths[2] != "") throw new Exception("Reimport appended instead of filling first reserved texture slot.");
        var atomicItem = new ClothItem { ModelPath = model, Gender = "Masculino", Category = item.Category, TexturePaths = Enumerable.Repeat(item.TexturePaths[0], 25).ToList() };
        var atomicProject = new ClothProject { Items = [atomicItem] }; var reviewedCounts = new List<int>();
        try
        {
            CatalogMerger.Merge(atomicProject, [incomingItem, incomingItem], Path.GetFullPath, _ => { }, (target, paths) => { reviewedCounts.Add(target.TexturePaths.Count); return paths.ToArray(); });
            throw new Exception("Batch exceeded the 26 texture limit.");
        }
        catch (InvalidDataException) { }
        if (!reviewedCounts.SequenceEqual(new[] { 25, 26 }) || atomicItem.TexturePaths.Count != 25) throw new Exception("Batch review ignored pending slots or changed the rejected project.");
        var separateModel = Path.Combine(dropFolder, "mp_m_freemode_01_demo^teef_001_u.ydd"); File.Copy(model, separateModel);
        var differentIndexProject = new ClothProject { Items = [new ClothItem { ModelPath = model, Gender = "Masculino", Category = item.Category, SourceIndex = 0 }] };
        var sameMeshDifferentIndex = new ClothItem { ModelPath = separateModel, Gender = "Masculino", Category = item.Category, SourceIndex = 1 };
        if (CatalogMerger.Merge(differentIndexProject, [sameMeshDifferentIndex], Path.GetFullPath, _ => { }).AddedPieces != 1 || differentIndexProject.Items.Count != 2) throw new Exception("Identical mesh at a different drawable index was incorrectly merged.");
        var otherCollection = Path.Combine(dropFolder, "mp_m_freemode_01_othercollection^teef_000_u.ydd"); File.Copy(model, otherCollection);
        var otherCollectionItem = new ClothItem { ModelPath = otherCollection, Gender = "Masculino", Category = item.Category, SourceIndex = 0 };
        if (CatalogMerger.Merge(differentIndexProject, [otherCollectionItem], Path.GetFullPath, _ => { }).AddedPieces != 1 || differentIndexProject.Items.Count != 3) throw new Exception("Identical mesh from another DLC collection was incorrectly merged.");
        var missingExisting = new ClothItem { ModelPath = Path.Combine(dropFolder, "missing", Path.GetFileName(model)), Gender = "Masculino", Category = item.Category, SourceIndex = 0 };
        var incompleteProject = new ClothProject { Items = [missingExisting] };
        var validIncoming = new ClothItem { ModelPath = model, Gender = "Masculino", Category = item.Category, SourceIndex = 0 };
        if (CatalogMerger.Merge(incompleteProject, [validIncoming], Path.GetFullPath, _ => { }).AddedPieces != 1 || incompleteProject.Items.Count != 2 || !incompleteProject.Items.Contains(missingExisting)) throw new Exception("Missing existing source prevented importing a valid model or removed the old entry.");
        var invalidTexture = Path.Combine(dropFolder, "invalid.ytd"); File.WriteAllBytes(invalidTexture, [0]);
        project.AlwaysImportDuplicates = true;
        var textureGroups = new Dictionary<ClothItem, List<string>> { [item] = [item.TexturePaths[0]], [otherGender] = [invalidTexture] };
        try { typeof(MainWindow).GetMethod("ReviewTextureGroups", flags)!.Invoke(owner, [textureGroups]); throw new Exception("Invalid texture group accepted."); }
        catch (TargetInvocationException) { }
        if (item.TexturePaths.Count != 1 || otherGender.TexturePaths.Count != 0) throw new Exception("Failed multi-drawable texture batch changed an earlier piece.");
        project.AlwaysImportDuplicates = false;
        var selectedTarget = new ClothItem { ModelPath = item.ModelPath, Name = "Mask target", Gender = "Feminino", Category = "Máscara [berd]", SourceIndex = 42, OriginalModelFileName = "mp_f_freemode_01_other^berd_042_u.ydd" };
        project.Items.Add(selectedTarget);
        typeof(MainWindow).GetMethod("RefreshItems", flags)!.Invoke(owner, null);
        ((ListBox)owner.FindName("ItemList")).SelectedItem = selectedTarget;
        var mismatchedName = Path.Combine(dropFolder, "mp_f_freemode_01_mp_f_kronos^berd_diff_001_a_uni.ytd");
        File.Copy(textureSource, mismatchedName);
        typeof(MainWindow).GetMethod("ImportFiles", flags)!.Invoke(owner, [new[] { mismatchedName }]);
        if (selectedTarget.TexturePaths.Count != 1 || selectedTarget.TexturePaths[0] != mismatchedName || selectedTarget.MissingTexture) throw new Exception("Explicit target rejected a valid texture due to original collection/index.");
        var appendProject = new ClothProject { Items = [new ClothItem { ModelPath = model, Gender = "Masculino", Category = item.Category, SourceIndex = 100, ExportPosition = 0 }, new ClothItem { ModelPath = anotherModel, Gender = "Masculino", Category = item.Category, SourceIndex = 200, ExportPosition = 1 }] };
        var appended = new ClothItem { ModelPath = separateModel, Gender = "Masculino", Category = item.Category, SourceIndex = 0 };
        CatalogMerger.Merge(appendProject, [appended], Path.GetFullPath, _ => { });
        if (appended.ExportPosition != 2 || appended.SourceIndex != 0) throw new Exception("New drawable was not appended independently of source index.");
        typeof(MainWindow).GetField("_project", flags)!.SetValue(owner, new ClothProject());
        typeof(MainWindow).GetMethod("RefreshItems", flags)!.Invoke(owner, null);
        typeof(MainWindow).GetMethod("ImportFiles", flags)!.Invoke(owner, [modelOnlyFiles]);
        var stagedProject = (ClothProject)typeof(MainWindow).GetField("_project", flags)!.GetValue(owner)!;
        var stagedItem = stagedProject.Items.Single();
        if (!stagedItem.MissingTexture || stagedItem.ExportPosition != 0 || ((ListBox)owner.FindName("ItemList")).SelectedItem != stagedItem) throw new Exception("Model stage did not select the missing-texture drawable at the last slot.");
        var textureOnlyFiles = ClothingDrop.Materialize(new DataObject(DataFormats.FileDrop, new[] { textureSource }));
        typeof(MainWindow).GetMethod("ImportFiles", flags)!.Invoke(owner, [textureOnlyFiles]);
        if (stagedItem.MissingTexture || stagedItem.TexturePaths.Count != 1 || !((TextBlock)owner.FindName("StatusText")).Text.StartsWith("Added texture ")) throw new Exception("Texture stage did not clear the alert and show the operation.");
        if (owner.Icon is null) throw new Exception("Window shirt icon missing.");
        var hierarchy = new ClothProject();
        foreach (var gender in new[] { "Masculino", "Feminino" }) for (var i = 0; i < 5; i++)
        {
            var rowTextures = i == 4 && gender == "Feminino" ? new List<string>() : new List<string> { textureSource };
            hierarchy.Items.Add(new ClothItem { Name = "Armor", Category = "Colete [task]", Gender = gender, ModelPath = model, SourceIndex = i, ExportPosition = i, TexturePaths = rowTextures, ModelSizeBytes = i == 3 ? 5 * 1048576 : 399 * 1024, MaxTextureSizeBytes = 100, MetricsKey = model + "|" + string.Join("|", rowTextures) });
        }
        typeof(MainWindow).GetField("_project", flags)!.SetValue(owner, hierarchy);
        typeof(MainWindow).GetMethod("RefreshItems", flags)!.Invoke(owner, null); owner.UpdateLayout();
        var hierarchyList = (ListBox)owner.FindName("ItemList");
        if (((ClothItem)hierarchyList.Items[0]).Gender != "Masculino") throw new Exception("Hierarchy does not put male before female.");
        var group = ((System.Windows.Data.ListCollectionView)hierarchyList.ItemsSource).Groups![0];
        var groupLabel = new ClothLabelConverter().Convert(group, typeof(string), null!, System.Globalization.CultureInfo.InvariantCulture).ToString();
        if (groupLabel != "Armor [task] (Vests) (5M 5F)") throw new Exception("Wrong hierarchy counts/description: " + groupLabel);
        owner.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
        var expander = Descendants(hierarchyList).OfType<Expander>().First(); expander.IsExpanded = false;
        typeof(MainWindow).GetMethod("RefreshItems", flags)!.Invoke(owner, null); owner.UpdateLayout(); owner.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
        if (Descendants(hierarchyList).OfType<Expander>().First().IsExpanded) throw new Exception("Refreshing the hierarchy lost its collapsed state.");
        typeof(MainWindow).GetMethod("ExpandCategory", flags)!.Invoke(owner, ["Colete [task]"]); owner.UpdateLayout();
        if (!Descendants(hierarchyList).OfType<Expander>().First().IsExpanded) throw new Exception("Added drawable category did not expand.");
        var shot = new System.Windows.Media.Imaging.RenderTargetBitmap((int)owner.ActualWidth, (int)owner.ActualHeight, 96,96,System.Windows.Media.PixelFormats.Pbgra32); shot.Render(owner);
        var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(shot));
        Directory.CreateDirectory("validation/hierarchy-reference"); using (var imageFile = File.Create("validation/hierarchy-reference/main.png")) png.Save(imageFile);
        hierarchyList.SelectedItem = hierarchy.Items[0]; owner.UpdateLayout();
        var selectedShot = new System.Windows.Media.Imaging.RenderTargetBitmap((int)owner.ActualWidth, (int)owner.ActualHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32); selectedShot.Render(owner);
        var selectedPng = new System.Windows.Media.Imaging.PngBitmapEncoder(); selectedPng.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(selectedShot)); using (var file = File.Create("validation/hierarchy-reference/selected.png")) selectedPng.Save(file);
        typeof(MainWindow).GetField("_dirty", flags)!.SetValue(owner, false); owner.Close(); app.Shutdown();
        Console.WriteLine("OK: owned drop copies, model companions, category/gender, content duplicate across folders, slot A warning/skip and virtual descriptor.");
    }
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr memory);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr memory);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr memory);
}
