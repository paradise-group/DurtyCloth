using FooxClothStudio;

var source = Path.GetFullPath(args[0]);
var count = YldInspector.CountClothes(source);
if (count <= 0) throw new Exception("No cloth entries.");
var invalid = Path.Combine(Path.GetFullPath(args[1]), "invalid.yld");
Directory.CreateDirectory(Path.GetDirectoryName(invalid)!);
File.WriteAllBytes(invalid, [1, 2, 3, 4]);
try
{
    YldInspector.CountClothes(invalid);
    throw new Exception("Invalid YLD was accepted.");
}
catch (InvalidDataException) { }
Console.WriteLine($"OK: {count} cloth entries in real YLD; invalid bytes rejected. {source}");
