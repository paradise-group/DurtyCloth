using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodeWalker.GameFiles;

namespace FooxClothStudio;

public partial class MainWindow
{
    private bool _duplicateReviewNeeded;
    private int _duplicateScanRequest, _duplicateImageRequest;
    private sealed record ReviewTexture(Guid ItemId, string ItemName, int Slot, string Path);
    private sealed record ReviewPair(ReviewTexture Old, ReviewTexture New)
    {
        public override string ToString() => $"{Old.ItemName} · existente {(char)('A' + Old.Slot)} / duplicada {(char)('A' + New.Slot)}";
    }
    private void DuplicateReview_Expanded(object sender, RoutedEventArgs e) => _ = RefreshDuplicateReviewAsync();
    private void RefreshDuplicateReview_Click(object sender, RoutedEventArgs e) => _ = RefreshDuplicateReviewAsync();

    private async Task RefreshDuplicateReviewAsync()
    {
        if (DuplicatePairList is null) return;
        var request = ++_duplicateScanRequest;
        ++_duplicateImageRequest;
        DuplicatePairList.ItemsSource = null; DuplicateComparison.Children.Clear();
        DuplicateReviewStatus.Text = "Comparando o conteúdo das texturas...";
        try
        {
            // Snapshot only metadata on the UI thread; hash/decode work stays off the dispatcher.
            var snapshot = _project.Items.Where(i => !i.IsDummy).Select(i =>
                i.TexturePaths.Select((p, slot) => string.IsNullOrWhiteSpace(p) ? null :
                    new ReviewTexture(i.Id, i.Name, slot, AbsoluteAssetPath(p))).OfType<ReviewTexture>().ToArray()).ToArray();
            var result = await Task.Run(() =>
            {
                var pairs = new List<ReviewPair>(); var errors = 0;
                foreach (var item in snapshot)
                {
                    var seen = new Dictionary<string, ReviewTexture>();
                    foreach (var texture in item)
                    {
                        try
                        {
                            var hash = TextureContent.Fingerprint(texture.Path);
                            if (seen.TryGetValue(hash, out var existing)) pairs.Add(new(existing, texture));
                            else seen[hash] = texture;
                        }
                        catch (Exception) { errors++; }
                    }
                }
                return (pairs, errors);
            });
            if (request != _duplicateScanRequest) return;
            DuplicateReviewStatus.Text = result.pairs.Count == 0 ? "Nenhuma variante duplicada na mesma peça." :
                $"{result.pairs.Count} comparação(ões). Confira existente acima e duplicada abaixo; remova a variante que preferir.";
            if (result.errors > 0) DuplicateReviewStatus.Text += $" {result.errors} arquivo(s) não puderam ser lidos.";
            DuplicatePairList.ItemsSource = result.pairs;
            if (result.pairs.Count > 0) DuplicatePairList.SelectedIndex = 0;
        }
        catch (Exception ex) { if (request == _duplicateScanRequest) DuplicateReviewStatus.Text = "Não foi possível comparar: " + ex.Message; }
    }

    private async void DuplicatePairList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var request = ++_duplicateImageRequest;
        DuplicateComparison.Children.Clear();
        if (DuplicatePairList.SelectedItem is not ReviewPair pair) return;
        var top = CreateComparisonCard(pair.Old, "Existente / antiga");
        var bottom = CreateComparisonCard(pair.New, "Duplicada / nova");
        DuplicateComparison.Children.Add(top.Card); DuplicateComparison.Children.Add(bottom.Card);
        async Task Load(ReviewTexture texture, WrapPanel images)
        {
            try
            {
                var previews = await Task.Run(() =>
                {
                    var ytd = new YtdFile(); ytd.Load(File.ReadAllBytes(texture.Path));
                    return (ytd.TextureDict?.Textures?.data_items ?? []).Select(t =>
                    {
                        try { return (Image: (BitmapSource?)DecodeTextureBitmap(t, 256), Label: $"{t.Name} · {t.Width}×{t.Height}"); }
                        catch (Exception ex) { return (Image: (BitmapSource?)null, Label: $"{t.Name}: {ex.Message}"); }
                    }).ToArray();
                });
                if (request != _duplicateImageRequest) return;
                foreach (var preview in previews)
                {
                    var tile = new StackPanel { Width = 180, Margin = new Thickness(0,0,8,0) };
                    if (preview.Image is not null) tile.Children.Add(new Border { Background = Brushes.DimGray,
                        Child = new Image { Source = preview.Image, Height = 90, Stretch = Stretch.Uniform } });
                    tile.Children.Add(new TextBlock { Text = preview.Label, TextWrapping = TextWrapping.Wrap, FontSize = 10 });
                    images.Children.Add(tile);
                }
            }
            catch (Exception ex) { if (request == _duplicateImageRequest) images.Children.Add(new TextBlock { Text = "Prévia indisponível: " + ex.Message }); }
        }
        await Task.WhenAll(Load(pair.Old, top.Images), Load(pair.New, bottom.Images));
    }

    private (Border Card, WrapPanel Images) CreateComparisonCard(ReviewTexture texture, string label)
    {
        var content = new StackPanel();
        var title = new DockPanel();
        var remove = new Button { Content = "Remover esta variante", Background = WorkflowUi.Accent,
            Margin = new Thickness(12,0,0,0), ToolTip = "Remove do projeto e preserva o arquivo de origem." };
        DockPanel.SetDock(remove, Dock.Right); title.Children.Add(remove);
        title.Children.Add(new TextBlock { Text = $"{label} · slot {(char)('A' + texture.Slot)} (posição {texture.Slot}) · {texture.ItemName}",
            FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(220,199,255)), TextWrapping = TextWrapping.Wrap });
        content.Children.Add(title);
        content.Children.Add(new TextBlock { Text = System.IO.Path.GetFileName(texture.Path), ToolTip = texture.Path, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,3,0,5) });
        var images = new WrapPanel(); content.Children.Add(images);
        remove.Click += (_, _) => RemoveReviewedTexture(texture);
        return (new Border { Child = content, Background = WorkflowUi.Panel, BorderBrush = new SolidColorBrush(Color.FromRgb(74,47,114)),
            BorderThickness = new Thickness(1), Padding = new Thickness(8), Margin = new Thickness(0,0,8,6) }, images);
    }

    private void RemoveReviewedTexture(ReviewTexture texture)
    {
        var item = _project.Items.FirstOrDefault(i => i.Id == texture.ItemId);
        if (item is null || texture.Slot >= item.TexturePaths.Count || string.IsNullOrWhiteSpace(item.TexturePaths[texture.Slot]) ||
            !string.Equals(AbsoluteAssetPath(item.TexturePaths[texture.Slot]), texture.Path, StringComparison.OrdinalIgnoreCase))
        { _ = RefreshDuplicateReviewAsync(); Status("A lista mudou; confira a comparação atualizada antes de remover."); return; }
        // Match the ordinary texture removal semantics, including reserved slots and aligned metadata.
        AlignTextureNames(item);
        var index = texture.Slot;
        if (_project.ReplaceWithDummyOnRemove)
        { item.TexturePaths[index] = ""; item.OriginalTextureFileNames[index] = "(dummy)"; item.TextureEffects[index] = new TextureEffect(); item.TextureLabels[index] = ""; }
        else
        { item.TexturePaths.RemoveAt(index); item.OriginalTextureFileNames.RemoveAt(index); item.TextureEffects.RemoveAt(index); item.TextureLabels.RemoveAt(index); }
        _dirty = true;
        if (ItemList.SelectedItem is ClothItem selected && selected.Id == item.Id) RefreshTextureList(item, Math.Min(index, item.TexturePaths.Count - 1));
        RefreshItems();
        Status($"Variante removida de {item.Name}; arquivo de origem preservado.");
    }
}
