using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using CodeWalker.GameFiles;
using CodeWalker.Utils;
using Microsoft.Win32;

namespace FooxClothStudio;

public partial class MainWindow : Window
{
    private ClothProject _project = new();
    private string? _projectPath;
    private bool _dirty;
    private bool _updatingDetails;
    private int _previewRequestId;
    private int _modelRequestId;
    private int _materialRequestId;
    private ModelPreviewData? _currentModelData;
    private AxisAngleRotation3D? _modelYaw;
    private AxisAngleRotation3D? _modelPitch;
    private Point? _modelDragStart;
    private string? _selectedTexturePath;
    private FiveMResource? _resource;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    // Keeps the first-open window inside the usable screen area (taskbar and DPI scaling included).
    private void FitToWorkArea()
    {
        var area = SystemParameters.WorkArea;
        Width = Math.Max(MinWidth, Math.Min(Width, area.Width * 0.92));
        Height = Math.Max(MinHeight, Math.Min(Height, area.Height * 0.92));
    }

    public MainWindow()
    {
        InitializeComponent();
        FitToWorkArea();
        RefreshRecentFilesMenu();
        Resources[typeof(ComboBox)] = WorkflowUi.ComboStyle();
        TypeBox.ItemsSource = ClothLabelConverter.TypeChoices(false).ToArray();
        MaskModelBox.ItemsSource = new[] { "None", "prop_mask_motobike", "prop_mask_motobike_a", "prop_mask_motobike_b", "prop_mask_motox", "prop_mask_flight", "prop_mask_scuba01", "prop_mask_scuba02", "prop_mask_scuba03", "prop_mask_scuba04", "prop_mask_specops", "prop_mask_ballistic", "prop_mask_bugstar", "prop_mask_fireman" };
        CategoryList.ItemsSource = new[] { "Todas" }.Concat(ClothCatalog.Categories).ToArray();
        CategoryList.SelectedIndex = 0;
        RefreshItems();
        ShowDetails(null);
        Status("Pronto para importar modelos YDD e texturas YTD.");
        var startupProject = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault();
        if (startupProject is not null && File.Exists(startupProject))
        {
            try { LoadProject(startupProject); }
            catch (Exception ex) { Status("Falha ao abrir projeto inicial: " + ex.Message); }
        }
    }

    private void Status(string message) => StatusText.Text = message;
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        try { HandleWindowShortcut(sender, e); }
        catch (Exception ex) { Status("Action failed: " + ex.Message); e.Handled = true; }
    }
    private void HandleWindowShortcut(object sender, KeyEventArgs e)
    {
        if (TryCopyShortcut(e)) return;
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        { SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.Control && ItemList.IsKeyboardFocusWithin)
        {
            if (e.Key == Key.H) { ReplaceModel_Click(sender, new RoutedEventArgs()); e.Handled = true; }
            else if (e.Key == Key.J) { ReplaceSelectedWithDummy(); e.Handled = true; }
            else if (e.Key == Key.E && ItemList.SelectedItem is ClothItem item && !item.IsDummy)
            { ShowSourceInExplorer(item.ModelPath); e.Handled = true; }
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && TextureList.IsKeyboardFocusWithin)
        {
            if (e.Key == Key.H) { ReplaceTexture_Click(sender, new RoutedEventArgs()); e.Handled = true; }
            else if (e.Key == Key.J) { ReplaceSelectedTextureWithDummy(); e.Handled = true; }
            else if (e.Key == Key.E && ItemList.SelectedItem is ClothItem item && TextureList.SelectedIndex >= 0 && !string.IsNullOrWhiteSpace(item.TexturePaths[TextureList.SelectedIndex]))
            { ShowSourceInExplorer(item.TexturePaths[TextureList.SelectedIndex]); e.Handled = true; }
        }
        else if (Keyboard.Modifiers == ModifierKeys.None && ItemList.IsKeyboardFocusWithin)
        {
            if (e.Key == Key.F2 && ItemList.SelectedItem is ClothItem)
            { RenameSelectedItem(); e.Handled = true; }
            else if (e.Key == Key.Delete) { Remove_Click(sender, new RoutedEventArgs()); e.Handled = true; }
            else if (e.Key == Key.Enter) { Preview_Click(sender, new RoutedEventArgs()); e.Handled = true; }
        }
        else if (Keyboard.Modifiers == ModifierKeys.None && TextureList.IsKeyboardFocusWithin)
        {
            if (e.Key == Key.Delete) { RemoveTexture_Click(sender, new RoutedEventArgs()); e.Handled = true; }
            else if (e.Key == Key.F2) { RenameSelectedTexture(); e.Handled = true; }
            else if (e.Key == Key.Enter) { Preview_Click(sender, new RoutedEventArgs()); e.Handled = true; }
        }
    }
    private ClothPreviewWindow? _clothPreviewWindow;
    private ModelPreviewPart[] _pedParts = [];
    private ModelPreviewData? _displayModelData;
    private Dictionary<string, BitmapSource> _pedTextures = new(StringComparer.OrdinalIgnoreCase);
    private string? _pedGender;
    private readonly Dictionary<(string Gender, int Tag), Matrix3D> _boneAttachments = new();
    private int _pedRequest;
    private bool _showBasePed = true;
    private readonly Dictionary<string, Task<(ModelPreviewPart[], Dictionary<string, BitmapSource>)>> _pedLoads = new();
    private async Task LoadBasePedAsync(ClothItem item)
    {
        var request = ++_pedRequest;
        var game = GamePedAssets.FindGameFolder();
        if (game is null) { _clothPreviewWindow?.SetPedStatus("Select GTA V Folder to load the base ped."); return; }
        var key = game + "|" + item.Gender;
        try
        {
            if (!_pedLoads.TryGetValue(key, out var task))
            {
                task = Task.Run(() =>
                {
                    var parts = new List<ModelPreviewPart>(); var textures = new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);
                    foreach (var asset in GamePedAssets.Load(game, item.Gender))
                    {
                        var model = ModelPreview.Read(asset.ModelPath, false);
                        var images = asset.TexturePath is null ? new Dictionary<string, BitmapSource>() : ReadMatchingTextures(asset.TexturePath, model);
                        foreach (var part in model.Parts)
                        {
                            var name = "__ped_" + asset.Slot + "_" + parts.Count;
                            if (images.TryGetValue(part.TextureName ?? "__selected_diffuse", out var image)) textures[name] = image;
                            parts.Add(part with { TextureName = name });
                        }
                    }
                    return (parts.ToArray(), textures);
                });
                _pedLoads[key] = task;
            }
            var loaded = await task;
            if (request != _pedRequest) return;
            _pedParts = loaded.Item1; _pedTextures = loaded.Item2; _pedGender = item.Gender;
            _clothPreviewWindow?.SetPedStatus((item.Gender == "Feminino" ? "Female" : "Male") + " freemode ped · GTA V base components");
            if (_currentModelData is not null)
            {
                ShowModelPreview(_currentModelData);
                if (_selectedTexturePath is not null) await LoadModelMaterialsAsync(_selectedTexturePath, _modelRequestId);
            }
        }
        catch (Exception ex)
        {
            if (_pedLoads.TryGetValue(key, out var failed) && (failed.IsFaulted || failed.IsCanceled)) _pedLoads.Remove(key);
            if (request == _pedRequest) _clothPreviewWindow?.SetPedStatus("Base ped unavailable: " + ex.Message);
        }
    }
    private void PreviewList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || sender is not ListBox list ||
            ItemsControl.ContainerFromElement(list, e.OriginalSource as DependencyObject) is not ListBoxItem) return;
        Preview_Click(sender, e);
        e.Handled = true;
    }
    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (ItemList.SelectedItem is not ClothItem item || item.IsDummy) return;
        if (_clothPreviewWindow is null)
        {
            _clothPreviewWindow = new ClothPreviewWindow(_showBasePed) { Owner = this };
            _clothPreviewWindow.FilesDropped += data =>
            {
                try { ImportFiles(ClothingDrop.Materialize(data)); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not import dropped files", MessageBoxButton.OK, MessageBoxImage.Error); }
            };
            _clothPreviewWindow.ShowPedChanged += show => { _showBasePed = show; if (_currentModelData is not null) { ShowModelPreview(_currentModelData); if (_selectedTexturePath is not null) _ = LoadModelMaterialsAsync(_selectedTexturePath, _modelRequestId); } };
            _clothPreviewWindow.SelectGameFolder += () =>
            {
                var picker = new Microsoft.Win32.OpenFolderDialog { Title = "Select GTA V Folder" };
                if (picker.ShowDialog(_clothPreviewWindow) != true) return;
                if (!File.Exists(Path.Combine(picker.FolderName, "x64v.rpf"))) { _clothPreviewWindow.SetPedStatus("Select the GTA V Legacy folder containing x64v.rpf."); return; }
                var settings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ParadiseClothStudio"); Directory.CreateDirectory(settings); File.WriteAllText(Path.Combine(settings, "gta-folder.txt"), picker.FolderName);
                _pedLoads.Clear(); if (ItemList.SelectedItem is ClothItem selected) _ = LoadBasePedAsync(selected);
            };
            _clothPreviewWindow.Closed += (_, _) => _clothPreviewWindow = null;
        }
        UpdateClothPreviewWindow();
        _clothPreviewWindow.Show();
        _clothPreviewWindow.Activate();
        _ = LoadBasePedAsync(item);
    }
    private void UpdateClothPreviewWindow()
    {
        if (_clothPreviewWindow is null) return;
        var item = ItemList.SelectedItem as ClothItem;
        _clothPreviewWindow.UpdatePreview(item?.DisplayName ?? "No drawable selected", TextureList.SelectedIndex,
            ModelPreviewVisual.Content as Model3DGroup, ModelPreviewText.Text, _displayModelData ?? _currentModelData, item?.Id.ToString());
    }
    private void Tattoos_Click(object sender, RoutedEventArgs e)
        => OpenTattooEditor("Both");
    private void OpenTattooEditor(string gender)
    {
        Keyboard.ClearFocus();
        var window = new TattooWindow(_project, AbsoluteAssetPath, gender) { Owner = this };
        if (window.ShowDialog() == true) { _project.Tattoos = window.Result; _dirty = true; RefreshItems(); Status(_project.Tattoos.Count + " tattoo(s) in project."); }
    }
    private void AddDummy_Click(object sender, RoutedEventArgs e)
        => AddReservation(null);
    private void AddReservation(string? gender)
    {
        var selected = ItemList.SelectedItem as ClothItem;
        var dialog = new ReservationWindow(gender ?? selected?.Gender ?? "Masculino", selected?.Category ?? ClothCatalog.Categories[11]) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        var group = _project.Items.Where(x => x.Gender == dialog.Gender && x.Category == dialog.Category).ToArray();
        var dummy = new ClothItem { Name = "Reservation", Gender = dialog.Gender, Category = dialog.Category, IsDummy = true,
            ExportPosition = group.Length == 0 ? 0 : group.Max(x => x.ExportPosition ?? x.SourceIndex ?? 0) + 1 };
        _project.Items.Add(dummy); _dirty = true; RefreshItems(); ItemList.SelectedItem = dummy;
    }
    private void AddDummyTexture_Click(object sender, RoutedEventArgs e)
    {
        if (ItemList.SelectedItem is not ClothItem item || item.IsDummy) return;
        if (item.TexturePaths.Count >= 26) { Status("Maximum 26 textures per drawable."); return; }
        AlignTextureNames(item); item.TexturePaths.Add(""); item.OriginalTextureFileNames.Add("(dummy)");
        _dirty = true; RefreshTextureList(item, item.TexturePaths.Count - 1); RefreshItems();
    }
    private void HairOptions_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingDetails || ItemList?.SelectedItem is not ClothItem item) return;
        item.HairOption = (HairOptionBox.SelectedItem as ListBoxItem)?.Content as string ?? "Show hairs";
        CustomHairPanel.Visibility = item.Category.Contains("[p_head]") && item.HairOption == "Custom cut hairs" ? Visibility.Visible : Visibility.Collapsed;
        _dirty = true;
    }
    private void CustomHair_Click(object sender, RoutedEventArgs e)
    {
        if (ItemList.SelectedItem is not ClothItem item || !item.Category.Contains("[p_head]")) return;
        CustomHairOption.Visibility = Visibility.Visible; HairOptionBox.SelectedItem = CustomHairOption;
    }
    private void Hoodie_Changed(object sender, RoutedEventArgs e)
    {
        if (_updatingDetails || ItemList?.SelectedItem is not ClothItem item) return;
        item.HoodieHairs = HoodieBox.IsChecked == true; _dirty = true;
    }
    private void TextureEffect_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_updatingDetails || ItemList.SelectedItem is not ClothItem item || TextureList.SelectedIndex < 0) return;
        if (!float.TryParse(TimeCycleIntensityBox.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture,
            out var intensity) || !float.IsFinite(intensity) || intensity is < 0.1f or > 1)
        { Status("TimeCycle Intensity must be between 0.1 and 1."); return; }
        AlignTextureNames(item);
        var effect = item.TextureEffects[TextureList.SelectedIndex];
        effect.TimeCycleModifier = TimeCycleBox.Text == "None" ? "" : TimeCycleBox.Text.Trim(); effect.Intensity = intensity; _dirty = true;
    }
    private void SetFirstPerson_Click(object sender, RoutedEventArgs e)
    {
        if (ItemList.SelectedItem is not ClothItem item || item.IsDummy) return;
        var picker = new OpenFileDialog { Filter = "GTA model (*.ydd)|*.ydd", Title = "Select first person model" };
        if (picker.ShowDialog() != true) return;
        try { CheckYdd(picker.FileName); if (item.AlternateModelPaths.Count == 0) item.AlternateModelPaths.Add(picker.FileName); else item.AlternateModelPaths[0] = picker.FileName;
            item.HasFirstPersonModel = true; _dirty = true; RefreshAlternateModelList(item); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Invalid model"); }
    }
    private void ChooseAlternate_Click(object sender, RoutedEventArgs e)
    {
        var code = (ItemList.SelectedItem as ClothItem)?.Category.Split('[').Last().TrimEnd(']');
        if (code is "teef" or "accs" or "task" or "jbib") SetFirstPerson_Click(sender, e);
        else AddAlternateModel_Click(sender, e);
    }
    private void ClearFirstPerson_Click(object sender, RoutedEventArgs e)
    {
        if (ItemList.SelectedItem is not ClothItem item || item.AlternateModelPaths.Count == 0) return;
        item.AlternateModelPaths.RemoveAt(0); item.HasFirstPersonModel = false; _dirty = true; RefreshAlternateModelList(item);
    }
    private void ClothKind_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingDetails || ItemList?.SelectedItem is not ClothItem item) return;
        var isProp = item.Category.Contains("[p_", StringComparison.Ordinal);
        if (isProp != (ClothKindBox.SelectedIndex == 1))
        {
            var props = ClothKindBox.SelectedIndex == 1;
            _updatingDetails = true;
            TypeBox.ItemsSource = ClothLabelConverter.TypeChoices(props).ToArray();
            TypeBox.SelectedItem = props ? ClothCatalog.Categories[9] : ClothCatalog.Categories[11];
            _updatingDetails = false;
            Details_Changed(sender, e);
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ProjectSettingsWindow(_project, AbsoluteAssetPath) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        // Retain item identities used by the selected list and active previews.
        foreach (var item in _project.Items)
            item.ExportPosition = dialog.Result.Items.Single(candidate => candidate.Id == item.Id).ExportPosition;
        dialog.Result.Items = _project.Items;
        _project = dialog.Result;
        _dirty = true;
        RefreshItems();
        Status("Project settings updated. Save the project to keep changes.");
    }

    private void AddMale_Click(object sender, RoutedEventArgs e) => ImportForGender("Masculino");
    private void AddMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        var gender = button.Tag as string;
        var menu = new ContextMenu { PlacementTarget = button, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            Background = WorkflowUi.Panel, Foreground = System.Windows.Media.Brushes.White };
        void Entry(string title, Action action)
        {
            var entry = new MenuItem { Header = title }; entry.Click += (_, _) => action(); menu.Items.Add(entry);
        }
        Entry("Add from package", () => { _ = ImportItemBundleAsync(gender); });
        Entry("Add from file(s)", () => { if (gender is null) ImportFiles_Click(button, new RoutedEventArgs()); else ImportForGender(gender); });
        Entry("Add from folder(s)", () =>
        {
            var existing = _project.Items.Select(item => item.Id).ToHashSet();
            ImportFolder_Click(button, new RoutedEventArgs());
            if (gender is not null) foreach (var item in _project.Items.Where(item => !existing.Contains(item.Id)))
            { item.Gender = gender; CollectionIndices.Append(_project, item); }
            RefreshItems();
        });
        Entry("Add tattoo", () => OpenTattooEditor(gender == "Masculino" ? "Male" : gender == "Feminino" ? "Female" : "Both"));
        Entry("Add reservation dummy", () => AddReservation(gender));
        button.ContextMenu = menu; menu.IsOpen = true;
    }
    private void AddFemale_Click(object sender, RoutedEventArgs e) => ImportForGender("Feminino");
    private void ImportForGender(string gender)
    {
        var dialog = new OpenFileDialog { Filter = "GTA clothing (*.ydd;*.ytd;*.yld)|*.ydd;*.ytd;*.yld", Multiselect = true };
        if (dialog.ShowDialog() != true) return;
        var known = _project.Items.Select(item => item.Id).ToHashSet();
        ImportFiles(dialog.FileNames);
        foreach (var item in _project.Items.Where(item => !known.Contains(item.Id)))
        { item.Gender = gender; CollectionIndices.Append(_project, item); }
        RefreshItems();
    }

    private void Metadata_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_updatingDetails || ItemList.SelectedItem is not ClothItem item) return;
        if (!int.TryParse(PositionBox.Text, out var position) || position < 0 || position > 999 ||
            !int.TryParse(ShopCostBox.Text, out var cost) || cost < 0)
        { Status("Position must be 0–999 and Cost must be a non-negative integer."); return; }
        uint? flags = null;
        if (!string.IsNullOrWhiteSpace(BehaviourBox.Text) &&
            !BehaviourBox.Text.Equals("Default", StringComparison.OrdinalIgnoreCase))
        {
            if (!uint.TryParse(BehaviourBox.Text, out var value))
            { Status("Behaviour Flags must be Default or an unsigned 32-bit value."); return; }
            flags = value;
        }
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        if (!float.TryParse(MaskXBox.Text, System.Globalization.NumberStyles.Float, culture, out var maskX) ||
            !float.TryParse(MaskYBox.Text, System.Globalization.NumberStyles.Float, culture, out var maskY) ||
            !float.TryParse(MaskZBox.Text, System.Globalization.NumberStyles.Float, culture, out var maskZ) ||
            !float.IsFinite(maskX) || !float.IsFinite(maskY) || !float.IsFinite(maskZ))
        { Status("Mask offsets must be finite numbers."); return; }
        if (!float.TryParse(HairCutBox.Text, System.Globalization.NumberStyles.Float, culture, out var hair) || !float.IsFinite(hair) || hair is < -1 or > 0)
        { Status("Custom cut amount must be between -1 and 0."); return; }
        if (!float.TryParse(HeelBox.Text, System.Globalization.NumberStyles.Float, culture, out var heel) || !float.IsFinite(heel) || heel is < 0 or > 1)
        { Status("Heel Height must be between 0 and 1."); return; }
        if (position != (item.ExportPosition ?? item.SourceIndex ?? 0))
        {
            if (_project.PublishedCollection is not null && !_project.UsePermanentClothingIds)
            { PositionBox.Text = (item.ExportPosition ?? item.SourceIndex ?? 0).ToString(); Status("Collection publicada: reordenação bloqueada; adicione peças no final."); return; }
            var group = _project.Items.Where(candidate => candidate.Gender == item.Gender && candidate.Category == item.Category)
                .OrderBy(candidate => candidate.ExportPosition ?? candidate.SourceIndex).ThenBy(candidate => candidate.Name).ToList();
            group.Remove(item); group.Insert(Math.Clamp(position, 0, group.Count), item);
            for (var index = 0; index < group.Count; index++) group[index].ExportPosition = index;
            PositionBox.Text = item.ExportPosition.ToString();
        }
        item.ShopCost = cost; item.BehaviourFlags = flags;
        item.FirstPersonMaskModel = MaskModelBox.Text == "None" ? "" : MaskModelBox.Text.Trim();
        item.MaskOffsetX = maskX; item.MaskOffsetY = maskY; item.MaskOffsetZ = maskZ;
        item.HairCutValue = hair; item.HeelHeight = heel;
        item.AudioPreset = string.IsNullOrWhiteSpace(AudioBox.Text) ? "none" : AudioBox.Text.Trim();
        item.ShopTextLabel = ShopLabelBox.Text; item.ShopLockHash = ShopLockBox.Text;
        item.Creator = CreatorBox.Text; item.CreatorWebsite = CreatorWebsiteBox.Text;
        item.CreatorDescription = CreatorDescriptionBox.Text;
        _dirty = true; RefreshItems();
    }

    private void Help_Click(object sender, RoutedEventArgs e) =>
        MessageBox.Show("Paradise Cloth Studio\nImport YDD/YTD/YLD with Options > Add.\n" +
            "Save projects with Project > Save.\nBuild a FiveM addon with Build project.",
            "Help", MessageBoxButton.OK, MessageBoxImage.Information);

    private readonly Dictionary<string, bool> _categoryExpanded = new();
    private void ExpandAllCategories_Click(object sender, RoutedEventArgs e) => SetAllCategoriesExpanded(true);
    private void CollapseAllCategories_Click(object sender, RoutedEventArgs e) => SetAllCategoriesExpanded(false);
    private void SetAllCategoriesExpanded(bool expanded)
    {
        foreach (var category in _project.Items.Select(item => item.Category).Distinct())
            _categoryExpanded[category] = expanded;
        void Visit(DependencyObject node)
        {
            if (node is Expander { DataContext: CollectionViewGroup } expander) expander.IsExpanded = expanded;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) Visit(VisualTreeHelper.GetChild(node, i));
        }
        ItemList.UpdateLayout(); Visit(ItemList);
    }
    private void Category_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is Expander expander && expander.DataContext is CollectionViewGroup group && group.Name is string category && _categoryExpanded.TryGetValue(category, out var open)) expander.IsExpanded = open;
    }
    private void Category_ExpansionChanged(object sender, RoutedEventArgs e)
    {
        if (sender is Expander { IsLoaded: true } expander && expander.DataContext is CollectionViewGroup group && group.Name is string category) _categoryExpanded[category] = expander.IsExpanded;
    }
    private void ExpandCategory(string category)
    {
        _categoryExpanded[category] = true;
        void Visit(DependencyObject node)
        {
            if (node is Expander expander && expander.DataContext is CollectionViewGroup group && Equals(group.Name, category)) expander.IsExpanded = true;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) Visit(VisualTreeHelper.GetChild(node, i));
        }
        ItemList.UpdateLayout(); Visit(ItemList);
    }
    private void RefreshItems()
    {
        if (ItemList is null || CategoryList is null || SearchBox is null) return;
        foreach (var item in _project.Items) ClothingIdentityMap.EnsureTextures(item);
        var selected = ItemList.SelectedItem as ClothItem;
        var selectedTexture = TextureList.SelectedIndex;
        var category = CategoryList.SelectedItem as string;
        var query = SearchBox.Text?.Trim() ?? "";
        var filtered = _project.Items.Where(item =>
            (category is null or "Todas" || item.Category == category) &&
            (query.Length == 0 ||
             item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             item.SourceIndex?.ToString("D3").Contains(query, StringComparison.OrdinalIgnoreCase) == true ||
             (item.OriginalModelFileName ?? Path.GetFileName(item.ModelPath))
                 .Contains(query, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(item => item.Category).ThenBy(item => item.Gender == "Masculino" ? 0 : item.Gender == "Feminino" ? 1 : 2).ThenBy(item => item.ExportPosition ?? item.SourceIndex)
            .ThenBy(item => item.Name).ToArray();
        foreach (var item in filtered)
        {
            item.ModelWarningMiB = _project.ModelWarningMiB; item.TextureWarningMiB = _project.TextureWarningMiB;
            var key = item.ModelPath + "|" + string.Join("|", item.TexturePaths);
            if (item.MetricsKey == key) continue;
            if (item.MetricsKey is null && item.ModelSizeBytes.HasValue && item.MaxTextureSizeBytes.HasValue)
            {
                // Reuse saved list metrics on project open; no automatic asset scan is needed.
                item.MetricsKey = key;
                continue;
            }
            item.MetricsKey = key;
            if (item.IsDummy) { item.ModelSizeBytes = null; item.MaxTextureSizeBytes = null; continue; }
            try
            {
                var modelPath = AbsoluteAssetPath(item.ModelPath);
                item.ModelSizeBytes = !item.IsDummy && File.Exists(modelPath) ? new FileInfo(modelPath).Length : null;
                item.MaxTextureSizeBytes = item.TexturePaths.Where(path => !string.IsNullOrWhiteSpace(path)).Select(AbsoluteAssetPath).Where(File.Exists).Select(path => new FileInfo(path).Length).DefaultIfEmpty(0).Max();
            }
            catch (IOException) { item.ModelSizeBytes = null; item.MaxTextureSizeBytes = null; }
        }
        var grouped = new ListCollectionView(filtered);
        grouped.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ClothItem.Category)));
        ItemList.ItemsSource = grouped;
        if (selected is not null && filtered.Contains(selected))
        {
            ItemList.SelectedItem = selected;
            if (selectedTexture >= 0 && selectedTexture < TextureList.Items.Count) TextureList.SelectedIndex = selectedTexture;
        }
        CountText.Text = filtered.Length + " / " + _project.Items.Count + " drawables • " +
            _project.Tattoos.Count + " tattoos • " + _project.AppearanceAssets.Count + " appearance textures";
        ProjectText.Text = _project.Name + (_dirty ? " *" : "") +
            (_projectPath is null ? "  •  projeto não salvo" : "  •  " + _projectPath);
        if (_duplicateReviewNeeded || DuplicateReview.IsExpanded)
        {
            var expand = _duplicateReviewNeeded; _duplicateReviewNeeded = false;
            if (expand && !DuplicateReview.IsExpanded) DuplicateReview.IsExpanded = true;
            else _ = RefreshDuplicateReviewAsync();
        }
    }

    private bool ConfirmDiscard()
    {
        if (!_dirty) return true;
        return MessageBox.Show("Descartar alterações não salvas?", "Paradise Cloth Studio",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        var candidate = new ClothProject { Name = "", OutputFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Paradise Cloth Studio") };
        var dialog = new ProjectSettingsWindow(candidate, AbsoluteAssetPath, creating: true) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try { var path = ProjectCreator.Create(dialog.Result); LoadProject(path); Status("Project created: " + path); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not create project", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        var dialog = new OpenFileDialog
        {
            Filter = "Projetos de roupas (*.fcsproj;*.dctproj)|*.fcsproj;*.dctproj|Paradise Cloth Studio (*.fcsproj)|*.fcsproj|Durty Cloth Tool (*.dctproj)|*.dctproj",
            Title = "Abrir projeto"
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var project = RunWithLoading("A abrir projeto", "A ler o projeto...", progress =>
            {
                var loaded = Path.GetExtension(dialog.FileName).Equals(".dctproj", StringComparison.OrdinalIgnoreCase)
                    ? DctProjectImporter.Import(dialog.FileName)
                    : JsonSerializer.Deserialize<ClothProject>(File.ReadAllText(dialog.FileName));
                if (loaded is null || loaded.FormatVersion != 1)
                    throw new InvalidDataException("Versão de projeto não suportada.");
                progress.Report((1, 1, "A preparar a lista de peças..."));
                return loaded;
            });
            LoadProject(dialog.FileName, project);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Não foi possível abrir", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadProject(string path, ClothProject? loaded = null)
    {
        if (Path.GetExtension(path).Equals(".dctproj", StringComparison.OrdinalIgnoreCase))
        {
            _project = loaded ?? DctProjectImporter.Import(path);
            _resource = null;
            _projectPath = null;
            _dirty = true;
            RefreshItems();
            ShowDetails(null);
            RememberRecentProject(path);
            Status("Projeto DCT importado: " + _project.Items.Count +
                " peças. Salve para criar um .fcsproj independente.");
            return;
        }
        var project = loaded ?? JsonSerializer.Deserialize<ClothProject>(File.ReadAllText(path));
        if (project is null || project.FormatVersion != 1)
            throw new InvalidDataException("Versão de projeto não suportada.");
        _project = project;
        _resource = null;
        _projectPath = path;
        _dirty = false;
        RefreshItems();
        ShowDetails(null);
        RememberRecentProject(path);
        Status("Projeto aberto: " + project.Items.Count + " peças.");
    }

    private async void Save_Click(object sender, RoutedEventArgs e) => await SaveProjectAsync();

    private async void SaveAs_Click(object sender, RoutedEventArgs e) => await SaveProjectAsync(true);

    private async Task<bool> SaveProjectAsync(bool saveAs = false)
    {
        var path = saveAs ? null : _projectPath;
        if (path is null)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Projeto Paradise Cloth Studio (*.fcsproj)|*.fcsproj",
                FileName = _projectPath is null ? "meu_projeto.fcsproj" : Path.GetFileName(_projectPath),
                Title = "Salvar projeto"
            };
            if (dialog.ShowDialog() != true) return false;
            path = dialog.FileName;
        }
        OperationProgressWindow? saving = null;
        try
        {
            var staged = JsonSerializer.Deserialize<ClothProject>(
                JsonSerializer.Serialize(_project, JsonOptions))!;
            IsEnabled = false;
            var total = staged.Items.Count(i => !i.IsDummy);
            saving = new OperationProgressWindow("Salvando projeto", total, ProjectOperationSummary.Describe(staged)) { Owner = this };
            saving.Show();
            var saved = 0;
            IProgress<(int Done, string Name)> saveProgress = new Progress<(int Done, string Name)>(p => saving.Report(p.Done, p.Name));
            Status($"Salvando {ProjectOperationSummary.Describe(staged)}...");
            await Task.Run(() =>
            {
                var projectRoot = Path.GetDirectoryName(Path.GetFullPath(path))!;
                var assetsRoot = Path.Combine(projectRoot, "assets");
                Directory.CreateDirectory(assetsRoot);
                if (staged.SourceAlternateVariationsPath is not null)
                    staged.SourceAlternateVariationsPath = StoreAsset(
                        staged.SourceAlternateVariationsPath, Guid.Empty, assetsRoot, projectRoot);
                foreach (var tattoo in staged.Tattoos)
                    tattoo.ImagePath = StoreAsset(tattoo.ImagePath, tattoo.Id, assetsRoot, projectRoot);
                foreach (var appearance in staged.AppearanceAssets)
                    appearance.TexturePath = StoreAsset(appearance.TexturePath, appearance.Id, assetsRoot, projectRoot);
                foreach (var item in staged.Items)
                {
                    if (!item.IsDummy) item.ModelPath = StoreAsset(item.ModelPath, item.Id, assetsRoot, projectRoot);
                    item.AlternateModelPaths = item.AlternateModelPaths.Select((source, index) =>
                        StoreAsset(source, item.Id, assetsRoot, projectRoot,
                            $"alternate_{index:D2}_{Path.GetFileName(AbsoluteAssetPath(source))}")).ToList();
                    item.TexturePaths = item.TexturePaths.Select((source, index) =>
                        string.IsNullOrWhiteSpace(source) ? "" : StoreAsset(source, item.Id, assetsRoot, projectRoot,
                            $"texture_{index:D2}_{Path.GetFileName(AbsoluteAssetPath(source))}")).ToList();
                    if (item.PhysicsPath is not null)
                        item.PhysicsPath = StoreAsset(item.PhysicsPath, item.Id, assetsRoot, projectRoot,
                            "physics_" + Path.GetFileName(AbsoluteAssetPath(item.PhysicsPath)));
                    if (!item.IsDummy) saveProgress.Report((++saved, item.DisplayName));
                }
                if (staged.Name == "Novo projeto")
                    staged.Name = Path.GetFileNameWithoutExtension(path);
                var json = JsonSerializer.Serialize(staged, JsonOptions);
                var temporary = path + ".tmp";
                File.WriteAllText(temporary, json, new UTF8Encoding(false));
                File.Move(temporary, path, true);
            });
            _project = staged;
            _projectPath = path;
            _dirty = false;
            RefreshItems();
            saving.Report(total, "Gravação concluída");
            RememberRecentProject(path);
            Status($"Projeto salvo: {ProjectOperationSummary.Describe(staged)}.");
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Não foi possível salvar", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        finally { saving?.Complete(); IsEnabled = true; }
    }

    private string StoreAsset(string source, Guid itemId, string assetsRoot, string projectRoot,
        string? destinationName = null)
    {
        var absolute = AbsoluteAssetPath(source);
        if (!File.Exists(absolute)) throw new FileNotFoundException("Arquivo ausente", absolute);
        var folder = Path.Combine(assetsRoot, itemId.ToString("N"));
        Directory.CreateDirectory(folder);
        var destination = Path.Combine(folder,
            absolute.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileName(absolute)
                : destinationName ?? Path.GetFileName(absolute));
        if (!string.Equals(absolute, destination, StringComparison.OrdinalIgnoreCase))
            File.Copy(absolute, destination, true);
        return Path.GetRelativePath(projectRoot, destination);
    }

    private string AbsoluteAssetPath(string path)
    {
        if (Path.IsPathRooted(path)) return Path.GetFullPath(path);
        if (_projectPath is null) throw new InvalidDataException("Arquivo relativo sem projeto salvo: " + path);
        var root = Path.GetDirectoryName(Path.GetFullPath(_projectPath))!;
        var absolute = Path.GetFullPath(Path.Combine(root, path));
        if (!absolute.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Caminho fora do projeto: " + path);
        return absolute;
    }

    private void ImportFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Modelos e texturas GTA|*.ydd;*.ytd;*.yld",
            Multiselect = true,
            Title = "Selecione YDD, YTD e YLD"
        };
        if (dialog.ShowDialog() == true) ImportFiles(dialog.FileNames);
    }

    private void ImportBundle_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        var fileDialog = new OpenFileDialog
        {
            Filter = "Pacote Paradise Cloth Studio (*.fcsbundle)|*.fcsbundle",
            Title = "Importar projeto completo"
        };
        if (fileDialog.ShowDialog() != true) return;
        var folderDialog = new OpenFolderDialog { Title = "Selecione a pasta de destino do projeto" };
        if (folderDialog.ShowDialog() != true) return;
        try
        {
            var path = RunWithLoading("A importar pacote", "A extrair e verificar os ficheiros...",
                progress =>
                {
                    var path = ProjectBundle.Import(fileDialog.FileName, folderDialog.FolderName);
                    progress.Report((1, 1, "A abrir o projeto..."));
                    return path;
                });
            LoadProject(path);
            Status("Pacote importado: " + path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Não foi possível importar o pacote",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ExportBundle_Click(object sender, RoutedEventArgs e)
    {
        if (_project.Items.Count == 0 && _project.Tattoos.Count == 0 && _project.AppearanceAssets.Count == 0) { Status("Add clothes or tattoos before exporting the project."); return; }
        var dialog = new SaveFileDialog
        {
            Filter = "Pacote Paradise Cloth Studio (*.fcsbundle)|*.fcsbundle",
            FileName = Sanitize(_project.Name) + ".fcsbundle",
            Title = "Exportar projeto completo"
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            IsEnabled = false;
            Status($"Exportando {ProjectOperationSummary.Describe(_project)} e seus arquivos...");
            await Task.Run(() => ProjectBundle.Export(_project, AbsoluteAssetPath, dialog.FileName));
            Status($"Projeto exportado: {ProjectOperationSummary.Describe(_project)}. " + dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Não foi possível exportar o projeto",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { IsEnabled = true; }
    }

    private async void ExportItemBundle_Click(object sender, RoutedEventArgs e)
    {
        if (ItemList.SelectedItem is not ClothItem item)
        {
            Status("Selecione uma peça para exportar.");
            return;
        }
        var dialog = new SaveFileDialog
        {
            Filter = "Peça Paradise Cloth Studio (*.fcsitem)|*.fcsitem",
            FileName = Sanitize(item.Name) + ".fcsitem",
            Title = "Exportar peça com YDD, YTD e YLD"
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            IsEnabled = false;
            Status("Empacotando a peça selecionada...");
            var selection = new ClothProject { Name = item.Name, Items = [item] };
            await Task.Run(() => ProjectBundle.Export(selection, AbsoluteAssetPath, dialog.FileName));
            Status("Peça exportada: " + dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Não foi possível exportar a peça",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { IsEnabled = true; }
    }

    private async void ExportItemFiles_Click(object sender, RoutedEventArgs e)
    {
        if (ItemList.SelectedItem is not ClothItem item)
        {
            Status("Selecione uma peça para exportar seus arquivos.");
            return;
        }
        var dialog = new OpenFolderDialog { Title = "Selecione a pasta de saída dos YDD/YTD/YLD" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            IsEnabled = false;
            Status("Exportando arquivos nativos da peça...");
            var result = await Task.Run(() => NativeItemExporter.Export(item,
                AbsoluteAssetPath, dialog.FolderName));
            Status($"{result.FileCount} arquivo(s) exportado(s) em {result.Folder}");
            MessageBox.Show("Arquivos exportados em:\n" + result.Folder,
                "Exportação concluída", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Status("Falha ao exportar arquivos da peça.");
            MessageBox.Show(ex.Message, "Exportação interrompida",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { IsEnabled = true; }
    }

    private async void ImportItemBundle_Click(object sender, RoutedEventArgs e)
        => await ImportItemBundleAsync();
    private async Task ImportItemBundleAsync(string? gender = null)
    {
        var fileDialog = new OpenFileDialog
        {
            Filter = "Peça Paradise Cloth Studio (*.fcsitem)|*.fcsitem",
            Title = "Importar peça"
        };
        if (fileDialog.ShowDialog() != true) return;
        var folderDialog = new OpenFolderDialog { Title = "Selecione a pasta para extrair a peça" };
        if (folderDialog.ShowDialog() != true) return;
        try
        {
            IsEnabled = false;
            Status("Importando peça e verificando arquivos...");
            var path = await Task.Run(() => ProjectBundle.Import(fileDialog.FileName, folderDialog.FolderName));
            var imported = JsonSerializer.Deserialize<ClothProject>(File.ReadAllText(path))!;
            var root = Path.GetDirectoryName(path)!;
            foreach (var item in imported.Items)
            {
                if (gender is not null && item.Gender != gender)
                {
                    item.Id = Guid.NewGuid();
                    item.TextureIds = item.TexturePaths.Select(_ => Guid.NewGuid()).ToList();
                }
                if (_project.Items.Any(existing => existing.Id == item.Id))
                    throw new InvalidDataException("Esta peça já existe no projeto com o mesmo ID permanente: " + item.Name);
            }
            foreach (var item in imported.Items)
            {
                if (gender is not null) item.Gender = gender;
                item.ModelPath = item.IsDummy ? "" : Path.Combine(root, item.ModelPath);
                item.AlternateModelPaths = item.AlternateModelPaths.Select(value => Path.Combine(root, value)).ToList();
                item.TexturePaths = item.TexturePaths.Select(value => string.IsNullOrWhiteSpace(value) ? "" : Path.Combine(root, value)).ToList();
                if (item.PhysicsPath is not null) item.PhysicsPath = Path.Combine(root, item.PhysicsPath);
                CollectionIndices.Append(_project, item);
                _project.Items.Add(item);
            }
            _dirty = true;
            RefreshItems();
            if (imported.Items.Count > 0) ItemList.SelectedItem = imported.Items[0];
            Status($"{imported.Items.Count} peça(s) importada(s) do pacote.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Não foi possível importar a peça",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { IsEnabled = true; }
    }

    private void OpenResource_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        var dialog = new OpenFolderDialog { Title = "Selecione a raiz do recurso FiveM" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var (resource, project) = RunWithLoading("A abrir recurso FiveM", "A verificar os ficheiros...",
                progress =>
                {
                    var resource = FiveMResource.Open(dialog.FolderName);
                    progress.Report((5, 100, "A identificar roupas e acessórios..."));
                    var clothes = Directory.EnumerateFiles(Path.Combine(resource.Root, "stream"), "*",
                        SearchOption.AllDirectories).Where(path =>
                        new[] { ".ydd", ".ytd", ".yld" }.Contains(Path.GetExtension(path),
                            StringComparer.OrdinalIgnoreCase));
                    var lastPercent = -1;
                    var items = ClothCatalog.Import(clothes, (done, total) =>
                    {
                        var percent = 5 + done * 75 / Math.Max(1, total);
                        if (percent == lastPercent) return;
                        lastPercent = percent;
                        progress.Report((percent, 100, $"A identificar roupas e acessórios: {done}/{total}"));
                    });
                    ClothingIdentityMap.Restore(resource.Root, items);
                    progress.Report((80, 100, "A importar tatuagens..."));
                    var hasTattooAssets = Directory.EnumerateFiles(resource.Root, "*.ytd", SearchOption.AllDirectories).Any(TattooImporter.LooksLikeTattooYtd) ||
                        Directory.EnumerateFiles(resource.Root, "*_overlays.xml", SearchOption.AllDirectories).Any();
                    var tattoos = hasTattooAssets ? TattooImporter.Import(resource.Root) : [];
                    progress.Report((90, 100, "A importar olhos, pelos e maquilhagem..."));
                    var appearanceAssets = AppearanceImporter.Import(resource.Root);
                    return (resource, new ClothProject
                    {
                        Name = Path.GetFileName(resource.Root),
                        SourceResourceRoot = resource.Root,
                        Items = items,
                        Tattoos = tattoos,
                        AppearanceAssets = appearanceAssets
                    });
                });
            _project = project;
            _resource = resource;
            _projectPath = null;
            _dirty = false;
            RefreshItems();
            ShowDetails(null);
            Status($"Recurso aberto: {_project.Items.Count} peças, {_project.Tattoos.Count} tatuagens/texturas de tatuagem, " +
                $"{_project.AppearanceAssets.Count} texturas de aparência, {resource.FileCount} arquivos. " +
                (_project.Tattoos.Any(t => string.IsNullOrEmpty(t.Zone)) ? "Algumas tatuagens precisam de zona em Options > Add tattoo / Edit tattoos. " : "") +
                "Copiar recurso preserva os arquivos originais.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Não foi possível abrir o recurso", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void CopyResource_Click(object sender, RoutedEventArgs e)
    {
        if (_resource is null)
        {
            Status("Abra um recurso FiveM antes de copiar.");
            return;
        }
        var dialog = new OpenFolderDialog { Title = "Selecione a pasta onde criar a cópia" };
        if (dialog.ShowDialog() != true) return;
        var output = Path.Combine(dialog.FolderName, Path.GetFileName(_resource.Root));
        try
        {
            Status("Copiando e verificando recurso...");
            var count = await Task.Run(() => _resource.Export(output));
            Status($"Recurso copiado e verificado: {count} arquivos em {output}");
            MessageBox.Show("Cópia integral concluída em:\n" + output +
                "\n\nA cópia contém os metadados originais. Edições feitas na lista de peças não alteram esta cópia.",
                "Recurso copiado", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Status("Falha ao copiar recurso.");
            MessageBox.Show(ex.Message, "Cópia interrompida", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ImportFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Selecione a pasta com roupas GTA" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var (files, imported) = RunWithLoading("A importar pasta", "A procurar ficheiros de roupa...",
                progress =>
                {
                    var files = Directory.EnumerateFiles(dialog.FolderName, "*", SearchOption.AllDirectories)
                        .Where(path => new[] { ".ydd", ".ytd", ".yld" }.Contains(Path.GetExtension(path),
                            StringComparer.OrdinalIgnoreCase)).ToArray();
                    progress.Report((1, 2, $"A classificar {files.Length} ficheiros..."));
                    return (files, ClothCatalog.Import(files));
                });
            ImportFiles(files, imported);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Não foi possível importar a pasta",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Clothing_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = ClothingDrop.Accepts(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        if (e.Effects == DragDropEffects.Copy && e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] names)
        {
            var model = names.FirstOrDefault(path => Path.GetExtension(path).Equals(".ydd", StringComparison.OrdinalIgnoreCase));
            if (model is not null) Status("Adicionar em: " + ClothCatalog.ResolveGender(model) + " / " + ClothCatalog.ResolveType(model).Category);
            else if (ItemList.SelectedItem is ClothItem target) Status("Adicionar textura em: " + target.DisplayName);
        }
        e.Handled = true;
    }
    private void Clothing_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        try
        {
            Status("Receiving clothing files...");
            Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
            var files = ClothingDrop.Materialize(e.Data);
            if (files.Length == 0) { Status("Drop YDD models and YTD textures from OpenIV or Explorer."); return; }
            if (ItemsControl.ContainerFromElement(ItemList, e.OriginalSource as DependencyObject) is ListBoxItem row && row.DataContext is ClothItem target) ItemList.SelectedItem = target;
            ImportFiles(files); e.Effects = DragDropEffects.Copy;
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not import dropped files", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
    private void ImportFiles(IEnumerable<string> paths, List<ClothItem>? prepared = null)
    {
        var selected = paths.ToArray();
        if (!selected.Any(path => Path.GetExtension(path).Equals(".ydd", StringComparison.OrdinalIgnoreCase)))
        {
            var textureFiles = selected.Where(path => Path.GetExtension(path)
                .Equals(".ytd", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (textureFiles.Length > 0)
            {
                try
                {
                    var groups = new Dictionary<ClothItem, List<string>>();
                    foreach (var texture in textureFiles)
                    {
                        var type = ClothCatalog.ResolveType(texture); var gender = ClothCatalog.ResolveGender(texture);
                        var matches = _project.Items.Where(item => !item.IsDummy && item.Category == type.Category && item.SourceIndex == type.Index && (gender == "Indefinido" || item.Gender == gender) && ClothCatalog.SameCollection(item.OriginalModelFileName ?? item.ModelPath, texture)).ToArray();
                        var current = ItemList.SelectedItem as ClothItem;
                        var target = current is { IsDummy: false } ? current : matches.Length == 1 ? matches[0] : null;
                        if (target is null) throw new InvalidDataException("Selecione a peça correspondente antes de adicionar: " + Path.GetFileName(texture));
                        if (!groups.TryGetValue(target, out var batch)) groups[target] = batch = new(); batch.Add(texture);
                    }
                    var reviewed = ReviewTextureGroups(groups);
                    foreach (var group in reviewed) { ItemList.SelectedItem = group.Item; ApplyImportedTextures(group.Item, group.Paths); }
                }
                catch (Exception ex) { MessageBox.Show(ex.Message, "Falha ao importar texturas",
                    MessageBoxButton.OK, MessageBoxImage.Error); }
                return;
            }
            Status("Selecione arquivos YDD para criar peças ou uma peça para adicionar YTD.");
            return;
        }
        try
        {
            Status($"Importing {selected.Length} clothing file(s)...");
            Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
            var imported = prepared ?? RunWithLoading("A importar ficheiros", "A localizar modelos e texturas...",
                progress =>
                {
                    var files = ClothCatalog.WithSiblingCompanions(selected);
                    progress.Report((1, 2, $"A classificar {files.Length} ficheiros..."));
                    return ClothCatalog.Import(files);
                });
            var before = _project.Items.Select(item => item.Id).ToHashSet();
            // Models are read lazily for the selected drawable; importing a folder does not decode every YDD.
            var changes = CatalogMerger.Merge(_project, imported, AbsoluteAssetPath, CheckYtd, ReviewTextureImports);
            if (changes != default) _dirty = true;
            RefreshItems();
            var added = _project.Items.FirstOrDefault(item => !before.Contains(item.Id));
            if (added is not null)
            {
                if (!ItemList.Items.Contains(added)) { SearchBox.Text = ""; CategoryList.SelectedIndex = 0; RefreshItems(); }
                ExpandCategory(added.Category);
                ItemList.SelectedItem = added; ItemList.ScrollIntoView(added);
            }
            if (ItemList.SelectedItem is ClothItem currentItem) ShowDetails(currentItem);
            Status(changes.AddedPieces == 1 && added is not null
                ? $"Added {(added.Gender == "Feminino" ? "Female" : added.Gender == "Masculino" ? "Male" : "Undefined")} {added.ExportPosition ?? added.SourceIndex ?? 0} {ClothLabelConverter.Label(added.Category)} with {added.TexturePaths.Count} found textures from {added.OriginalModelFileName ?? Path.GetFileName(added.ModelPath)}."
                : $"{changes.AddedPieces} drawable(s), {changes.AddedTextures} YTD, {changes.AddedModels} alternate YDD and {changes.AddedPhysics} YLD added.");
        }
        catch (Exception ex)
        {
            Status("Falha ao importar arquivos da peça.");
            MessageBox.Show(ex.Message, "Importação interrompida", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void Filter_Changed(object sender, RoutedEventArgs e) => RefreshItems();

    private void ItemList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ShowDetails(ItemList.SelectedItem as ClothItem);

    private void ShowDetails(ClothItem? item)
    {
        if (ItemNameBox is null || GenderBox is null || TypeBox is null) return;
        _previewRequestId++;
        var modelRequestId = ++_modelRequestId;
        _materialRequestId++;
        _currentModelData = null;
        _displayModelData = null;
        _updatingDetails = true;
        var identity = item is null ? null : CollectionIndices.Current(_project).FirstOrDefault(entry => entry.ItemId == item.Id);
        ClothingIdentitySummary.Text = identity is null ? "" :
            $"Original model namespace: {Path.GetFileNameWithoutExtension(item!.OriginalModelFileName ?? item.ModelPath).Split('^')[0]} / source index {item.SourceIndex}\n" +
            $"If rebuilding as a new addon: {identity.Collection} / slot {identity.Slot} / index {identity.LocalIndex}. Original-resource mode keeps the source addresses.";
        EditorPanel.IsEnabled = item is not null;
        EditorScroll.Visibility = item is null ? Visibility.Collapsed : Visibility.Visible;
        EmptyEditorText.Visibility = item is null ? Visibility.Visible : Visibility.Collapsed;
        ItemNameBox.Text = item?.Name ?? "";
        PositionBox.Text = (item?.ExportPosition ?? item?.SourceIndex ?? 0).ToString();
        AudioBox.ItemsSource = new[] { "none" }.Concat(_project.Items.Select(x => x.AudioPreset)).Distinct().OrderBy(x => x).ToArray();
        AudioBox.Text = item?.AudioPreset ?? "none";
        BehaviourBox.ItemsSource = new[] { "Default", "0" }.Concat(_project.Items.Where(x => x.BehaviourFlags.HasValue).Select(x => x.BehaviourFlags!.Value.ToString())).Distinct().ToArray();
        TimeCycleBox.ItemsSource = new[] { "None", "glasses_black" }.Concat(_project.Items.SelectMany(x => x.TextureEffects).Select(x => x.TimeCycleModifier).Where(x => !string.IsNullOrWhiteSpace(x))).Distinct().ToArray();
        BehaviourBox.Text = item?.BehaviourFlags?.ToString() ?? "Default";
        MaskModelBox.Text = string.IsNullOrWhiteSpace(item?.FirstPersonMaskModel) ? "None" : item.FirstPersonMaskModel;
        MaskXBox.Text = (item?.MaskOffsetX ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture);
        MaskYBox.Text = (item?.MaskOffsetY ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture);
        MaskZBox.Text = (item?.MaskOffsetZ ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (ListBoxItem option in HairOptionBox.Items) if ((string)option.Content == (item?.HairOption ?? "Show hairs")) HairOptionBox.SelectedItem = option;
        HairCutBox.Text = (item?.HairCutValue ?? -0.5f).ToString(System.Globalization.CultureInfo.InvariantCulture);
        HeelBox.Text = (item?.HeelHeight ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture);
        HoodieBox.IsChecked = item?.HoodieHairs == true;
        var optionCode = item?.Category.Split('[').Last().TrimEnd(']') ?? "";
        AlternateModelHeading.Text = optionCode is "teef" or "accs" or "task" or "jbib" ? "First person model" : "Alternate models";
        HairPanel.Visibility = optionCode is "p_head" or "berd" or "accs" or "jbib" ? Visibility.Visible : Visibility.Collapsed;
        HairControlsPanel.Visibility = optionCode is "p_head" or "berd" ? Visibility.Visible : Visibility.Collapsed;
        CustomHairOption.Visibility = optionCode == "p_head" && item?.HairOption == "Custom cut hairs" ? Visibility.Visible : Visibility.Collapsed;
        CustomHairPanel.Visibility = optionCode == "p_head" && item?.HairOption == "Custom cut hairs" ? Visibility.Visible : Visibility.Collapsed;
        HoodieBox.Visibility = optionCode is "accs" or "jbib" ? Visibility.Visible : Visibility.Collapsed;
        HairCutBox.IsEnabled = optionCode == "p_head";
        HeelPanel.Visibility = optionCode == "feet" ? Visibility.Visible : Visibility.Collapsed;
        FirstPersonOptionsPanel.Visibility = optionCode is "p_head" or "p_eyes" or "berd" or "accs" ? Visibility.Visible : Visibility.Collapsed;
        ShopCostBox.Text = (item?.ShopCost ?? 0).ToString();
        ShopLabelBox.Text = item?.ShopTextLabel ?? "";
        ShopLockBox.Text = item?.ShopLockHash ?? "";
        CreatorBox.Text = item?.Creator ?? "";
        CreatorWebsiteBox.Text = item?.CreatorWebsite ?? "";
        CreatorDescriptionBox.Text = item?.CreatorDescription ?? "";
        TypeBox.ItemsSource = ClothLabelConverter.TypeChoices(item?.Category.Contains("[p_", StringComparison.Ordinal) == true).ToArray();
        TypeBox.SelectedItem = item?.Category;
        ClothKindBox.SelectedIndex = item?.Category.Contains("[p_", StringComparison.Ordinal) == true ? 1 : 0;
        foreach (ComboBoxItem option in GenderBox.Items)
            if ((string)option.Tag == (item?.Gender ?? "Indefinido"))
                GenderBox.SelectedItem = option;
        ModelText.Text = item is null ? "" :
            item.OriginalModelFileName ?? Path.GetFileName(item.ModelPath);
        ModelText.ToolTip = item?.ModelPath;
        ModelInfoText.Text = item is null ? "" : "Lendo estrutura YDD...";
        RefreshAlternateModelList(item);
        _modelYaw = null;
        _modelPitch = null;
        _modelDragStart = null;
        ModelViewport.ReleaseMouseCapture();
        if (ModelViewport.Camera is PerspectiveCamera camera)
            camera.Position = new Point3D(0, 0, 3.3);
        ModelPreviewVisual.Content = null;
        ModelPreviewText.Text = item is null ? "" : "Preparando prévia 3D...";
        UpdateClothPreviewWindow();
        TextureReferencesList.ItemsSource = null;
        RefreshTextureList(item);
        _selectedTexturePath = null;
        TextureEntryBox.ItemsSource = null;
        TexturePreview.Source = null;
        TexturePreviewText.Text = item is null ? "" : "Selecione uma textura.";
        PhysicsText.Text = item?.PhysicsPath is null ? "Select a file" : Path.GetFileName(item.PhysicsPath);
        _updatingDetails = false;
        if (item?.TexturePaths.Count > 0) { TextureList.SelectedIndex = -1; TextureList.SelectedIndex = 0; }
        if (item is not null && !item.IsDummy)
        {
            if (_clothPreviewWindow is not null) _ = LoadBasePedAsync(item);
            _ = LoadModelDetailsAsync(item, modelRequestId);
            _ = LoadModelPreviewAsync(item, modelRequestId);
            if (item.PhysicsPath is not null) _ = LoadPhysicsDetailsAsync(item, modelRequestId);
        }
    }

    private async Task LoadPhysicsDetailsAsync(ClothItem item, int requestId)
    {
        try
        {
            var path = AbsoluteAssetPath(item.PhysicsPath!);
            var count = await Task.Run(() => YldInspector.CountClothes(path));
            if (requestId == _modelRequestId && item.PhysicsPath is not null &&
                string.Equals(AbsoluteAssetPath(item.PhysicsPath), path,
                    StringComparison.OrdinalIgnoreCase))
                PhysicsText.Text = Path.GetFileName(path) + "  •  " + count + " entrada(s) de tecido";
        }
        catch (Exception ex)
        {
            if (requestId == _modelRequestId && item.PhysicsPath is not null)
                PhysicsText.Text = Path.GetFileName(item.PhysicsPath) + "  •  YLD inválido: " + ex.Message;
        }
    }

    private async Task LoadModelDetailsAsync(ClothItem item, int requestId)
    {
        try
        {
            var path = AbsoluteAssetPath(item.ModelPath);
            var result = await Task.Run(() => ReadModelDetails(path));
            if (requestId == _modelRequestId) ModelInfoText.Text = result;
        }
        catch (Exception ex)
        {
            if (requestId == _modelRequestId)
                ModelInfoText.Text = "YDD indisponível: " + ex.Message;
        }
    }

    private static string ReadModelDetails(string path)
    {
        var ydd = new YddFile();
        ydd.Load(File.ReadAllBytes(path));
        var drawables = ydd.Drawables?.Where(x => x is not null).ToArray() ?? [];
        var models = drawables.SelectMany(x => x.AllModels ?? []).Where(x => x is not null).ToArray();
        var geometries = models.Sum(x => x.Geometries?.Count(y => y is not null) ?? 0);
        return $"{drawables.Length} drawable(s)  •  {models.Length} modelo(s)  •  {geometries} geometria(s)";
    }

    private async Task LoadModelPreviewAsync(ClothItem item, int requestId)
    {
        try
        {
            var path = AbsoluteAssetPath(item.ModelPath);
            var data = await Task.Run(() => ModelPreview.Read(path, false));
            if (requestId != _modelRequestId) return;
            _currentModelData = data;
            ShowModelPreview(data);
            RefreshTextureReferences();
            if (_selectedTexturePath is not null)
                _ = LoadModelMaterialsAsync(_selectedTexturePath, requestId);
        }
        catch (Exception ex)
        {
            if (requestId == _modelRequestId)
                ModelPreviewText.Text = "Prévia 3D indisponível: " + ex.Message;
            UpdateClothPreviewWindow();
        }
    }

    private async Task LoadModelMaterialsAsync(string ytdPath, int modelRequestId)
    {
        var requestId = ++_materialRequestId;
        var data = _currentModelData;
        if (data is null) return;
        try
        {
            var textures = await Task.Run(() => ReadMatchingTextures(ytdPath, data));
            if (requestId != _materialRequestId || modelRequestId != _modelRequestId) return;
            ShowModelPreview(data, textures);
            RefreshTextureReferences();
        }
        catch (Exception ex)
        {
            if (requestId == _materialRequestId && modelRequestId == _modelRequestId)
                ModelPreviewText.Text += " Textura 3D indisponível: " + ex.Message;
        }
    }

    internal static Dictionary<string, BitmapSource> ReadMatchingTextures(string path, ModelPreviewData data)
    {
        var result = new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);
        var ytd = new YtdFile();
        ytd.Load(File.ReadAllBytes(path));
        var entries = (ytd.TextureDict?.Textures?.data_items ?? []).Where(t => t?.Name is not null).ToArray();
        foreach (var texture in entries)
        {
            try { result[texture.Name] = DecodeTextureBitmap(texture, 1024); }
            catch (InvalidDataException) { /* O modelo continua visível sem este formato. */ }
        }
        var availableImages = result.Keys.ToArray();
        foreach (var part in data.Parts)
        {
            var key = part.TextureName ?? "__selected_diffuse";
            var bound = ytd.TextureDict?.Lookup(JenkHash.GenHash(key.ToLowerInvariant()));
            if (bound is not null)
            {
                try
                {
                    result[key] = bound.Name is not null && result.TryGetValue(bound.Name, out var boundImage)
                        ? boundImage : DecodeTextureBitmap(bound, 1024);
                }
                catch (InvalidDataException) { }
                continue;
            }
            if (result.ContainsKey(key)) continue;
            var selected = TextureReferenceCoverage.Resolve(part.TextureName, availableImages);
            if (selected is not null && result.TryGetValue(selected, out var image)) result[key] = image;
        }
        return result;
    }

    private void ShowModelPreview(ModelPreviewData data, Dictionary<string, BitmapSource>? textures = null)
    {
        if (_showBasePed && ItemList.SelectedItem is ClothItem selected && _pedGender == selected.Gender)
        {
            var slot = selected.Category.Split('[').Last().TrimEnd(']');
            if (ModelPreview.PropBoneTag(slot) is int boneTag)
            {
                var key = (selected.Gender, boneTag);
                if (!_boneAttachments.TryGetValue(key, out var attachment))
                {
                    var head = GamePedAssets.Load(GamePedAssets.FindGameFolder()!, selected.Gender).First(a => a.Slot == "head");
                    attachment = ModelPreview.BoneAttachment(head.ModelPath, boneTag);
                    _boneAttachments[key] = attachment;
                }
                data = ModelPreview.AttachToBone(data, attachment);
            }
            var body = _pedParts.Where(p => !p.TextureName!.StartsWith("__ped_" + slot + "_", StringComparison.Ordinal)).ToArray();
            var bodyTriangles = body.Sum(p => p.Indices.Length / 3);
            data = data with { Parts = data.Parts.Concat(body).ToArray(), SourceTriangles = data.SourceTriangles + bodyTriangles, PreviewTriangles = data.PreviewTriangles + bodyTriangles };
            var combined = textures is null ? new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase) : new Dictionary<string, BitmapSource>(textures, StringComparer.OrdinalIgnoreCase);
            foreach (var pair in _pedTextures) combined[pair.Key] = pair.Value;
            textures = combined;
        }
        _displayModelData = data;
        var models = new Model3DGroup();
        var texturedParts = 0;
        foreach (var part in data.Parts)
        {
            var normals = new Vector3D[part.Positions.Length];
            for (var i = 0; i + 2 < part.Indices.Length; i += 3)
            {
                var a = part.Indices[i];
                var b = part.Indices[i + 1];
                var c = part.Indices[i + 2];
                var normal = Vector3D.CrossProduct(part.Positions[b] - part.Positions[a],
                    part.Positions[c] - part.Positions[a]);
                normals[a] += normal;
                normals[b] += normal;
                normals[c] += normal;
            }
            for (var i = 0; i < normals.Length; i++)
                if (normals[i].LengthSquared > 0) normals[i].Normalize();
            var mesh = new MeshGeometry3D
            {
                Positions = new Point3DCollection(part.Positions),
                TriangleIndices = new Int32Collection(part.Indices),
                Normals = new Vector3DCollection(normals)
            };
            Material material;
            if (textures?.TryGetValue(part.TextureName ?? "__selected_diffuse", out var bitmap) == true &&
                part.TextureCoordinates.Length == part.Positions.Length)
            {
                mesh.TextureCoordinates = new PointCollection(part.TextureCoordinates);
                material = new DiffuseMaterial(new ImageBrush(bitmap)
                {
                    TileMode = TileMode.Tile,
                    ViewportUnits = BrushMappingMode.Absolute,
                    Viewport = new Rect(0, 0, 1, 1),
                    Stretch = Stretch.Fill
                });
                texturedParts++;
            }
            else
                material = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(194, 202, 215)));
            mesh.Freeze();
            models.Children.Add(new GeometryModel3D(mesh, material) { BackMaterial = material });
        }
        _modelYaw = new AxisAngleRotation3D(new Vector3D(0, 1, 0), _modelYaw?.Angle ?? 25);
        _modelPitch = new AxisAngleRotation3D(new Vector3D(1, 0, 0), _modelPitch?.Angle ?? 0);
        var transforms = new Transform3DGroup();
        transforms.Children.Add(new RotateTransform3D(_modelPitch));
        transforms.Children.Add(new RotateTransform3D(_modelYaw));
        models.Transform = transforms;
        var scene = new Model3DGroup();
        scene.Children.Add(new AmbientLight(Color.FromRgb(95, 95, 95)));
        scene.Children.Add(new DirectionalLight(Colors.White, new Vector3D(-0.4, -0.7, -1)));
        scene.Children.Add(models);
        ModelPreviewVisual.Content = scene;
        ModelPreviewText.Text = data.PreviewTriangles + " triângulos na prévia" +
            (data.PreviewTriangles < data.SourceTriangles ? " (limite de 25.000)" : "") +
            (textures is null ? "" : $". {texturedParts}/{data.Parts.Length} partes com textura") +
            ". Arraste para girar; use a roda para zoom.";
        UpdateClothPreviewWindow();
    }

    private void LockModelPed_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_modelPitch is not null) _modelPitch.Angle = 0;
    }

    private void ModelViewport_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_modelYaw is null || _modelPitch is null || e.ChangedButton != MouseButton.Left) return;
        _modelDragStart = e.GetPosition(ModelViewport);
        ModelViewport.CaptureMouse();
    }

    private void ModelViewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (_modelDragStart is null || _modelYaw is null || _modelPitch is null) return;
        var current = e.GetPosition(ModelViewport);
        _modelYaw.Angle += current.X - _modelDragStart.Value.X;
        if (UnlockModelPed.IsChecked == true) _modelPitch.Angle = Math.Clamp(_modelPitch.Angle + current.Y - _modelDragStart.Value.Y, -85, 85);
        _modelDragStart = current;
    }

    private void ModelViewport_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _modelDragStart = null;
        ModelViewport.ReleaseMouseCapture();
    }

    private void ModelViewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (ModelViewport.Camera is not PerspectiveCamera camera) return;
        var distance = Math.Clamp(camera.Position.Z * (e.Delta > 0 ? 0.88 : 1.12), 1.2, 8);
        camera.Position = new Point3D(0, 0, distance);
        e.Handled = true;
    }

    private async void TextureList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingDetails || ItemList?.SelectedItem is not ClothItem item ||
            TextureList.SelectedIndex < 0 || TextureList.SelectedIndex >= item.TexturePaths.Count) return;
        var requestId = ++_previewRequestId;
        _materialRequestId++;
        _selectedTexturePath = null;
        TextureEntryBox.ItemsSource = null;
        RefreshTextureReferences();
        TexturePreview.Source = null;
        TexturePreviewText.Text = "Lendo texturas do YTD...";
        if (_currentModelData is not null) ShowModelPreview(_currentModelData);
        try
        {
            if (string.IsNullOrWhiteSpace(item.TexturePaths[TextureList.SelectedIndex]))
            { TexturePreviewText.Text = "Reserved texture slot (dummy)."; return; }
            AlignTextureNames(item);
            var effect = item.TextureEffects[TextureList.SelectedIndex];
            TimeCycleBox.Text = string.IsNullOrWhiteSpace(effect.TimeCycleModifier) ? "None" : effect.TimeCycleModifier;
            TimeCycleIntensityBox.Text = effect.Intensity.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var path = AbsoluteAssetPath(item.TexturePaths[TextureList.SelectedIndex]);
            var names = await Task.Run(() => ReadTextureNames(path));
            if (requestId != _previewRequestId) return;
            _selectedTexturePath = path;
            TextureEntryBox.ItemsSource = names;
            TextureEntryBox.SelectedIndex = 0;
            RefreshTextureReferences();
            _ = LoadModelMaterialsAsync(path, _modelRequestId);
        }
        catch (Exception ex)
        {
            if (requestId != _previewRequestId) return;
            TexturePreviewText.Text = "Prévia indisponível: " + ex.Message;
        }
    }

    private async void TextureEntryBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_selectedTexturePath is null || TextureEntryBox.SelectedIndex < 0) return;
        var path = _selectedTexturePath;
        var index = TextureEntryBox.SelectedIndex;
        var requestId = ++_previewRequestId;
        TexturePreview.Source = null;
        TexturePreviewText.Text = "Carregando textura...";
        try
        {
            var preview = await Task.Run(() => DecodeTexturePreview(path, index));
            if (requestId != _previewRequestId) return;
            TexturePreview.Source = preview.Image;
            TexturePreviewText.Text = $"{preview.Name}  •  {preview.Width}×{preview.Height}";
        }
        catch (Exception ex)
        {
            if (requestId != _previewRequestId) return;
            TexturePreviewText.Text = "Prévia indisponível: " + ex.Message;
        }
    }

    private async void SaveTexturePng_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTexturePath is null || TextureEntryBox.SelectedIndex < 0)
        {
            Status("Selecione uma textura YTD para salvar.");
            return;
        }
        var path = _selectedTexturePath;
        var index = TextureEntryBox.SelectedIndex;
        var suggestedName = (TextureEntryBox.SelectedItem as string ?? "textura") + ".png";
        var dialog = new SaveFileDialog
        {
            Filter = "Imagem PNG (*.png)|*.png",
            FileName = suggestedName,
            Title = "Salvar textura em resolução original"
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            Status("Decodificando textura em resolução original...");
            var preview = await Task.Run(() => DecodeTexturePreview(path, index, 0));
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(preview.Image));
            using var output = File.Create(dialog.FileName);
            encoder.Save(output);
            Status("Textura salva em " + dialog.FileName);
        }
        catch (Exception ex)
        {
            Status("Falha ao salvar textura.");
            MessageBox.Show(ex.Message, "Exportação PNG interrompida", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void ReplaceInnerTexture_Click(object sender, RoutedEventArgs e)
    {
        if (ItemList.SelectedItem is not ClothItem item || TextureList.SelectedIndex < 0 ||
            TextureEntryBox.SelectedIndex < 0 || _selectedTexturePath is null)
        {
            Status("Selecione uma textura dentro de um YTD.");
            return;
        }
        var variant = TextureList.SelectedIndex;
        var entry = TextureEntryBox.SelectedIndex;
        var input = _selectedTexturePath;
        var chooseTexture = new OpenFileDialog
        {
            Filter = "Textura PNG ou DDS (*.png;*.dds)|*.png;*.dds|PNG (*.png)|*.png|DDS (*.dds)|*.dds",
            Title = "Selecione o PNG ou DDS para a textura interna"
        };
        if (chooseTexture.ShowDialog() != true) return;
        var name = Path.GetFileNameWithoutExtension(
            item.OriginalTextureFileNames.ElementAtOrDefault(variant) ?? input);
        var chooseOutput = new SaveFileDialog
        {
            Filter = "Dicionário de texturas GTA (*.ytd)|*.ytd",
            FileName = name + "_editado.ytd",
            Title = "Salvar novo YTD sem alterar o original"
        };
        if (chooseOutput.ShowDialog() != true) return;
        try
        {
            IsEnabled = false;
            Status("Substituindo textura e verificando o novo YTD...");
            var textureName = await Task.Run(() =>
                Path.GetExtension(chooseTexture.FileName).Equals(".png", StringComparison.OrdinalIgnoreCase)
                    ? YtdTextureEditor.ReplaceFromPng(input, entry, chooseTexture.FileName, chooseOutput.FileName)
                    : YtdTextureEditor.ReplaceFromDds(input, entry, chooseTexture.FileName, chooseOutput.FileName));
            item.TexturePaths[variant] = chooseOutput.FileName;
            _dirty = true;
            RefreshTextureList(item, variant);
            RefreshItems();
            Status("Textura " + textureName + " substituída; projeto ainda não salvo.");
        }
        catch (Exception ex)
        {
            Status("Falha ao substituir textura interna.");
            MessageBox.Show(ex.Message, "Edição YTD interrompida", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally { IsEnabled = true; }
    }

    private async void SaveTextureDds_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTexturePath is null || TextureEntryBox.SelectedIndex < 0)
        {
            Status("Selecione uma textura YTD para salvar.");
            return;
        }
        var input = _selectedTexturePath;
        var index = TextureEntryBox.SelectedIndex;
        var dialog = new SaveFileDialog
        {
            Filter = "Textura DDS (*.dds)|*.dds",
            FileName = (TextureEntryBox.SelectedItem as string ?? "textura") + ".dds",
            Title = "Salvar a textura com todos os mipmaps como DDS"
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            IsEnabled = false;
            await Task.Run(() => YtdTextureEditor.ExportDds(input, index, dialog.FileName));
            Status("DDS salvo em " + dialog.FileName);
        }
        catch (Exception ex)
        {
            Status("Falha ao salvar DDS.");
            MessageBox.Show(ex.Message, "Exportação DDS interrompida", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally { IsEnabled = true; }
    }

    private async void AddInnerTexture_Click(object sender, RoutedEventArgs e)
    {
        if (ItemList.SelectedItem is not ClothItem item || TextureList.SelectedIndex < 0)
        {
            Status("Selecione um YTD da peça para adicionar uma textura interna.");
            return;
        }
        var variant = TextureList.SelectedIndex;
        var input = AbsoluteAssetPath(item.TexturePaths[variant]);
        var chooseTexture = new OpenFileDialog
        {
            Filter = "Textura PNG ou DDS (*.png;*.dds)|*.png;*.dds|PNG (*.png)|*.png|DDS (*.dds)|*.dds",
            Title = "Selecione o PNG ou DDS da nova textura interna"
        };
        if (chooseTexture.ShowDialog() != true) return;
        var nameDialog = new TextureNameDialog(Path.GetFileNameWithoutExtension(chooseTexture.FileName))
        { Owner = this };
        if (nameDialog.ShowDialog() != true) return;
        var output = ChooseEditedYtdPath(item, variant);
        if (output is null) return;
        try
        {
            IsEnabled = false;
            Status("Adicionando textura e verificando o novo YTD...");
            var name = await Task.Run(() =>
                Path.GetExtension(chooseTexture.FileName).Equals(".png", StringComparison.OrdinalIgnoreCase)
                    ? YtdTextureEditor.AddFromPng(input, chooseTexture.FileName, nameDialog.TextureName, output)
                    : YtdTextureEditor.AddFromDds(input, chooseTexture.FileName, nameDialog.TextureName, output));
            item.TexturePaths[variant] = output;
            _dirty = true;
            RefreshTextureList(item, variant);
            RefreshItems();
            Status("Textura " + name + " adicionada; projeto ainda não salvo.");
        }
        catch (Exception ex)
        {
            Status("Falha ao adicionar textura interna.");
            MessageBox.Show(ex.Message, "Edição YTD interrompida", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally { IsEnabled = true; }
    }

    private async void RemoveInnerTexture_Click(object sender, RoutedEventArgs e)
    {
        if (ItemList.SelectedItem is not ClothItem item || TextureList.SelectedIndex < 0 ||
            TextureEntryBox.SelectedIndex < 0 || _selectedTexturePath is null)
        {
            Status("Selecione uma textura interna para remover.");
            return;
        }
        var variant = TextureList.SelectedIndex;
        var entry = TextureEntryBox.SelectedIndex;
        var input = _selectedTexturePath;
        var output = ChooseEditedYtdPath(item, variant);
        if (output is null) return;
        try
        {
            IsEnabled = false;
            Status("Removendo textura e verificando o novo YTD...");
            var name = await Task.Run(() => YtdTextureEditor.Remove(input, entry, output));
            item.TexturePaths[variant] = output;
            _dirty = true;
            RefreshTextureList(item, variant);
            RefreshItems();
            Status("Textura " + name + " removida; projeto ainda não salvo.");
        }
        catch (Exception ex)
        {
            Status("Falha ao remover textura interna.");
            MessageBox.Show(ex.Message, "Edição YTD interrompida", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally { IsEnabled = true; }
    }

    private async void RenameInnerTexture_Click(object sender, RoutedEventArgs e)
    {
        if (ItemList.SelectedItem is not ClothItem item || TextureList.SelectedIndex < 0 ||
            TextureEntryBox.SelectedIndex < 0 || _selectedTexturePath is null)
        {
            Status("Selecione uma textura interna para renomear.");
            return;
        }
        var variant = TextureList.SelectedIndex;
        var entry = TextureEntryBox.SelectedIndex;
        var input = _selectedTexturePath;
        var nameDialog = new TextureNameDialog(TextureEntryBox.SelectedItem as string ?? "", true)
        { Owner = this };
        if (nameDialog.ShowDialog() != true) return;
        var output = ChooseEditedYtdPath(item, variant);
        if (output is null) return;
        try
        {
            IsEnabled = false;
            Status("Renomeando textura e verificando o novo YTD...");
            var name = await Task.Run(() => YtdTextureEditor.Rename(input, entry,
                nameDialog.TextureName, output));
            item.TexturePaths[variant] = output;
            _dirty = true;
            RefreshTextureList(item, variant);
            RefreshItems();
            Status("Textura renomeada para " + name + "; projeto ainda não salvo.");
        }
        catch (Exception ex)
        {
            Status("Falha ao renomear textura interna.");
            MessageBox.Show(ex.Message, "Edição YTD interrompida", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally { IsEnabled = true; }
    }

    private string? ChooseEditedYtdPath(ClothItem item, int variant)
    {
        var name = Path.GetFileNameWithoutExtension(
            item.OriginalTextureFileNames.ElementAtOrDefault(variant) ?? item.TexturePaths[variant]);
        var dialog = new SaveFileDialog
        {
            Filter = "Dicionário de texturas GTA (*.ytd)|*.ytd",
            FileName = name + "_editado.ytd",
            Title = "Salvar novo YTD sem alterar o original"
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private void RefreshTextureReferences()
    {
        if (_currentModelData is null)
        {
            TextureReferencesList.ItemsSource = null;
            return;
        }
        var requested = _currentModelData.Parts.Select(part => part.TextureName)
            .Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        TextureReferencesList.ItemsSource = requested.Length == 0
            ? ["Nenhuma referência difusa com UV na prévia."]
            : _selectedTexturePath is null
                ? requested.Select(name => "YDD: " + name).ToArray()
                : TextureReferenceCoverage.Describe(requested,
                    TextureEntryBox.ItemsSource as IEnumerable<string> ?? []);
    }

    private static string[] ReadTextureNames(string path)
    {
        var ytd = new YtdFile();
        ytd.Load(File.ReadAllBytes(path));
        var textures = ytd.TextureDict?.Textures?.data_items;
        if (textures is null || textures.Length == 0)
            throw new InvalidDataException("YTD sem texturas.");
        return textures.Select((texture, index) => texture?.Name ?? "textura " + index).ToArray();
    }

    private static (BitmapSource Image, string Name, int Width, int Height) DecodeTexturePreview(
        string path, int index, int maxDimension = 512)
    {
        var ytd = new YtdFile();
        ytd.Load(File.ReadAllBytes(path));
        var texture = ytd.TextureDict?.Textures?.data_items?.ElementAtOrDefault(index)
            ?? throw new InvalidDataException("YTD sem texturas.");
        return (DecodeTextureBitmap(texture, maxDimension), texture.Name ?? "textura",
            texture.Width, texture.Height);
    }

    internal static BitmapSource DecodeTextureBitmap(Texture texture, int maxDimension)
    {
        var mip = 0;
        while (maxDimension > 0 && Math.Max(texture.Width >> mip, texture.Height >> mip) > maxDimension &&
               mip + 1 < texture.Levels)
            mip++;
        var width = Math.Max(1, texture.Width >> mip);
        var height = Math.Max(1, texture.Height >> mip);
        var pixels = texture.Format == TextureFormat.D3DFMT_BC7
            ? TexturePreviewDecoder.DecodeBc7(texture, mip)
            : DDSIO.GetPixels(texture, mip);
        if (pixels is null || pixels.Length < width * height * 4)
            throw new InvalidDataException("Formato de textura ainda não suportado para prévia.");
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32,
            null, pixels, width * 4);
        image.Freeze();
        return image;
    }

    private void ItemNameBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_updatingDetails || ItemList.SelectedItem is not ClothItem item) return;
        var name = ItemNameBox.Text.Trim();
        if (name.Length == 0) { ItemNameBox.Text = item.Name; return; }
        if (name == item.Name) return;
        item.Name = name;
        _dirty = true;
        RefreshItems();
    }

    private void Details_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingDetails || ItemList?.SelectedItem is not ClothItem item) return;
        var gender = (GenderBox.SelectedItem as ComboBoxItem)?.Tag as string;
        var category = TypeBox.SelectedItem as string;
        if (gender is null || category is null) return;
        if (item.Gender == gender && item.Category == category) return;
        if (CollectionIndices.IsPublished(_project, item))
        { ShowDetails(item); Status("Collection publicada: mudança de gênero/slot bloqueada para preservar índices."); return; }
        item.Gender = gender;
        item.Category = category;
        if (_project.PublishedCollection is not null) CollectionIndices.Append(_project, item);
        _dirty = true;
        RefreshItems();
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (ItemList.SelectedItem is not ClothItem item) return;
        if (CollectionIndices.IsPublished(_project, item))
        {
            if (item.IsDummy) { Status("Reserva publicada: remoção bloqueada para preservar o índice."); return; }
            if (MessageBox.Show(this, "Esta peça tem um índice publicado. Remover deslocaria as roupas salvas no servidor. Substituir por dummy (Replace with Dummy), preservando o índice?",
                "Collection publicada", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                ReplaceSelectedWithDummy();
            return;
        }
        if (_project.ReplaceWithDummyOnRemove && !item.IsDummy)
            ClothEditActions.ReplaceWithDummy(item);
        else _project.Items.Remove(item);
        _dirty = true;
        RefreshItems();
        ShowDetails(null);
        Status("Peça removida do projeto; arquivos de origem preservados.");
    }

    private void RefreshTextureList(ClothItem? item, int selectedIndex = 0)
    {
        TexturesHeading.Text = $"Textures {item?.TexturePaths.Count ?? 0}/26";
        TextureList.ItemsSource = item?.TexturePaths.Select((path, index) =>
            $"{(char)('a' + index)}  {(string.IsNullOrWhiteSpace(item.TextureLabels.ElementAtOrDefault(index)) ? Path.GetFileName(item.OriginalTextureFileNames.ElementAtOrDefault(index) ?? path) : item.TextureLabels[index])}")
            .ToArray();
        if (item?.TexturePaths.Count > 0)
            TextureList.SelectedIndex = Math.Clamp(selectedIndex, 0, item.TexturePaths.Count - 1);
    }

    private static void AlignTextureNames(ClothItem item)
    {
        ClothingIdentityMap.EnsureTextures(item);
        while (item.TextureLabels.Count < item.TexturePaths.Count) item.TextureLabels.Add("");
        if (item.TextureLabels.Count > item.TexturePaths.Count) item.TextureLabels.RemoveRange(item.TexturePaths.Count, item.TextureLabels.Count - item.TexturePaths.Count);
        while (item.TextureEffects.Count < item.TexturePaths.Count) item.TextureEffects.Add(new TextureEffect());
        if (item.TextureEffects.Count > item.TexturePaths.Count) item.TextureEffects.RemoveRange(item.TexturePaths.Count, item.TextureEffects.Count - item.TexturePaths.Count);
        while (item.OriginalTextureFileNames.Count < item.TexturePaths.Count)
            item.OriginalTextureFileNames.Add(Path.GetFileName(
                item.TexturePaths[item.OriginalTextureFileNames.Count]));
        if (item.OriginalTextureFileNames.Count > item.TexturePaths.Count)
            item.OriginalTextureFileNames.RemoveRange(item.TexturePaths.Count,
                item.OriginalTextureFileNames.Count - item.TexturePaths.Count);
    }

    private static void CheckYtd(string path)
    {
        var ytd = new YtdFile();
        ytd.Load(File.ReadAllBytes(path));
        if (ytd.TextureDict?.Textures?.data_items is not { Length: > 0 })
            throw new InvalidDataException("YTD sem texturas: " + path);
    }

    private static void CheckYdd(string path)
    {
        var ydd = new YddFile();
        ydd.Load(File.ReadAllBytes(path));
        if (ydd.Drawables?.Any(drawable => drawable is not null) != true)
            throw new InvalidDataException("YDD sem drawables: " + path);
    }

    private void RefreshAlternateModelList(ClothItem? item, int selectedIndex = 0)
    {
        AlternateModelList.ItemsSource = item?.AlternateModelPaths.Select((path, index) =>
            $"{index + 1}  {Path.GetFileName(path)}").ToArray();
        if (item?.AlternateModelPaths.Count > 0)
            AlternateModelList.SelectedIndex = Math.Clamp(selectedIndex, 0, item.AlternateModelPaths.Count - 1);
    }

    private void ReplaceModel_Click(object sender, RoutedEventArgs e)
    {
        if (ItemList.SelectedItem is not ClothItem item) return;
        var dialog = new OpenFileDialog
        {
            Filter = "Modelo GTA (*.ydd)|*.ydd",
            Title = "Substituir YDD principal da peça"
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            CheckYdd(dialog.FileName);
            item.ModelPath = dialog.FileName;
            item.IsDummy = false;
            item.OriginalModelFileName = Path.GetFileName(dialog.FileName);
            _dirty = true;
            ShowDetails(item);
            RefreshItems();
            Status("YDD principal substituído em " + item.Name + ".");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Falha ao substituir YDD", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AddAlternateModel_Click(object sender, RoutedEventArgs e)
    {
        if (ItemList.SelectedItem is not ClothItem item) return;
        var dialog = new OpenFileDialog
        {
            Filter = "Modelo GTA (*.ydd)|*.ydd",
            Multiselect = true,
            Title = "Adicionar modelos YDD alternativos"
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var existing = item.AlternateModelPaths.Select(AbsoluteAssetPath)
                .Append(AbsoluteAssetPath(item.ModelPath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var incoming = dialog.FileNames.Select(Path.GetFullPath).Where(existing.Add).ToArray();
            if (incoming.Length == 0) { Status("Nenhum YDD alternativo novo."); return; }
            if (item.AlternateModelPaths.Count + incoming.Length > 3)
                throw new InvalidDataException("São aceitos até três YDD alternativos por peça.");
            foreach (var path in incoming) CheckYdd(path);
            item.AlternateModelPaths.AddRange(incoming);
            _dirty = true;
            RefreshAlternateModelList(item, item.AlternateModelPaths.Count - 1);
            RefreshItems();
            Status($"{incoming.Length} YDD alternativo(s) adicionado(s) a {item.Name}.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Falha ao adicionar YDD", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RemoveAlternateModel_Click(object sender, RoutedEventArgs e)
    {
        if (ItemList.SelectedItem is not ClothItem item || AlternateModelList.SelectedIndex < 0) return;
        var index = AlternateModelList.SelectedIndex;
        item.AlternateModelPaths.RemoveAt(index);
        if (index == 0) item.HasFirstPersonModel = false;
        _dirty = true;
        RefreshAlternateModelList(item, index);
        RefreshItems();
        Status("YDD alternativo removido da peça; arquivo de origem preservado.");
    }

    private void MoveAlternateModelUp_Click(object sender, RoutedEventArgs e) => MoveAlternateModel(-1);
    private void MoveAlternateModelDown_Click(object sender, RoutedEventArgs e) => MoveAlternateModel(1);

    private void MoveAlternateModel(int direction)
    {
        if (ItemList.SelectedItem is not ClothItem item || AlternateModelList.SelectedIndex < 0) return;
        var index = AlternateModelList.SelectedIndex;
        var target = index + direction;
        if (target < 0 || target >= item.AlternateModelPaths.Count) return;
        (item.AlternateModelPaths[index], item.AlternateModelPaths[target]) =
            (item.AlternateModelPaths[target], item.AlternateModelPaths[index]);
        _dirty = true;
        RefreshAlternateModelList(item, target);
        RefreshItems();
        Status("Ordem dos YDD alternativos atualizada.");
    }

    private async void SetPhysics_Click(object sender, RoutedEventArgs e)
    {
        if (ItemList.SelectedItem is not ClothItem item) return;
        var dialog = new OpenFileDialog
        {
            Filter = "Física de roupa GTA (*.yld)|*.yld",
            Title = "Associar física YLD à peça"
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            IsEnabled = false;
            var count = await Task.Run(() => YldInspector.CountClothes(dialog.FileName));
            item.PhysicsPath = dialog.FileName;
            PhysicsText.Text = Path.GetFileName(dialog.FileName) + "  •  " + count + " entrada(s) de tecido";
            _dirty = true;
            RefreshItems();
            Status("YLD associado a " + item.Name + ".");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Falha ao associar YLD", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { IsEnabled = true; }
    }

    private void RemovePhysics_Click(object sender, RoutedEventArgs e)
    {
        if (ItemList.SelectedItem is not ClothItem item || item.PhysicsPath is null) return;
        item.PhysicsPath = null;
        PhysicsText.Text = "Select a file";
        _dirty = true;
        RefreshItems();
        Status("YLD removido da peça; arquivo de origem preservado.");
    }

    private void AddTexture_Click(object sender, RoutedEventArgs e)
    {
        if (ItemList.SelectedItem is not ClothItem item)
        {
            Status("Selecione uma peça antes de adicionar texturas.");
            return;
        }
        var dialog = new OpenFileDialog
        {
            Filter = "Dicionário de texturas GTA (*.ytd)|*.ytd",
            Multiselect = true,
            Title = "Adicionar variantes YTD à peça selecionada"
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            AttachTextures(item, dialog.FileNames);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Falha ao adicionar textura", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private (ClothItem Item, string[] Paths)[] ReviewTextureGroups(Dictionary<ClothItem, List<string>> groups)
        => groups.Select(group => (group.Key, ReviewTextureImports(group.Key, group.Value))).ToArray();
    private string[] ReviewTextureImports(ClothItem item, IEnumerable<string> files)
    {
        var incoming = files.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var path in incoming) CheckYtd(path);
        var accepted = new List<string>();
        var content = new Dictionary<string, (string Name, int Slot)>();
        for (var i = 0; i < item.TexturePaths.Count; i++)
            if (!string.IsNullOrWhiteSpace(item.TexturePaths[i])) content.TryAdd(TextureContent.Fingerprint(AbsoluteAssetPath(item.TexturePaths[i])), (Path.GetFileName(item.TexturePaths[i]), i));
        var empty = new Queue<int>(_project.ReplaceDummiesOnAdding ? Enumerable.Range(0, item.TexturePaths.Count).Where(i => string.IsNullOrWhiteSpace(item.TexturePaths[i])) : []);
        var nextSlot = item.TexturePaths.Count;
        bool? duplicatePolicy = _project.AlwaysImportDuplicates ? true : null;
        foreach (var path in incoming)
        {
            var fingerprint = TextureContent.Fingerprint(path);
            if (content.TryGetValue(fingerprint, out var match))
            {
                _duplicateReviewNeeded = true;
                var import = duplicatePolicy;
                if (import is null)
                {
                    var dialog = new DuplicateTextureWindow(match.Name, match.Slot) { Owner = this };
                    if (dialog.ShowDialog() != true) continue;
                    import = dialog.Import;
                    if (dialog.ApplyToAll) duplicatePolicy = import;
                }
                if (import != true) continue;
            }
            var slot = empty.Count > 0 ? empty.Dequeue() : nextSlot++;
            if (slot >= 26) throw new InvalidDataException("O addon aceita no máximo 26 variantes por peça.");
            accepted.Add(path); content.TryAdd(fingerprint, (Path.GetFileName(path), slot));
        }
        return accepted.ToArray();
    }
    private void AttachTextures(ClothItem item, IEnumerable<string> files)
    {
        var accepted = ReviewTextureImports(item, files);
        ApplyImportedTextures(item, accepted);
    }
    private void ApplyImportedTextures(ClothItem item, string[] accepted)
    {
        AlignTextureNames(item);
        foreach (var path in accepted)
        {
            var dummyIndex = _project.ReplaceDummiesOnAdding ? item.TexturePaths.FindIndex(string.IsNullOrWhiteSpace) : -1;
            if (dummyIndex >= 0) { item.TexturePaths[dummyIndex] = path; item.OriginalTextureFileNames[dummyIndex] = Path.GetFileName(path); continue; }
            item.TexturePaths.Add(path);
            item.OriginalTextureFileNames.Add(Path.GetFileName(path));
        }
        if (accepted.Length == 0) { Status("No textures imported."); return; }
        _dirty = true;
        RefreshTextureList(item, item.TexturePaths.Count - 1);
        RefreshItems();
        Status(accepted.Length == 1
            ? $"Added texture {Path.GetFileName(accepted[0])} to '{(item.Gender == "Feminino" ? "Female" : item.Gender == "Masculino" ? "Male" : "Undefined")} {item.ExportPosition ?? item.SourceIndex ?? 0} {item.Name}' at pos {item.ExportPosition ?? item.SourceIndex ?? 0}"
            : $"Added {accepted.Length} texture(s) to '{item.Name}'.");
    }

    private void ReplaceTexture_Click(object sender, RoutedEventArgs e)
    {
        if (ItemList.SelectedItem is not ClothItem item || TextureList.SelectedIndex < 0) return;
        var index = TextureList.SelectedIndex;
        var dialog = new OpenFileDialog
        {
            Filter = "Dicionário de texturas GTA (*.ytd)|*.ytd",
            Title = "Substituir variante YTD"
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            CheckYtd(dialog.FileName);
            AlignTextureNames(item);
            item.TexturePaths[index] = dialog.FileName;
            item.OriginalTextureFileNames[index] = Path.GetFileName(dialog.FileName);
            _dirty = true;
            RefreshTextureList(item, index);
            RefreshItems();
            Status($"Variante {(char)('a' + index)} substituída em {item.Name}.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Falha ao substituir textura", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RemoveTexture_Click(object sender, RoutedEventArgs e)
    {
        if (ItemList.SelectedItem is not ClothItem item || TextureList.SelectedIndex < 0) return;
        var index = TextureList.SelectedIndex;
        AlignTextureNames(item);
        if (_project.ReplaceWithDummyOnRemove && !string.IsNullOrWhiteSpace(item.TexturePaths[index]))
        { item.TexturePaths[index] = ""; item.OriginalTextureFileNames[index] = "(dummy)"; item.TextureEffects[index] = new TextureEffect(); item.TextureLabels[index] = ""; }
        else { item.TexturePaths.RemoveAt(index); item.TextureIds.RemoveAt(index); item.OriginalTextureFileNames.RemoveAt(index); item.TextureEffects.RemoveAt(index); item.TextureLabels.RemoveAt(index); }
        _dirty = true;
        RefreshTextureList(item, index);
        RefreshItems();
        Status($"Variante removida de {item.Name}; arquivo de origem preservado.");
    }

    private void MoveTextureUp_Click(object sender, RoutedEventArgs e) => MoveTexture(-1);
    private void MoveTextureDown_Click(object sender, RoutedEventArgs e) => MoveTexture(1);

    private void MoveTexture(int direction)
    {
        if (ItemList.SelectedItem is not ClothItem item || TextureList.SelectedIndex < 0) return;
        var index = TextureList.SelectedIndex;
        var target = index + direction;
        if (target < 0 || target >= item.TexturePaths.Count) return;
        AlignTextureNames(item);
        (item.TexturePaths[index], item.TexturePaths[target]) =
            (item.TexturePaths[target], item.TexturePaths[index]);
        (item.TextureIds[index], item.TextureIds[target]) = (item.TextureIds[target], item.TextureIds[index]);
        (item.TextureLabels[index], item.TextureLabels[target]) = (item.TextureLabels[target], item.TextureLabels[index]);
        (item.OriginalTextureFileNames[index], item.OriginalTextureFileNames[target]) =
            (item.OriginalTextureFileNames[target], item.OriginalTextureFileNames[index]);
        (item.TextureEffects[index], item.TextureEffects[target]) = (item.TextureEffects[target], item.TextureEffects[index]);
        _dirty = true;
        RefreshTextureList(item, target);
        RefreshItems();
        Status($"Variante movida para {(char)('a' + target)}.");
    }

    private List<string> ValidateProject()
    {
        var errors = FiveMResourceValidator.Validate(_project);
        var outputNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in _project.Items)
        {
            try
            {
                foreach (var asset in NativeItemExporter.Plan(item, AbsoluteAssetPath))
                {
                    var absolute = Path.GetFullPath(asset.Source);
                    if (outputNames.TryGetValue(asset.Name, out var previous) &&
                        !AssetContent.Equivalent(previous, absolute))
                        errors.Add("Nome duplicado no stream: " + asset.Name);
                    else
                        outputNames[asset.Name] = absolute;
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException)
            { errors.Add(item.Name + ": " + ex.Message); }
        }
        foreach (var tattoo in _project.Tattoos)
        {
            if (string.IsNullOrWhiteSpace(tattoo.Zone))
            {
                errors.Add("Pending: Tattoo " + tattoo.Name + ": body zone is not configured. Kept in the project; only blocks builds that include tattoos.");
                continue;
            }
            try { TattooExporter.Validate(tattoo); TattooExporter.ReadImage(tattoo, AbsoluteAssetPath(tattoo.ImagePath)); }
            catch (Exception ex) { errors.Add(tattoo.Name + ": " + ex.Message); }
        }
        return errors;
    }

    private void Validate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_resource is not null && !_dirty)
            {
                var resourceErrors = FiveMResourceValidator.Validate(_resource.Root);
                new ErrorListWindow(resourceErrors) { Owner = this }.ShowDialog();
                Status(resourceErrors.Count == 0 ? "Recurso validado estaticamente." :
                    resourceErrors.Count + " inconsistência(s) no recurso.");
                return;
            }
            var errors = ValidateProject();
            var undefined = _project.Items.Count(item => item.Gender == "Indefinido");
            var message = errors.Count == 0
                ? "Todos os arquivos referenciados existem."
                : string.Join(Environment.NewLine, errors.Take(15));
            if (undefined > 0) message += Environment.NewLine + undefined + " peças com gênero indefinido.";
            if (undefined > 0) errors.Add(undefined + " drawable(s) with undefined gender.");
            new ErrorListWindow(errors) { Owner = this }.ShowDialog();
            var warnings = errors.Count(message => message.StartsWith("Aviso:") || message.StartsWith("Warning:"));
            var pending = errors.Count(message => message.StartsWith("Pending:"));
            Status($"Validation: {errors.Count - warnings - pending} errors, {warnings} warnings, {pending} pending items.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Falha de validação", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_project.Items.Count == 0) { Status("Importe ao menos um YDD antes de exportar."); return; }
        var dialog = new OpenFolderDialog { Title = "Selecione a pasta de saída" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            IsEnabled = false;
            Status($"Exportando {ProjectOperationSummary.Describe(_project)} e verificando arquivos...");
            var result = await Task.Run(() => StreamExporter.Export(_project,
                AbsoluteAssetPath, dialog.FolderName));
            Status(ProjectOperationSummary.Describe(_project) + " exportadas; " + result.FileCount + " arquivos em " + result.Folder);
            MessageBox.Show(ProjectOperationSummary.Describe(_project) + " exportadas.\nStream exportado em:\n" + result.Folder +
                "\n\nEste estágio ainda não gera metadados de addon.", "Exportação concluída",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Status("Falha ao exportar stream.");
            MessageBox.Show(ex.Message, "Exportação interrompida", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { IsEnabled = true; }
    }

    private async void ExportAddon_Click(object sender, RoutedEventArgs e)
    {
        Keyboard.ClearFocus();
        var dialog = new BuildProjectWindow(_project, AbsoluteAssetPath, _resource?.Root ?? _project.SourceResourceRoot) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is ClothProject result)
        {
            _project.DlcName = result.DlcName; _project.OutputFolder = result.OutputFolder;
            _project.SourceResourceRoot = result.SourceResourceRoot;
            _project.BuildTarget = result.BuildTarget;
            _project.CreateDrawableSubfolders = result.CreateDrawableSubfolders;
            _project.CreateClothesInfoDump = result.CreateClothesInfoDump;
            _project.DisableExtraAlternateVariationFeatures = result.DisableExtraAlternateVariationFeatures;
            _project.RemoveDefaultCollectionPrefix = result.RemoveDefaultCollectionPrefix;
            _project.DisableExtraYmtFiles = result.DisableExtraYmtFiles;
            _project.ReplaceDlcCollection = result.ReplaceDlcCollection;
            _project.ReplacementResource = result.ReplacementResource;
            _project.ReplacementMaleCollection = result.ReplacementMaleCollection;
            _project.ReplacementFemaleCollection = result.ReplacementFemaleCollection;
            _project.SingleplayerSetupOrder = result.SingleplayerSetupOrder;
            _project.CreateTattoosInfoDump = result.CreateTattoosInfoDump;
            _project.CreateIlleniumConfig = result.CreateIlleniumConfig;
            _project.CreateEsxTattooConfig = result.CreateEsxTattooConfig;
            _project.CreateMenyooTattooConfig = result.CreateMenyooTattooConfig;
            _dirty = true; RefreshItems(); Status("Exportação concluída: " + ProjectOperationSummary.Describe(result) + ". " + dialog.OutputPath);
        }
        await Task.CompletedTask;
    }

    private static string Sanitize(string name)
    {
        var text = new string(name.ToLowerInvariant().Select(ch =>
            char.IsAsciiLetterOrDigit(ch) ? ch : '_').ToArray()).Trim('_');
        return text.Length == 0 ? "projeto" : text;
    }
}
