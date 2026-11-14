using System.IO;
using System.Text.Json;
using FooxClothStudio;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
static ClothProject Clone(ClothProject source) => JsonSerializer.Deserialize<ClothProject>(JsonSerializer.Serialize(source))!;
static ClothItem Item(string name, string gender = "Masculino", string category = "Tops [jbib]") =>
    new() { Name = name, Gender = gender, Category = category, IsDummy = true };

var project = new ClothProject { Name = "Published test", DlcName = "published_test" };
project.Items = [Item("Zulu"), Item("Alpha"), Item("Female", "Feminino"), Item("Watch", "Masculino", "Watch [p_lwrist]")];
CollectionIndices.Publish(project);
Check(project.PublishedCollection!.Drawables.Count == 4, "Snapshot missing entries");
Check(CollectionIndices.Validate(project).Count == 0, "Initial publication diverged");
Check(CollectionIndices.Current(project).Single(entry => entry.Name == "Watch").Slot == "p6", "Prop anchor mapping incorrect");
project.Items[0].Name = "AAA";
Check(CollectionIndices.Validate(project).Count == 0, "Label rename shifted a published index");
var roundtrip = Clone(project);
Check(CollectionIndices.Validate(roundtrip).Count == 0 && roundtrip.PublishedCollection!.Drawables.SequenceEqual(project.PublishedCollection.Drawables), "JSON roundtrip lost snapshot");

foreach (var mutate in new Action<ClothProject>[] {
    p => p.Items.RemoveAt(0), p => p.Items[0].ExportPosition = -1,
    p => p.Items[0].Gender = "Feminino", p => p.Items[0].Category = "Legs [lowr]",
    p => p.Items[0].Id = Guid.NewGuid(), p => p.DlcName = "changed",
    p => p.RemoveDefaultCollectionPrefix = true, p => p.ReplaceDlcCollection = true,
    p => p.Items.Add(new ClothItem { Name = "Middle", Gender = "Masculino", Category = "Tops [jbib]", ExportPosition = -1 }) })
{
    var invalid = Clone(project); mutate(invalid);
    Check(CollectionIndices.Validate(invalid).Count > 0, "Unsafe edit accepted");
    try { CollectionIndices.Publish(invalid); throw new Exception("Re-publication accepted divergence"); }
    catch (InvalidDataException) { }
}
var duplicate = Clone(project); duplicate.Items[0].Id = duplicate.Items[1].Id;
Check(CollectionIndices.Validate(duplicate).Count > 0, "Duplicate stable ID accepted");
var dummy = Clone(project); ClothEditActions.ReplaceWithDummy(dummy.Items[0]);
Check(CollectionIndices.Validate(dummy).Count == 0, "Dummy replacement changed identity");
var added = Item("New"); CollectionIndices.Append(project, added); project.Items.Add(added);
Check(CollectionIndices.Validate(project).Count == 0, "Append changed published index");
CollectionIndices.Publish(project);
Check(project.PublishedCollection.Drawables.Count == 5, "Publication did not capture appended piece");
var capacity = new ClothProject { Items = Enumerable.Range(0, 128).Select(index => Item(index.ToString())).ToList() };
Check(CollectionIndices.Warnings(capacity).Count == 1, "128 drawables warning missing");
capacity.Items.Add(Item("129"));
Check(FiveMResourceValidator.Validate(capacity).Any(message => message.StartsWith("Aviso:")), "Error List capacity warning missing");

var root = Path.Combine(Path.GetTempPath(), "collection-indices-probe-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var bundle = Path.Combine(root, "published.fcsbundle");
    ProjectBundle.Export(project, path => path, bundle);
    var unpacked = ProjectBundle.Import(bundle, root);
    var restored = JsonSerializer.Deserialize<ClothProject>(File.ReadAllText(unpacked))!;
    Check(CollectionIndices.Validate(restored).Count == 0 && restored.PublishedCollection!.Drawables.SequenceEqual(project.PublishedCollection.Drawables), "Bundle lost snapshot/IDs");
    var output = AddonExporter.Export(restored, path => path, root);
    using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "collection-index.json")));
    var entries = json.RootElement.GetProperty("entries").EnumerateArray().ToArray();
    Check(entries.Length == 5, "Build index missing entries");
    foreach (var entry in CollectionIndices.Current(restored))
    {
        var actual = entries.Single(value => value.GetProperty("itemId").GetGuid() == entry.ItemId);
        Check(actual.GetProperty("localIndex").GetInt32() == entry.LocalIndex && actual.GetProperty("collection").GetString() == entry.Collection &&
            actual.GetProperty("textureCount").GetInt32() == 0, "Build index differs from snapshot");
    }
    Check(FiveMResourceValidator.Validate(output).Count == 0, "Generated YMT/shop metadata invalid");
    var invalid = Clone(restored); invalid.DlcName = "changed";
    try { AddonExporter.Export(invalid, path => path, root); throw new Exception("Build accepted divergence"); }
    catch (InvalidDataException) { }
    Check(!Directory.Exists(Path.Combine(root, "changed")), "Invalid build created output");
    var merged = Clone(restored); merged.ReplaceDummiesOnAdding = true;
    var incoming = new ClothItem { Name = "Imported", ModelPath = Path.Combine(root, "new.ydd"), Gender = "Masculino", Category = "Tops [jbib]", SourceIndex = 0 };
    CatalogMerger.Merge(merged, [incoming], path => path, _ => { });
    Check(CollectionIndices.Validate(merged).Count == 0 && merged.Items.Count == restored.Items.Count + 1 && incoming.ExportPosition == 3,
        "Import reused a published dummy or source index");
    var copy = ClothEditActions.CopyForOtherGender(merged, incoming);
    Check(CollectionIndices.Validate(merged).Count == 0 && copy.ExportPosition == 1, "Gender copy shifted published slot");
    var textures = Clone(restored); textures.Items[0].IsDummy = false; textures.Items[0].TexturePaths = ["a", "", "b"];
    var counted = CollectionIndices.Current(textures).Single(entry => entry.ItemId == textures.Items[0].Id);
    Check(counted.TextureCount == 3, "Texture count lost reserved texture position");
    Exception? uiError = null;
    var ui = new Thread(() =>
    {
        try
        {
            var settings = new ProjectSettingsWindow(restored, path => path);
            Check(((System.Windows.Controls.TextBox)settings.FindName("DlcNameField")).IsReadOnly &&
                ((System.Windows.Controls.TextBox)settings.FindName("ProjectNameField")).IsReadOnly, "Published project name fields remained editable");
            var window = new MainWindow();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(MainWindow).GetField("_project", flags)!.SetValue(window, restored);
            typeof(MainWindow).GetField("_projectPath", flags)!.SetValue(window, unpacked);
            typeof(MainWindow).GetMethod("RefreshItems", flags)!.Invoke(window, null);
            var list = (System.Windows.Controls.ListBox)window.FindName("ItemList");
            list.SelectedItem = restored.Items[0];
            typeof(MainWindow).GetMethod("Remove_Click", flags)!.Invoke(window, [window, new System.Windows.RoutedEventArgs()]);
            Check(restored.Items.Count == 5, "UI deleted a published dummy");
            window.Close(); settings.Close();
        }
        catch (Exception ex) { uiError = ex; }
    });
    ui.SetApartmentState(ApartmentState.STA); ui.Start(); ui.Join();
    if (uiError is not null) throw new Exception("WPF publication guards failed", uiError);
    Console.WriteLine("PASS: snapshot, stable rename, JSON/bundle roundtrip, reorder/remove/insert/gender/slot/name/prefix guards, dummy, append/re-publication, prop anchors, 128/129 warning, real addon YMT/shop validation and collection-index.json.");
}
finally { Directory.Delete(root, true); }
