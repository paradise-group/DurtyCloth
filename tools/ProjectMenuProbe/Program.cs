using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FooxClothStudio;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var window = new MainWindow { ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000 };
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        if (window.Title != "Paradise Cloth Studio") throw new Exception("Window brand was not updated.");
        window.Show(); window.UpdateLayout();
        var creationRoot = Path.Combine(Path.GetFullPath(args[1]), Guid.NewGuid().ToString("N"));
        void RunNew(bool cancel)
        {
            var modalTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            modalTimer.Tick += (_, _) =>
            {
                var dialog = application.Windows.OfType<ProjectSettingsWindow>().FirstOrDefault();
                if (dialog == null) return;
                modalTimer.Stop();
                if (cancel) { dialog.DialogResult = false; return; }
                ((TextBox)dialog.FindName("ProjectNameField")).Text = "Creation probe";
                if (((TextBox)dialog.FindName("DlcNameField")).Text != "creationprobe") throw new Exception("Automatic DLC name failed.");
                ((TextBox)dialog.FindName("OutputFolderField")).Text = creationRoot;
                dialog.UpdateLayout();
                var apply = (Button)dialog.FindName("ApplyProjectSettings");
                if (!apply.IsVisible || apply.ActualHeight <= 0) throw new Exception("Create button is not visible.");
                apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            modalTimer.Start();
            ((MenuItem)window.FindName("NewProjectMenuItem")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        }
        RunNew(false);
        var created = (ClothProject)typeof(MainWindow).GetField("_project", flags)!.GetValue(window)!;
        var createdPath = (string)typeof(MainWindow).GetField("_projectPath", flags)!.GetValue(window)!;
        if (created.Items.Count != 0 || created.Name != "Creation probe" || !File.Exists(createdPath)) throw new Exception("New project was not created and saved.");
        RunNew(true);
        if (!ReferenceEquals(created, typeof(MainWindow).GetField("_project", flags)!.GetValue(window))) throw new Exception("Cancel replaced the open project.");
        try { ProjectCreator.Create(new ClothProject { Name = "Duplicate", DlcName = "creationprobe", OutputFolder = creationRoot }); throw new Exception("Existing project was overwritten."); }
        catch (IOException) { }
        typeof(MainWindow).GetMethod("LoadProject", flags)!.Invoke(window, [Path.GetFullPath(args[0])]);
        var loaded = (ClothProject)typeof(MainWindow).GetField("_project", flags)!.GetValue(window)!;
        if (loaded.Items.Count == 0) throw new Exception("Project open did not load the fixture.");
        window.Show(); window.UpdateLayout();
        var menu = (MenuItem)window.FindName("ProjectMenu"); menu.IsSubmenuOpen = true;
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
        var popup = (Popup)menu.Template.FindName("PART_Popup", menu);
        if (popup.Child is not FrameworkElement child || child.ActualWidth == 0) throw new Exception("Project menu popup was not rendered.");
        var first = (MenuItem)window.FindName("NewProjectMenuItem");
        if (first.Foreground is not SolidColorBrush text || text.Color.R < 200 || first.Background is not SolidColorBrush background || background.Color.R > 100)
            throw new Exception("Project menu has unreadable foreground/background contrast.");
        var image = new RenderTargetBitmap((int)Math.Ceiling(child.ActualWidth), (int)Math.Ceiling(child.ActualHeight), 96, 96, PixelFormats.Pbgra32); image.Render(child);
        Directory.CreateDirectory(args[1]); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using (var file = File.Create(Path.Combine(args[1], "project-menu.png"))) encoder.Save(file);
        menu.IsSubmenuOpen = false; window.Close(); application.Shutdown();
        Console.WriteLine("OK: creation dialog, automatic DLC, visible Create button, saved project, cancel preservation, duplicate rejection, fixture open and readable menu.");
    }
}
