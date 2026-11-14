using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace FooxClothStudio;

public partial class MainWindow
{
    private void SelectContextRow(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox list && e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(list, source) is ListBoxItem row)
            row.IsSelected = true;
    }
    private void ContextEntry(ContextMenu menu, string label, Action action, bool enabled = true)
    {
        var gesture = label switch
        {
            "Remove" => "Del", "Rename" => "F2", "Replace" => "Ctrl+H", "Replace with Dummy" => "Ctrl+J",
            "Show in Windows Explorer" => "Ctrl+E", "3D Preview Selected Cloth" => "Enter",
            "Copy Name" when menu == ItemList.ContextMenu => "Ctrl+Shift+C",
            "Copy Game Name" when menu == ItemList.ContextMenu => "Ctrl+Alt+C",
            "Copy File Path" when menu == ItemList.ContextMenu => "Ctrl+Shift+P", _ => ""
        };
        var entry = new MenuItem { Header = label, IsEnabled = enabled, InputGestureText = gesture };
        entry.Click += (_, _) =>
        {
            try { action(); }
            catch (Exception ex) { Status(ex.Message); MessageBox.Show(this, ex.Message, label, MessageBoxButton.OK, MessageBoxImage.Error); }
        };
        menu.Items.Add(entry);
    }
    private void ItemContextMenu_Opening(object sender, ContextMenuEventArgs e)
    {
        if (ItemList.SelectedItem is not ClothItem item) { e.Handled = true; return; }
        var menu = ItemList.ContextMenu; menu.Items.Clear();
        ContextEntry(menu, "Remove", () => Remove_Click(sender, new RoutedEventArgs()));
        ContextEntry(menu, "Replace", () => ReplaceModel_Click(sender, new RoutedEventArgs()));
        ContextEntry(menu, "Replace with Dummy", () => ReplaceSelectedWithDummy(), !item.IsDummy);
        ContextEntry(menu, "Create Copy for Other Gender", () =>
        {
            var copy = ClothEditActions.CopyForOtherGender(_project, item); _dirty = true;
            RefreshItems(); ItemList.SelectedItem = copy; Status("Created copy for " + (copy.Gender == "Masculino" ? "Male" : "Female") + ".");
        }, item.Gender is "Masculino" or "Feminino");
        menu.Items.Add(new Separator());
        ContextEntry(menu, "Show in Windows Explorer", () => ShowSourceInExplorer(item.ModelPath), !item.IsDummy);
        ContextEntry(menu, "Export as Paradise Package", () => ExportItemBundle_Click(sender, new RoutedEventArgs()));
        ContextEntry(menu, "Export", () => ExportSourceFile(item.ModelPath, item.OriginalModelFileName), !item.IsDummy);
        ContextEntry(menu, "Export with Textures", () => ExportItemFiles_Click(sender, new RoutedEventArgs()), !item.IsDummy);
        ContextEntry(menu, "Export XML", () => ExportAssetAnalysis(item.ModelPath, "XML"), !item.IsDummy);
        ContextEntry(menu, "Export Textures as DDS", () => ExportAssetAnalysis(item.ModelPath, "DDS"), !item.IsDummy);
        ContextEntry(menu, "Export Textures as PNG", () => ExportAssetAnalysis(item.ModelPath, "PNG"), !item.IsDummy);
        menu.Items.Add(new Separator());
        ContextEntry(menu, "Copy Name", () => Clipboard.SetText(item.Name));
        ContextEntry(menu, "Copy Permanent ID", () => Clipboard.SetText(item.Id.ToString("D")));
        ContextEntry(menu, "Copy Game Name", () => Clipboard.SetText(Path.GetFileNameWithoutExtension(item.OriginalModelFileName ?? item.ModelPath)), !item.IsDummy);
        ContextEntry(menu, "Copy File Path", () => Clipboard.SetText(AbsoluteAssetPath(item.ModelPath)), !item.IsDummy);
        ContextEntry(menu, "Rename", RenameSelectedItem);
        ContextEntry(menu, "3D Preview Selected Cloth", () => Preview_Click(sender, new RoutedEventArgs()), !item.IsDummy);
    }
    private void TextureContextMenu_Opening(object sender, ContextMenuEventArgs e)
    {
        if (ItemList.SelectedItem is not ClothItem item || TextureList.SelectedIndex < 0) { e.Handled = true; return; }
        var index = TextureList.SelectedIndex; var path = item.TexturePaths[index];
        ClothingIdentityMap.EnsureTextures(item);
        var menu = TextureList.ContextMenu; menu.Items.Clear();
        ContextEntry(menu, "Copy Permanent Texture ID", () => Clipboard.SetText(item.TextureIds[index].ToString("D")));
        ContextEntry(menu, "Remove", () => RemoveTexture_Click(sender, new RoutedEventArgs()));
        ContextEntry(menu, "Rename", RenameSelectedTexture);
        ContextEntry(menu, "Replace", () => ReplaceTexture_Click(sender, new RoutedEventArgs()));
        ContextEntry(menu, "Replace with Dummy", () =>
            ReplaceSelectedTextureWithDummy(), !string.IsNullOrWhiteSpace(path));
        menu.Items.Add(new Separator());
        ContextEntry(menu, "Show in Windows Explorer", () => ShowSourceInExplorer(path), !string.IsNullOrWhiteSpace(path));
        ContextEntry(menu, "Export", () => ExportSourceFile(path, item.OriginalTextureFileNames.ElementAtOrDefault(index)), !string.IsNullOrWhiteSpace(path));
        ContextEntry(menu, "Export XML", () => ExportAssetAnalysis(path, "XML"), !string.IsNullOrWhiteSpace(path));
        ContextEntry(menu, "Export DDS", () => ExportAssetAnalysis(path, "DDS"), !string.IsNullOrWhiteSpace(path));
        ContextEntry(menu, "Export PNG", () => ExportAssetAnalysis(path, "PNG"), !string.IsNullOrWhiteSpace(path));
        ContextEntry(menu, "Copy Letter", () => Clipboard.SetText(((char)('a' + index)).ToString()));
        ContextEntry(menu, "Copy Game Name", () => Clipboard.SetText(Path.GetFileNameWithoutExtension(item.OriginalTextureFileNames.ElementAtOrDefault(index) ?? path)), !string.IsNullOrWhiteSpace(path));
        ContextEntry(menu, "Copy Name", () => Clipboard.SetText(item.TextureLabels.ElementAtOrDefault(index) is { Length: > 0 } label ? label : item.OriginalTextureFileNames.ElementAtOrDefault(index) ?? Path.GetFileName(path)));
        ContextEntry(menu, "Copy File Path", () => Clipboard.SetText(AbsoluteAssetPath(path)), !string.IsNullOrWhiteSpace(path));
        ContextEntry(menu, "3D Preview Selected Cloth", () => Preview_Click(sender, new RoutedEventArgs()), !item.IsDummy);
    }
    private void ReplaceSelectedWithDummy()
    {
        if (ItemList.SelectedItem is not ClothItem item) return;
        ClothEditActions.ReplaceWithDummy(item); _dirty = true; RefreshItems(); ItemList.SelectedItem = item;
        Status("Drawable replaced with a reservation dummy.");
    }
    private void ReplaceSelectedTextureWithDummy()
    {
        if (ItemList.SelectedItem is not ClothItem item || TextureList.SelectedIndex < 0) return;
        var index = TextureList.SelectedIndex; AlignTextureNames(item);
        item.TexturePaths[index] = ""; item.OriginalTextureFileNames[index] = "(dummy)";
        item.TextureEffects[index] = new TextureEffect(); item.TextureLabels[index] = "";
        _dirty = true; RefreshTextureList(item, index); RefreshItems();
    }
    private bool TryCopyShortcut(KeyEventArgs e)
    {
        if (!ItemList.IsKeyboardFocusWithin || ItemList.SelectedItem is not ClothItem item) return false;
        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.C) Clipboard.SetText(item.Name);
        else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Alt) && e.Key == Key.C && !item.IsDummy)
            Clipboard.SetText(Path.GetFileNameWithoutExtension(item.OriginalModelFileName ?? item.ModelPath));
        else if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.P && !item.IsDummy)
            Clipboard.SetText(AbsoluteAssetPath(item.ModelPath));
        else return false;
        e.Handled = true; return true;
    }
    private void RenameSelectedItem()
    {
        if (ItemList.SelectedItem is not ClothItem item) return;
        var dialog = new RenameWindow(item.Name) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        item.Name = dialog.Result; _dirty = true; RefreshItems(); ItemList.SelectedItem = item;
    }
    private void RenameSelectedTexture()
    {
        if (ItemList.SelectedItem is not ClothItem item || TextureList.SelectedIndex < 0) return;
        var index = TextureList.SelectedIndex; AlignTextureNames(item);
        var name = string.IsNullOrWhiteSpace(item.TextureLabels[index]) ? Path.GetFileName(item.OriginalTextureFileNames[index]) : item.TextureLabels[index];
        var dialog = new RenameWindow(name) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        item.TextureLabels[index] = dialog.Result; _dirty = true; RefreshTextureList(item, index);
    }
    private void ShowSourceInExplorer(string source)
    {
        var path = AbsoluteAssetPath(source);
        if (!File.Exists(path)) throw new FileNotFoundException("Source file is missing.", path);
        var start = new System.Diagnostics.ProcessStartInfo("explorer.exe") { UseShellExecute = false };
        start.ArgumentList.Add("/select," + path); System.Diagnostics.Process.Start(start);
    }
    private void ExportSourceFile(string source, string? originalName)
    {
        var input = AbsoluteAssetPath(source);
        var extension = Path.GetExtension(input);
        var dialog = new SaveFileDialog { Filter = "GTA asset|*" + extension, FileName = Path.GetFileName(originalName ?? input) };
        if (dialog.ShowDialog(this) != true) return;
        var output = Path.GetFullPath(dialog.FileName);
        if (output.Equals(input, StringComparison.OrdinalIgnoreCase)) throw new IOException("Choose a different output file.");
        AssetFileExporter.Copy(input, output, true);
        Status("Export completed: " + output);
    }
    private async void ExportAssetAnalysis(string source, string format)
    {
        var picker = new OpenFolderDialog { Title = "Export " + format };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            var input = AbsoluteAssetPath(source); IsEnabled = false;
            var output = await Task.Run(() => AssetAnalysisExporter.Export(input, picker.FolderName, format));
            Status("Export completed: " + output);
        }
        catch (Exception ex) { Status(ex.Message); MessageBox.Show(this, ex.Message, "Export " + format, MessageBoxButton.OK, MessageBoxImage.Error); }
        finally { IsEnabled = true; }
    }
}
