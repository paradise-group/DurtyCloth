using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using CodeWalker.GameFiles;

namespace FooxClothStudio;

public partial class MainWindow
{
    private async void ExportPreviewPhotos_Click(object sender, RoutedEventArgs e)
    {
        var items = _project.Items.Where(item => !item.IsDummy).ToArray();
        foreach (var item in items) ClothingIdentityMap.EnsureTextures(item);
        var tattoos = _project.Tattoos.ToArray();
        var appearances = _project.AppearanceAssets.ToArray();
        if (items.Length == 0 && tattoos.Length == 0 && appearances.Length == 0)
        {
            MessageBox.Show(this, "O projeto não contém peças nem tatuagens para exportar.", "Exportar fotografias");
            return;
        }
        var options = new PhotoExportOptionsWindow { Owner = this };
        if (options.ShowDialog() != true) return;
        var picker = new OpenFolderDialog { Title = "Selecione a pasta onde guardar as fotografias" };
        if (picker.ShowDialog(this) != true) return;

        try
        {
        var projectName = SafePhotoName(_project.Name);
        var output = Path.Combine(picker.FolderName, projectName + "_photos_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));
        var total = items.Sum(item => Math.Max(1, item.TexturePaths.Count)) +
            tattoos.Sum(tattoo => tattoo.Gender == "Both" ? 2 : 1) +
            appearances.Sum(appearance => appearance.Gender == "Both" ? 2 : 1);
        var progress = new PhotoExportProgressWindow(total) { Owner = this };
        var written = 0; var failed = 0; var unplaced = 0; var completed = 0;
        var pedCache = new Dictionary<string, PhotoPed>(StringComparer.OrdinalIgnoreCase);
        var configuredGameFolder = GamePedAssets.FindGameFolder();
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "export-settings.json"), JsonSerializer.Serialize(new {
            image = options.ImageOptions, background = options.PhotoBackground.ToString(), includePed = options.IncludePed
        }, JsonOptions));
        using var index = new StreamWriter(Path.Combine(output, "index.jsonl"), false, new UTF8Encoding(false));
        progress.Show();
        IsEnabled = false;
        try
        {
            foreach (var item in items)
            {
                if (progress.CancelRequested) break;
                ModelPreviewData? model = null;
                string? modelError = null;
                try { model = await Task.Run(() => ModelPreview.Read(AbsoluteAssetPath(item.ModelPath), false)); }
                catch (Exception ex) { modelError = ex.Message; }
                var slot = item.Category.Contains('[') ? item.Category.Split('[').Last().TrimEnd(']') : "";
                var needsPed = slot is "head" or "hair" or "berd" or "p_eyes" or "p_ears" or "p_mouth";
                var gameFolder = needsPed || options.IncludePed ? configuredGameFolder : null;
                PhotoPed? ped = null;
                string? pedError = null;
                if (needsPed && gameFolder is null) pedError = "Configure a pasta do GTA V na janela de prévia para fotografar esta categoria com ped.";
                if (model is not null && gameFolder is not null && item.Gender is "Masculino" or "Feminino")
                {
                    if (!pedCache.TryGetValue(item.Gender, out ped))
                    {
                        try
                        {
                            ped = await Task.Run(() => PreviewPhotoRenderer.LoadPed(gameFolder, item.Gender));
                            pedCache[item.Gender] = ped;
                        }
                        catch (Exception ex)
                        {
                            pedError = ex.Message;
                            await index.WriteLineAsync(JsonSerializer.Serialize(new { kind = "warning", message = "Ped base indisponível: " + ex.Message }));
                        }
                    }
                }
                var variants = item.TexturePaths.Count == 0 ? new[] { -1 } : Enumerable.Range(0, item.TexturePaths.Count).ToArray();
                foreach (var variant in variants)
                {
                    if (progress.CancelRequested) break;
                    var category = SafePhotoName(item.Category);
                    var gender = SafePhotoName(item.Gender);
                    var folder = Path.Combine(output, gender, category);
                    var textureId = variant < 0 ? (Guid?)null : item.TextureIds[variant];
                    var name = item.Id.ToString("D") + "." + (textureId?.ToString("D") ?? "sem_textura") + options.ImageOptions.Extension;
                    var path = Path.Combine(folder, name);
                    try
                    {
                        if (model is null) throw new InvalidDataException(modelError ?? "Modelo indisponível.");
                        if (needsPed && ped is null) throw new InvalidDataException(pedError ?? "Ped base indisponível.");
                        var images = new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);
                        if (variant >= 0 && !string.IsNullOrWhiteSpace(item.TexturePaths[variant]))
                            images = await Task.Run(() => ReadMatchingTextures(AbsoluteAssetPath(item.TexturePaths[variant]), model));
                        Directory.CreateDirectory(folder);
                        PreviewPhotoRenderer.Save(path, model, images, ped, item.Category, options.PhotoBackground, options.ImageOptions);
                        await index.WriteLineAsync(JsonSerializer.Serialize(new { kind = "drawable", status = "ok", item = item.Name,
                            itemId = item.Id, textureId,
                            item.Gender, item.Category, item.SourceIndex, variant, file = Path.GetRelativePath(output, path),
                            model = item.ModelPath, texture = variant < 0 ? null : item.TexturePaths[variant],
                            renderMode = ped is null ? "isolated-model" : "base-ped" }));
                        written++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        await index.WriteLineAsync(JsonSerializer.Serialize(new { kind = "drawable", status = "error", item = item.Name,
                            item.Gender, item.Category, variant, message = ex.Message }));
                    }
                    progress.Report(++completed, item.DisplayName);
                    await Task.Yield();
                }
            }
            foreach (var tattoo in tattoos)
            {
                if (progress.CancelRequested) break;
                foreach (var tattooGender in tattoo.Gender == "Both" ? new[] { "Masculino", "Feminino" } :
                    new[] { tattoo.Gender == "Male" ? "Masculino" : "Feminino" })
                {
                    if (progress.CancelRequested) break;
                    if (string.IsNullOrEmpty(tattoo.Zone))
                    {
                        try
                        {
                            if (configuredGameFolder is null) throw new InvalidDataException("Configure a pasta do GTA V para fotografar tatuagens com o ped.");
                            if (!pedCache.TryGetValue(tattooGender, out var unplacedPed))
                            {
                                unplacedPed = await Task.Run(() => PreviewPhotoRenderer.LoadPed(configuredGameFolder, tattooGender));
                                pedCache[tattooGender] = unplacedPed;
                            }
                            var texture = await Task.Run(() => TattooExporter.ReadImage(tattoo, AbsoluteAssetPath(tattoo.ImagePath)));
                            var image = DecodeTextureBitmap(texture, 1024);
                            var folder = Path.Combine(output, "tattoos", "zona_por_definir", tattooGender);
                            Directory.CreateDirectory(folder);
                            var path = Path.Combine(folder, SafePhotoName(tattoo.Name) + "_" + tattoo.Id.ToString("N")[..8] + options.ImageOptions.Extension);
                            PreviewPhotoRenderer.SaveUnplacedTattoo(path, image, unplacedPed, options.PhotoBackground, options.ImageOptions);
                            await index.WriteLineAsync(JsonSerializer.Serialize(new { kind = "tattoo", status = "ok", tattoo.Name,
                                gender = tattooGender, file = Path.GetRelativePath(output, path), renderMode = "base-ped-source-swatch",
                                message = "Zona desconhecida; textura mostrada ao lado do ped." }));
                            written++; unplaced++;
                        }
                        catch (Exception ex)
                        {
                            failed++;
                            await index.WriteLineAsync(JsonSerializer.Serialize(new { kind = "tattoo", status = "error", tattoo.Name,
                                gender = tattooGender, message = ex.Message }));
                        }
                        progress.Report(++completed, tattoo.Name + " / zona por definir");
                        await Task.Yield();
                        continue;
                    }
                    try
                    {
                        TattooExporter.Validate(tattoo);
                        if (configuredGameFolder is null) throw new InvalidDataException("Configure a pasta do GTA V para fotografar tatuagens no ped.");
                        if (!pedCache.TryGetValue(tattooGender, out var tattooPed))
                        {
                            tattooPed = await Task.Run(() => PreviewPhotoRenderer.LoadPed(configuredGameFolder, tattooGender));
                            pedCache[tattooGender] = tattooPed;
                        }
                        var texture = await Task.Run(() => TattooExporter.ReadImage(tattoo, AbsoluteAssetPath(tattoo.ImagePath)));
                        var image = DecodeTextureBitmap(texture, 1024);
                        var folder = Path.Combine(output, "tattoos", SafePhotoName(tattooGender), SafePhotoName(tattoo.Zone));
                        Directory.CreateDirectory(folder);
                        var path = Path.Combine(folder, SafePhotoName(tattoo.Name) + "_" + tattoo.Id.ToString("N")[..8] + options.ImageOptions.Extension);
                        PreviewPhotoRenderer.SaveTattoo(path, tattoo, image, tattooPed, options.PhotoBackground, options.ImageOptions);
                        await index.WriteLineAsync(JsonSerializer.Serialize(new { kind = "tattoo", status = "ok", tattoo.Name,
                            tattoo.Zone, gender = tattooGender, file = Path.GetRelativePath(output, path), renderMode = "base-ped-approximate-decal" }));
                        written++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        await index.WriteLineAsync(JsonSerializer.Serialize(new { kind = "tattoo", status = "error", tattoo.Name,
                            gender = tattooGender, message = ex.Message }));
                    }
                    progress.Report(++completed, tattoo.Name + " / " + tattooGender);
                    await Task.Yield();
                }
            }
            foreach (var appearance in appearances)
            {
                if (progress.CancelRequested) break;
                foreach (var appearanceGender in appearance.Gender == "Both" ? new[] { "Masculino", "Feminino" } :
                    new[] { appearance.Gender == "Male" ? "Masculino" : "Feminino" })
                {
                    if (progress.CancelRequested) break;
                    try
                    {
                        if (configuredGameFolder is null) throw new InvalidDataException("Configure a pasta do GTA V para fotografar esta textura de aparência.");
                        if (!pedCache.TryGetValue(appearanceGender, out var appearancePed))
                        {
                            appearancePed = await Task.Run(() => PreviewPhotoRenderer.LoadPed(configuredGameFolder, appearanceGender));
                            pedCache[appearanceGender] = appearancePed;
                        }
                        var ytd = await Task.Run(() =>
                        {
                            var loaded = new YtdFile(); loaded.Load(File.ReadAllBytes(AbsoluteAssetPath(appearance.TexturePath)));
                            return loaded;
                        });
                        var textures = (ytd.TextureDict?.Textures?.data_items ?? []).Where(texture => texture is not null).ToArray();
                        if (textures.Length == 0) throw new InvalidDataException("YTD sem texturas internas.");
                        for (var textureIndex = 0; textureIndex < textures.Length; textureIndex++)
                        {
                            var texture = textures[textureIndex];
                            try
                            {
                                var image = DecodeTextureBitmap(texture, 1024);
                                var folder = Path.Combine(output, "aparencia", SafePhotoName(appearance.Kind), appearanceGender);
                                Directory.CreateDirectory(folder);
                                var path = Path.Combine(folder, SafePhotoName(appearance.Name) + "_" + appearance.Id.ToString("N")[..8] +
                                    "_" + textureIndex.ToString("D2") + options.ImageOptions.Extension);
                                var overlay = new TattooItem
                                {
                                    Zone = appearance.Kind == "Pelos corporais" ? "ZONE_TORSO" : "ZONE_HEAD",
                                    ScaleX = appearance.Kind == "Olhos" ? 0.22f : 0.45f,
                                    ScaleY = appearance.Kind == "Olhos" ? 0.16f : 0.35f
                                };
                                PreviewPhotoRenderer.SaveTattoo(path, overlay, image, appearancePed, options.PhotoBackground, options.ImageOptions);
                                await index.WriteLineAsync(JsonSerializer.Serialize(new { kind = "appearance", status = "ok", appearance.Name,
                                    appearance.Kind, gender = appearanceGender, texture = texture.Name, file = Path.GetRelativePath(output, path),
                                    renderMode = "base-ped-approximate-overlay" }));
                                written++;
                            }
                            catch (Exception ex)
                            {
                                failed++;
                                await index.WriteLineAsync(JsonSerializer.Serialize(new { kind = "appearance", status = "error", appearance.Name,
                                    appearance.Kind, gender = appearanceGender, texture = texture.Name, message = ex.Message }));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        await index.WriteLineAsync(JsonSerializer.Serialize(new { kind = "appearance", status = "error", appearance.Name,
                            appearance.Kind, gender = appearanceGender, message = ex.Message }));
                    }
                    progress.Report(++completed, appearance.Name + " / " + appearanceGender);
                    await Task.Yield();
                }
            }
            await index.FlushAsync();
            File.WriteAllText(Path.Combine(output, "LEIA-ME.txt"),
                "Fotografias da prévia offline do Paradise Cloth Studio.\n" +
                "Cada drawable tem uma imagem por variante YTD. O enquadramento usa a geometria da peça.\n" +
                "As tatuagens do projeto são mostradas no ped por uma projeção aproximada; a posição exata no GTA pode diferir.\n" +
                "Tatuagens sem zona mostram a textura ao lado do ped, sem aplicação na pele.\n" +
                "Olhos, pelos, maquilhagem e outras texturas de aparência também usam projeção aproximada no ped.\n" +
                "Cores dos olhos, tintas de cabelo/pelos, overlays e traços faciais configurados por natives do GTA não são reproduzidos nesta prévia.\n" +
                "Consulte index.jsonl para a origem e o estado de cada imagem.\n", new UTF8Encoding(false));
            var summary = $"{written} imagens exportadas; {unplaced} tatuagens com zona desconhecida; {failed} falhas" +
                (progress.CancelRequested ? "; exportação cancelada" : "") + ".\n\n" + output;
            Status(summary.Replace("\n", " "));
            MessageBox.Show(this, summary, "Exportar fotografias", MessageBoxButton.OK,
                failed == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        finally
        {
            IsEnabled = true;
            progress.Complete();
        }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Exportação de fotografias interrompida",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string SafePhotoName(string name)
    {
        var cleaned = new string(name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) || ch is '[' or ']' ? '_' : ch).ToArray()).Trim(' ', '.');
        return cleaned.Length == 0 ? "item" : cleaned.Length > 90 ? cleaned[..90] : cleaned;
    }
}

internal sealed class PhotoExportOptionsWindow : Window
{
    private readonly TextBox _background = new() { Text = "#00FF00", Width = 120, Margin = new Thickness(0, 5, 0, 12) };
    private readonly CheckBox _transparent = new() { Content = "Sem fundo (PNG transparente)", Margin = new Thickness(0, 0, 0, 8) };
    private readonly ComboBox _format = new() { ItemsSource = new[] { "PNG", "JPEG" }, SelectedIndex = 0, Margin = new Thickness(0, 4, 0, 10) };
    private readonly ComboBox _size = new() { ItemsSource = new[] { 256, 384, 512, 768 }, SelectedItem = 768, Margin = new Thickness(0, 4, 0, 10) };
    private readonly Slider _quality = new() { Minimum = 40, Maximum = 100, Value = 85, TickFrequency = 1, IsSnapToTickEnabled = true, IsEnabled = false };
    private readonly CheckBox _ped = new() { Content = "Mostrar ped base (se GTA V estiver configurado)", IsChecked = false };
    public Color PhotoBackground { get; private set; } = Colors.Lime;
    public bool IncludePed => _ped.IsChecked == true;
    public PhotoImageOptions ImageOptions { get; private set; } = new();
    public PhotoExportOptionsWindow()
    {
        WorkflowUi.Theme(this, "Exportar fotografias", 470, 310);
        MinHeight = 0;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = WorkflowUi.Stack(); Content = panel;
        panel.Children.Add(new TextBlock { Text = "Formato: PNG preserva transparência; JPEG comprime com perda." });
        panel.Children.Add(_format);
        panel.Children.Add(new TextBlock { Text = "Tamanho em píxeis (512 recomendado para miniaturas):" });
        panel.Children.Add(_size);
        var qualityLabel = new TextBlock { Text = "Qualidade JPEG: 85%", Margin = new Thickness(0, 0, 0, 4) };
        panel.Children.Add(qualityLabel);
        panel.Children.Add(_quality);
        _quality.ValueChanged += (_, _) => qualityLabel.Text = $"Qualidade JPEG: {_quality.Value:0}%";
        _format.SelectionChanged += (_, _) =>
        {
            var jpeg = _format.SelectedItem as string == "JPEG";
            _quality.IsEnabled = jpeg;
            if (jpeg) _transparent.IsChecked = false;
            _transparent.IsEnabled = !jpeg;
        };
        panel.Children.Add(new TextBlock { Text = "Cor de fundo (#RRGGBB ou #AARRGGBB). Verde: #00FF00; branco: #FFFFFF; preto: #000000.", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(_background);
        _transparent.Checked += (_, _) => _background.IsEnabled = false;
        _transparent.Unchecked += (_, _) => _background.IsEnabled = true;
        panel.Children.Add(_transparent);
        panel.Children.Add(_ped);
        panel.Children.Add(new TextBlock { Text = "Uma imagem por peça e textura. Tamanhos menores reduzem o peso dos ficheiros. JPEG usa fundo opaco. A saída será criada numa pasta nova.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) });
        var button = new Button { Content = "Continuar", Width = 110, HorizontalAlignment = HorizontalAlignment.Right };
        button.Click += (_, _) =>
        {
            try
            {
                PhotoBackground = _transparent.IsChecked == true
                    ? Colors.Transparent
                    : (Color)ColorConverter.ConvertFromString(_background.Text.Trim())!;
                ImageOptions = new PhotoImageOptions((int)_size.SelectedItem, (string)_format.SelectedItem, (int)_quality.Value);
                if (ImageOptions.Format == "JPEG") PhotoBackground = Color.FromRgb(PhotoBackground.R, PhotoBackground.G, PhotoBackground.B);
                DialogResult = true;
            }
            catch { MessageBox.Show(this, "Indique uma cor hexadecimal válida, como #00FF00.", "Cor inválida"); }
        };
        panel.Children.Add(button);
    }
}

internal sealed class PhotoExportProgressWindow : Window
{
    private readonly TextBlock _label = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _timing = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly ProgressEstimate _estimate = new();
    private readonly ProgressBar _bar = new() { Height = 12, Margin = new Thickness(0, 12, 0, 12) };
    private readonly int _total;
    private bool _complete;
    public bool CancelRequested { get; private set; }
    public PhotoExportProgressWindow(int total)
    {
        _total = total;
        WorkflowUi.Theme(this, "Exportando fotografias", 440, 170);
        MinHeight = 0;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = WorkflowUi.Stack(); Content = panel;
        panel.Children.Add(_label); panel.Children.Add(_timing); panel.Children.Add(_bar);
        _bar.Maximum = Math.Max(total, 1);
        var cancel = new Button { Content = "Cancelar", Width = 100, HorizontalAlignment = HorizontalAlignment.Right };
        cancel.Click += (_, _) => { CancelRequested = true; cancel.IsEnabled = false; _timing.Text = "Finishing the current image..."; };
        panel.Children.Add(cancel);
        Closing += (_, args) => { if (!_complete) { CancelRequested = true; args.Cancel = true; } };
        Report(0, "A preparar");
    }
    public void Report(int done, string item)
    {
        if (_complete) return;
        _label.Text = $"{done}/{_total}\n{item}";
        _timing.Text = CancelRequested ? "Finishing the current image..." : _estimate.Report(done, _total);
        _bar.IsIndeterminate = _total <= 0;
        _bar.Value = done;
    }
    public void Complete() { _complete = true; Close(); }
}
