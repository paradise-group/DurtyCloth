using System.IO;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using CodeWalker.Utils;

namespace FooxClothStudio;

public sealed class TattooWindow : Window
{
    public List<TattooItem> Result { get; }
    public TattooWindow(ClothProject project, Func<string, string> resolve, string defaultGender = "Both")
    {
        Result = JsonSerializer.Deserialize<List<TattooItem>>(JsonSerializer.Serialize(project.Tattoos))!;
        WorkflowUi.Theme(this, "Tattoos", 1000, 720);
        NameScope.SetNameScope(this, new NameScope());
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) }); grid.ColumnDefinitions.Add(new ColumnDefinition()); Content = grid;
        var left = WorkflowUi.Stack(); grid.Children.Add(left);
        left.Children.Add(new TextBlock { Text = "Tattoos" });
        var list = new ListBox { Name = "TattooList", Height = 500, Background = WorkflowUi.Panel, Foreground = Brushes.White }; RegisterName(list.Name, list); left.Children.Add(list);
        var right = WorkflowUi.Stack(); Grid.SetColumn(right, 1); grid.Children.Add(right);
        var tabs = new TabControl(); right.Children.Add(tabs);
        var general = WorkflowUi.Stack(); tabs.Items.Add(new TabItem { Header = "General", Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = general, Height = 480 } });
        var name = WorkflowUi.Field(general, "Name / overlay hash", ""); name.Name = "TattooName"; RegisterName(name.Name, name);
        var label = WorkflowUi.Field(general, "Label", "");
        var file = WorkflowUi.Field(general, "Image / texture file", ""); file.IsReadOnly = true;
        var textureName = WorkflowUi.Field(general, "Internal texture name (YTD)", "");
        general.Children.Add(new TextBlock { Text = "Gender", Margin = new Thickness(0, 8, 0, 4) });
        var gender = new ComboBox { ItemsSource = new[] { "Male", "Female", "Both" } }; general.Children.Add(gender);
        general.Children.Add(new TextBlock { Text = "Zone", Margin = new Thickness(0, 8, 0, 4) });
        var zone = new ComboBox { ItemsSource = TattooExporter.Zones }; general.Children.Add(zone);
        var uvGrid = new Grid(); uvGrid.ColumnDefinitions.Add(new ColumnDefinition()); uvGrid.ColumnDefinitions.Add(new ColumnDefinition()); general.Children.Add(uvGrid);
        var uvLeft = new StackPanel { Margin = new Thickness(0, 0, 8, 0) }; var uvRight = new StackPanel();
        uvGrid.Children.Add(uvLeft); Grid.SetColumn(uvRight, 1); uvGrid.Children.Add(uvRight);
        var u = WorkflowUi.Field(uvLeft, "UV Position X", "0.5"); var v = WorkflowUi.Field(uvRight, "UV Position Y", "0.5");
        var sx = WorkflowUi.Field(uvLeft, "Scale X", "0.5"); var sy = WorkflowUi.Field(uvRight, "Scale Y", "0.5");
        var rotation = WorkflowUi.Field(general, "Rotation", "0");
        var shop = WorkflowUi.Stack(); tabs.Items.Add(new TabItem { Header = "Shop Meta", Content = shop });
        var cost = WorkflowUi.Field(shop, "Cost", "100");
        var cameraX = WorkflowUi.Field(shop, "ESX preview camera X", "0.7");
        var cameraY = WorkflowUi.Field(shop, "ESX preview camera Y", "0.4");
        var cameraZ = WorkflowUi.Field(shop, "ESX preview camera Z", "0.3");
        var cameraHeading = WorkflowUi.Field(shop, "ESX preview camera heading", "117.3");
        var preview = new Image { Height = 400, Stretch = Stretch.Uniform }; tabs.Items.Add(new TabItem { Header = "Texture Preview", Content = preview });
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) }; right.Children.Add(status);
        TattooItem? selected = null; bool updating = false;
        string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);
        void Refresh() { list.ItemsSource = null; list.ItemsSource = Result; }
        bool Commit()
        {
            if (selected is null) return true;
            try
            {
                var candidate = JsonSerializer.Deserialize<TattooItem>(JsonSerializer.Serialize(selected))!;
                candidate.Name = name.Text.Trim(); candidate.Label = label.Text;
                candidate.SourceTextureName = textureName.Text.Trim(); candidate.Gender = gender.SelectedItem as string ?? "Both";
                candidate.Zone = zone.SelectedItem as string ?? "ZONE_TORSO";
                candidate.U = float.Parse(u.Text, CultureInfo.InvariantCulture); candidate.V = float.Parse(v.Text, CultureInfo.InvariantCulture);
                candidate.ScaleX = float.Parse(sx.Text, CultureInfo.InvariantCulture); candidate.ScaleY = float.Parse(sy.Text, CultureInfo.InvariantCulture);
                candidate.Rotation = float.Parse(rotation.Text, CultureInfo.InvariantCulture); candidate.Cost = int.Parse(cost.Text);
                candidate.CameraX = float.Parse(cameraX.Text, CultureInfo.InvariantCulture); candidate.CameraY = float.Parse(cameraY.Text, CultureInfo.InvariantCulture);
                candidate.CameraZ = float.Parse(cameraZ.Text, CultureInfo.InvariantCulture); candidate.CameraHeading = float.Parse(cameraHeading.Text, CultureInfo.InvariantCulture);
                TattooExporter.Validate(candidate);
                if (Result.Any(x => x.Id != candidate.Id && x.Name.Equals(candidate.Name, StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("Tattoo name already exists.");
                TattooExporter.ReadImage(candidate, resolve(candidate.ImagePath));
                selected.Name = candidate.Name; selected.Label = candidate.Label; selected.SourceTextureName = candidate.SourceTextureName;
                selected.Gender = candidate.Gender; selected.Zone = candidate.Zone; selected.U = candidate.U; selected.V = candidate.V;
                selected.ScaleX = candidate.ScaleX; selected.ScaleY = candidate.ScaleY; selected.Rotation = candidate.Rotation; selected.Cost = candidate.Cost;
                selected.CameraX = candidate.CameraX; selected.CameraY = candidate.CameraY; selected.CameraZ = candidate.CameraZ; selected.CameraHeading = candidate.CameraHeading;
                return true;
            }
            catch (Exception ex) { status.Text = ex.Message; return false; }
        }
        void ShowItem(TattooItem? item)
        {
            selected = item; general.IsEnabled = shop.IsEnabled = item is not null;
            name.Text = item?.Name ?? ""; label.Text = item?.Label ?? ""; file.Text = item?.ImagePath ?? "";
            textureName.Text = item?.SourceTextureName ?? ""; gender.SelectedItem = item?.Gender ?? "Both"; zone.SelectedItem = item?.Zone ?? "ZONE_TORSO";
            u.Text = Number(item?.U ?? 0.5f); v.Text = Number(item?.V ?? 0.5f); sx.Text = Number(item?.ScaleX ?? 0.5f); sy.Text = Number(item?.ScaleY ?? 0.5f);
            rotation.Text = Number(item?.Rotation ?? 0); cost.Text = (item?.Cost ?? 100).ToString(); preview.Source = null;
            cameraX.Text = Number(item?.CameraX ?? 0.7f); cameraY.Text = Number(item?.CameraY ?? 0.4f); cameraZ.Text = Number(item?.CameraZ ?? 0.3f); cameraHeading.Text = Number(item?.CameraHeading ?? 117.3f);
            if (item is null) return;
            try
            {
                var texture = TattooExporter.ReadImage(item, resolve(item.ImagePath)); var pixels = DDSIO.GetPixels(texture, 0);
                var bitmap = BitmapSource.Create(texture.Width, texture.Height, 96, 96, PixelFormats.Bgra32, null, pixels, texture.Width * 4); bitmap.Freeze(); preview.Source = bitmap;
                status.Text = texture.Width + " × " + texture.Height + " • " + texture.Levels + " mipmaps";
            }
            catch (Exception ex) { status.Text = ex.Message; }
        }
        list.SelectionChanged += (_, _) =>
        {
            if (updating) return;
            var next = list.SelectedItem as TattooItem;
            if (!Commit()) { updating = true; list.SelectedItem = selected; updating = false; return; }
            ShowItem(next);
        };
        WorkflowUi.Button(left, "Add tattoo", (_, _) =>
        {
            if (!Commit()) return;
            var picker = new OpenFileDialog { Filter = "Tattoo image (*.png;*.dds;*.ytd)|*.png;*.dds;*.ytd" };
            if (picker.ShowDialog(this) != true) return;
            var stem = new string(Path.GetFileNameWithoutExtension(picker.FileName).Where(c => char.IsAsciiLetterOrDigit(c) || c == '_').Take(40).ToArray());
            if (stem.Length == 0) stem = "tattoo";
            var unique = stem; var n = 1; while (Result.Any(x => x.Name.Equals(unique, StringComparison.OrdinalIgnoreCase))) unique = stem + "_" + n++;
            var added = new TattooItem { Name = unique, Label = Path.GetFileNameWithoutExtension(picker.FileName), ImagePath = picker.FileName, Gender = defaultGender };
            updating = true; Result.Add(added); Refresh(); list.SelectedItem = added; updating = false; ShowItem(added);
        });
        WorkflowUi.Button(left, "Remove selected", (_, _) =>
        {
            if (selected is null) return;
            updating = true; Result.Remove(selected); selected = null; Refresh(); list.SelectedIndex = Result.Count > 0 ? 0 : -1; updating = false; ShowItem(list.SelectedItem as TattooItem);
        });
        WorkflowUi.Button(left, "Import tattoo resource", (_, _) =>
        {
            if (!Commit()) return;
            var picker = new OpenFolderDialog(); if (picker.ShowDialog(this) != true) return;
            try
            {
                var incoming = TattooImporter.Import(picker.FolderName);
                foreach (var tattoo in incoming)
                {
                    var stem = tattoo.Name; var suffix = 1;
                    while (Result.Any(x => x.Name.Equals(tattoo.Name, StringComparison.OrdinalIgnoreCase))) tattoo.Name = stem[..Math.Min(stem.Length, 40)] + "_" + suffix++;
                    Result.Add(tattoo);
                }
                updating = true; Refresh(); list.SelectedItem = incoming[0]; updating = false; ShowItem(incoming[0]);
            }
            catch (Exception ex) { status.Text = ex.Message; }
        });
        WorkflowUi.Button(general, "Replace image", (_, _) =>
        {
            if (selected is null || !Commit()) return;
            var picker = new OpenFileDialog { Filter = "Tattoo image|*.png;*.dds;*.ytd" };
            if (picker.ShowDialog(this) != true) return;
            selected.ImagePath = picker.FileName; selected.SourceTextureName = ""; ShowItem(selected);
        });
        var apply = WorkflowUi.Button(right, "Apply", (_, _) =>
        {
            if (!Commit()) return;
            try { foreach (var tattoo in Result) { TattooExporter.Validate(tattoo); TattooExporter.ReadImage(tattoo, resolve(tattoo.ImagePath)); } DialogResult = true; }
            catch (Exception ex) { status.Text = ex.Message; }
        });
        apply.Name = "ApplyTattoos"; RegisterName(apply.Name, apply);
        WorkflowUi.Button(right, "Cancel", (_, _) => DialogResult = false);
        Refresh(); list.SelectedIndex = Result.Count > 0 ? 0 : -1; ShowItem(list.SelectedItem as TattooItem);
    }
}
