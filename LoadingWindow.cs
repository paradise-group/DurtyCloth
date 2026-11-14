using System.Windows;
using System.Windows.Controls;

namespace FooxClothStudio;

internal sealed class LoadingWindow : Window
{
    private readonly TextBlock _stage = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _percentage = new() { Text = "0%", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
    private readonly ProgressBar _bar = new() { Height = 12, Margin = new Thickness(0, 16, 0, 0), IsIndeterminate = true };
    private bool _complete;

    public LoadingWindow(string title, string message)
    {
        WorkflowUi.Theme(this, title, 440, 150);
        MinHeight = 0;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = WorkflowUi.Stack();
        Content = panel;
        _stage.Text = message;
        panel.Children.Add(_stage);
        panel.Children.Add(_percentage);
        panel.Children.Add(_bar);
        Closing += (_, args) => { if (!_complete) args.Cancel = true; };
    }

    public void Report(int done, int total, string stage)
    {
        _stage.Text = stage;
        _bar.IsIndeterminate = total <= 0;
        if (total > 0)
        {
            _bar.Maximum = total;
            _bar.Value = Math.Clamp(done, 0, total);
            _percentage.Text = $"{100.0 * _bar.Value / total:0}%";
        }
    }

    public void Complete()
    {
        _complete = true;
        Close();
    }
}
