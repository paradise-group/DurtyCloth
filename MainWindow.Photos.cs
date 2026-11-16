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
        try
        {
            var serverCatalog = RunWithLoading("Fotografias do servidor", "A obter as peças disponíveis no FiveM...",
                _ => FiveMPhotoCatalog.ReadAsync().GetAwaiter().GetResult());
            var gameFolder = GamePedAssets.FindGameFolder();
            if (gameFolder is null)
            {
                var picker = new OpenFolderDialog { Title = "Selecione a pasta do GTA V instalado" };
                if (picker.ShowDialog(this) != true) return;
                gameFolder = picker.FolderName;
            }
            var catalog = RunWithLoading("Fotografias do GTA V", "A localizar roupas e acessórios do GTA instalado...",
                progress => GtaPhotoCatalog.Scan(gameFolder, progress: progress));
            var filtered = serverCatalog.Filter(catalog.Project, firstTextureOnly: false);
            Status($"Catálogo do servidor: {filtered.MatchedItems} peças e {filtered.Textures} texturas presentes no GTA; {filtered.UnmatchedItems} peças sem origem nesta instalação.");
            if (catalog.Warnings.Count > 0)
                MessageBox.Show(this, $"Foram encontrados {catalog.Warnings.Count} avisos ao ler os arquivos do GTA. A exportação inclui apenas os arquivos que puderam ser lidos.",
                    "Fotografias do GTA V", MessageBoxButton.OK, MessageBoxImage.Warning);
            var combined = new ClothProject
            {
                Name = _project.Name, Items = _project.Items.ToList(), Tattoos = _project.Tattoos.ToList(),
                AppearanceAssets = _project.AppearanceAssets.ToList()
            };
            var projectIds = combined.Items.Select(item => item.Id).ToHashSet();
            foreach (var item in catalog.Project.Items.Where(item => !projectIds.Contains(item.Id)))
                combined.Items.Add(new ClothItem
                {
                    Id = item.Id, Name = item.Name, Gender = item.Gender, Category = item.Category, SourceIndex = item.SourceIndex,
                    ModelPath = "gta://" + item.ModelPath, OriginalModelFileName = item.OriginalModelFileName,
                    TexturePaths = item.TexturePaths.Select(path => "gta://" + path).ToList(), TextureIds = item.TextureIds.ToList(),
                    OriginalTextureFileNames = item.OriginalTextureFileNames.ToList()
                });
            string ResolvePhotoAsset(string path) => path.StartsWith("gta://", StringComparison.Ordinal)
                ? catalog.Resolve(path[6..]) : AbsoluteAssetPath(path);
            await ExportPreviewPhotosAsync(combined, ResolvePhotoAsset, gameFolder, catalog.WriteIdentityMap);
        }
        catch (Exception error)
        {
            if (_project.Items.Count + _project.Tattoos.Count + _project.AppearanceAssets.Count == 0)
                MessageBox.Show(this, error.Message, "Fotografias do servidor", MessageBoxButton.OK, MessageBoxImage.Warning);
            else if (MessageBox.Show(this, error.Message + "\n\nContinuar apenas com as fotografias do projeto carregado?",
                "GTA do servidor indisponível", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                await ExportPreviewPhotosAsync(_project, AbsoluteAssetPath);
        }
    }

    private async Task ExportPreviewPhotosAsync(ClothProject project, Func<string, string> resolveAsset,
        string? sourceGameFolder = null, Action<string>? prepareOutput = null)
    {
        var items = project.Items.Where(item => !item.IsDummy).ToArray();
        var tattoos = project.Tattoos.ToArray();
        var appearances = project.AppearanceAssets.ToArray();
        if (items.Length == 0 && tattoos.Length == 0 && appearances.Length == 0)
        {
            MessageBox.Show(this, "O projeto não contém peças nem tatuagens para exportar.", "Exportar fotografias");
            return;
        }
        var options = new PhotoExportOptionsWindow(project, sourceGameFolder is null ? null :
            "Projeto carregado + peças do GTA disponíveis no servidor. Use a pasta consolidada e ignore UUIDs existentes para fotografar só o que falta.") { Owner = this };
        if (options.ShowDialog() != true) return;
        items = items.Where(options.IncludesItem).ToArray();
        tattoos = tattoos.Where(options.IncludesTattoo).ToArray();
        appearances = appearances.Where(options.IncludesAppearance).ToArray();
        var picker = new OpenFolderDialog { Title = "Selecione a pasta das fotografias" };
        if (picker.ShowDialog(this) != true) return;

        try
        {
        foreach (var item in items) ClothingIdentityMap.EnsureTextures(item);
        var output = picker.FolderName;
        var settings = new PhotoExportSettings(options.ImageOptions, options.PhotoBackground.ToString(), options.IncludePed, options.ExportAllTextures);
        var imageOptions = settings.image;
        var photoBackground = options.PhotoBackground;
        var includePed = settings.includePed;
        var session = new PhotoExportSession(output, options.OverwriteExisting);
        var total = items.Sum(item => options.ExportVariants(item).Length) +
            tattoos.Sum(tattoo => options.ExportGenders(tattoo.Gender).Length) +
            appearances.Sum(appearance => options.ExportGenders(appearance.Gender).Length);
        var progress = new PhotoExportProgressWindow(total) { Owner = this };
        var reused = 0; var written = 0; var failed = 0; var unplaced = 0; var completed = 0;
        var pedCache = new Dictionary<string, PhotoPed>(StringComparer.OrdinalIgnoreCase);
        var configuredGameFolder = sourceGameFolder ?? GamePedAssets.FindGameFolder();
        Directory.CreateDirectory(output);
        prepareOutput?.Invoke(output);
        var settingsPath = Path.Combine(output, "export-settings.json");
        var settingsJson = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(settingsPath, settingsJson);
        using var index = session.OpenIndex();
        progress.Show();
        IsEnabled = false;
        try
        {
            foreach (var item in items)
            {
                if (progress.CancelRequested) break;
                var variants = options.ExportVariants(item);
                string DrawablePath(int variant) => Path.Combine(output, SafePhotoName(item.Gender), SafePhotoName(item.Category),
                    item.Id.ToString("D") + "." + (variant < 0 ? "sem_textura" : item.TextureIds[variant].ToString("D")) + imageOptions.Extension);
                var existing = new Dictionary<int, string>();
                if (!options.OverwriteExisting)
                    foreach (var variant in variants)
                        if (session.FindExisting(DrawablePath(variant)) is string saved) existing[variant] = saved;
                if (progress.CancelRequested) break;
                ModelPreviewData? model = null;
                string? modelError = null;
                try { if (existing.Count != variants.Length) model = await Task.Run(() => ModelPreview.Read(resolveAsset(item.ModelPath), false, preserveAllTriangles: true)); }
                catch (Exception ex) { modelError = ex.Message; }
                var slot = item.Category.Contains('[') ? item.Category.Split('[').Last().TrimEnd(']') : "";
                var needsPed = slot is "head" or "hair" or "berd" or "p_eyes" or "p_ears" or "p_mouth";
                var gameFolder = needsPed || includePed ? configuredGameFolder : null;
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
                foreach (var variant in variants)
                {
                    if (progress.CancelRequested) break;
                    var category = SafePhotoName(item.Category);
                    var gender = SafePhotoName(item.Gender);
                    var folder = Path.Combine(output, gender, category);
                    var textureId = variant < 0 ? (Guid?)null : item.TextureIds[variant];
                    var name = item.Id.ToString("D") + "." + (textureId?.ToString("D") ?? "sem_textura") + imageOptions.Extension;
                    var path = Path.Combine(folder, name);
                    try
                    {
                        if (!existing.ContainsKey(variant))
                        {
                            if (model is null) throw new InvalidDataException(modelError ?? "Modelo indisponível.");
                            if (needsPed && ped is null) throw new InvalidDataException(pedError ?? "Ped base indisponível.");
                            var images = new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);
                            if (variant >= 0 && !string.IsNullOrWhiteSpace(item.TexturePaths[variant]))
                                images = await Task.Run(() => ReadMatchingTextures(resolveAsset(item.TexturePaths[variant]), model));
                            Directory.CreateDirectory(folder);
                            path = session.SaveImage(path, temporary => PreviewPhotoRenderer.Save(temporary, model, images, ped, item.Category, photoBackground, imageOptions));
                        }
                        else { path = existing[variant]; reused++; }
                        if (options.OverwriteExisting || session.NeedsIndexEntry(path))
                            await index.WriteLineAsync(JsonSerializer.Serialize(new { kind = "drawable", status = "ok", item = item.Name,
                                itemId = item.Id, textureId,
                                item.Gender, item.Category, item.SourceIndex, variant, file = Path.GetRelativePath(output, path),
                                model = item.ModelPath, texture = variant < 0 ? null : item.TexturePaths[variant],
                                renderMode = ped is not null || (existing.ContainsKey(variant) && gameFolder is not null) ? "base-ped" : "isolated-model" }));
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
                foreach (var tattooGender in options.ExportGenders(tattoo.Gender))
                {
                    if (progress.CancelRequested) break;
                    if (string.IsNullOrEmpty(tattoo.Zone))
                    {
                        try
                        {
                            var folder = Path.Combine(output, "tattoos", "zona_por_definir", tattooGender);
                            var legacyPath = Path.Combine(folder, SafePhotoName(tattoo.Name) + "_" + tattoo.Id.ToString("N")[..8] + imageOptions.Extension);
                            var path = Path.Combine(folder, tattoo.Id.ToString("D") + imageOptions.Extension);
                            var savedPath = options.OverwriteExisting ? null : session.FindExisting(path, legacyPath);
                            if (savedPath is null)
                            {
                                if (configuredGameFolder is null) throw new InvalidDataException("Configure a pasta do GTA V para fotografar tatuagens com o ped.");
                                if (!pedCache.TryGetValue(tattooGender, out var unplacedPed))
                                {
                                    unplacedPed = await Task.Run(() => PreviewPhotoRenderer.LoadPed(configuredGameFolder, tattooGender));
                                    pedCache[tattooGender] = unplacedPed;
                                }
                                var texture = await Task.Run(() => TattooExporter.ReadImage(tattoo, resolveAsset(tattoo.ImagePath)));
                                var image = DecodeTextureBitmap(texture, 1024);
                                Directory.CreateDirectory(folder);
                                path = session.SaveImage(path, temporary => PreviewPhotoRenderer.SaveUnplacedTattoo(temporary, image, unplacedPed, photoBackground, imageOptions), legacyPath);
                            }
                            else { path = savedPath; reused++; }
                            if (options.OverwriteExisting || session.NeedsIndexEntry(path))
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
                        var folder = Path.Combine(output, "tattoos", SafePhotoName(tattooGender), SafePhotoName(tattoo.Zone));
                        var legacyPath = Path.Combine(folder, SafePhotoName(tattoo.Name) + "_" + tattoo.Id.ToString("N")[..8] + imageOptions.Extension);
                        var path = Path.Combine(folder, tattoo.Id.ToString("D") + imageOptions.Extension);
                        var savedPath = options.OverwriteExisting ? null : session.FindExisting(path, legacyPath);
                        if (savedPath is null)
                        {
                            TattooExporter.Validate(tattoo);
                            if (configuredGameFolder is null) throw new InvalidDataException("Configure a pasta do GTA V para fotografar tatuagens no ped.");
                            if (!pedCache.TryGetValue(tattooGender, out var tattooPed))
                            {
                                tattooPed = await Task.Run(() => PreviewPhotoRenderer.LoadPed(configuredGameFolder, tattooGender));
                                pedCache[tattooGender] = tattooPed;
                            }
                            var texture = await Task.Run(() => TattooExporter.ReadImage(tattoo, resolveAsset(tattoo.ImagePath)));
                            var image = DecodeTextureBitmap(texture, 1024);
                            Directory.CreateDirectory(folder);
                            path = session.SaveImage(path, temporary => PreviewPhotoRenderer.SaveTattoo(temporary, tattoo, image, tattooPed, photoBackground, imageOptions), legacyPath);
                        }
                        else { path = savedPath; reused++; }
                        if (options.OverwriteExisting || session.NeedsIndexEntry(path))
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
                foreach (var appearanceGender in options.ExportGenders(appearance.Gender))
                {
                    if (progress.CancelRequested) break;
                    try
                    {
                        var ytd = await Task.Run(() =>
                        {
                            var loaded = new YtdFile(); loaded.Load(File.ReadAllBytes(resolveAsset(appearance.TexturePath)));
                            return loaded;
                        });
                        var textures = (ytd.TextureDict?.Textures?.data_items ?? []).Where(texture => texture is not null).ToArray();
                        if (textures.Length == 0) throw new InvalidDataException("YTD sem texturas internas.");
                        for (var textureIndex = 0; textureIndex < textures.Length; textureIndex++)
                        {
                            if (progress.CancelRequested) break;
                            var texture = textures[textureIndex];
                            try
                            {
                                var folder = Path.Combine(output, "aparencia", SafePhotoName(appearance.Kind), appearanceGender);
                                Directory.CreateDirectory(folder);
                                var legacyPath = Path.Combine(folder, SafePhotoName(appearance.Name) + "_" + appearance.Id.ToString("N")[..8] +
                                    "_" + textureIndex.ToString("D2") + imageOptions.Extension);
                                var path = Path.Combine(folder, appearance.Id.ToString("D") + "." + textureIndex.ToString("D2") + imageOptions.Extension);
                                var savedPath = options.OverwriteExisting ? null : session.FindExisting(path, legacyPath);
                                if (savedPath is null)
                                {
                                    if (configuredGameFolder is null) throw new InvalidDataException("Configure a pasta do GTA V para fotografar esta textura de aparência.");
                                    if (!pedCache.TryGetValue(appearanceGender, out var appearancePed))
                                    {
                                        appearancePed = await Task.Run(() => PreviewPhotoRenderer.LoadPed(configuredGameFolder, appearanceGender));
                                        pedCache[appearanceGender] = appearancePed;
                                    }
                                    var image = DecodeTextureBitmap(texture, 1024);
                                    var overlay = new TattooItem
                                    {
                                        Zone = appearance.Kind == "Pelos corporais" ? "ZONE_TORSO" : "ZONE_HEAD",
                                        ScaleX = appearance.Kind == "Olhos" ? 0.22f : 0.45f,
                                        ScaleY = appearance.Kind == "Olhos" ? 0.16f : 0.35f
                                    };
                                    path = session.SaveImage(path, temporary => PreviewPhotoRenderer.SaveTattoo(temporary, overlay, image, appearancePed, photoBackground, imageOptions), legacyPath);
                                }
                                else { path = savedPath; reused++; }
                                if (options.OverwriteExisting || session.NeedsIndexEntry(path))
                                    await index.WriteLineAsync(JsonSerializer.Serialize(new { kind = "appearance", status = "ok", appearance.Name,
                                        appearanceKind = appearance.Kind, gender = appearanceGender, texture = texture.Name, file = Path.GetRelativePath(output, path),
                                        renderMode = "base-ped-approximate-overlay" }));
                                written++;
                            }
                            catch (Exception ex)
                            {
                                failed++;
                                await index.WriteLineAsync(JsonSerializer.Serialize(new { kind = "appearance", status = "error", appearance.Name,
                                    appearanceKind = appearance.Kind, gender = appearanceGender, texture = texture.Name, message = ex.Message }));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        await index.WriteLineAsync(JsonSerializer.Serialize(new { kind = "appearance", status = "error", appearance.Name,
                            appearanceKind = appearance.Kind, gender = appearanceGender, message = ex.Message }));
                    }
                    progress.Report(++completed, appearance.Name + " / " + appearanceGender);
                    await Task.Yield();
                }
            }
            await index.FlushAsync();
            File.WriteAllText(Path.Combine(output, "LEIA-ME.txt"),
                "Fotografias da prévia offline do Paradise Cloth Studio.\n" +
                (options.ExportAllTextures ? "Modo: todas as texturas de cada peça.\n" : "Modo: só a primeira textura disponível de cada peça.\n") +
                "As tatuagens do projeto são mostradas no ped por uma projeção aproximada; a posição exata no GTA pode diferir.\n" +
                "Tatuagens sem zona mostram a textura ao lado do ped, sem aplicação na pele.\n" +
                "Olhos, pelos, maquilhagem e outras texturas de aparência também usam projeção aproximada no ped.\n" +
                "Cores dos olhos, tintas de cabelo/pelos, overlays e traços faciais configurados por natives do GTA não são reproduzidos nesta prévia.\n" +
                "Consulte index.jsonl para a origem e o estado de cada imagem.\n", new UTF8Encoding(false));
            var summary = $"{written - reused} imagens novas; {reused} imagens aproveitadas; {unplaced} tatuagens com zona desconhecida; {failed} falhas" +
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
