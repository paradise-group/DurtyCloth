using System.Windows;
using System.Windows.Controls;

namespace FooxClothStudio;

public sealed class OperationProgressWindow : Window
{
    private readonly TextBlock _label = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar _bar = new() { Height = 10, Margin = new Thickness(0,12,0,0) };
    private readonly int _total;
    private readonly ProgressEstimate _estimate = new();
    private readonly TextBlock _timing = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private bool _complete;
    public OperationProgressWindow(string title, int total, string summary)
    {
        _total = total;
        WorkflowUi.Theme(this, title, 440, 160);
        MinHeight = 0;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = WorkflowUi.Stack(); Content = panel;
        panel.Children.Add(new TextBlock { Text = summary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,8) });
        panel.Children.Add(_label); panel.Children.Add(_timing); panel.Children.Add(_bar);
        _bar.Maximum = System.Math.Max(total, 1);
        Report(0, "Preparando arquivos do projeto");
        Closing += (_, e) => e.Cancel = !_complete;
    }
    public void Report(int done, string name)
    {
        if (_complete) return;
        _label.Text = _total > 0
            ? $"Saving items: {Math.Clamp(done, 0, _total)}/{_total}\n{name}"
            : name;
        _timing.Text = _estimate.Report(done, _total);
        _bar.Value = done; _bar.IsIndeterminate = _total == 0;
    }
    public void Complete() { _complete = true; Close(); }
}
