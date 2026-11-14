using System.Diagnostics;

namespace FooxClothStudio;

public sealed class ProgressEstimate
{
    private readonly Func<TimeSpan> _elapsed;
    private TimeSpan _started;
    private int _initialDone;
    private int _lastDone;
    private int _total = -1;

    public ProgressEstimate(Func<TimeSpan>? elapsed = null)
    {
        if (elapsed is not null) _elapsed = elapsed;
        else
        {
            var stopwatch = Stopwatch.StartNew();
            _elapsed = () => stopwatch.Elapsed;
        }
    }

    public string Report(int done, int total)
    {
        var now = _elapsed();
        if (total <= 0)
        {
            _total = -1;
            return "Estimating remaining time...";
        }
        done = Math.Clamp(done, 0, total);
        var percentage = $"{100.0 * done / total:0}%";
        if (_total != total || done < _lastDone || now < _started)
        {
            _started = now;
            _initialDone = done;
            _total = total;
        }
        _lastDone = done;
        if (done == total) return percentage + " · Completed";
        var completed = done - _initialDone;
        var elapsed = now - _started;
        if (completed <= 0 || elapsed <= TimeSpan.Zero)
            return percentage + " · Estimating remaining time...";
        var ticks = elapsed.Ticks * ((double)(total - done) / completed);
        if (!double.IsFinite(ticks) || ticks >= TimeSpan.MaxValue.Ticks)
            return percentage + " · Estimating remaining time...";
        return percentage + " · Estimated remaining: " + Format(TimeSpan.FromTicks((long)Math.Ceiling(ticks)));
    }

    private static string Format(TimeSpan remaining)
    {
        if (remaining.TotalSeconds < 1) return "<1s";
        var wholeSeconds = Math.Ceiling(remaining.TotalSeconds);
        var rounded = wholeSeconds >= TimeSpan.MaxValue.TotalSeconds ? TimeSpan.MaxValue : TimeSpan.FromSeconds(wholeSeconds);
        if (rounded.Days > 0) return $"{rounded.Days}d {rounded.Hours}h {rounded.Minutes}m";
        if (rounded.Hours > 0) return $"{rounded.Hours}h {rounded.Minutes}m";
        if (rounded.Minutes > 0) return $"{rounded.Minutes}m {rounded.Seconds}s";
        return $"{rounded.Seconds}s";
    }
}
