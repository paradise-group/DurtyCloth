using System.Windows;

namespace FooxClothStudio;

public partial class MainWindow
{
    private void GenerateClothingIds_Click(object sender, RoutedEventArgs e)
    {
        if (_project.Items.Count == 0) { Status("Add clothing or open a FiveM resource first."); return; }
        try
        {
            var generated = ClothingIdentityMap.GenerateMissing(_project);
            _dirty = true;
            RefreshItems();
            Status($"Permanent IDs ready for {_project.Items.Count} items and {_project.Items.Sum(item => item.TextureIds.Count)} variants. " +
                $"{generated} missing IDs generated; existing IDs preserved. Save the project and build the addon to export the map.");
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Permanent IDs", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
}
