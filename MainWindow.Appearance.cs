using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FooxClothStudio;

public partial class MainWindow
{
    private void ShowAppearanceAssets_Click(object sender, RoutedEventArgs e)
    {
        var window = new Window
        {
            Title = "Appearance assets",
            Owner = this,
            Width = 700, Height = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(29, 31, 34))
        };
        var rows = _project.AppearanceAssets.OrderBy(asset => asset.Kind).ThenBy(asset => asset.Name)
            .Select(asset => asset.Kind + " • " + asset.Gender + " • " + asset.Name).ToArray();
        window.Content = new ListBox
        {
            ItemsSource = rows,
            Background = new SolidColorBrush(Color.FromRgb(37, 40, 44)),
            Foreground = Brushes.White,
            Margin = new Thickness(12)
        };
        window.ShowDialog();
    }

}
