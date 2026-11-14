using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace FooxClothStudio;

public partial class MainWindow
{
    private void InsertBeforeDrawable_Click(object sender, RoutedEventArgs e)
    {
        if (ItemList.SelectedItem is not ClothItem item || item.IsDummy) return;
        var source = OriginalResourceExporter.FindSource(_project, _resource?.Root);
        if (source is null) { Status("Open the original FiveM resource before choosing an insertion target."); return; }
        var stream = Path.Combine(source, "stream");
        var originalNames = Directory.EnumerateFiles(stream, "*.ydd", SearchOption.AllDirectories).Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (originalNames.Contains(Path.GetFileName(item.OriginalModelFileName ?? item.ModelPath)))
        { Status("Select the newly added clothing item to insert before an existing drawable."); return; }
        if (CollectionIndices.CodeOf(item.Category).StartsWith("p_"))
        { Status("Native insert-before currently supports clothing components. Props can be added in new collections."); return; }
        var ymts = Directory.EnumerateFiles(stream, "*.ymt", SearchOption.AllDirectories).Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var targets = _project.Items.Where(candidate => !candidate.IsDummy && candidate.Id != item.Id && candidate.Gender == item.Gender && candidate.Category == item.Category
            && originalNames.Contains(Path.GetFileName(candidate.OriginalModelFileName ?? candidate.ModelPath))
            && ymts.Contains(Path.GetFileName(candidate.OriginalModelFileName ?? candidate.ModelPath).Split('^')[0] + ".ymt"))
            .Select(candidate => new { Item = candidate, Label = Path.GetFileName(candidate.OriginalModelFileName ?? candidate.ModelPath) })
            .OrderBy(candidate => candidate.Label).ToArray();
        if (targets.Length == 0) { Status("No existing addon drawable of this type has its collection YMT in this resource."); return; }
        var window = new Window { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        WorkflowUi.Theme(window, "Insert before existing drawable", 780, 510);
        var panel = WorkflowUi.Stack(); window.Content = panel;
        panel.Children.Add(new TextBlock { Text = "Choose the existing addon drawable. The new clothing takes its native index; that drawable and the following ones shift. Their UUIDs are retained. Save character and outfit references by UUID before deploying.", TextWrapping = TextWrapping.Wrap });
        var search = WorkflowUi.Field(panel, "Search collection or drawable filename", "");
        var list = new ListBox { Height = 300, ItemsSource = targets, DisplayMemberPath = "Label", SelectedValuePath = "Item", Background = WorkflowUi.Panel, Foreground = System.Windows.Media.Brushes.White };
        panel.Children.Add(list);
        search.TextChanged += (_, _) => list.ItemsSource = targets.Where(target => target.Label.Contains(search.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
        WorkflowUi.Button(panel, "Insert before selected drawable", (_, _) =>
        {
            if (list.SelectedValue is not ClothItem target) return;
            item.InsertBeforeItemId = target.Id;
            item.ExportPosition = target.SourceIndex;
            window.DialogResult = true;
        });
        if (window.ShowDialog() != true) return;
        _dirty = true;
        RefreshItems();
        ItemList.SelectedItem = item;
        ShowDetails(item);
        Status("Native insertion target selected. Build with 'Keep original collections and add new clothes' to apply it.");
    }

    private void ClearNativeInsertion_Click(object sender, RoutedEventArgs e)
    {
        if (ItemList.SelectedItem is not ClothItem item) return;
        item.InsertBeforeItemId = null;
        _dirty = true;
        ShowDetails(item);
        Status("Native insertion cleared; this item will be added in a separate collection.");
    }
}
