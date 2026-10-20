using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FooxClothStudio;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var fixture = Path.GetFullPath(args[0]);
        var output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        System.Threading.SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var project = JsonSerializer.Deserialize<ClothProject>(File.ReadAllText(fixture))!;
        var root = Path.GetDirectoryName(fixture)!;
        string Resolve(string path) => Path.GetFullPath(Path.Combine(root, path));
        void Render(Window window, string name)
        {
            window.ShowActivated = false; window.ShowInTaskbar = false;
            window.Show(); window.UpdateLayout();
            if (window is MainWindow main && main.FindName("ItemList") is System.Windows.Controls.ListBox list && list.Items.Count > 0)
            {
                list.SelectedIndex = -1; list.SelectedIndex = args.Length > 2 ? int.Parse(args[2]) : 0;
                if (main.FindName("EditorScroll") is System.Windows.Controls.ScrollViewer editor && editor.Visibility != Visibility.Visible)
                    throw new Exception("Selecting a drawable did not show its editor.");
            }
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start(); Dispatcher.PushFrame(frame);
            if (window is MainWindow modelWindow && modelWindow.FindName("ItemList") is System.Windows.Controls.ListBox selected && selected.SelectedItem is ClothItem item)
            {
                foreach (var buttonName in new[] { "AddButton", "AddMaleButton", "AddFemaleButton" })
                {
                    var add = (System.Windows.Controls.Button)modelWindow.FindName(buttonName);
                    add.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    if (add.ContextMenu?.IsOpen != true || add.ContextMenu.Items.Count != 5) throw new Exception("Add menu did not expose all five import choices.");
                    add.ContextMenu.IsOpen = false;
                }
                var originalCategory = item.Category;
                var originallyProp = originalCategory.Contains("[p_");
                var kind = (System.Windows.Controls.ComboBox)modelWindow.FindName("ClothKindBox");
                var types = (System.Windows.Controls.ComboBox)modelWindow.FindName("TypeBox");
                kind.SelectedIndex = originallyProp ? 0 : 1;
                if (item.Category.Contains("[p_") == originallyProp || types.Items.Cast<string>().Any(category => category.Contains("[p_") == originallyProp))
                    throw new Exception("Cloth type change did not update the model and filter drawable types.");
                kind.SelectedIndex = originallyProp ? 1 : 0; types.SelectedItem = originalCategory;
                if (item.Category != originalCategory) throw new Exception("Original drawable type could not be restored.");
                var hair = (System.Windows.Controls.ListBox)modelWindow.FindName("HairOptionBox");
                if (originalCategory.Contains("[p_head]"))
                {
                    var originalHair = item.HairOption;
                    hair.SelectedIndex = 1;
                    if (item.HairOption != "Cut hairs") throw new Exception("Hair options list did not update the drawable.");
                    hair.SelectedItem = hair.Items.Cast<System.Windows.Controls.ListBoxItem>().Single(option => (string)option.Content == originalHair);
                }
                var flags = (System.Windows.Controls.ComboBox)modelWindow.FindName("BehaviourBox");
                flags.ApplyTemplate(); flags.IsDropDownOpen = true; flags.UpdateLayout();
                if (flags.Template.FindName("PART_Popup", flags) is not System.Windows.Controls.Primitives.Popup popup || !popup.IsOpen)
                    throw new Exception("Behaviour Flags dropdown did not open.");
                flags.IsDropDownOpen = false;
                if (flags.Template.FindName("PART_EditableTextBox", flags) is not System.Windows.Controls.TextBox editable)
                    throw new Exception("Editable flags field was missing.");
                var originalFlags = item.BehaviourFlags;
                editable.Text = "128";
                flags.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
                if (item.BehaviourFlags != 128) throw new Exception("Typing flags did not update the selected drawable.");
                flags.Text = originalFlags?.ToString() ?? "Default";
                flags.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
                var effect = item.TextureEffects.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(effect?.TimeCycleModifier) &&
                    (modelWindow.FindName("TimeCycleBox") as System.Windows.Controls.ComboBox)?.Text != effect.TimeCycleModifier)
                    throw new Exception("Selected texture effects were not loaded into the editor.");
            }
            var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight,
                96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(output, name + ".png"))) encoder.Save(file);
            window.Close();
        }
        Render(new MainWindow(), "main");
        Render(new ProjectSettingsWindow(project, Resolve), "settings");
        Render(new BuildProjectWindow(project, Resolve), "build");
        Render(new ErrorListWindow(["Fixture missing texture", "Fixture undefined gender"]), "errors");
        Render(new DuplicateTextureWindow("teef_diff_000_a_uni.ytd"), "duplicates");
        Render(new ReservationWindow("Masculino", ClothCatalog.Categories[11]), "reservation");
        Render(new TattooWindow(project, Resolve), "tattoos");
        if (project.Tattoos.Count > 0)
        {
            var originalName = project.Tattoos[0].Name;
            var dialog = new TattooWindow(project, Resolve) { ShowActivated = false, ShowInTaskbar = false };
            var editTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            editTimer.Tick += (_, _) =>
            {
                editTimer.Stop();
                ((System.Windows.Controls.TextBox)dialog.FindName("TattooName")).Text = "edited_probe";
                ((System.Windows.Controls.Button)dialog.FindName("ApplyTattoos")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                if (dialog.IsVisible) { dialog.Close(); throw new Exception("Valid tattoo edit was not applied."); }
            };
            editTimer.Start();
            if (dialog.ShowDialog() != true || dialog.Result[0].Name != "edited_probe" || project.Tattoos[0].Name != originalName)
                throw new Exception("Tattoo editing failed or modified the original project before Apply.");
            Console.WriteLine("OK: tattoo modal Apply persisted the edit and isolated the original project.");
            var saveRoot = Path.Combine(output, "save-roundtrip"); Directory.CreateDirectory(saveRoot);
            var savePath = Path.Combine(saveRoot, "project.fcsproj"); File.WriteAllText(savePath, JsonSerializer.Serialize(project));
            var saveWindow = new MainWindow();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(MainWindow).GetMethod("LoadProject", flags)!.Invoke(saveWindow, [savePath]);
            var saveTask = (Task<bool>)typeof(MainWindow).GetMethod("SaveProjectAsync", flags)!.Invoke(saveWindow, [false])!;
            var saveFrame = new DispatcherFrame(); var saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            var deadline = DateTime.UtcNow.AddSeconds(30);
            saveTimer.Tick += (_, _) => { if (saveTask.IsCompleted || DateTime.UtcNow > deadline) { saveTimer.Stop(); saveFrame.Continue = false; } };
            saveTimer.Start(); Dispatcher.PushFrame(saveFrame);
            if (!saveTask.IsCompletedSuccessfully || !saveTask.Result) throw new Exception("Saving the tattoo project failed.");
            var savedProject = JsonSerializer.Deserialize<ClothProject>(File.ReadAllText(savePath))!;
            if (Path.IsPathRooted(savedProject.Tattoos[0].ImagePath) || !File.Exists(Path.Combine(saveRoot, savedProject.Tattoos[0].ImagePath)))
                throw new Exception("Saved project did not include its tattoo asset.");
            typeof(MainWindow).GetMethod("LoadProject", flags)!.Invoke(saveWindow, [savePath]);
            var loadedProject = (ClothProject)typeof(MainWindow).GetField("_project", flags)!.GetValue(saveWindow)!;
            if (loadedProject.Tattoos.Count != project.Tattoos.Count || loadedProject.Tattoos[0].U != project.Tattoos[0].U)
                throw new Exception("Opening the saved tattoo project lost its metadata.");
            saveWindow.Close(); Console.WriteLine("OK: actual MainWindow save/open roundtrip included tattoo assets and metadata.");
        }
        application.Shutdown();
        Console.WriteLine("OK: rendered main, settings, build, Error List and duplicate dialogs. " + output);
    }
}
