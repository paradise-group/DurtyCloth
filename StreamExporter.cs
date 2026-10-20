using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace FooxClothStudio;

public static class StreamExporter
{
    public static (string Folder, int FileCount) Export(ClothProject project,
        Func<string, string> resolvePath, string parentFolder)
    {
        if (project.Items.Count == 0)
            throw new InvalidDataException("O projeto não contém peças para exportar.");

        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in project.Items)
        {
            foreach (var asset in NativeItemExporter.Plan(item, resolvePath))
            {
                var source = Path.GetFullPath(asset.Source);
                if (files.TryGetValue(asset.Name, out var previous))
                {
                    if (!string.Equals(previous, source, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Duas peças geram o mesmo nome no stream: " + asset.Name);
                    continue;
                }
                files.Add(asset.Name, source);
            }
        }

        var safeName = new string(project.Name.ToLowerInvariant().Select(ch =>
            char.IsAsciiLetterOrDigit(ch) ? ch : '_').ToArray()).Trim('_');
        if (safeName.Length == 0) safeName = "projeto";
        var output = Path.Combine(Path.GetFullPath(parentFolder), "foox_cloth_" + safeName);
        if (Directory.Exists(output) || File.Exists(output))
            throw new IOException("A pasta de saída já existe: " + output);

        var stream = Path.Combine(output, "stream");
        Directory.CreateDirectory(stream);
        try
        {
            foreach (var (name, source) in files)
            {
                var destination = Path.Combine(stream, name);
                File.Copy(source, destination);
                using var input = File.OpenRead(source);
                using var copied = File.OpenRead(destination);
                if (!SHA256.HashData(input).SequenceEqual(SHA256.HashData(copied)))
                    throw new IOException("Falha na verificação da cópia: " + name);
            }
            File.WriteAllText(Path.Combine(output, "fxmanifest.lua"),
                "fx_version 'cerulean'\ngame 'gta5'\n", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(output, "LEIA-ME.txt"),
                "Exportação de arquivos nativos para stream.\n" +
                "Este estágio não gera metadados de addon (.ymt / shop.meta).\n" +
                "Teste no FXServer antes de usar; nomes e índices precisam ser compatíveis.\n",
                new UTF8Encoding(false));
            return (output, files.Count);
        }
        catch
        {
            if (Directory.Exists(output)) Directory.Delete(output, true);
            throw;
        }
    }
}
