using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FooxClothStudio;

internal sealed class PhotoExportOptionsWindow : Window
{
    private readonly Dictionary<string, CheckBox> _categories = new();
    private readonly Dictionary<string, CheckBox> _appearanceKinds = new();
    private readonly Dictionary<string, CheckBox> _genders = new();
    private readonly CheckBox _tattoos = new() { Content = "Tatuagens", IsChecked = true };
    private readonly ComboBox _existing = new() { ItemsSource = new[] { "Ignorar fotografias com o mesmo UUID", "Substituir fotografias com o mesmo UUID" }, SelectedIndex = 0 };
    private readonly TextBox _background = new() { Text = "#625F68", Margin = new Thickness(0, 4, 0, 10) };
    private readonly CheckBox _transparent = new() { Content = "Fundo transparente (PNG / WebP)", Margin = new Thickness(0, 0, 0, 10) };
    private readonly ComboBox _format = new() { ItemsSource = new[] { "WebP", "PNG", "JPEG" }, SelectedIndex = 0, Margin = new Thickness(0, 4, 0, 10) };
    private readonly ComboBox _size = new() { ItemsSource = new[] { 256, 384, 512, 768 }, SelectedItem = 512, Margin = new Thickness(0, 4, 0, 10) };
    private readonly Slider _quality = new() { Minimum = 40, Maximum = 100, Value = 85, TickFrequency = 1, IsSnapToTickEnabled = true };
    private readonly CheckBox _ped = new() { Content = "Mostrar ped base (se GTA V estiver configurado)", Margin = new Thickness(0, 10, 0, 10) };

    public bool OverwriteExisting => _existing.SelectedIndex == 1;
    public bool IncludePed => _ped.IsChecked == true;
    public Color PhotoBackground { get; private set; } = Color.FromRgb(0x62, 0x5F, 0x68);
    public PhotoImageOptions ImageOptions { get; private set; } = new(512, "WebP", 85);
    public bool IncludesItem(ClothItem item) => !item.IsDummy && _categories.GetValueOrDefault(item.Category)?.IsChecked == true && _genders.GetValueOrDefault(item.Gender)?.IsChecked == true;
    public bool IncludesTattoo(TattooItem item) => _tattoos.IsChecked == true && ExportGenders(item.Gender).Any();
    public bool IncludesAppearance(AppearanceAsset item) => _appearanceKinds.GetValueOrDefault(item.Kind)?.IsChecked == true && ExportGenders(item.Gender).Any();
    public string[] ExportGenders(string gender) => (gender == "Both" ? new[] { "Masculino", "Feminino" } :
        new[] { gender is "Male" or "Masculino" ? "Masculino" : "Feminino" })
        .Where(value => _genders.GetValueOrDefault(value)?.IsChecked == true).ToArray();

    public PhotoExportOptionsWindow(ClothProject project) : this(project, null) { }

    public PhotoExportOptionsWindow(ClothProject project, string? sourceDescription)
    {
        WorkflowUi.Theme(this, "Exportar fotografias", 620, 740);
        MinHeight = 650;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var outer = WorkflowUi.Stack(); Content = outer;
        if (sourceDescription is not null)
            outer.Children.Add(new TextBlock { Text = sourceDescription, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        var tabs = new TabControl { Height = 420, Margin = new Thickness(0, 0, 0, 12) };
        outer.Children.Add(tabs);
        var selection = WorkflowUi.Stack();
        tabs.Items.Add(new TabItem { Header = "O que fotografar", Content = new ScrollViewer { Content = selection, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        foreach (var (label, value) in new[] { ("Selecionar tudo", true), ("Limpar seleção", false) })
        {
            var button = new Button { Content = label, Margin = new Thickness(0, 0, 10, 0) };
            button.Click += (_, _) =>
            {
                foreach (var box in _categories.Values.Concat(_appearanceKinds.Values).Concat(_genders.Values).Append(_tattoos))
                    if (box.IsEnabled) box.IsChecked = value;
            };
            controls.Children.Add(button);
        }
        selection.Children.Add(controls);
        selection.Children.Add(new TextBlock { Text = "Género", FontWeight = FontWeights.Bold });
        foreach (var gender in project.Items.Where(item => !item.IsDummy).Select(item => item.Gender)
            .Concat(project.Tattoos.Count + project.AppearanceAssets.Count > 0 ? new[] { "Masculino", "Feminino" } : [])
            .Distinct().OrderBy(value => value)) AddOption(selection, _genders, gender, gender);
        selection.Children.Add(new TextBlock { Text = "Peças e acessórios", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 12, 0, 4) });
        foreach (var group in project.Items.Where(item => !item.IsDummy).GroupBy(item => item.Category).OrderBy(group => group.Key))
            AddOption(selection, _categories, group.Key, $"{group.Key} ({group.Count()})");
        _tattoos.IsEnabled = project.Tattoos.Count > 0;
        _tattoos.IsChecked = _tattoos.IsEnabled;
        _tattoos.Content = $"Tatuagens ({project.Tattoos.Count})";
        _tattoos.Margin = new Thickness(0, 12, 0, 4);
        selection.Children.Add(_tattoos);
        if (project.AppearanceAssets.Count > 0)
            selection.Children.Add(new TextBlock { Text = "Aparência", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 12, 0, 4) });
        foreach (var group in project.AppearanceAssets.GroupBy(item => item.Kind).OrderBy(group => group.Key))
            AddOption(selection, _appearanceKinds, group.Key, $"{group.Key} ({group.Count()})");

        var image = WorkflowUi.Stack();
        tabs.Items.Add(new TabItem { Header = "Imagem", Content = new ScrollViewer { Content = image, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        image.Children.Add(new TextBlock { Text = "Formato" }); image.Children.Add(_format);
        image.Children.Add(new TextBlock { Text = "Tamanho em píxeis" }); image.Children.Add(_size);
        var qualityLabel = new TextBlock { Text = "Qualidade: 85%" };
        image.Children.Add(qualityLabel); image.Children.Add(_quality);
        _quality.ValueChanged += (_, _) => qualityLabel.Text = $"Qualidade: {_quality.Value:0}%";
        _format.SelectionChanged += (_, _) =>
        {
            var jpeg = _format.SelectedItem as string == "JPEG";
            _quality.IsEnabled = _format.SelectedItem as string != "PNG";
            if (jpeg) _transparent.IsChecked = false;
            _transparent.IsEnabled = !jpeg;
        };
        image.Children.Add(new TextBlock { Text = "Cor de fundo (#RRGGBB ou #AARRGGBB)", Margin = new Thickness(0, 12, 0, 0) });
        image.Children.Add(_background); image.Children.Add(_transparent); image.Children.Add(_ped);
        _transparent.Checked += (_, _) => _background.IsEnabled = false;
        _transparent.Unchecked += (_, _) => _background.IsEnabled = true;
        outer.Children.Add(new TextBlock { Text = "Se já existirem fotografias na pasta:", Margin = new Thickness(0, 0, 0, 4) });
        outer.Children.Add(_existing);
        outer.Children.Add(new TextBlock { Text = "A pasta escolhida é usada diretamente. O UUID da peça e da textura identifica cada fotografia, mesmo noutro formato. Substituir troca a imagem anterior sem guardar cópias.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 12) });
        var export = new Button { Content = "Escolher pasta e exportar", HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 190 };
        export.Click += (_, _) =>
        {
            if (!project.Items.Any(IncludesItem) && !project.Tattoos.Any(IncludesTattoo) && !project.AppearanceAssets.Any(IncludesAppearance))
            {
                MessageBox.Show(this, "Selecione pelo menos um tipo de conteúdo e um género.", "Exportar fotografias");
                return;
            }
            try
            {
                PhotoBackground = _transparent.IsChecked == true ? Colors.Transparent : (Color)ColorConverter.ConvertFromString(_background.Text.Trim())!;
                ImageOptions = new PhotoImageOptions((int)_size.SelectedItem, (string)_format.SelectedItem, (int)_quality.Value);
                if (ImageOptions.Format == "JPEG") PhotoBackground = Color.FromRgb(PhotoBackground.R, PhotoBackground.G, PhotoBackground.B);
                DialogResult = true;
            }
            catch { MessageBox.Show(this, "Indique uma cor hexadecimal válida, como #00FF00.", "Cor inválida"); }
        };
        outer.Children.Add(export);
    }

    private static void AddOption(Panel panel, Dictionary<string, CheckBox> options, string key, string label)
    {
        var box = new CheckBox { Content = label, IsChecked = true, Margin = new Thickness(0, 4, 0, 4) };
        options.Add(key, box); panel.Children.Add(box);
    }
}
