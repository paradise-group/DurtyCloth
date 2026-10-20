using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace FooxClothStudio;

public sealed class ClothPreviewWindow : Window
{
    public event Action<bool>? ShowPedChanged;
    public event Action? SelectGameFolder;
    public event Action<IDataObject>? FilesDropped;
    private readonly TextBlock _pedStatus = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
    public void SetPedStatus(string text) => _pedStatus.Text = text;
    private readonly Viewport3D _viewport = new();
    private readonly ModelVisual3D _visual = new();
    private readonly PerspectiveCamera _camera = new(new Point3D(0, 0, 3.3), new Vector3D(0, 0, -1), new Vector3D(0, 1, 0), 45);
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
    private readonly StackPanel _models = WorkflowUi.Stack();
    private readonly StackPanel _textures = WorkflowUi.Stack();
    private readonly AxisAngleRotation3D _yaw = new(new Vector3D(0, 1, 0), 25);
    private readonly AxisAngleRotation3D _pitch = new(new Vector3D(1, 0, 0), 0);
    private Point? _drag;
    private readonly PreviewCameraMotion _motion = new();
    private bool _panning, _animating;
    private TimeSpan? _lastFrame;
    private void QueueCameraFrame()
    {
        if (_animating) return;
        _animating = true; _lastFrame = null;
        CompositionTarget.Rendering += CameraFrame;
    }
    private void CameraFrame(object? sender, EventArgs e)
    {
        var time = ((RenderingEventArgs)e).RenderingTime;
        var seconds = _lastFrame is TimeSpan previous ? (time - previous).TotalSeconds : 1.0 / 60;
        _lastFrame = time;
        AdvanceCamera(seconds);
    }
    private void AdvanceCamera(double seconds)
    {
        var moving = _motion.Advance(seconds); ApplyCamera();
        if (!moving) StopCameraFrames();
    }
    private void ApplyCamera()
    {
        _yaw.Angle = _motion.Yaw; _pitch.Angle = _motion.Pitch;
        _camera.Position = new Point3D(_motion.X, _motion.Y, _motion.Distance);
    }
    private void StopCameraFrames() { CompositionTarget.Rendering -= CameraFrame; _animating = false; _lastFrame = null; }
    private string? _itemIdentity;
    private readonly Dictionary<int, bool> _meshVisibility = new();
    public ClothPreviewWindow(bool showBasePed = true)
    {
        WorkflowUi.Theme(this, "Clothes Preview", 1150, 790);
        AllowDrop = true;
        PreviewDragOver += (_, e) => { e.Effects = ClothingDrop.Accepts(e.Data) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        PreviewDrop += (_, e) => { e.Handled = true; FilesDropped?.Invoke(e.Data); };
        var root = new Grid(); root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) }); root.ColumnDefinitions.Add(new ColumnDefinition());
        var sidebar = new DockPanel(); root.Children.Add(sidebar);
        var menu = new Button { Content = "< Menu", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(8) };
        menu.Click += (_, _) => Close(); DockPanel.SetDock(menu, Dock.Top); sidebar.Children.Add(menu);
        DockPanel.SetDock(_status, Dock.Bottom); sidebar.Children.Add(_status);
        var tabs = new TabControl(); sidebar.Children.Add(tabs);
        var options = WorkflowUi.Stack();
        WorkflowUi.Button(options, "Reset camera", (_, _) => ResetCamera());
        options.Children.Add(new TextBlock { Text = "Arraste para girar · roda para zoom\nDestrave o ped para inclinar ou deslocar", Margin = new Thickness(0, 12, 0, 0) });
        tabs.Items.Add(new TabItem { Header = "Options", Content = options });
        var ped = WorkflowUi.Stack();
        var showPed = WorkflowUi.Check(ped, "Show base ped", showBasePed);
        showPed.Checked += (_, _) => ShowPedChanged?.Invoke(true);
        showPed.Unchecked += (_, _) => ShowPedChanged?.Invoke(false);
        WorkflowUi.Button(ped, "Select GTA V Folder", (_, _) => SelectGameFolder?.Invoke());
        ped.Children.Add(_pedStatus);
        tabs.Items.Add(new TabItem { Header = "Ped", Content = ped });
        tabs.Items.Add(new TabItem { Header = "Models data", Content = new ScrollViewer { Content = _models } });
        tabs.Items.Add(new TabItem { Header = "Textures data", Content = new ScrollViewer { Content = _textures } });
        var surface = new Border { Background = new RadialGradientBrush { GradientOrigin = new Point(.48, .32), Center = new Point(.5, .4), RadiusX = .8, RadiusY = .85, GradientStops = new GradientStopCollection { new(Color.FromRgb(48, 62, 89), 0), new(Color.FromRgb(25, 32, 50), .55), new(Color.FromRgb(12, 16, 28), 1) } }, Child = _viewport };
        Grid.SetColumn(surface, 1); root.Children.Add(surface); Content = root;
        var unlock = new CheckBox { Content = "Destravar ped", Foreground = Brushes.White,
            Background = Brushes.Transparent, Margin = new Thickness(16),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            ToolTip = "Permite inclinar e deslocar. Desmarque para manter o ped reto e centralizado." };
        var unlockPanel = new Border { Background = new SolidColorBrush(Color.FromArgb(220, 18, 24, 38)),
            CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(16), Child = unlock };
        Grid.SetColumn(unlockPanel, 1); root.Children.Add(unlockPanel);
        unlock.Checked += (_, _) => _motion.SetUnlocked(true);
        unlock.Unchecked += (_, _) => { _motion.SetUnlocked(false); ApplyCamera(); };
        _viewport.Camera = _camera; _viewport.Children.Add(_visual);
        surface.MouseDown += (_, e) => { if (e.ChangedButton is not (MouseButton.Left or MouseButton.Right or MouseButton.Middle)) return; _panning = e.ChangedButton != MouseButton.Left; _drag = e.GetPosition(surface); surface.CaptureMouse(); e.Handled = true; };
        surface.MouseMove += (_, e) => { if (_drag is not Point previous) return; var point = e.GetPosition(surface); if (_panning) _motion.Pan(point.X - previous.X, point.Y - previous.Y, surface.ActualHeight); else _motion.Rotate(point.X - previous.X, point.Y - previous.Y); _drag = point; QueueCameraFrame(); };
        surface.MouseUp += (_, _) => { _drag = null; surface.ReleaseMouseCapture(); };
        surface.LostMouseCapture += (_, _) => _drag = null;
        surface.MouseWheel += (_, e) => { _motion.Zoom(e.Delta); QueueCameraFrame(); e.Handled = true; };
        Closed += (_, _) => StopCameraFrames();
    }
    public void ResetCamera() { StopCameraFrames(); _motion.Reset(); ApplyCamera(); }
    public void UpdatePreview(string name, int variation, Model3DGroup? scene, string status, ModelPreviewData? data, string? itemIdentity = null)
    {
        if (_itemIdentity != itemIdentity) { _meshVisibility.Clear(); _itemIdentity = itemIdentity; }
        Title = "Paradise Cloth Studio - Clothes Preview - " + name + (variation >= 0 ? " => " + (char)('a' + variation) : "");
        _status.Text = status;
        _models.Children.Clear(); _textures.Children.Clear();
        _visual.Content = scene?.Clone();
        if (_visual.Content is Model3DGroup copy && copy.Children.OfType<Model3DGroup>().FirstOrDefault() is Model3DGroup geometry)
        {
            var transform = new Transform3DGroup(); transform.Children.Add(new RotateTransform3D(_pitch)); transform.Children.Add(new RotateTransform3D(_yaw)); geometry.Transform = transform;
            var index = 0;
            foreach (var mesh in geometry.Children.OfType<GeometryModel3D>())
            {
                var part = index++;
                var material = mesh.Material; var back = mesh.BackMaterial;
                var visible = !_meshVisibility.TryGetValue(part, out var remembered) || remembered;
                var toggle = WorkflowUi.Check(_models, "Mesh " + part + " - " + (data?.Parts.ElementAtOrDefault(part)?.TextureName ?? "Diffuse"), visible);
                if (!visible) { mesh.Material = null; mesh.BackMaterial = null; }
                toggle.Checked += (_, _) => { _meshVisibility[part] = true; mesh.Material = material; mesh.BackMaterial = back; };
                toggle.Unchecked += (_, _) => { _meshVisibility[part] = false; mesh.Material = null; mesh.BackMaterial = null; };
                if (material is DiffuseMaterial { Brush: ImageBrush image })
                {
                    _textures.Children.Add(new TextBlock { Text = "Mesh " + part, Margin = new Thickness(0, 6, 0, 4) });
                    _textures.Children.Add(new Image { Source = image.ImageSource, Height = 140, Stretch = Stretch.Uniform });
                }
            }
        }
    }
}
