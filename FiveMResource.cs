using System.IO;
using System.Security.Cryptography;

namespace FooxClothStudio;

public sealed record FiveMResource(string Root, int FileCount, long TotalBytes)
{
    public static FiveMResource Open(string root)
    {
        root = Path.GetFullPath(root);
        if (!File.Exists(Path.Combine(root, "fxmanifest.lua")) ||
            !Directory.Exists(Path.Combine(root, "stream")))
            throw new InvalidDataException("Selecione a raiz de um recurso FiveM com fxmanifest.lua e stream.");
        var files = GetFiles(root).ToArray();
        return new FiveMResource(root, files.Length, files.Sum(file => new FileInfo(file).Length));
    }

    public int Export(string destination)
    {
        destination = Path.GetFullPath(destination);
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new IOException("A saída já existe: " + destination);
        var sourcePrefix = Root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var destinationPrefix = destination.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (destinationPrefix.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase) ||
            sourcePrefix.StartsWith(destinationPrefix, StringComparison.OrdinalIgnoreCase))
            throw new IOException("A saída deve ficar fora da pasta do recurso original.");

        var files = GetFiles(Root).ToArray();
        Directory.CreateDirectory(destination);
        try
        {
            Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = 4 }, source =>
            {
                var relative = Path.GetRelativePath(Root, source);
                var target = Path.Combine(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(source, target);
                using var sourceStream = File.OpenRead(source);
                using var targetStream = File.OpenRead(target);
                if (!SHA256.HashData(sourceStream).SequenceEqual(SHA256.HashData(targetStream)))
                    throw new IOException("Falha de verificação: " + relative);
            });
            return files.Length;
        }
        catch
        {
            Directory.Delete(destination, true);
            throw;
        }
    }

    private static IEnumerable<string> GetFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if (Path.GetFileName(entry).Equals(".git", StringComparison.OrdinalIgnoreCase)) continue;
                var attributes = File.GetAttributes(entry);
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                    throw new InvalidDataException("Links de pasta ou arquivo não são aceitos: " + entry);
                if (attributes.HasFlag(FileAttributes.Directory)) pending.Push(entry);
                else yield return entry;
            }
        }
    }
}
