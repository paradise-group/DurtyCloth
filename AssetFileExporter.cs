using System.IO;
using System.Security.Cryptography;

namespace FooxClothStudio;

public static class AssetFileExporter
{
    public static void Copy(string source, string destination, bool overwrite = false)
    {
        var input = Path.GetFullPath(source); var output = Path.GetFullPath(destination);
        if (output.Equals(input, StringComparison.OrdinalIgnoreCase)) throw new IOException("Choose a different output file.");
        var temporary = Path.Combine(Path.GetDirectoryName(output)!, ".foox-export-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.Copy(input, temporary);
            using (var original = File.OpenRead(input))
            using (var copied = File.OpenRead(temporary))
                if (!SHA256.HashData(original).SequenceEqual(SHA256.HashData(copied))) throw new IOException("Exported asset differs from source.");
            File.Move(temporary, output, overwrite);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
