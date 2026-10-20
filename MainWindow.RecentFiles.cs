using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace FooxClothStudio;

public partial class MainWindow
{
    private readonly RecentProjects _recentProjects = new();
    private void RememberRecentProject(string path)
    {
        // History failure must never turn a successful project save/open into a failure.
        try { _recentProjects.Remember(path); }
        catch (Exception) { }
        RefreshRecentFilesMenu();
    }
    private void RefreshRecentFilesMenu()
    {
        RecentFilesMenu.Items.Clear();
        var paths = _recentProjects.Read();
        if (paths.Length == 0)
        { RecentFilesMenu.Items.Add(new MenuItem { Header = "Nenhum projeto recente", IsEnabled = false }); return; }
        foreach (var path in paths)
        {
            var entry = new MenuItem { Header = Path.GetFileNameWithoutExtension(path).Replace("_", "__"), ToolTip = path, Tag = path };
            entry.Click += (_, _) => OpenRecentProject(path);
            RecentFilesMenu.Items.Add(entry);
        }
    }
    private void OpenRecentProject(string path)
    {
        if (!File.Exists(path))
        {
            MessageBox.Show(this, "O projeto foi movido ou removido:\n" + path, "Projeto não encontrado", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!ConfirmDiscard()) return;
        try { LoadProject(path); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Não foi possível abrir", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
}
