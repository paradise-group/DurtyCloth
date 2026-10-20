using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using FooxClothStudio;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var owner = new MainWindow { Left = -10000, Top = -10000, WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false };
        owner.Show();
        typeof(MainWindow).GetMethod("LoadProject", flags)!.Invoke(owner, [Path.GetFullPath(args[0])]);
        var project = (ClothProject)typeof(MainWindow).GetField("_project", flags)!.GetValue(owner)!;
        var item = project.Items.First(i => !i.IsDummy);
        var cache = (System.Collections.IDictionary)typeof(MainWindow).GetField("_pedLoads", flags)!.GetValue(owner)!;
        var cacheKey = GamePedAssets.FindGameFolder() + "|" + item.Gender;
        cache[cacheKey] = Task.FromException<(ModelPreviewPart[], Dictionary<string, BitmapSource>)>(new IOException("Transient ped load test"));
        var failedLoad = (Task)typeof(MainWindow).GetMethod("LoadBasePedAsync", flags)!.Invoke(owner, [item])!;
        if (!failedLoad.IsCompleted || cache.Contains(cacheKey)) throw new Exception("Failed ped task stayed cached and prevents retry.");
        var list = (ListBox)owner.FindName("ItemList"); list.SelectedItem = item; owner.UpdateLayout();
        void Pump(int ms) { var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) }; timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame); }
        void DoubleClick(ListBox target)
        {
            owner.UpdateLayout(); target.ScrollIntoView(target.SelectedItem); target.UpdateLayout();
            var row = target.ItemContainerGenerator.ContainerFromItem(target.SelectedItem) as ListBoxItem ?? throw new Exception("Missing rendered row.");
            target.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Control.MouseDoubleClickEvent, Source = row });
        }
        DoubleClick(list);
        var preview = app.Windows.OfType<ClothPreviewWindow>().Single();
        if (!preview.IsVisible || preview.Owner != owner) throw new Exception("Double click did not open separate owned preview.");
        var deadline = DateTime.UtcNow.AddSeconds(25);
        Model3DGroup? scene = null;
        while (DateTime.UtcNow < deadline)
        {
            Pump(100);
            scene = ((ModelVisual3D)owner.FindName("ModelPreviewVisual")).Content as Model3DGroup;
            if (scene?.Children.OfType<Model3DGroup>().SelectMany(g => g.Children).OfType<GeometryModel3D>().Any(g => g.Material is DiffuseMaterial { Brush: ImageBrush }) == true) break;
        }
        if (scene?.Children.OfType<Model3DGroup>().SelectMany(g => g.Children).OfType<GeometryModel3D>().Any(g => g.Material is DiffuseMaterial { Brush: ImageBrush }) != true) throw new Exception("Selected YTD was not applied to real YDD geometry.");
        var textures = (ListBox)owner.FindName("TextureList");
        // Exercise the actual selection/load path with a distinct, temporary YTD variation.
        var temporary = Path.Combine(Path.GetFullPath(args[1]), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temporary);
        var red = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0,0,255,255, 0,0,255,255, 0,0,255,255, 0,0,255,255 }, 8);
        var redEncoder = new PngBitmapEncoder(); redEncoder.Frames.Add(BitmapFrame.Create(red));
        var redPath = Path.Combine(temporary, "red.png"); using (var file = File.Create(redPath)) redEncoder.Save(file);
        var sourceTexture = Path.IsPathRooted(item.TexturePaths[0]) ? item.TexturePaths[0] : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0]))!, item.TexturePaths[0]);
        var secondYtd = Path.Combine(temporary, "variation-b.ytd"); YtdTextureEditor.ReplaceFromPng(sourceTexture, 0, redPath, secondYtd);
        item.TexturePaths.Add(secondYtd);
        typeof(MainWindow).GetMethod("RefreshTextureList", flags)!.Invoke(owner, [item, 0]);
        textures.SelectedIndex = item.TexturePaths.Count - 1;
        var switched = false; deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            Pump(100);
            scene = ((ModelVisual3D)owner.FindName("ModelPreviewVisual")).Content as Model3DGroup;
            var image = scene?.Children.OfType<Model3DGroup>().SelectMany(g => g.Children).OfType<GeometryModel3D>().Select(g => (g.Material as DiffuseMaterial)?.Brush).OfType<ImageBrush>().Select(b => b.ImageSource).OfType<BitmapSource>().FirstOrDefault();
            if (image?.PixelWidth != 2) continue;
            var pixels = new byte[16]; image.CopyPixels(pixels, 8, 0);
            if (pixels[2] == 255 && pixels[0] == 0) { switched = true; break; }
        }
        if (!switched || !preview.Title.EndsWith("=> " + (char)('a' + textures.SelectedIndex))) throw new Exception("Changing variation did not update the actual 3D image and window title.");
        textures.SelectedIndex = 0; Pump(500);
        DoubleClick(textures);
        if (app.Windows.OfType<ClothPreviewWindow>().Count() != 1) throw new Exception("Repeated preview creates duplicate windows.");
        preview.Close(); DoubleClick(textures);
        preview = app.Windows.OfType<ClothPreviewWindow>().Single();
        if (!preview.IsVisible || !preview.Title.EndsWith("=> a")) throw new Exception("Texture double click did not reopen selected variation.");
        deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline && ((ModelPreviewPart[])typeof(MainWindow).GetField("_pedParts", flags)!.GetValue(owner)!).Length == 0) Pump(100);
        var bodyParts = (ModelPreviewPart[])typeof(MainWindow).GetField("_pedParts", flags)!.GetValue(owner)!;
        if (bodyParts.Length == 0) throw new Exception("Base ped did not load from GTA V.");
        foreach (var slot in new[] { "head", "hair", "uppr", "lowr", "feet" })
            if (!bodyParts.Any(p => p.TextureName!.StartsWith("__ped_" + slot + "_"))) throw new Exception("Missing ped slot " + slot);
        Pump(700);
        var display = (ModelPreviewData)typeof(MainWindow).GetField("_displayModelData", flags)!.GetValue(owner)!;
        var garment = (ModelPreviewData)typeof(MainWindow).GetField("_currentModelData", flags)!.GetValue(owner)!;
        if (item.Category.EndsWith("[p_head]")) garment = ModelPreview.AttachToHead(garment, ModelPreview.HeadAttachment(GamePedAssets.Load(GamePedAssets.FindGameFolder()!, item.Gender).First(a => a.Slot == "head").ModelPath));
        if (!display.Parts.Take(garment.Parts.Length).SelectMany(p => p.Positions).SequenceEqual(garment.Parts.SelectMany(p => p.Positions))) throw new Exception("Ped assembly changed garment positions.");
        var slotCode = item.Category.Split('[').Last().TrimEnd(']');
        if (display.Parts.Any(p => p.TextureName?.StartsWith("__ped_" + slotCode + "_") == true)) throw new Exception("Selected garment overlaps a duplicate base component.");
        var modelsPanel = (StackPanel)typeof(ClothPreviewWindow).GetField("_models", flags)!.GetValue(preview)!;
        var meshToggle = modelsPanel.Children.OfType<CheckBox>().First(); meshToggle.IsChecked = false;
        typeof(MainWindow).GetMethod("UpdateClothPreviewWindow", flags)!.Invoke(owner, null);
        if (modelsPanel.Children.OfType<CheckBox>().First().IsChecked != false) throw new Exception("Material refresh reset mesh visibility.");
        modelsPanel.Children.OfType<CheckBox>().First().IsChecked = true;
        preview.UpdateLayout(); Directory.CreateDirectory(args[1]);
        var bitmap = new RenderTargetBitmap((int)preview.ActualWidth, (int)preview.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(preview);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using (var output = File.Create(Path.Combine(args[1], "preview.png"))) png.Save(output);
        var motion = (PreviewCameraMotion)typeof(ClothPreviewWindow).GetField("_motion", flags)!.GetValue(preview)!;
        motion.Rotate(100, 0);
        typeof(ClothPreviewWindow).GetMethod("QueueCameraFrame", flags)!.Invoke(preview, null);
        // Advance one deterministic frame: wall-clock scheduling can settle the entire animation.
        typeof(ClothPreviewWindow).GetMethod("AdvanceCamera", flags)!.Invoke(preview, [1.0 / 60]);
        if (motion.Yaw <= 25 || motion.Yaw >= 60) throw new Exception("Camera skipped interpolation.");
        // Offscreen WPF windows can stop receiving compositor callbacks. Advance their frame clock explicitly.
        for (var i = 1; i <= 120; i++) typeof(ClothPreviewWindow).GetMethod("AdvanceCamera", flags)!.Invoke(preview, [1.0 / 60]);
        if (Math.Abs(motion.Yaw - 60) > .001) throw new Exception("Camera did not reach its target.");
        preview.ResetCamera();
        if (motion.Yaw != 25 || motion.Distance != 3.3) throw new Exception("Camera reset failed.");
        // Locked mode must ignore tilt and panning, including after relocking mid-animation.
        var locked = new PreviewCameraMotion(); locked.Rotate(80, 90); locked.Pan(40, 40, 600);
        for (var i = 0; i < 120; i++) locked.Advance(1.0 / 60);
        if (locked.Pitch != 0 || locked.X != 0 || locked.Y != 0 || locked.Yaw <= 25) throw new Exception("Locked ped moved or tilted.");
        var unlockBox = ((Grid)preview.Content).Children.OfType<Border>().Select(b=>b.Child).OfType<CheckBox>().Single();
        unlockBox.IsChecked = true;
        if (!motion.Unlocked) throw new Exception("Unlock checkbox did not enable free movement.");
        motion.Rotate(0, 60); motion.Pan(40, 20, 600);
        for (var i = 0; i < 120; i++) motion.Advance(1.0 / 60);
        if (motion.Pitch == 0 || motion.X == 0) throw new Exception("Unlocked ped cannot move.");
        unlockBox.IsChecked = false;
        if (motion.Unlocked || motion.Pitch != 0 || motion.X != 0 || motion.Y != 0) throw new Exception("Relocking failed to straighten/recenter.");
        var slow = new PreviewCameraMotion(); var fast = new PreviewCameraMotion(); slow.SetUnlocked(true); fast.SetUnlocked(true); slow.Rotate(100, 40); fast.Rotate(100, 40);
        for (var i = 0; i < 30; i++) slow.Advance(1.0 / 30);
        for (var i = 0; i < 144; i++) fast.Advance(1.0 / 144);
        if (Math.Abs(slow.Yaw - fast.Yaw) > .001 || Math.Abs(slow.Pitch - fast.Pitch) > .001) throw new Exception("Camera damping depends on frame rate.");
        var wheel = new PreviewCameraMotion(); var halfWheel = new PreviewCameraMotion(); wheel.Zoom(120); halfWheel.Zoom(60); halfWheel.Zoom(60);
        for (var i = 0; i < 120; i++) { wheel.Advance(1.0 / 60); halfWheel.Advance(1.0 / 60); }
        if (Math.Abs(wheel.Distance - halfWheel.Distance) > .001) throw new Exception("High resolution wheel zoom differs.");
        wheel.SetUnlocked(true); wheel.Rotate(0, 10000); wheel.Zoom(100000); wheel.Pan(20, 10, 600);
        for (var i = 0; i < 120; i++) wheel.Advance(1.0 / 60);
        if (wheel.Pitch > 85 || wheel.Distance < .6 || wheel.X >= 0 || wheel.Y <= 0) throw new Exception("Camera bounds or pan direction failed.");
        preview.Close();
        if ((bool)typeof(ClothPreviewWindow).GetField("_animating", flags)!.GetValue(preview)!) throw new Exception("Camera kept rendering after close.");
        typeof(MainWindow).GetField("_showBasePed", flags)!.SetValue(owner, false);
        // A mesh using only the right half of an atlas must never show its red left half.
        var atlas = BitmapSource.Create(4, 2, 96, 96, PixelFormats.Bgra32, null,
            new byte[] { 0,0,255,255,0,0,255,255,0,255,0,255,0,255,0,255, 0,0,255,255,0,0,255,255,0,255,0,255,0,255,0,255 }, 16); atlas.Freeze();
        var synthetic = new ModelPreviewData([new ModelPreviewPart(
            [new(-1,-1,0), new(1,-1,0), new(1,1,0), new(-1,1,0)], [0,1,2,0,2,3],
            [new(.65,.8),new(.9,.8),new(.9,.2),new(.65,.2)], "atlas")], 2, 2);
        typeof(MainWindow).GetMethod("ShowModelPreview", flags)!.Invoke(owner, [synthetic, new Dictionary<string, BitmapSource> { ["atlas"] = atlas }]);
        var atlasScene = ((ModelVisual3D)owner.FindName("ModelPreviewVisual")).Content.Clone();
        ((Model3DGroup)atlasScene).Children.OfType<Model3DGroup>().Single().Transform = Transform3D.Identity;
        var viewport = new Viewport3D { Width = 256, Height = 256, Camera = new OrthographicCamera(new Point3D(0,0,3), new Vector3D(0,0,-1), new Vector3D(0,1,0), 2.4) };
        viewport.Children.Add(new ModelVisual3D { Content = atlasScene }); viewport.Measure(new Size(256,256)); viewport.Arrange(new Rect(0,0,256,256)); viewport.UpdateLayout();
        var rendered = new RenderTargetBitmap(256,256,96,96,PixelFormats.Pbgra32); rendered.Render(viewport);
        var sample = new byte[4]; rendered.CopyPixels(new Int32Rect(65,128,1,1), sample,4,0);
        if (sample[1] < 80 || sample[2] > 20) throw new Exception($"UV atlas region was stretched: BGR={sample[0]},{sample[1]},{sample[2]}");
        var atlasPng = new PngBitmapEncoder(); atlasPng.Frames.Add(BitmapFrame.Create(rendered)); using (var file = File.Create(Path.Combine(args[1], "uv-atlas.png"))) atlasPng.Save(file);
        owner.Close(); app.Shutdown();
        Console.WriteLine("OK: drawable and texture double-click, separate owned window, actual YTD image material on YDD, changed variation pixels, reuse, close/reopen, selected variation and rendered preview.");
    }
}
