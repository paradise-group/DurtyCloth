using FooxClothStudio;

internal static class ProgressEstimateTests
{
    public static void Run()
    {
        var now = TimeSpan.Zero;
        var estimate = new ProgressEstimate(() => now);
        Check(estimate.Report(0, 10).Contains("Estimating"), "Zero progress must not invent an ETA");
        now = TimeSpan.FromSeconds(2);
        Check(estimate.Report(1, 10) == "10% · Estimated remaining: 18s", "ETA did not use observed throughput");
        now = TimeSpan.FromSeconds(20);
        Check(estimate.Report(5, 10) == "50% · Estimated remaining: 20s", "ETA did not adapt to a slower operation");
        Check(estimate.Report(10, 10) == "100% · Completed", "Completion retained a stale ETA");
        Check(estimate.Report(0, 10).Contains("Estimating"), "A restarted operation retained the previous rate");
        now += TimeSpan.FromMinutes(1);
        Check(estimate.Report(1, 10) == "10% · Estimated remaining: 9m 0s", "Minute formatting is incorrect");
        Check(estimate.Report(2, 20).Contains("Estimating"), "A changed total retained an unrelated ETA");
        now += TimeSpan.FromHours(1);
        Check(estimate.Report(3, 20).Contains("17h 0m"), "Hour formatting is incorrect");
        Check(estimate.Report(0, 0) == "Estimating remaining time...", "An indeterminate operation retained a percentage");
        Check(estimate.Report(-1, 10).StartsWith("0%"), "Negative progress was not clamped");
        now += TimeSpan.FromDays(1);
        Check(estimate.Report(1, 10).Contains("9d 0h 0m"), "Day formatting is incorrect");
        Check(estimate.Report(100, 10) == "100% · Completed", "Over-completion was not clamped");
        now = TimeSpan.Zero;
        estimate = new ProgressEstimate(() => now);
        estimate.Report(0, 2);
        Check(estimate.Report(1, 2).Contains("Estimating"), "Zero elapsed time caused an invalid rate");
        now = TimeSpan.FromMilliseconds(5);
        Check(estimate.Report(1, 2).Contains("<1s"), "Sub-second ETA formatting is incorrect");
        now = TimeSpan.Zero;
        Check(estimate.Report(0, 2).Contains("Estimating"), "A clock reset retained an invalid duration");
        now = TimeSpan.MaxValue;
        Check(estimate.Report(1, 2).Contains("Estimating"), "Unrepresentable ETA overflowed");
        Console.WriteLine("PASS: ETA uses actual throughput, adapts to progress and resets, handles completion/unknown totals and formats sub-second through multi-day operations without invalid durations.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
