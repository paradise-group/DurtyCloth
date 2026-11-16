using System.IO;
using FooxClothStudio;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var output = Path.Combine(Path.GetTempPath(), "photo-regressions-" + Guid.NewGuid().ToString("N"));
        PhotoResumeTests.Run();
        PhotoIndexTests.Run();
        ServerPhotoTests.Run();
        WebPExportTests.Run(Path.Combine(output, "formats"));
        HeadPhotoTests.Run(Path.Combine(output, "head"));
        SurfacePhotoTests.Run(Path.Combine(output, "surfaces"));
        var game = GamePedAssets.FindGameFolder();
        if (game is not null) GtaPhotoTests.Run(game, Path.Combine(output, "gta"));
        Console.WriteLine("PASS: default photo export regression suite. Fixtures: " + output);
    }
}
