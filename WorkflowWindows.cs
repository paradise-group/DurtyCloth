using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace FooxClothStudio;

public static class WorkflowUi
{
    public static Style ComboStyle() => (Style)System.Windows.Markup.XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ComboBox">
          <Setter Property="Foreground" Value="#F0F1F2"/><Setter Property="Background" Value="#363A40"/>
          <Setter Property="BorderBrush" Value="#51565D"/><Setter Property="Padding" Value="6,4"/>
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBox">
            <Grid>
              <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1"/>
              <ToggleButton Focusable="False" IsChecked="{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}">
                <ToggleButton.Template><ControlTemplate TargetType="ToggleButton"><Border Background="Transparent"><TextBlock Text="▾" HorizontalAlignment="Right" VerticalAlignment="Center" Margin="0,0,7,0" Foreground="#F0F1F2"/></Border></ControlTemplate></ToggleButton.Template>
              </ToggleButton>
              <ContentPresenter x:Name="Selection" Content="{TemplateBinding SelectionBoxItem}" ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}" Margin="6,4,25,4" VerticalAlignment="Center" IsHitTestVisible="False"/>
              <TextBox x:Name="PART_EditableTextBox" Visibility="Collapsed" Background="Transparent" Foreground="#F0F1F2" BorderThickness="0" Padding="0" Margin="6,4,25,4" IsReadOnly="{TemplateBinding IsReadOnly}">
                <TextBox.Template><ControlTemplate TargetType="TextBox"><ScrollViewer x:Name="PART_ContentHost"/></ControlTemplate></TextBox.Template>
              </TextBox>
              <Popup x:Name="PART_Popup" IsOpen="{TemplateBinding IsDropDownOpen}" Placement="Bottom" AllowsTransparency="True" Focusable="False" PopupAnimation="Slide">
                <Border Background="#363A40" BorderBrush="#51565D" BorderThickness="1" MinWidth="{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}">
                  <ScrollViewer MaxHeight="260"><ItemsPresenter KeyboardNavigation.DirectionalNavigation="Contained"/></ScrollViewer>
                </Border>
              </Popup>
            </Grid>
            <ControlTemplate.Triggers>
              <Trigger Property="IsEditable" Value="True"><Setter TargetName="Selection" Property="Visibility" Value="Collapsed"/><Setter TargetName="PART_EditableTextBox" Property="Visibility" Value="Visible"/></Trigger>
              <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.5"/></Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate></Setter.Value></Setter>
        </Style>
        """);
    public static readonly Brush Background = new SolidColorBrush(Color.FromRgb(29, 31, 34));
    public static readonly Brush Panel = new SolidColorBrush(Color.FromRgb(37, 40, 44));
    public static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(155, 113, 232));
    public static StackPanel Stack() => new() { Margin = new Thickness(12) };
    public static TextBox Field(Panel panel, string label, string value, bool multiline = false)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 4) });
        var box = new TextBox { Text = value, Background = Panel, Foreground = Brushes.White,
            Padding = new Thickness(5), AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinHeight = multiline ? 70 : 24 };
        panel.Children.Add(box);
        return box;
    }
    public static CheckBox Check(Panel panel, string label, bool value)
    {
        var box = new CheckBox { Content = label, IsChecked = value,
            Foreground = Brushes.White, Margin = new Thickness(0, 7, 0, 0) };
        panel.Children.Add(box);
        return box;
    }
    public static TextBox FolderField(Panel panel, string label, string value, Window owner)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 4) });
        var row = new DockPanel(); panel.Children.Add(row);
        var browse = new Button { Content = "...", Width = 28, Background = Panel, Foreground = Brushes.White };
        var clear = new Button { Content = "×", Width = 24, Background = Panel, Foreground = Brushes.White };
        DockPanel.SetDock(browse, Dock.Right); row.Children.Add(browse);
        DockPanel.SetDock(clear, Dock.Right); row.Children.Add(clear);
        var field = new TextBox { Text = value, Background = Panel, Foreground = Brushes.White, Padding = new Thickness(5) };
        row.Children.Add(field);
        clear.Click += (_, _) => field.Clear();
        browse.Click += (_, _) => { var picker = new OpenFolderDialog(); if (picker.ShowDialog(owner) == true) field.Text = picker.FolderName; };
        return field;
    }
    public static Button Button(Panel panel, string label, RoutedEventHandler click)
    {
        // Side-by-side buttons (footer rows) get a gap from the previous one.
        var horizontal = panel is WrapPanel || panel is StackPanel { Orientation: Orientation.Horizontal };
        var gap = horizontal && panel.Children.Count > 0 ? 8 : 0;
        var button = new Button { Content = label, Padding = new Thickness(8, 4, 8, 4),
            Background = Accent, Foreground = Brushes.White, Margin = new Thickness(gap, 8, 0, 0) };
        button.Click += click; panel.Children.Add(button); return button;
    }
    public static void Theme(Window window, string title, double width, double height)
    {
        window.Title = "Paradise Cloth Studio - " + title;
        window.Width = width; window.Height = height;
        window.MinWidth = width; window.MinHeight = 400;
        window.Background = Background; window.Foreground = Brushes.White;
        window.FontFamily = new FontFamily("Segoe UI"); window.FontSize = 12;
        window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var labels = new Style(typeof(TextBlock)); labels.Setters.Add(new Setter(TextBlock.ForegroundProperty, Brushes.White));
        window.Resources[typeof(TextBlock)] = labels;
        window.Resources[typeof(ComboBox)] = ComboStyle();
        var tabs = new Style(typeof(TabControl));
        tabs.Setters.Add(new Setter(Control.BackgroundProperty, Panel)); tabs.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
        tabs.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(81, 86, 93))));
        window.Resources[typeof(TabControl)] = tabs;
        window.Resources[typeof(TabItem)] = (Style)System.Windows.Markup.XamlReader.Parse("""
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="TabItem">
              <Setter Property="Foreground" Value="#F0F1F2"/><Setter Property="Background" Value="#363A40"/>
              <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="TabItem">
                <Border Name="HeaderBorder" Background="{TemplateBinding Background}" Padding="8,5" BorderBrush="#51565D" BorderThickness="0,0,0,1"><ContentPresenter ContentSource="Header"/></Border>
                <ControlTemplate.Triggers><Trigger Property="IsSelected" Value="True"><Setter TargetName="HeaderBorder" Property="BorderBrush" Value="#9B71E8"/><Setter TargetName="HeaderBorder" Property="BorderThickness" Value="0,0,0,2"/></Trigger></ControlTemplate.Triggers>
              </ControlTemplate></Setter.Value></Setter>
            </Style>
            """);
    }
}

public sealed class ProjectSettingsWindow : Window
{
    public ClothProject Result { get; private set; }
    public ProjectSettingsWindow(ClothProject project, Func<string, string> resolve, bool creating = false)
    {
        Result = JsonSerializer.Deserialize<ClothProject>(JsonSerializer.Serialize(project))!;
        NameScope.SetNameScope(this, new NameScope());
        WorkflowUi.Theme(this, creating ? "New Project" : "Project Settings", 1040, 600);
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(225) });
        grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition()); Content = grid;
        var navigation = WorkflowUi.Stack(); grid.Children.Add(navigation);
        Grid.SetRowSpan(navigation, 2);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12) };
        Grid.SetColumn(footer, 1); Grid.SetRow(footer, 1); grid.Children.Add(footer);
        var search = WorkflowUi.Field(navigation, "Search Settings", "");
        var sections = new ListBox { ItemsSource = new[] { "General", "Sharing and Collaboration", "Warnings and Validations" },
            Background = WorkflowUi.Panel, Foreground = Brushes.White, Margin = new Thickness(0, 8, 0, 0) };
        navigation.Children.Add(sections);
        var tabs = new TabControl { Background = WorkflowUi.Background, BorderThickness = new Thickness(0) };
        var hiddenHeader = new Style(typeof(TabItem)); hiddenHeader.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed));
        tabs.Resources[typeof(TabItem)] = hiddenHeader;
        Grid.SetColumn(tabs, 1); grid.Children.Add(tabs);
        var general = WorkflowUi.Stack();
        var projectName = WorkflowUi.Field(general, "Project Name *", creating ? "" : project.Name);
        RegisterName("ProjectNameField", projectName);
        var identity = new Grid(); identity.ColumnDefinitions.Add(new ColumnDefinition());
        identity.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) }); identity.ColumnDefinitions.Add(new ColumnDefinition());
        var authorPanel = new StackPanel(); var versionPanel = new StackPanel();
        identity.Children.Add(authorPanel); Grid.SetColumn(versionPanel, 2); identity.Children.Add(versionPanel); general.Children.Add(identity);
        var author = WorkflowUi.Field(authorPanel, "Author (optional)", project.Author);
        var version = WorkflowUi.Field(versionPanel, "Version (optional)", project.Version);
        var description = WorkflowUi.Field(general, "Description (optional)", project.Description, true);
        var dlc = WorkflowUi.Field(general, "DLC Name *", string.IsNullOrWhiteSpace(project.DlcName)
            ? new string(project.Name.ToLowerInvariant().Where(char.IsAsciiLetterOrDigit).Take(14).ToArray()) : project.DlcName);
        var autoDlc = dlc.Text;
        if (project.PublishedCollection is not null) { projectName.IsReadOnly = true; dlc.IsReadOnly = true; }
        RegisterName("DlcNameField", dlc);
        projectName.TextChanged += (_, _) =>
        {
            var generated = new string(projectName.Text.ToLowerInvariant().Where(char.IsAsciiLetterOrDigit).Take(24).ToArray());
            if (dlc.Text == autoDlc || string.IsNullOrWhiteSpace(dlc.Text)) dlc.Text = generated;
            autoDlc = generated;
        };
        var folder = WorkflowUi.FolderField(general, "Output Folder *", project.OutputFolder, this);
        RegisterName("OutputFolderField", folder);
        general.Children.Add(new TextBlock { Text = "Server target game build", Margin = new Thickness(0, 8, 0, 4) });
        var build = new ComboBox { IsEditable = true, ItemsSource = new[] { "3095", "3258", "3407", "3570" }, Text = project.TargetGameBuild.ToString() };
        general.Children.Add(build);
        general.Children.Add(new TextBlock { Text = "Behavior:", Margin = new Thickness(0, 12, 0, 4) });
        var unique = WorkflowUi.Check(general, "Use texture name as uniqueNameHash in shop metas", project.UseTextureNameAsUniqueHash);
        var duplicates = WorkflowUi.Check(general, "Always import duplicates without confirmation", project.AlwaysImportDuplicates);
        var dummyRemove = WorkflowUi.Check(general, "Automatically replace with dummies on remove", project.ReplaceWithDummyOnRemove);
        var dummyAdd = WorkflowUi.Check(general, "Automatically replace dummies when adding clothes", project.ReplaceDummiesOnAdding);
        var permanentIds = WorkflowUi.Check(general, "Use permanent IDs in scripts (allows reordering items and variants)", project.UsePermanentClothingIds);
        general.Children.Add(new TextBlock { Text = "Enable after integrating the adapter into clothing, uniform and wardrobe systems and migrating saved data to itemId + textureId. Collection + local index alone does not track reordering within a collection.", TextWrapping = TextWrapping.Wrap });
        if (project.PublishedCollection is not null) dummyAdd.IsEnabled = false;
        var published = WorkflowUi.Check(general, "Collection publicada: proteger índices (irreversível neste projeto)", project.PublishedCollection is not null);
        published.IsEnabled = project.PublishedCollection is null;
        var updatePublished = WorkflowUi.Check(general, "Registrar também as novas peças como publicadas", false);
        updatePublished.Visibility = project.PublishedCollection is null ? Visibility.Collapsed : Visibility.Visible;
        void UpdateIdentityMode()
        {
            var locked = project.PublishedCollection is not null && permanentIds.IsChecked != true;
            projectName.IsReadOnly = locked; dlc.IsReadOnly = locked;
            dummyAdd.IsEnabled = !locked;
            published.IsEnabled = project.PublishedCollection is null && permanentIds.IsChecked != true;
            updatePublished.IsEnabled = permanentIds.IsChecked != true;
        }
        permanentIds.Checked += (_, _) => UpdateIdentityMode();
        permanentIds.Unchecked += (_, _) => UpdateIdentityMode();
        UpdateIdentityMode();
        var folders = WorkflowUi.Check(general, "Create sub folders for cloth drawable types", project.CreateDrawableSubfolders);
        var dump = WorkflowUi.Check(general, "Create clothes info dump", project.CreateClothesInfoDump);
        var modelLimit = WorkflowUi.Field(general, "Model size warning (MiB)", project.ModelWarningMiB.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var textureLimit = WorkflowUi.Field(general, "Texture size warning (MiB)", project.TextureWarningMiB.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var apply = WorkflowUi.Button(footer, creating ? "Create project" : "Apply", (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(projectName.Text)) { MessageBox.Show(this, "Enter a Project Name."); return; }
            if (!Regex.IsMatch(dlc.Text, "^[a-z0-9_]{1,24}$") || !int.TryParse(build.Text, out var target) || target < 1)
            { MessageBox.Show(this, "Use a lowercase DLC name (1–24 letters, numbers or _) and a positive game build."); return; }
            if (string.IsNullOrWhiteSpace(folder.Text)) { MessageBox.Show(this, "Select an Output Folder."); return; }
            try { Result.OutputFolder = Path.GetFullPath(folder.Text); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message); return; }
            if (!double.TryParse(modelLimit.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var modelMiB) || !double.TryParse(textureLimit.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var textureMiB) || !double.IsFinite(modelMiB) || !double.IsFinite(textureMiB) || modelMiB <= 0 || textureMiB <= 0) { MessageBox.Show(this, "Size warning thresholds must be positive MiB values."); return; }
            Result.ModelWarningMiB = modelMiB; Result.TextureWarningMiB = textureMiB;
            Result.Author = author.Text; Result.Version = version.Text; Result.Description = description.Text;
            Result.Name = projectName.Text.Trim();
            Result.DlcName = dlc.Text; Result.TargetGameBuild = target;
            Result.UseTextureNameAsUniqueHash = unique.IsChecked == true;
            Result.AlwaysImportDuplicates = duplicates.IsChecked == true;
            Result.ReplaceWithDummyOnRemove = dummyRemove.IsChecked == true;
            Result.ReplaceDummiesOnAdding = dummyAdd.IsChecked == true;
            Result.UsePermanentClothingIds = permanentIds.IsChecked == true;
            Result.CreateDrawableSubfolders = folders.IsChecked == true;
            Result.CreateClothesInfoDump = dump.IsChecked == true;
            try
            {
                CollectionIndices.EnsureValid(Result);
                if (!Result.UsePermanentClothingIds && ((published.IsChecked == true && project.PublishedCollection is null) || updatePublished.IsChecked == true))
                {
                    if (MessageBox.Show(this, "Confirma que estes índices e o DLC name correspondem à collection publicada no servidor? Eles serão protegidos; novas peças devem entrar no final e remoções devem usar Replace with Dummy.",
                        "Publicar collection", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                    CollectionIndices.Publish(Result);
                    Result.ReplaceDummiesOnAdding = false;
                }
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Collection publicada"); return; }
            DialogResult = true;
        });
        RegisterName("ApplyProjectSettings", apply);
        WorkflowUi.Button(footer, "Cancel", (_, _) => DialogResult = false);
        tabs.Items.Add(new TabItem { Header = "General", Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = general } });
        var sharing = WorkflowUi.Stack();
        sharing.Children.Add(new TextBlock { Text = "Export project packages with Project > Export project.\nPackages include all referenced YDD, YTD and YLD files.", TextWrapping = TextWrapping.Wrap });
        WorkflowUi.Button(sharing, "Export project package", (_, _) =>
        {
            var dialog = new SaveFileDialog { Filter = "Foox project package (*.fcsbundle)|*.fcsbundle", FileName = project.Name + ".fcsbundle" };
            if (dialog.ShowDialog(this) != true) return;
            try { ProjectBundle.Export(project, resolve, dialog.FileName); MessageBox.Show(this, "Project package exported."); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Export failed"); }
        });
        tabs.Items.Add(new TabItem { Header = "Sharing and Collaboration", Content = sharing });
        var warnings = WorkflowUi.Stack();
        warnings.Children.Add(new TextBlock { Text = "Build checks missing assets, drawable types, genders, duplicate output names, texture counts, YMT and physics files.\nOpen Error List before building to inspect the current project.", TextWrapping = TextWrapping.Wrap });
        tabs.Items.Add(new TabItem { Header = "Warnings and Validations", Content = warnings });
        sections.SelectionChanged += (_, _) => tabs.SelectedIndex = sections.SelectedIndex;
        search.TextChanged += (_, _) => { var term = search.Text; var index = Array.FindIndex(new[] { "General Author Version Description DLC Output build", "Sharing and Collaboration", "Warnings and Validations" }, x => x.Contains(term, StringComparison.OrdinalIgnoreCase)); if (index >= 0) sections.SelectedIndex = index; };
        sections.SelectedIndex = 0;
    }
}

public sealed class BuildProjectWindow : Window
{
    public string? OutputPath { get; private set; }
    public ClothProject? Result { get; private set; }
    public BuildProjectWindow(ClothProject project, Func<string, string> resolve, string? sourceRoot = null)
    {
        WorkflowUi.Theme(this, "Build Project", 360, 690);
        var panel = WorkflowUi.Stack(); Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel };
        panel.Children.Add(new TextBlock { Text = "Target resource type" });
        var types = new ListBox { Background = WorkflowUi.Panel, Foreground = Brushes.White, Margin = new Thickness(0, 4, 0, 6) };
        foreach (var label in new[] { "alt:V Resource", "Single player RPF", "FiveM Resource", "RageMP Resource" })
            types.Items.Add(new ListBoxItem { Content = label,
                ToolTip = "Generate clothing data for this target" });
        types.SelectedIndex = Array.IndexOf(new[] { "alt:V Resource", "Single player RPF", "FiveM Resource", "RageMP Resource" }, project.BuildTarget);
        if (types.SelectedIndex < 0) types.SelectedIndex = 2;
        panel.Children.Add(types);
        var originalRoot = OriginalResourceExporter.FindSource(project, sourceRoot);
        var preserveOriginal = WorkflowUi.Check(panel, "Keep original resource structure (add IDs only)", originalRoot is not null);
        var originalFolder = WorkflowUi.FolderField(panel, "Original FiveM resource folder", originalRoot ?? "", this);
        panel.Children.Add(new TextBlock { Text = "Original mode keeps all collections, local indices, models, textures and metadata. It adds the ID map and adapter; it does not rebuild the pack or apply asset edits.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8) });
        panel.Children.Add(new TextBlock { Text = "Replace GTA DLC clothes (advanced)", Margin = new Thickness(0, 10, 0, 0) });
        var replace = WorkflowUi.Check(panel, "Show GTA DLC replace options", project.ReplaceDlcCollection);
        replace.IsEnabled = project.Items.Count > 0;
        var replacementPanel = new StackPanel(); panel.Children.Add(replacementPanel);
        var replacementResource = WorkflowUi.Field(replacementPanel, "DLC resource name", project.ReplacementResource);
        var replacementMale = WorkflowUi.Field(replacementPanel, "Male clothes collection", project.ReplacementMaleCollection);
        var replacementFemale = WorkflowUi.Field(replacementPanel, "Female clothes collection", project.ReplacementFemaleCollection);
        replacementPanel.Children.Add(new TextBlock { Text = "Replaces the complete selected collection with this project's clothes.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        replacementPanel.Visibility = replace.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        replace.Checked += (_, _) => replacementPanel.Visibility = Visibility.Visible;
        replace.Unchecked += (_, _) => replacementPanel.Visibility = Visibility.Collapsed;
        var suggestedName = new string(project.Name.ToLowerInvariant().Where(char.IsAsciiLetterOrDigit).Take(14).ToArray());
        var name = WorkflowUi.Field(panel, "Clothes collection name", string.IsNullOrWhiteSpace(project.DlcName) ? (suggestedName.Length == 0 ? "roupas" : suggestedName) : project.DlcName);
        var prefix = WorkflowUi.Check(panel, "Remove default added prefix (mp_m/mp_f)", project.RemoveDefaultCollectionPrefix);
        prefix.IsEnabled = project.Items.Count > 0;
        if (project.PublishedCollection is not null && !project.UsePermanentClothingIds)
        {
            name.IsReadOnly = true; prefix.IsEnabled = replace.IsEnabled = false;
            replacementResource.IsReadOnly = replacementMale.IsReadOnly = replacementFemale.IsReadOnly = true;
        }
        panel.Children.Add(new TextBlock { Text = "Extra options", Margin = new Thickness(0, 12, 0, 0) });
        var folders = WorkflowUi.Check(panel, "Create sub folders for cloth drawable types", project.CreateDrawableSubfolders);
        var extraYmt = WorkflowUi.Check(panel, "Disable extra YMT files creation", project.DisableExtraYmtFiles);
        extraYmt.ToolTip = "Omit creature metadata and its hat hair/heel deformation modifiers. Required collection YMT and first person metadata are retained.";
        var alternate = WorkflowUi.Check(panel, "Disable extra alternate variation features", project.DisableExtraAlternateVariationFeatures);
        var dump = WorkflowUi.Check(panel, "Create clothes info dump", project.CreateClothesInfoDump);
        folders.IsEnabled = extraYmt.IsEnabled = alternate.IsEnabled = dump.IsEnabled = project.Items.Count > 0;
        var missingTattooZones = project.Tattoos.Count(tattoo => string.IsNullOrWhiteSpace(tattoo.Zone));
        var includeTattoos = WorkflowUi.Check(panel, $"Include tattoos ({project.Tattoos.Count} in project)", project.Tattoos.Count > 0 && missingTattooZones == 0);
        includeTattoos.IsEnabled = project.Tattoos.Count > 0;
        var missingTattooNote = new TextBlock { Text = $"{missingTattooZones} tattoos have no body zone. They are excluded from rebuilt addons by default and remain in the project. Original mode copies existing tattoo assets as-is.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8), Visibility = missingTattooZones > 0 ? Visibility.Visible : Visibility.Collapsed };
        panel.Children.Add(missingTattooNote);
        var tattooDump = WorkflowUi.Check(panel, "Create tattoos info dump", project.CreateTattoosInfoDump);
        var illenium = WorkflowUi.Check(panel, "Create illenium-appearance tattoo config file (FiveM)", project.CreateIlleniumConfig);
        var esx = WorkflowUi.Check(panel, "Create ESX-TattooShop config file (FiveM)", project.CreateEsxTattooConfig);
        var menyoo = WorkflowUi.Check(panel, "Create Menyoo PedDecalOverlays.xml", project.CreateMenyooTattooConfig);
        tattooDump.IsEnabled = illenium.IsEnabled = esx.IsEnabled = menyoo.IsEnabled = project.Tattoos.Count > 0;
        void UpdateTattooOptions() => tattooDump.IsEnabled = illenium.IsEnabled = esx.IsEnabled = menyoo.IsEnabled = includeTattoos.IsChecked == true;
        includeTattoos.Checked += (_, _) => UpdateTattooOptions();
        includeTattoos.Unchecked += (_, _) => UpdateTattooOptions();
        UpdateTattooOptions();
        var orderPanel = new StackPanel(); panel.Children.Add(orderPanel);
        var order = WorkflowUi.Field(orderPanel, "DLC load order", project.SingleplayerSetupOrder.ToString());
        void UpdateTarget()
        {
            orderPanel.Visibility = types.SelectedIndex is 1 or 3 ? Visibility.Visible : Visibility.Collapsed;
            preserveOriginal.IsEnabled = types.SelectedIndex == 2;
        }
        types.SelectionChanged += (_, _) => UpdateTarget(); UpdateTarget();
        var output = WorkflowUi.FolderField(panel, "Build output folder", project.OutputFolder, this);
        var progress = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) }; panel.Children.Add(progress);
        var buildBar = new ProgressBar { Height = 8, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
        panel.Children.Add(buildBar);
        var buildContents = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        void UpdateBuildContents() => buildContents.Text = preserveOriginal.IsChecked == true && types.SelectedIndex == 2
            ? "Build: complete original resource with its existing tattoo assets and metadata, plus permanent IDs."
            : $"Build: {project.Items.Count} items, {project.Items.Sum(item => item.TexturePaths.Count)} textures, {(includeTattoos.IsChecked == true ? project.Tattoos.Count : 0)} tattoos.";
        void UpdateMode()
        {
            var original = preserveOriginal.IsChecked == true && types.SelectedIndex == 2;
            originalFolder.IsEnabled = original;
            name.IsEnabled = !original;
            replace.IsEnabled = !original && project.Items.Count > 0 && (project.PublishedCollection is null || project.UsePermanentClothingIds);
            prefix.IsEnabled = replace.IsEnabled;
            folders.IsEnabled = extraYmt.IsEnabled = alternate.IsEnabled = dump.IsEnabled = !original && project.Items.Count > 0;
            includeTattoos.IsEnabled = !original && project.Tattoos.Count > 0;
            UpdateTattooOptions();
            if (original) tattooDump.IsEnabled = illenium.IsEnabled = esx.IsEnabled = menyoo.IsEnabled = false;
            UpdateBuildContents();
        }
        preserveOriginal.Checked += (_, _) => UpdateMode(); preserveOriginal.Unchecked += (_, _) => UpdateMode();
        types.SelectionChanged += (_, _) => UpdateMode();
        includeTattoos.Checked += (_, _) => UpdateBuildContents(); includeTattoos.Unchecked += (_, _) => UpdateBuildContents();
        UpdateBuildContents(); panel.Children.Add(buildContents);
        UpdateMode();
        var busy = false;
        Closing += (_, e) => { if (busy) e.Cancel = true; };
        var button = WorkflowUi.Button(panel, "Build", async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(output.Text)) { progress.Text = "Select a Build output folder."; return; }
            if (!int.TryParse(order.Text, out var loadOrder) || loadOrder < 0) { progress.Text = "DLC load order must be a nonnegative integer."; return; }
            var snapshot = JsonSerializer.Deserialize<ClothProject>(JsonSerializer.Serialize(project))!;
            var originalBuild = preserveOriginal.IsChecked == true && types.SelectedIndex == 2;
            if (originalBuild && string.IsNullOrWhiteSpace(originalFolder.Text)) { progress.Text = "Select the original FiveM resource folder."; return; }
            if (!originalBuild && includeTattoos.IsChecked != true) snapshot.Tattoos.Clear();
            snapshot.DlcName = name.Text.Trim(); snapshot.CreateDrawableSubfolders = folders.IsChecked == true;
            snapshot.CreateClothesInfoDump = dump.IsChecked == true;
            snapshot.DisableExtraAlternateVariationFeatures = alternate.IsChecked == true;
            snapshot.RemoveDefaultCollectionPrefix = prefix.IsChecked == true;
            snapshot.DisableExtraYmtFiles = extraYmt.IsChecked == true;
            snapshot.ReplaceDlcCollection = replace.IsEnabled && replace.IsChecked == true;
            snapshot.ReplacementResource = replacementResource.Text.Trim();
            snapshot.ReplacementMaleCollection = replacementMale.Text.Trim();
            snapshot.ReplacementFemaleCollection = replacementFemale.Text.Trim();
            snapshot.SingleplayerSetupOrder = loadOrder;
            snapshot.CreateTattoosInfoDump = tattooDump.IsChecked == true;
            snapshot.CreateIlleniumConfig = illenium.IsChecked == true;
            snapshot.CreateEsxTattooConfig = esx.IsChecked == true;
            snapshot.CreateMenyooTattooConfig = menyoo.IsChecked == true;
            var target = (types.SelectedItem as ListBoxItem)?.Content as string ?? "FiveM Resource";
            snapshot.BuildTarget = target;
            var indexErrors = originalBuild ? new List<string>() : CollectionIndices.Validate(snapshot);
            if (indexErrors.Count > 0) { new ErrorListWindow(indexErrors) { Owner = this }.ShowDialog(); return; }
            var indexWarnings = originalBuild ? new List<string>() : CollectionIndices.Warnings(snapshot);
            if (indexWarnings.Count > 0 && MessageBox.Show(this, string.Join("\n", indexWarnings) + "\nContinuar o build?", "Collection: capacidade", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            busy = true; panel.IsEnabled = false; progress.Text = $"Exportando {ProjectOperationSummary.Describe(snapshot)}...\nGerando stream, YMT e shop.meta...";
            buildBar.Visibility = Visibility.Visible; buildBar.IsIndeterminate = true;
            try
            {
                var parent = Path.GetFullPath(output.Text);
                if (originalBuild)
                {
                    var originalPath = originalFolder.Text;
                    IProgress<string> copyProgress = new Progress<string>(message => progress.Text = message);
                    OutputPath = await Task.Run(() => OriginalResourceExporter.Export(snapshot, resolve, originalPath, parent, copyProgress));
                }
                else
                {
                var gameFolder = GamePedAssets.FindGameFolder();
                var dependencyErrors = await Task.Run(() => BuildDependencies.Prepare(snapshot, resolve, gameFolder));
                if (dependencyErrors.Count > 0)
                {
                    busy = false; panel.IsEnabled = true; buildBar.Visibility = Visibility.Collapsed;
                    progress.Text = $"Build needs attention: {dependencyErrors.Count} unresolved dependencies. Open the error list to review them.";
                    new ErrorListWindow(dependencyErrors) { Owner = this }.ShowDialog();
                    return;
                }
                OutputPath = await Task.Run(() => PlatformExporter.Export(snapshot, resolve, parent, target));
                }
                snapshot.OutputFolder = parent; Result = snapshot;
                buildBar.IsIndeterminate = false; buildBar.Value = 100;
                progress.Text = $"Exportação concluída: {ProjectOperationSummary.Describe(snapshot)}.\n\n" + OutputPath;
                if (originalBuild)
                {
                    var unavailable = OriginalResourceExporter.Describe(snapshot).Entries.Count(entry => !entry.Available);
                    progress.Text += "\n\nOriginal collections, indices and metadata were retained.";
                    if (unavailable > 0) progress.Text += $"\n{unavailable} nonstandard source model names were copied but are not exposed as equippable IDs. Review ORIGINAL-LAYOUT.txt before mapping these entries.";
                }
                busy = false; panel.IsEnabled = true;
                MessageBox.Show(this, progress.Text, "Build completed", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
            }
            catch (Exception ex) { busy = false; buildBar.IsIndeterminate = false; buildBar.Visibility = Visibility.Collapsed; progress.Text = "Build failed: " + ex.Message; panel.IsEnabled = true; }
        });
    }
}

public sealed class DuplicateTextureWindow : Window
{
    public bool Import { get; private set; }
    public bool ApplyToAll { get; private set; }
    public DuplicateTextureWindow(string matchingName, int? slot = null)
    {
        WorkflowUi.Theme(this, "Add duplicate texture?", 570, 180); MinHeight = 180;
        var panel = WorkflowUi.Stack(); Content = panel;
        panel.Children.Add(new TextBlock { Text = (slot.HasValue ? $"Essa textura já existe no slot {(char)('A' + slot.Value)} (posição {slot.Value}).\n" : "Looks like a texture with the same content already exists.\n") +
            matchingName + "\n\nDo you still want to add this texture?\nImporte para comparar antiga e nova no rodapé do aplicativo.", TextWrapping = TextWrapping.Wrap });
        var buttons = new WrapPanel(); panel.Children.Add(buttons);
        foreach (var choice in new[] { ("Import duplicate", true, false), ("Don't import", false, false),
            ("Import all", true, true), ("Don't import any duplicate", false, true) })
            WorkflowUi.Button(buttons, choice.Item1, (_, _) =>
            { Import = choice.Item2; ApplyToAll = choice.Item3; DialogResult = true; });
    }
}

public sealed class RenameWindow : Window
{
    public string Result { get; private set; }
    public RenameWindow(string value)
    {
        Result = value; WorkflowUi.Theme(this, "Rename", 520, 190); MinHeight = 190;
        var panel = WorkflowUi.Stack(); Content = panel;
        var name = WorkflowUi.Field(panel, "New name", value); name.MaxLength = 128;
        var buttons = new WrapPanel(); panel.Children.Add(buttons);
        WorkflowUi.Button(buttons, "Apply", (_, _) => { if (string.IsNullOrWhiteSpace(name.Text)) return; Result = name.Text.Trim(); DialogResult = true; });
        WorkflowUi.Button(buttons, "Cancel", (_, _) => DialogResult = false);
        Loaded += (_, _) => { name.Focus(); name.SelectAll(); };
    }
}

public sealed class ErrorListWindow : Window
{
    public ErrorListWindow(IEnumerable<string> errors)
    {
        WorkflowUi.Theme(this, "Error List", 800, 450);
        var panel = WorkflowUi.Stack(); Content = panel;
        var list = errors.Select(message => message.StartsWith("Aviso:", StringComparison.OrdinalIgnoreCase) && message.Contains("drawables", StringComparison.OrdinalIgnoreCase)
            ? "Warning: Rebuilt addon target (not original resource layout): " + message[6..].TrimStart()
            : message).ToArray();
        static string Kind(string message) => message.StartsWith("Aviso:", StringComparison.OrdinalIgnoreCase) ||
            message.StartsWith("Warning:", StringComparison.OrdinalIgnoreCase) ? "Warnings" :
            message.StartsWith("Pending:", StringComparison.OrdinalIgnoreCase) ? "Pending" : "Errors";
        var summary = $"Errors: {list.Count(message => Kind(message) == "Errors")}   |   " +
            $"Warnings: {list.Count(message => Kind(message) == "Warnings")}   |   Pending: {list.Count(message => Kind(message) == "Pending")}";
        panel.Children.Add(new TextBlock { Text = summary, Margin = new Thickness(0, 0, 0, 10) });
        var filter = new ComboBox { ItemsSource = new[] { "All issues", "Errors", "Warnings", "Pending" }, SelectedIndex = 0,
            Width = 170, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8) };
        panel.Children.Add(filter);
        var rows = new ListBox { Height = 280, Background = WorkflowUi.Panel, Foreground = Brushes.White };
        panel.Children.Add(rows);
        void Refresh()
        {
            var selection = filter.SelectedItem as string;
            var matching = list.Where(message => selection == "All issues" || Kind(message) == selection).ToArray();
            rows.ItemsSource = matching.Length == 0 ? ["No issues in this category."] : matching;
        }
        filter.SelectionChanged += (_, _) => Refresh(); Refresh();
        var buttons = new WrapPanel(); panel.Children.Add(buttons);
        WorkflowUi.Button(buttons, "Copy report", (_, _) => Clipboard.SetText(summary + "\n\n" + string.Join("\n", list)));
        WorkflowUi.Button(buttons, "Save report", (_, _) =>
        {
            var dialog = new SaveFileDialog { Filter = "Text report (*.txt)|*.txt", FileName = "validation-report.txt" };
            if (dialog.ShowDialog(this) == true)
                try { File.WriteAllText(dialog.FileName, summary + "\n\n" + string.Join("\n", list)); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not save report"); }
        });
        WorkflowUi.Button(buttons, "Close", (_, _) => Close());
    }
}

public sealed class ReservationWindow : Window
{
    public string Gender { get; private set; }
    public string Category { get; private set; }
    public ReservationWindow(string gender, string category)
    {
        Gender = gender; Category = category;
        WorkflowUi.Theme(this, "Add Reservation Dummy", 420, 250); MinHeight = 250;
        var panel = WorkflowUi.Stack(); Content = panel;
        panel.Children.Add(new TextBlock { Text = "Gender" });
        var genders = new ComboBox { ItemsSource = new[] { "Male", "Female" }, SelectedIndex = gender == "Feminino" ? 1 : 0 }; panel.Children.Add(genders);
        panel.Children.Add(new TextBlock { Text = "Drawable type", Margin = new Thickness(0, 8, 0, 4) });
        var categories = new ComboBox { ItemsSource = ClothCatalog.Categories.Where(c => c != "Outros"), SelectedItem = category }; panel.Children.Add(categories);
        WorkflowUi.Button(panel, "Add", (_, _) => { Gender = genders.SelectedIndex == 1 ? "Feminino" : "Masculino"; Category = categories.SelectedItem as string ?? category; DialogResult = true; });
    }
}
