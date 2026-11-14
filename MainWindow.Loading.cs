using System.Runtime.ExceptionServices;

namespace FooxClothStudio;

public partial class MainWindow
{
    private T RunWithLoading<T>(string title, string message,
        Func<IProgress<(int Done, int Total, string Stage)>, T> work)
    {
        var window = new LoadingWindow(title, message) { Owner = this };
        T? result = default;
        Exception? error = null;
        var progress = new Progress<(int Done, int Total, string Stage)>(step =>
            window.Report(step.Done, step.Total, step.Stage));
        window.Loaded += async (_, _) =>
        {
            await Task.Yield();
            try { result = await Task.Run(() => work(progress)); }
            catch (Exception ex) { error = ex; }
            finally
            {
                if (error is null) window.Report(1, 1, "Concluído");
                window.Complete();
            }
        };
        window.ShowDialog();
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
        return result!;
    }
}
