using System.IO;
using CodeWalker.GameFiles;

namespace FooxClothStudio;

public static class YldInspector
{
    public static int CountClothes(string path)
    {
        var file = new YldFile();
        try { RpfFile.LoadResourceFile(file, File.ReadAllBytes(path), 8); }
        catch (Exception ex) { throw new InvalidDataException("YLD inválido: " + ex.Message, ex); }
        var clothes = file.ClothDictionary?.Clothes?.data_items;
        if (clothes is null || !clothes.Any(cloth => cloth is not null))
            throw new InvalidDataException("YLD sem entradas de tecido.");
        return clothes.Count(cloth => cloth is not null);
    }
}
